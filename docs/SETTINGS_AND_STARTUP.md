# 設定保存、Overlay 快捷鍵與 Windows 登入啟動

## Schema 與儲存位置

設定檔固定為 `%LocalAppData%\RunCatDashboard\settings.json`，目前 schema
version 為 `2`：

```json
{
  "version": 2,
  "window": {
    "left": -420.5,
    "top": 18.25,
    "isDashboardVisible": true
  },
  "overlay": {
    "interactionMode": "ClickThrough",
    "interactionHotKey": {
      "control": true,
      "alt": true,
      "shift": true,
      "windows": false,
      "key": "R"
    }
  },
  "metrics": {
    "samplingIntervalMilliseconds": 1000
  },
  "startup": {
    "runAtLoginRequested": false
  }
}
```

`left`／`top` 是 WPF device-independent units，必須成對、finite；允許負座標。
不保存 Width／Height。沒有有效位置時使用既有 primary work area 右上角預設位置。
`isDashboardVisible` 是 user-requested visibility，不是 fullscreen 暫時隱藏後的
`Window.IsVisible`。`interactionMode` 同樣是 requested mode，不保存 native applied
mode。`interactionHotKey` 結構化保存 modifier 與主要按鍵，不以顯示字串作為
持久化格式。未知 JSON 欄位會忽略；不預建其他 Issue 的 section。

預設值為 Dashboard visible、`ClickThrough`、Overlay 模式快捷鍵
`Ctrl+Alt+Shift+R`、1000ms、Windows 登入啟動關閉。
sampling interval 僅接受 250、500、1000、2000、5000ms，非法值直接回 1000ms，
不做 clamp。非法 interaction mode 回 `ClickThrough`；無效或不成對的位置回未設定。
schema version 1 仍可讀取：所有既有 window、overlay mode、metrics 與 startup 值均
保留，缺少的 `interactionHotKey` 補預設值，下一次保存時寫為 version 2。無效或缺少
快捷鍵也回預設值，不會使其他有效設定遺失。

## Load、fallback 與原子寫入

Settings 在建立 MainWindow 及第一次顯示以前載入、驗證。Malformed JSON 會移到
`settings.corrupt-<timestamp>.json`；不支援的 version 會移到
`settings.unsupported-v<version>-<timestamp>.json`，兩者都以 defaults 啟動，且不
直接覆寫原檔。兩類備份合計只保留最近 3 份。

每次保存先在設定檔同目錄建立唯一 temporary file，完成 JSON serialization、
flush 與 disk flush 後，再以 replace（既有檔）或 move（首次建立）切換成正式檔。
失敗會清理本次 temporary file、保留舊設定檔並發布 diagnostic，不使 App crash。
啟動載入時也會清理先前異常中止留下的同名 temporary files。

所有更新由 settings service 的單一 write gate 序列化。一般變更採 500ms trailing
debounce；真正退出前取消待執行 debounce，並以 `FlushAsync` 保存最新 snapshot。

## 啟動順序與 hidden startup

啟動順序為 single-instance ownership、建立正式 logger、建立 DI、載入/驗證 settings、reconcile
HKCU Run、套用 requested visibility/interaction 與 sampling interval、建立
MainWindow HWND、初始化 native styles/tray/hotkeys/sampling、restore/clamp 位置，
最後才依 requested visibility 與 fullscreen policy 決定是否 Show。

Overlay 模式快捷鍵註冊使用保存值。保存值無效時先使用預設值；有效但註冊失敗時
也嘗試 `Ctrl+Alt+Shift+R`。回退成功後 runtime 與 settings current snapshot 都改為
實際有效的預設組合。預設值仍註冊失敗時 App 不崩潰，Dashboard 強制保持可見及
Interactive，並保留 system tray 作為第二控制入口。Dashboard 顯示／隱藏快捷鍵
固定為 `Ctrl+Alt+Shift+D`，不屬於可自訂範圍。

上次 requested hidden 時使用 `EnsureHandle` 建立 HWND，不先 Show 再 Hide，因此
tray、hotkeys、sampling 與 shared tray animation 仍會初始化且不產生 Dashboard
閃爍。tray 或 D hotkey 可再次要求顯示。

Fullscreen policy 只改 policy visibility input，不覆寫保存的 user-requested
visibility。使用者 hidden 後進出 fullscreen 仍 hidden；user visible 且 fullscreen
active 時只暫時 actual hidden，離開後才恢復。Fullscreen policy 本輪仍不持久化，
每次啟動仍使用既有預設 `HideOverFullscreenApps`。

## Sampling 與視窗位置

sampling 保持單一 loop。interval 改變會以 bounded change signal 喚醒目前 delay，
相同值不重新排程；不 Stop/Start loop。Stop／Dispose 後 interval 修改只更新資料，
不會復活 loop。

MainWindow code-behind 只把 Left／Top 純資料交給 settings service。restore 期間關閉
LocationChanged 回寫，restore 後的移動與 display clamp 走 debounce；drag/clamp 完成
以及真正退出前都擷取最終位置。persisted 座標先套用，再用 HWND 所在 monitor 的
DPI-aware work area clamp。

## Windows 登入啟動

Registry adapter 只操作：

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

value name 固定為 `RunCatDashboard`。JSON 保存 requested state；重新讀取 Registry
確認 applied state，兩者與 fault 分開保存。requested true 時建立或更新成正確
quote 的完整 `RunCatDashboard.exe` 路徑；process path 不是該 exe 時不寫入。
requested false 時只刪除 `RunCatDashboard` value，不修改其他 Run values。不使用
HKLM、Scheduled Task、Startup folder，也不需要系統管理員權限。

## Settings Window 與關閉流程

tray 選單的「設定...」開啟單一 Settings Window；重複要求會 restore minimized
window 並 Activate，Closed 後可重新建立。它不會成為 `Application.MainWindow`，
Dashboard hidden 時也能開啟，關閉它不會退出 App。

每次開啟從 current settings 建立 draft。取消只關閉且不套用快捷鍵；儲存先驗證
Overlay 快捷鍵並要求 Windows-specific controller 套用，成功後才更新 requested
Dashboard visibility、interaction mode、sampling interval，reconcile HKCU Run，再
flush JSON。相同快捷鍵是 no-op，不重複 unregister/register。新組合會先解除舊組合
再註冊；新註冊失敗時 rollback 舊組合且不保存 draft。rollback 也失敗時不保存，
Dashboard 強制保持可見及 Interactive，system tray 保持可用，Settings Window 顯示
可理解錯誤。MainWindow 位置以及 tray/hotkey 引起的 visibility/interaction 變更仍
自動保存，不等待 Settings Window 儲存。

App 使用 `OnExplicitShutdown`。tray Exit 先做冪等 BeginExit，擷取最後位置並 flush，
關閉 Settings Window，再關閉 MainWindow；MainWindow cleanup 解除 hotkeys/message
hook、dispose tray/animation/metrics/native controllers，接著以最多 2 秒 flush 日誌，最後才呼叫
`Application.Shutdown()`，DI disposal 作為冪等的最後清理。

Logging policy 不寫入設定檔、不新增 Logging Settings UI。Data 與
Logs 路徑由同一 `ApplicationPaths` 提供；詳細規則見
[`DIAGNOSTIC_LOGGING.md`](DIAGNOSTIC_LOGGING.md)。

## 人工驗證

單元測試不證明真實 HKCU、Windows shell、DPI、多螢幕、焦點或畫面無閃爍。發布前
需人工驗證 hidden cold start、負座標/拔除螢幕後 clamp、Settings Window activation、
Registry command、Explorer recovery、hotkey conflict、fullscreen precedence、
Windows 登出/重啟與 tray Exit cleanup。

另需人工確認 A-Z、0-9、F1-F12 選擇與 Ctrl／Alt／Shift／Windows modifier 顯示一致，
禁止組合無法保存，衝突後 rollback 可恢復原快捷鍵，以及 rollback failure 的安全狀態。
部分遊戲會攔截或獨占鍵盤輸入；即使更換組合，全域快捷鍵也不保證在遊戲中有效，
此時應使用 system tray 控制。
