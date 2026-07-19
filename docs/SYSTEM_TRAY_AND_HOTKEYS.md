# 系統匣與全域快捷鍵決策（Issue #9）

## 技術選擇與責任邊界

系統匣使用 .NET 內建的 `System.Windows.Forms.NotifyIcon`，不引入第三方 UI
套件。`NotifyIcon`、右鍵選單、雙擊事件、Explorer recovery 與 disposal
均封裝在 Windowing infrastructure；ViewModel 不引用 `NotifyIcon`、Window、
HWND、`HwndSource` 或 P/Invoke。

Tray 使用 assembly 內嵌的 `RunCatDashboard.Tray.ico`，內含 16×16 與 32×32
高對比尺寸，不使用 `SystemIcons.Application`。Loader 從 manifest resource
建立獨立 owned `Icon`；stream 可立即關閉，但 owned icon 會涵蓋 NotifyIcon
與 Explorer recovery 的完整生命週期。Dispose 時先隱藏並 dispose NotifyIcon，
再 dispose owned icon。載入失敗會使 tray initialization 明確失敗並發布診斷，
不以空白 icon 靜默繼續。

`RegisterHotKey`、`UnregisterHotKey` 與 `RegisterWindowMessage` 宣告集中在
`Interop`。`MainWindow` 只在 WPF lifecycle 持有一個 `HwndSourceHook`，將訊息
分派給 hotkey handler 與 tray service。

## Tray 行為

右鍵選單依序包含：

1. `顯示 Dashboard` 或 `隱藏 Dashboard`。
2. `切換為 Interactive` 或 `切換為 Click-through`。
3. 分隔線。
4. `退出`。

文字代表下一個動作。左鍵單擊不處理；左鍵雙擊與第一個選單項目都透過
同一 visibility coordinator 切換 user-requested visibility。互動模式選單
與 R hotkey 都只呼叫同一個 application-level `InteractionModeToggleAction`；
action 經 WPF Dispatcher 呼叫既有 `OverlayModeCoordinator`，不在 tray service
重寫 native style。confirmed／fault state 隨後同時更新 Dashboard 與 tray 選單；
套用失敗時選單仍以 applied mode 顯示可重試的下一個動作。

## Visibility 雙狀態模型

Coordinator 至少保存：

- `IsUserRequestedVisible`：使用者最近是否要求顯示。
- `IsFullscreenPolicyVisible`：fullscreen policy 是否允許顯示。
- `IsActuallyVisible`：前兩者的 AND。

Fullscreen 只能改變 policy input，不能覆寫 user input。因此使用者手動隱藏
後，fullscreen 出現及離開仍保持隱藏。若使用者在 fullscreen 期間要求顯示，
requested-visible 會保留，Window 暫時隱藏，離開 fullscreen 後才恢復。

Hide 不重建 Window、ViewModel、metrics sampling loop 或 animation cache。
動畫 timer 隨實際可見性停止／恢復，metrics sampling 持續。

## Close 與真正退出

任何 Dashboard Window Closing 在未要求退出時都會被取消，並把
user-requested visibility 設為 false。系統匣 `退出` 是唯一真正退出來源：

1. `ApplicationExitCoordinator` 先以冪等方式標記真正退出。
2. Window Closing 因退出旗標而不取消。
3. 解除所有成功註冊的 hotkey。
4. 移除共用 HWND message hook。
5. 隱藏並 dispose tray icon。
6. 停止 fullscreen monitor、display-settings subscription、window controller、
   animation 與 metrics lifecycle。
7. DI disposal 再次安全地 dispose tray、hotkey 與 coordinators，App 結束。

各 Dispose／Stop 可重複呼叫；退出要求也只處理一次。Single-instance Mutex
取得與第二執行個體拒絕流程未改變。

## 快捷鍵與衝突處理

固定快捷鍵為：

- `Ctrl + Alt + Shift + R`：切換 Interactive／Click-through。
- `Ctrl + Alt + Shift + D`：切換 Dashboard user-requested visibility。

兩者使用不同 ID，保留
`MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT`。Controller 一次逐項嘗試
註冊，重複呼叫不會重複註冊。`WM_HOTKEY` 只分派成功註冊的 ID。

其中一項失敗不阻止另一項、tray 或 App 啟動。Dashboard 顯示可理解的中文
fault；registration state 另保留 Win32 error code 供診斷，不直接把裸錯誤碼
當 UI 訊息。Dispose 只解除成功註冊的項目；解除失敗不拋出或靜默遺失，
而會留在該項 registration fault state。

## Explorer recovery

啟動時以 `RegisterWindowMessage("TaskbarCreated")` 取得 Windows registered
message。共用 HWND hook 收到該訊息後，tray service 以既有 `NotifyIcon` instance
重新加入 shell notification area 並刷新選單狀態；不重建 App、MainWindow、
ViewModel 或 tray service，也不重新註冊 hotkey。重複訊息安全，恢復失敗會保留
在 tray diagnostic state 並顯示於 Dashboard。

## 人工驗證清單

單元測試不能完整驗證 Windows shell、Explorer、全域鍵盤路由、焦點或原生視窗
生命週期。需在 Windows 10／11 x64 人工確認：

- tray icon 顯示、右鍵順序與文字，以及左鍵單擊無作用、雙擊切換。
- Window Close 只隱藏；tray `退出` 才結束程序，且圖示不殘留。
- R／D 在一般桌面雙向切換，按住時 `MOD_NOREPEAT` 生效。
- 以其他程式占用 R 或 D 時，App 仍啟動、另一快捷鍵與 tray 仍可用，診斷清楚。
- 結束 Explorer 後重新啟動，tray icon 恢復；重複 Explorer recovery 不產生重複圖示。
- user hidden／user visible 與 fullscreen 進出組合符合 visibility precedence。
- Hide／Show 不重建 Window 或 ViewModel，metrics 保持採樣，動畫停止後可續跑。
- 重複退出／重新啟動不殘留 hotkey；unregister 或 tray recovery 故障可診斷。
- Topmost、click-through、no-activate、DPI、多螢幕、desktop shell 排除與
  single-instance 行為沒有退化。
