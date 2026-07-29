# 正式診斷日誌（Issue #28）

## 技術與責任邊界

RunCatDashboard 透過 `Microsoft.Extensions.Logging` abstraction 發布 structured
events，並以 NLog Microsoft logging integration 寫入本機 rolling file。V1 不使用
Generic Host、telemetry、遠端上傳、log viewer 或 Logging Settings UI，也不自行實作
完整 file logger。

Logger 只在 session-local single-instance ownership 成功後建立；同一 Windows
session 的第二個 instance 不開啟 log file。Logger 在 DI 建立前完成初始化，因此可涵蓋
DI、settings、Run-at-login、ViewModel、MainWindow、HWND 與 native initialization。

## 路徑與檔案政策

所有應用程式資料路徑由 `ApplicationPaths` 集中提供：

- Data：`%LocalAppData%\RunCatDashboard`
- Logs：`%LocalAppData%\RunCatDashboard\Logs`

日誌不得寫入 repository、執行目錄或安裝目錄。固定預設為：

- UTF-8 without BOM
- 每日 rolling
- 單檔上限 5 MiB
- 保留 7 天
- active file在內最多 14 個檔案
- 同日超過大小時使用 sequence
- active filename 包含 Windows session ID，避免同一使用者跨登入 session 共用 active file

## 等級與短期診斷

等級由單一 `LoggingPolicy` 決定，功能類別不得散落 `#if DEBUG`：

- Development／Debug：`RunCatDashboard` categories 從 `Debug` 開始；
  `Microsoft`／`System` 從 `Warning` 開始；`Trace` 預設關閉。
- Release：startup／shutdown lifecycle 從 `Information` 開始；其他
  `RunCatDashboard`、`Microsoft` 與 `System` categories 從 `Warning` 開始；不輸出
  `Debug`／`Trace`。

可用以下單次程序參數暫時啟用 Trace，不會保存至 settings：

```powershell
dotnet run --project ".\src\RunCatDashboard.App" -- --log-level Trace
dotnet run --project ".\src\RunCatDashboard.App" -- --log-level Trace --enable-high-frequency-trace
```

兩個參數刻意分開。只使用 `--log-level Trace` 不會開啟 polling 類高頻 category。
兩者同時使用時可短期記錄 fullscreen reconciliation observation；即使明確啟用
high-frequency Trace，也不得逐 animation frame 記錄。

## 事件、結構與節流

事件使用 message template 及適用的 structured fields，例如 `Operation`、
`Subsystem`、`RequestedState`、`AppliedState`、`FaultState`、`NativeErrorCode`、
`HResult`、`SettingsVersion`、`FullscreenPolicy`、`HotKeyId`、`WindowsSessionId`
與 `ApplicationVersion`。Error／Critical 包含 exception；Win32 failure 在可取得時
保留 native error code、HRESULT 與 requested／applied／fault context。

正常等級只記 lifecycle、semantic state transition、fault episode 的第一次 failure
與 recovery。不得逐筆記錄 CPU／Memory sample、CPU history、animation frame、
animation interval calculation、fullscreen polling、tray frame assignment、
`LocationChanged`、UI binding update或未變更 reconciliation。

Fullscreen、sampling 及會持續重試的 subsystem 使用 episode tracking；相同 fault
持續存在時不重複寫入。Logging failure 不得改變既有 requested／applied／fault state。

Global hotkey 記錄首次 registration failure、runtime replacement rollback failure、
startup fallback 與 fault recovery；相同持續 fault 仍依 episode 節流。不得記錄每次
正常 `WM_HOTKEY` 或按鍵事件。使用者介面只顯示可處理的中文訊息，native error code
與 HRESULT 僅進 structured log。

## Startup、fallback 與 shutdown

File logger 建立失敗時改用 `NullLoggerFactory`，App 核心功能盡可能繼續。失敗只經
獨立 one-shot self-diagnostic 發布，不用失敗 logger 記錄自身錯誤、不遞迴，也不反覆
顯示 UI。真正 startup failure 仍記 `Critical` 並保留使用者提示。

Explicit shutdown 順序固定為：BeginExit、擷取位置、flush settings、關閉 Settings
Window、關閉 MainWindow及其 hotkey／tray／native lifecycle、最多 2 秒 logging
flush、`Application.Shutdown()`。`OnExit` 再冪等 final flush／dispose。Logging failure
不得阻止退出；退出完成後 log handle 必須釋放。

## Overlay 與使用者訊息

Overlay 不顯示三筆 CPU 平均、`ms/frame`、Visible／Topmost raw state、fullscreen
detection detail、foreground HWND／class／bounds、monitor、position／DPI／work-area／
clamp或 requested／applied native raw diagnostics。詳細 operation、exception 與 native
code 只進 structured log。

跑貓動畫、正式 metrics、CPU history、fullscreen policy selector、interaction badge、
sampling status/failure，以及使用者可理解且需要處理的 Warning／Error仍保留。Overlay
頂部以跑貓動畫為第一個內容，並在同一 row 顯示 interaction badge；不顯示跑貓標題、
應用程式標題、拖曳說明或快捷鍵組合提示。
本 Issue 不重做 Issue #18 正式版面。

## 隱私與 settings

不得記錄 Password、Token、Secret、API Key、Cookie、完整 settings JSON、剪貼簿、
未清理長使用者輸入或不必要的完整使用者資料夾路徑。路徑優先記檔名、相對路徑或資料
目錄類型。日誌只保留在本機，不上傳。

Logging policy 不持久化。`settings.json` schema version 維持 `1`，不新增 logging
section或 Logging Settings UI。
