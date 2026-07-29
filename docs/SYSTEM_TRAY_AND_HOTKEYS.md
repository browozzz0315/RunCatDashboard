# 系統匣與全域快捷鍵決策（Issue #9）

## 技術選擇與責任邊界

系統匣使用 .NET 內建的 `System.Windows.Forms.NotifyIcon`，不引入第三方 UI
套件。`NotifyIcon`、右鍵選單、雙擊事件、Explorer recovery 與 disposal
均封裝在 Windowing infrastructure；ViewModel 不引用 `NotifyIcon`、Window、
HWND、`HwndSource` 或 P/Invoke。

Tray 預設使用 Cat-2 黑貓跑步動畫。八個 assembly embedded `.ico` frame 各含
16×16、20×20、24×24、32×32 的透明、無外框圖像，皆由正式 Cat-2 frame
預先產生；runtime 不直接用 Dashboard 的 50×50 PNG，也不在 frame update 做
PNG→Icon 轉換。所有 animation icon 在啟動時載入一次，並由 adapter 持有至
真正退出。

既有 `RunCatDashboard.Tray.ico`（16×16、32×32）保留為靜態模式與動畫資源
載入失敗時的 fallback，不使用 `SystemIcons.Application`。Loader 從 manifest
resource 建立獨立 owned `Icon`；stream 可立即關閉，但 static 與 animation
owned icons 都涵蓋 NotifyIcon 與 Explorer recovery 的完整生命週期。Dispose
時先停止 frame subscription、隱藏並 dispose NotifyIcon，再 dispose 全部 owned
icons。單次 frame 指定失敗時保留上一個有效 icon 並發布診斷；完整 animation
set 載入失敗時使用已驗證的 static icon 並保留診斷。只有 static icon 也無法
載入時，tray initialization 才明確失敗，不以空白 icon 靜默繼續。

`RegisterHotKey`、`UnregisterHotKey` 與 `RegisterWindowMessage` 宣告集中在
`Interop`。`MainWindow` 只在 WPF lifecycle 持有一個 `HwndSourceHook`，將訊息
分派給 hotkey handler 與 tray service。

## Tray 行為

右鍵選單依序包含：

1. `顯示 Dashboard` 或 `隱藏 Dashboard`。
2. `切換為 Interactive` 或 `切換為 Click-through`。
3. `停用系統匣動畫（改用靜態圖示）` 或 `啟用系統匣動畫`。
4. 分隔線。
5. `設定...`。
6. 分隔線。
7. `退出`。

文字代表下一個動作。左鍵單擊不處理；左鍵雙擊與第一個選單項目都透過
同一 visibility coordinator 切換 user-requested visibility。互動模式選單
與 R hotkey 都只呼叫同一個 application-level `InteractionModeToggleAction`；
action 經 WPF Dispatcher 呼叫既有 `OverlayModeCoordinator`，不在 tray service
重寫 native style。confirmed／fault state 隨後同時更新 Dashboard 與 tray 選單；
套用失敗時選單仍以 applied mode 顯示可重試的下一個動作。

「設定...」發布 `SettingsRequested`，由 WPF lifecycle 的單一 Settings Window
service 處理；Dashboard hidden 時仍可開啟。重複要求只 restore/Activate 現有視窗，
Settings Window 關閉不會觸發 App exit。

系統匣動畫模式每次啟動預設為 animated。第三個選單項目只切換本次 process
中的 tray presentation；不寫入設定、不新增 Dashboard 設定 UI，重新啟動後
恢復 animated。切成 static 不停止或改變 Dashboard animation；切回 animated
時立即套用 shared controller 的目前 frame。

## Visibility 雙狀態模型

Coordinator 至少保存：

- `IsUserRequestedVisible`：使用者最近是否要求顯示。
- `IsFullscreenPolicyVisible`：fullscreen policy 是否允許顯示。
- `IsActuallyVisible`：前兩者的 AND。

Fullscreen 只能改變 policy input，不能覆寫 user input。因此使用者手動隱藏
後，fullscreen 出現及離開仍保持隱藏。若使用者在 fullscreen 期間要求顯示，
requested-visible 會保留，Window 暫時隱藏，離開 fullscreen 後才恢復。

Hide 不重建 Window、ViewModel、metrics sampling loop 或 animation cache。
唯一的動畫 timer 與 metrics sampling 都持續；Dashboard 與 tray animation
共用同一 frame index 與同一 CPU speed mapping，因此 tray 在 user hide、
Close→Hide 與 fullscreen policy hide 時仍持續更新。

## Close 與真正退出

任何 Dashboard Window Closing 在未要求退出時都會被取消，並把
user-requested visibility 設為 false。系統匣 `退出` 是唯一真正退出來源：

1. `ApplicationExitCoordinator` 發布要求，visibility coordinator 以冪等方式標記真正退出。
2. 擷取最終 Window 位置並 flush settings，再關閉 Settings Window。
3. Window Closing 因退出旗標而不取消。
4. 解除所有成功註冊的 hotkey並移除共用 HWND message hook。
5. 停止 tray frame subscription，隱藏並 dispose NotifyIcon 與全部 icon 資源。
6. 停止 fullscreen monitor、display-settings subscription、window controller、
   animation 與 metrics lifecycle。
7. `Application.Shutdown()` 後由 DI disposal 再次安全地 dispose settings、tray、
   hotkey 與 coordinators，App 結束。

各 Dispose／Stop 可重複呼叫；退出要求也只處理一次。Single-instance Mutex
取得與第二執行個體拒絕流程未改變。

## 快捷鍵、自訂範圍與衝突處理

快捷鍵為：

- 預設 `Ctrl + Alt + Shift + R`：切換 Interactive／Click-through；可在 Settings
  自訂 Ctrl、Alt、Shift、Windows modifier 與 A-Z、0-9、F1-F12 主要按鍵。
- 固定 `Ctrl + Alt + Shift + D`：切換 Dashboard user-requested visibility；不在
  Issue #17 的自訂範圍內。

兩者使用不同 ID，保留既有快捷鍵 ID 與 `MOD_NOREPEAT`；預設組合的 native flags
仍為 `MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT`。Controller 一次逐項嘗試
註冊，重複呼叫不會重複註冊。`WM_HOTKEY` 只分派成功註冊的 ID。顯示文字直接由
結構化快捷鍵模型產生，不保存另一份 display string。

自訂組合必須至少有一個 modifier 與一個允許的主要按鍵。明確禁止 `Alt+F4`、
`Alt+Tab`、`Ctrl+Esc`、`Win+D`、`Win+E`、`Win+I`、`Win+L`、`Win+R`、`Win+Tab`。
Settings Save 先驗證及套用，相同組合為 no-op。替換不同組合時先 unregister 舊組合，
再 register 新組合；新註冊失敗會 rollback 舊組合，且 draft 不保存。rollback 也失敗
會記錄錯誤、保持 Dashboard visible + Interactive，並保留 system tray 控制入口。

啟動時使用保存組合；version 1 或缺少欄位使用預設 `Ctrl+Alt+Shift+R`。無效保存值
或保存組合無法註冊時回退預設；預設仍失敗不使 App crash，而以 Dashboard 訊息、
structured log 與 system tray 保留可靠控制方式。首次註冊失敗、startup fallback、
rollback failure 與 recovery 會記錄；正常 `WM_HOTKEY` 與每次按鍵事件不記錄。

其中一項失敗不阻止另一項、tray 或 App 啟動。Dashboard 顯示可理解的中文
fault；registration state 另保留 Win32 error code 供診斷，不直接把裸錯誤碼
當 UI 訊息。Dispose 只解除成功註冊的項目；解除失敗不拋出或靜默遺失，
而會留在該項 registration fault state。

Register／Unregister、tray initialization與 Explorer recovery failure會以 operation、
hotkey ID、native error code／HRESULT及 requested／applied／fault context寫入 structured
log。相同持續 fault只記第一次與 recovery；不得逐 tray animation frame記錄。UI仍只
顯示使用者可理解且可處理的訊息，不顯示 raw Win32 diagnostic。

Windows 全域快捷鍵仍受前景程式行為影響。部分遊戲會攔截或獨占輸入，因此即使
更換組合也不保證能在遊戲內觸發；system tray 是第二控制入口。

## Explorer recovery

啟動時以 `RegisterWindowMessage("TaskbarCreated")` 取得 Windows registered
message。共用 HWND hook 收到該訊息後，tray service 先依目前模式重套 shared
controller 的當前 animated frame 或 static icon，再以既有 `NotifyIcon` instance
重新加入 shell notification area 並刷新選單狀態；不重建 App、MainWindow、
ViewModel、tray service 或 animation controller，也不重新註冊 hotkey 或新增
timer。重複訊息安全，恢復失敗會保留上一個有效 icon，並將 diagnostic state
顯示於 Dashboard。

## 人工驗證清單

單元測試不能完整驗證 Windows shell、Explorer、全域鍵盤路由、焦點或原生視窗
生命週期。需在 Windows 10／11 x64 人工確認：

- tray icon 動畫流暢度、與 Dashboard 同幀及 CPU 速度同步、右鍵順序與文字，
  以及左鍵單擊無作用、雙擊切換。
- animated/static 可反覆切換；重新啟動回到 animated，且不建立設定檔變更。
- Window Close 只隱藏；tray `退出` 才結束程序，且圖示不殘留。
- 自訂 Overlay 快捷鍵／固定 D 在一般桌面雙向切換，按住時 `MOD_NOREPEAT` 生效。
- A-Z、0-9、F1-F12 與四種 modifier 的 UI 顯示、保存值及實際註冊一致；禁止的
  系統組合不可保存。
- 相同組合 Save 不重新註冊；新組合衝突時 rollback 舊組合；新舊組合都失敗時
  Dashboard 可見且為 Interactive，system tray 仍可控制。
- 以其他程式占用自訂組合或 D 時，App 仍啟動、另一快捷鍵與 tray 仍可用，診斷清楚。
- 結束 Explorer 後重新啟動，tray icon 恢復；重複 Explorer recovery 不產生重複圖示。
- user hidden／user visible 與 fullscreen 進出組合符合 visibility precedence，
  且各種 hide 狀態下 tray animation 仍持續。
- Hide／Show 不重建 Window 或 ViewModel，metrics 與 shared animation 保持執行。
- 模擬單幀更新失敗與整套 animation resource 失敗時，前一 icon／static fallback
  與 Dashboard diagnostic 符合設計，tray item 不消失。
- 重複退出／重新啟動不殘留 hotkey；unregister 或 tray recovery 故障可診斷。
- Topmost、click-through、no-activate、DPI、多螢幕、desktop shell 排除與
  single-instance 行為沒有退化。
- 在會攔截輸入的遊戲中確認快捷鍵限制，並確認 system tray 仍是可用的第二入口。
