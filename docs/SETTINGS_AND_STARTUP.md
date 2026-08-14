# 設定保存、雙快捷鍵與 Windows 登入啟動

## Schema 與儲存位置

設定檔固定為 `%LocalAppData%\RunCatDashboard\settings.json`，目前 schema
version 為 `5`：

```json
{
  "version": 5,
  "window": {
    "left": -420.5,
    "top": 18.25,
    "isDashboardVisible": true,
    "visibilityHotKey": {
      "control": true,
      "alt": true,
      "shift": true,
      "windows": false,
      "key": "D"
    }
  },
  "overlay": {
    "interactionMode": "ClickThrough",
    "interactionHotKey": {
      "control": true,
      "alt": true,
      "shift": true,
      "windows": false,
      "key": "R"
    },
    "sizeMode": "Standard",
    "fields": {
      "showCpu": true,
      "showMemory": true,
      "showUsedAndTotalMemory": true,
      "showLastUpdated": true,
      "showSamplingStatus": true,
      "showRecentCpuHistory": false,
      "showInteractionMode": true,
      "showHotKeyHints": false
    }
  },
  "metrics": {
    "samplingIntervalMilliseconds": 1000
  },
  "startup": {
    "runAtLoginRequested": false
  },
  "appearance": {
    "themePreference": "System"
  }
}
```

`left`／`top` 是 WPF device-independent units，必須成對、finite；允許負座標。
不保存 Width／Height；實際 Width、cat viewport、padding 與 MaxHeight 由 `sizeMode`
對應的固定 profile 衍生，Height 由目前可見內容決定。沒有有效位置時使用既有
primary work area 右上角預設位置。
`isDashboardVisible` 是 user-requested visibility，不是 fullscreen 暫時隱藏後的
`Window.IsVisible`。`interactionMode` 同樣是 requested mode，不保存 native applied
mode。`interactionHotKey` 與 `visibilityHotKey` 都結構化保存 modifier 與主要按鍵，
不以顯示字串作為持久化格式。`fields` 只保存目前一組欄位狀態，不保存 per-mode
設定。未知 JSON 欄位會忽略；不預建其他 Issue 的 section。

預設值為 Dashboard visible、`ClickThrough`、Overlay 模式快捷鍵
`Ctrl+Alt+Shift+R`、Dashboard 顯示／隱藏快捷鍵 `Ctrl+Alt+Shift+D`、1000ms、
Windows 登入啟動關閉、`Standard` 與其預設欄位組合。
sampling interval 僅接受 250、500、1000、2000、5000ms，非法值直接回 1000ms，
不做 clamp。非法 interaction mode 回 `ClickThrough`；無效或不成對的位置回未設定。
schema version 1、2、3 與 4 仍可讀取：所有既有 window、interaction mode、hotkey、
metrics 與 startup 值均保留，缺少的快捷鍵補各自預設值，presentation migration 為
`Standard` defaults；缺少或未知的 `appearance.themePreference` 使用 `System`，下一次保存時寫為 version 5。
未知 `sizeMode` 回退 `Standard`；缺少 `fields` 時使用該 mode defaults。CatOnly 一律
normalize 為全部欄位關閉。損壞 JSON 中非 CatOnly 的 CPU／Memory 皆關閉時回退該
mode defaults；Settings Window draft 則明確拒絕保存，不靜默改值。
無效或缺少的單一快捷鍵只回退該組預設；合法自訂值不會被 normalize 覆蓋。
快捷鍵 JSON 只保存 `control`、`alt`、`shift`、`windows` 與 `key`；`DisplayText`
及 `UsageWarning` 是 runtime 衍生值，不屬於持久化契約。舊 version 3 檔案若包含
`displayText` 或 `usageWarning`，載入時會安全忽略。

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
Settings Window 的交易保存會在取得 write gate 後，以最新 current snapshot 建立
candidate 並先寫入 temporary file。commit 前若 revision 已改變，便捨棄該 temporary
file，以最新 snapshot 重新 merge；只有 revision 穩定時才原子 commit 並發布 current。

## 啟動順序與 hidden startup

啟動順序為 single-instance ownership、建立正式 logger、建立 DI、載入/驗證 settings、reconcile
HKCU Run、套用 requested visibility/interaction、presentation 與 sampling interval、建立
MainWindow HWND、初始化 native styles/tray/hotkeys/sampling、restore/clamp 位置，
最後才依 requested visibility 與 fullscreen policy 決定是否 Show。

兩組快捷鍵各自註冊保存值。保存值無效或註冊失敗時，只嘗試該組預設 R 或 D；回退
成功後 runtime 與 settings current snapshot 改為該組實際有效值。任一預設仍失敗時
App 不崩潰，也不 unregister 或重新註冊另一組。Overlay 快捷鍵完全失效時 Dashboard
保持可見及 Interactive；Dashboard 快捷鍵完全失效時保留 system tray，hidden Dashboard
仍可由 tray 恢復。

上次 requested hidden 時使用 `EnsureHandle` 建立 HWND，不先 Show 再 Hide，因此
tray、hotkeys、sampling 與 shared tray animation 仍會初始化且不產生 Dashboard
閃爍。tray 或目前有效的 Dashboard hotkey 可再次要求顯示。

Fullscreen policy 只改 policy visibility input，不覆寫保存的 user-requested
visibility。使用者 hidden 後進出 fullscreen 仍 hidden；user visible 且 fullscreen
active 時只暫時 actual hidden，離開後才恢復。Fullscreen policy 仍不持久化，每次啟動
使用 `HideOverFullscreenApps`。Policy ComboBox 已移至 Settings Window，Save 成功後
才套用本次 runtime draft；MainWindow 不再顯示該控制項。

## Sampling 與視窗位置

sampling 保持單一 loop。interval 改變會以 bounded change signal 喚醒目前 delay，
相同值不重新排程；不 Stop/Start loop。Stop／Dispose 後 interval 修改只更新資料，
不會復活 loop。

MainWindow code-behind 只把 Left／Top 純資料交給 settings service。restore 期間關閉
LocationChanged 回寫，restore 後的移動與 display clamp 走 debounce；drag/clamp 完成
以及真正退出前都擷取最終位置。persisted 座標先套用，再用 HWND 所在 monitor 的
DPI-aware work area clamp。

Overlay 尺寸或可見欄位改變時不重建 MainWindow，也不停止或重啟 sampling／animation。
WPF 完成 content-driven layout 後，MainWindow lifecycle 使用新的
`ActualWidth`／`ActualHeight` 再次呼叫相同 work-area clamp。Clamp 純函式本身不變；
首次 restore 也會等到 layout 產生有限且大於零的實際尺寸後才 clamp。若內容超過
profile MaxHeight，ScrollViewer 保留滑鼠滾輪垂直捲動，但水平與垂直 scrollbar
固定不顯示。

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

每次開啟從 current settings 建立兩組快捷鍵、size mode、單一 fields 與 runtime
fullscreen policy draft。切換成另一個 size mode 時套用該 mode defaults；同 mode
重複指定不清除目前 draft，defaults 套用後仍可修改欄位。Settings Window 以最新成功套用值
作為 baseline，所有可編輯值以單一結構化 draft 比較 dirty state。Apply 只在 dirty 且非套用中
啟用；成功時保持視窗開啟、以實際 commit／merge 後的 current settings 更新 baseline 並清除
dirty 與舊錯誤。Save 與 Apply 共用相同驗證及 application pipeline；dirty Save 成功後關閉，
clean Save 不重複保存而直接關閉。Cancel 與標題列 X 只捨棄最新 baseline 後的 draft 並關閉，
不 rollback 先前成功的 Apply。套用中會停用三個動作並暫時阻止標題列關閉，避免重疊交易。
Apply／dirty Save 先驗證兩組快捷鍵及彼此不得相同，再要求 Windows-specific controller
分別套用。每組相同新舊值是
no-op；不同值只解除及註冊該組，新組合失敗時 rollback 該組原值且不更新 current 或
JSON。兩組 native 套用完成後，以 atomic write 保存完整 candidate；保存失敗時 rollback
已套用的 gesture，current snapshot、requested visibility、interaction mode、sampling 與
startup 與 presentation state 都不發布新值。Presentation 只在 persistence commit 成功後
透過 UI dispatcher 套用。Dashboard rollback 失敗保留 system tray；Overlay rollback
失敗另保持 visible + Interactive。MainWindow 位置以及 tray/hotkey 引起的
visibility/interaction 變更仍自動保存，不等待 Settings Window 儲存。

App 使用 `OnExplicitShutdown`。tray Exit 先做冪等 BeginExit，擷取最後位置並 flush，
關閉 Settings Window，再關閉 MainWindow；MainWindow cleanup 解除 hotkeys/message
hook、dispose tray/animation/metrics/native controllers，接著以最多 2 秒 flush 日誌，最後才呼叫
`Application.Shutdown()`，DI disposal 作為冪等的最後清理。

Logging policy 不寫入設定檔、不新增 Logging Settings UI。Data 與
Logs 路徑由同一 `ApplicationPaths` 提供；詳細規則見
[`DIAGNOSTIC_LOGGING.md`](DIAGNOSTIC_LOGGING.md)。

## 人工驗證

單元測試不證明真實 HKCU、Windows shell、DPI、多螢幕、焦點或畫面無閃爍。發布前
需人工驗證 hidden cold start、四種 size mode、欄位自然收縮、CatOnly 操作、尺寸變更後
負座標／多螢幕 clamp、Settings Window activation、
Registry command、Explorer recovery、hotkey conflict、fullscreen precedence、
Windows 登出/重啟與 tray Exit cleanup。

另需人工確認 A-Z、0-9、F1-F12 選擇與 Ctrl／Alt／Shift／Windows modifier 顯示一致，
禁止組合無法保存，衝突後 rollback 可恢復原快捷鍵，以及 rollback failure 的安全狀態。
部分遊戲會攔截或獨占鍵盤輸入；即使更換組合，全域快捷鍵也不保證在遊戲中有效，
此時應使用 system tray 控制。
