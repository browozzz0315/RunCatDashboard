# Windows Overlay 正式決策（Issue #7）

## 1. 決策摘要

Issue #6 已證明單一 WPF `MainWindow` 可在不重建視窗、不停止 Dashboard 採樣的前提下，原地切換 `Interactive` 與 `ClickThrough`。Issue #7 將該研究收斂為正式 Overlay 視窗模式：

- 正式預設為 `ClickThrough`。
- `Ctrl + Alt + Shift + R` 是固定且位於 Overlay 外部的主要模式切換入口。
- WPF 管理固定視窗外觀與呈現；Win32 controller 管理動態 extended styles、全域快捷鍵及 monitor work area。
- 不加入系統匣、設定保存、開機啟動、全螢幕偵測、跑貓動畫或快捷鍵設定 UI。

## 2. 正式模式與 extended styles

正式生命週期使用下列單一規則：

```text
Interactive
  └─ 保持 WS_EX_TOOLWINDOW
  └─ 移除 WS_EX_TRANSPARENT | WS_EX_NOACTIVATE

ClickThrough
  └─ 保持 WS_EX_TOOLWINDOW
  └─ 加入 WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
```

Controller 每次先讀取完整的 `GWL_EXSTYLE`，只改變本元件管理的 bits，因此不會移除 WPF 或其他元件設定的 unrelated extended styles。寫入後以 `SetWindowPos` 搭配 `SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE` 刷新 frame，不接管 WPF 的 topmost Z-order。

`RequestedMode` 在 controller 建立時即為 `ClickThrough`。HWND 尚不存在時沒有可確認的 native applied mode，因此 `AppliedMode` 是 `null`；`OnSourceInitialized` 成功後立即套用 `ClickThrough`，再將 confirmed state 同步到 ViewModel。相同 applied mode 的重複要求不讀寫 native style。

## 3. WPF 與 Win32 責任界線

| 需求 | 責任方 | 正式作法 |
|---|---|---|
| 永遠置頂 | WPF | `Topmost=true` |
| 不顯示於工作列 | WPF | `ShowInTaskbar=false` |
| 無邊框、透明 Window | WPF | `WindowStyle=None`、`AllowsTransparency=true`、透明背景 |
| 啟動不搶焦點 | WPF | `ShowActivated=false`，且不呼叫 `Activate`、`Focus` 或 foreground API |
| 排除一般 Alt+Tab | Win32 + WPF | controller 常駐 `WS_EX_TOOLWINDOW`，並保留 `ShowInTaskbar=false` |
| 穿透與 no-activate | Win32 | 動態管理 `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` |
| 全域快捷鍵 | Win32 service | `RegisterHotKey`／`UnregisterHotKey` |
| 快捷鍵訊息 | View lifecycle + 純邏輯 handler | 一個 `HwndSourceHook` 只辨識目標 `WM_HOTKEY` |
| 工作區域 | Win32 service + View lifecycle | monitor work area pixels 轉為 WPF DIPs，再由純函式 clamp |
| 顯示狀態 | ViewModel | 顯示 applied mode、固定快捷鍵及可診斷錯誤，不接觸 HWND/P/Invoke |

## 4. 全域快捷鍵與 message hook

固定快捷鍵為：

```text
Ctrl + Alt + Shift + R
```

註冊使用 `MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT` 與虛擬鍵 `R`。`MOD_NOREPEAT` 避免按住按鍵時因自動重複而快速來回切換。本輪只註冊這一組固定快捷鍵，不建立 polling thread。

生命週期如下：

1. `OnSourceInitialized` 取得有效 HWND。
2. 從 HWND 取得 `HwndSource`，一次性呼叫 `AddHook`。
3. 使用相同 HWND 呼叫 `RegisterHotKey`，先確保外部回復入口可用。
4. Controller 隨即將正式預設套用為 `ClickThrough`。
5. Hook 收到訊息時只將目標 `WM_HOTKEY` 交給 `OverlayHotKeyMessageHandler`；非目標訊息不處理、不標記 handled，也不改寫結果。
6. 純邏輯 handler 將目標事件轉交給 `OverlayModeCoordinator`，再把 confirmed controller state 交給 ViewModel 顯示。
7. unmanaged message callback 會攔住例外並轉成 UI 可見錯誤，不讓例外穿出訊息邊界。
8. 關閉時先 `UnregisterHotKey`，再 `RemoveHook`，之後才關閉 Overlay controller、清除 HWND 並停止 ViewModel 採樣。

重複註冊不會再次呼叫 native API；重複解除註冊安全且不會再次呼叫 native API。若快捷鍵註冊失敗，錯誤包含固定鍵位、Win32 error code 與 native message。Controller 不會開始套用 ClickThrough，視窗保持 WPF 原生 Interactive 狀態並顯示一般關閉按鈕，避免留下無法用滑鼠操作且沒有外部回復入口的視窗。正常路徑的註冊與 ClickThrough 套用都在同一次 `OnSourceInitialized` callback 內同步完成，不建立背景輪詢或可見的第二階段視窗。

### 已驗證的快捷鍵與遊戲相容限制

`RegisterHotKey` 在一般桌面與部分遊戲環境可正常運作；目前已確認一般桌面及新楓之谷可觸發固定快捷鍵。但固定快捷鍵不能宣稱在所有遊戲環境都可靠：部分遊戲取得輸入後，可能不再將該組合交給 Windows hotkey registration。英雄聯盟的無邊框／視窗模式已觀察到遊戲取得輸入後快捷鍵可能暫時無法觸發，Overlay 重新取得焦點後才再次可用。

本 Issue 不改用 low-level keyboard hook、Raw Input、遊戲 hook、注入，也不要求使用者以管理員權限執行。這些方案會擴大安全性、相容性、權限與生命週期風險，不屬於輕量 Overlay 的本輪範圍。

獨佔全螢幕可能繞過或覆蓋一般 desktop composition；英雄聯盟獨佔全螢幕已確認 Overlay 不可見且固定快捷鍵無效。無邊框全螢幕通常仍由桌面合成，因此 Overlay 通常可見，但也可能遮擋遊戲內容。本輪不加入全螢幕偵測或自動顯示政策。

## 5. Mode、錯誤與 fault state

最小正式狀態為：

- `RequestedMode`：最近一次有效要求。
- `AppliedMode`：最近一次已由 native style set 與 frame refresh 確認的模式；未知或未初始化時為 `null`。
- `IsFaulted`：style 更新失敗且 rollback 也失敗，native style 已不可可靠判定。
- `LastError`：包含操作名稱與 Win32 錯誤資訊的使用者可診斷訊息。

Native style set 失敗時不更新 `AppliedMode`。Frame refresh 失敗時會寫回舊 style 並再次 refresh；rollback 成功則保留舊 `AppliedMode` 並回報可重試錯誤。Rollback 失敗時將 `AppliedMode` 清為未知、設定 `IsFaulted=true`，後續一般模式操作會被拒絕，不能再假裝 native 狀態正常。

模式切換只經過 windowing controller／coordinator，不呼叫 metrics service、`Start()` 或 `StopAsync()`，因此不建立、停止或重啟 Dashboard 採樣迴圈。

## 6. 已移除的研究行為

正式 App 已移除：

- 10 秒自動回復 `DispatcherTimer`。
- 「研究安全機制」與研究限制文案。
- `Enable click-through` 實驗按鈕。
- `Close experiment`、`Overlay behavior research` 等研究命名。
- 依賴 Overlay 自身滑鼠輸入的返回互動控制。
- Esc 回復 handler；Esc 在穿透且未保有焦點時不可靠，因此不作為正式或暗示性的安全入口。
- ViewModel 中僅為研究按鈕存在的模式命令。

Interactive 模式仍可拖曳並顯示一般 `Close` 按鈕；ClickThrough 模式的正式控制入口是全域快捷鍵。

## 7. DPI、多螢幕與位置安全範圍

本輪不保存位置。初次啟動使用 WPF `SystemParameters.WorkArea` 提供的主工作區 DIPs，將視窗放在工作區右上並以純 DIPs clamp，沒有寫死螢幕解析度。

互動拖曳完成及顯示配置變更後：

1. `MonitorFromWindow(MONITOR_DEFAULTTONEAREST)` 找到目前或最近的有效 monitor。
2. `GetMonitorInfo` 取得以實體 pixels 表示的 work area，支援負座標。
3. `GetDpiForWindow` 提供目前 HWND 的 DPI scale，將 work area 明確換算成 WPF DIPs。
4. 純函式 clamp 將一般大小視窗完整限制在 work area；若視窗比 work area 大，則貼齊工作區左上，確保仍有可見內容。
5. `SystemEvents.DisplaySettingsChanged` 觸發重新 clamp；handler 在關閉時解除，不留下 static event subscription。

這是最小安全方案，不自行處理 `WM_DPICHANGED`，也不覆寫 WPF 的 per-monitor DPI scaling。100%、125%、150%、mixed-DPI、負座標、螢幕移除、旋轉與 remote desktop reconnect 仍須實機驗證。若後續發現 WPF logical desktop 與 mixed-DPI physical origin 的轉換誤差，再建立專用 positioning service；位置格式與 monitor identity 應和未來設定保存 Issue 一起定義。

## 8. 半透明與效能限制

本輪繼續使用 `AllowsTransparency=true`，因為目前需要透明 Window canvas 與半透明面板。沒有加入 blur、Mica、Acrylic、動畫、高頻 render loop 或每秒效能 log。

Layered WPF window 可能增加 CPU/GPU composition 成本；目前只以小面積固定內容降低風險，不能據此宣稱長時間資源已最佳化。後續仍須量測：

- collapsed／expanded 平均 CPU。
- working set 長時間趨勢。
- GPU composition 成本。
- 睡眠喚醒、鎖定解鎖與長時間執行後是否成長。

若量測顯示 `AllowsTransparency=true` 無法達成專案工程目標，後續應評估一般無邊框矩形視窗，但不在 Issue #7 變更 rendering 架構。

目前長時間效能仍在人工觀察階段，不宣稱已完成長時間穩定性驗證。

## 9. 色彩與後續主題

面板背景、邊框、主要文字、次要文字、警告、錯誤、模式標籤及歷史區背景已集中為 Application brush resources，維持目前深色外觀。未加入主題設定 UI，也不保存主題選擇。

後續 Light／Dark／System 主題應替換 resource dictionary，而不是再把色碼散落回 control；完整主題切換仍屬後續 Issue。

## 10. 自動化與人工驗證邊界

不建立真正 WPF Window 的測試涵蓋 controller state transition、native failure／rollback／fault、hotkey registration、message filtering、coordinator toggle、採樣生命週期不變及 work-area clamp。這些測試不能證明真實 Windows 的 topmost、Alt+Tab、焦點、跨 process 滑鼠穿透、hotkey 衝突、DPI、monitor 拔除、透明 rendering 或資源目標。

正式完成仍須在 Windows 10／11 x64 人工驗證啟動焦點、工作列／Alt+Tab、不同 process 穿透、快捷鍵雙向切換、Interactive 拖曳、採樣連續性、DPI／多螢幕、關閉後重新註冊及程序正常結束。

已完成的人工驗證包括 1920×1080、100% 縮放下的預設 ClickThrough、快捷鍵雙向與快速切換、狀態同步、Close、Console 無未處理例外，以及虛擬桌面切換後維持相同位置。這些結果不取代其他 DPI、多螢幕、Windows 版本與長時間執行驗證。

## 11. 後續需求

下列需求已記錄，但不在 Issue #7 本輪實作：

- 可自訂快捷鍵。
- 系統匣作為第二控制入口。
- `Always visible`／`Hide over fullscreen apps`／`Not topmost` 顯示政策。
- `Compact`／`Standard`／`Expanded` 尺寸。
- 可選顯示欄位。

這些項目需要各自定義設定、生命週期與驗收標準，不應在正式 Overlay 收斂中順帶加入。

全螢幕顯示政策已由 Issue #16 正式定義；後續規則以
[`OVERLAY_FULLSCREEN_POLICY.md`](OVERLAY_FULLSCREEN_POLICY.md) 為準。

## 12. API 契約參考

- Extended Window Styles: <https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles>
- `RegisterHotKey`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey>
- `UnregisterHotKey`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-unregisterhotkey>
- `WM_HOTKEY`: <https://learn.microsoft.com/windows/win32/inputdev/wm-hotkey>
- `MonitorFromWindow`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfromwindow>
- `GetMonitorInfo`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmonitorinfow>
- `GetDpiForWindow`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getdpiforwindow>
- WPF `HwndSource.AddHook`: <https://learn.microsoft.com/dotnet/api/system.windows.interop.hwndsource.addhook>
- WPF `Window.ShowActivated`: <https://learn.microsoft.com/dotnet/api/system.windows.window.showactivated>
- WPF `Window.AllowsTransparency`: <https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency>

焦點、滑鼠穿透、Alt+Tab、DPI、多螢幕與 rendering 成本仍以目標 Windows 實機結果為最終依據。
