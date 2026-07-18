# Windows Overlay 視窗行為研究

## 1. 研究目標與範圍

本研究以單一 WPF `MainWindow` 建立最小可執行實驗，釐清 WPF 與 Win32 對下列行為的責任界線：

- 永遠置頂、不顯示於工作列、無邊框與半透明背景。
- 啟動時不主動取得焦點。
- `Interactive` 與 `ClickThrough` 模式的原地切換。
- native handle、extended window styles 與視窗關閉的生命週期。
- DPI、多螢幕及全螢幕應用程式的限制與後續風險。

本輪保留既有 CPU／記憶體 Dashboard 與採樣生命週期，但不包含跑貓動畫、系統匣、全域快捷鍵、設定保存、開機啟動、完整螢幕配置、全螢幕偵測或正式視覺設計。

## 2. 修改前狀態

修改前的 `App.OnStartup` 從 DI 取得 singleton `MainWindow`，指定 `Application.MainWindow` 後呼叫一次 `Show()`。`MainWindow` 建構子設定 `DataContext` 並註冊 `Loaded`；`Loaded` 啟動 `MainWindowViewModel` 的採樣迴圈，`Closed` 則同步等待 ViewModel 完成非同步處置。ViewModel 的 `Start()`、`StopAsync()` 與 `DisposeAsync()` 已有重複呼叫防護及 cancellation 流程。

修改前沒有 `SourceInitialized`、HWND、extended window styles 或 overlay interaction mode。XAML 是一般有框線、會顯示於工作列的 Dashboard 視窗。

## 3. 採用方案

本輪採用一個 `MainWindow`、一個 `OverlayWindowController` 與兩個明確模式：

```text
Interactive
  └─ 加入 WS_EX_TOOLWINDOW
  └─ 移除 WS_EX_TRANSPARENT | WS_EX_NOACTIVATE

ClickThrough
  └─ 加入 WS_EX_TOOLWINDOW
  └─ 加入 WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
```

WPF 宣告固定的外觀與視窗層級屬性。`MainWindow.OnSourceInitialized` 只負責取得 HWND 並交給 controller；ViewModel 透過抽象切換模式，不接觸 P/Invoke。Controller 每次變更前讀取現有 `GWL_EXSTYLE`，只加入或移除自己管理的旗標，寫回後呼叫 `SetWindowPos` 刷新 frame。

初始模式是 `Interactive`，但 `ShowActivated=false` 使首次顯示不主動啟用視窗。使用者主動點擊互動模式視窗時可以取得焦點，以便操作控制項。切換為 `ClickThrough` 後加入 `WS_EX_NOACTIVATE`，避免該模式因滑鼠操作而成為前景視窗。

研究期間若進入穿透模式，會在 10 秒後自動回到互動模式；若視窗仍保有鍵盤焦點，也可按 Esc 提前回復。這只是研究安全措施，不是正式輸入設計。

## 4. WPF 與 Win32 的責任分工

| 需求 | 本輪責任方 | 採用方式 | 說明 |
|---|---|---|---|
| 永遠置頂 | WPF | `Topmost=true` | WPF 管理 topmost Z-order；不額外呼叫 `HWND_TOPMOST`。 |
| 不顯示於工作列 | WPF | `ShowInTaskbar=false` | 保留 WPF 對 owner/taskbar 呈現的管理。 |
| 無邊框 | WPF | `WindowStyle=None` | 也是 `AllowsTransparency=true` 的必要搭配。 |
| 半透明背景 | WPF | `AllowsTransparency=true`、透明 Window 背景、半透明內容 Border | 提供 per-pixel alpha，文字本身維持不透明。 |
| 固定研究尺寸 | WPF | `ResizeMode=NoResize` | 本輪不研究 resize hit testing。 |
| 首次顯示不啟用 | WPF | `ShowActivated=false` | 僅處理首次顯示策略，不等同永久 no-activate。 |
| 滑鼠穿透 | Win32 | 動態 `WS_EX_TRANSPARENT` | 配合 WPF layered window，讓穿透模式不參與一般滑鼠命中。仍須實機驗證跨程式傳遞。 |
| 穿透時不啟用 | Win32 | 動態 `WS_EX_NOACTIVATE` | 只在 `ClickThrough` 存在，以免破壞互動模式。 |
| 工具視窗語意 | Win32 + WPF | 常駐 `WS_EX_TOOLWINDOW`，並保留 `ShowInTaskbar=false` | 穩定排除 Alt+Tab／一般應用程式視窗呈現；兩者目的重疊但不互相取代。 |

不使用 P/Invoke 重做 `Topmost`、`ShowInTaskbar` 或透明視覺，是為了避免同一狀態同時由 WPF 與自訂 native 程式碼競爭。

## 5. Extended styles 的實際作用與限制

### `WS_EX_TRANSPARENT` (`0x00000020`)

Win32 的一般定義包含同一 thread 之 sibling window 的繪製順序語意；對 layered window，Microsoft 的 layered window 說明另指出，具有此 style 時 layered window 的形狀會在 hit testing 中被忽略，滑鼠事件會傳給下方視窗。本輪的 `AllowsTransparency=true` 會建立 layered WPF window，因此採用此 style 做最小穿透實驗。

這個 style 不代表視窗內容的 alpha 值，也不應與 WPF `Background=Transparent` 混為一談。前者處理 native 行為，後者處理 WPF 視覺。是否在所有目標 Windows 10／11、WPF rendering 與下方程式組合都能可靠跨 process 穿透，只能實機驗證。

### `WS_EX_NOACTIVATE` (`0x08000000`)

此 style 防止 top-level window 因使用者點擊而成為 foreground window。本輪只在 `ClickThrough` 加入；若永久保留，互動模式的按鈕、鍵盤與拖曳操作會出現不合理限制。回到 `Interactive` 時與 `WS_EX_TRANSPARENT` 一起移除。

### `WS_EX_TOOLWINDOW` (`0x00000080`)

此 style 表示浮動工具視窗，通常不出現在工作列或 Alt+Tab。WPF 的 `ShowInTaskbar=false` 已負責工作列需求，而且 WPF 內部可能使用相關 extended style；controller 仍以 OR 的方式確保 `WS_EX_TOOLWINDOW` 存在，但不移除 WPF 已有的其他 styles。

本輪不加入 `WS_EX_LAYERED`。`AllowsTransparency=true` 的 layered window 建立與 rendering 由 WPF 負責，手動重複管理可能與 WPF 競爭。

## 6. 模式切換與狀態一致性

`OverlayInteractionMode` 只有 `Interactive` 與 `ClickThrough`。Controller 是模式的 native 真相來源，ViewModel 在成功後同步顯示狀態。

切換流程如下：

1. 確認 controller 未關閉且 HWND 已初始化。
2. 驗證 enum 值；相同模式立即回傳 `false`，不呼叫 native API。
3. `GetWindowLongPtr(GWL_EXSTYLE)` 讀取現有 styles。
4. 以 OR 加入本模式所需的 bits，以 AND-NOT 只移除本模式管理的 bits。
5. 若結果沒有變化，不寫入也不 refresh。
6. `SetWindowLongPtr(GWL_EXSTYLE)` 寫回完整結果。
7. `SetWindowPos` 使用 `SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED` 刷新 cached frame 資訊；`SWP_NOZORDER` 確保不破壞 WPF 的 topmost Z-order。
8. 全部成功後才提交新的 `Mode`。

若讀取或寫入失敗，controller 將 `Win32Exception` 包裝為含操作名稱的 `OverlayWindowException`。若寫入成功但 frame refresh 失敗，controller 會嘗試寫回舊 style 並再次 refresh。回復成功時，模式不提交；回復也失敗時，例外明確指出 native style 已無法判定，不會默默顯示成已成功。

模式切換不呼叫 ViewModel 的 `Start()`、`StopAsync()` 或任何 metrics service，因此不會建立、停止或重啟採樣迴圈。

## 7. Native handle 生命週期

### 採用 `SourceInitialized` 的原因

- 建構子及 `InitializeComponent()` 期間 HWND 可能尚未建立，不能安全套用 style。
- `SourceInitialized` 發生於 HWND source 建立完成後，早於一般 `Loaded` UI 生命週期，適合一次性取得 handle。
- `WindowInteropHelper.Handle` 足以取得 HWND。本輪不處理 window messages，因此不需要 `HwndSource.AddHook`。
- `HwndSource` 應留給日後需要處理 `WM_DPICHANGED`、hotkey 或其他訊息時使用；無需求時建立 hook 只會增加解除註冊責任。

`MainWindow` override `OnSourceInitialized`，controller 也拒絕第二次初始化，形成雙重防護。模式重複設定則允許且是 idempotent。

Controller 只在視窗存活期間保存一個切換必要的 HWND。`OnClosed` 先停止安全計時器、解除 ViewModel 事件、呼叫 controller `Close()` 將 HWND 清零並標記關閉，之後才處置採樣 ViewModel。關閉後任何 style 操作都拋出 `ObjectDisposedException`；不會對失效 HWND 呼叫 P/Invoke。

`GetWindowLongPtrW`／`SetWindowLongPtrW` 在 x64 使用 pointer-sized 回傳值；封裝同時提供 32-bit `GetWindowLongW`／`SetWindowLongW` 對應。讀取或設定回傳 0 可能是合法舊值，因此呼叫前先清除 last-error，只有「結果為 0 且 last-error 非 0」才判定失敗。

## 8. 焦點與啟動策略

- App 啟動：`ShowActivated=false`，避免首次 `Show()` 主動搶走使用者正在操作程式的鍵盤輸入。
- `Interactive`：不保留 `WS_EX_NOACTIVATE`。使用者明確點擊 overlay 時允許短暫取得焦點，才能可靠操作按鈕與鍵盤安全回復。
- `ClickThrough`：加入 `WS_EX_NOACTIVATE` 與 `WS_EX_TRANSPARENT`。Overlay 保持 visible/topmost，但不應因滑鼠而啟用。
- 本輪不主動呼叫 `Activate()`、`Focus()`、`SetForegroundWindow()` 或 `SetActiveWindow()`。

`ShowActivated=false` 只決定首次顯示；它不能取代動態 `WS_EX_NOACTIVATE`。反之，永久 `WS_EX_NOACTIVATE` 會妨礙研究所需的互動模式。因此兩者各自處理不同階段。

焦點、foreground window 與鍵盤輸入是否符合預期，無法由不建立真實 Window 的單元測試證明。

## 9. 拖曳策略

Header Border 處理 `MouseLeftButtonDown`，只在 `Interactive` 且確實為滑鼠左鍵時呼叫 WPF `DragMove()`。按鈕位於 header 外，不會因事件 bubbling 誤觸拖曳。`ClickThrough` 模式收不到滑鼠事件，而且 code-behind 仍有 mode guard。

拖曳屬於 View 與 native window 的呈現行為，不進入 ViewModel。本輪不保存位置。

## 10. 半透明方案與效能取捨

本輪需要透明的 Window canvas 與不透明文字之間的 per-pixel alpha，因此使用：

- `WindowStyle=None`
- `AllowsTransparency=true`
- `Window.Background=Transparent`
- 小面積、固定 alpha 的內容 Border

`AllowsTransparency=true` 要求 `WindowStyle=None`，WPF 會使用 layered window。Layered rendering、per-pixel alpha 及透明區域組合可能增加 CPU/GPU 與合成成本，也帶來部分原生 child window／rendering 相容限制。本輪視窗固定為小面積，沒有 blur、Acrylic、Mica、動畫或 60 FPS rendering，但仍須量測而不能假定成本為零。

正式版本若不需要不規則透明邊緣，可考慮 `AllowsTransparency=false` 的一般無邊框視窗，使用不透明背景或整窗 `Opacity`。整窗 `Opacity` 會連文字一起變淡；一般 WPF 背景 alpha 在非 layered top-level window 也不能提供同等 per-pixel desktop transparency。另一方向是交由 DWM 的平台特效處理，但不屬於本輪，而且 Acrylic／Mica 已明確排除。

## 11. DPI 與多螢幕

WPF 的 `Width`、`Height`、`Left`、`Top` 使用 device-independent units（理論基準為每英吋 96 單位），Win32 monitor rectangle 與許多 window API 則使用實體像素。兩者不可直接相加、比較或保存。

本輪沒有根據單一解析度寫死 `Left`／`Top`，視窗可由 `DragMove()` 移至其他螢幕；也沒有在移動時存取 monitor array，因此不會因找不到固定螢幕而崩潰。但這不等於完成 per-monitor DPI 與位置恢復。

Issue #7 應處理：

- 以 `MonitorFromWindow`／`GetMonitorInfo` 取得目前 monitor 與 work area。
- 以 `GetDpiForWindow`、WPF `VisualTreeHelper.GetDpi` 或 transformation matrix 明確轉換像素與 DIPs。
- 釐清 WPF 自身 DPI 處理與是否需要 `WM_DPICHANGED` hook，避免重複縮放。
- 支援位於主螢幕左側／上方之螢幕的負座標。
- 螢幕移除、解析度變更、旋轉、工作列位置改變或 remote desktop reconnect 後，將視窗 clamp 回有效 work area。
- 決定位置保存採用 DIPs、相對 monitor 座標或實體像素，並記錄 monitor identity 與 fallback 規則。

100%、125%、150% 縮放、混合 DPI monitors、負座標與跨螢幕拖曳都必須人工驗證。

## 12. 全螢幕應用程式限制

WPF `Topmost=true` 只保證一般 desktop window Z-order 中的 topmost 行為，不能保證覆蓋獨佔全螢幕 DirectX 應用程式。獨佔模式可能繞過一般 DWM composition，Overlay 可能不可見、閃爍，或在應用切換顯示模式時重新排序。

無邊框全螢幕應用程式通常仍由 DWM composition，topmost overlay 因此可能蓋在影片、簡報或遊戲上；這既可能是預期，也可能干擾使用者。正式版本宜提供「全螢幕時隱藏」選項，但必須另行定義偵測規則、例外與更新頻率。本輪不加入偵測、game hook、注入或 polling。

獨佔全螢幕、無邊框全螢幕、HDR、不同 graphics API、遊戲 anti-cheat 與 Alt+Tab 行為只能人工驗證；不得由單元測試宣稱支援。

## 13. 自動化測試邊界

本輪單元測試不建立真正 WPF Window，涵蓋：

- Controller 與 ViewModel 的初始 `Interactive` 狀態。
- `Interactive → ClickThrough → Interactive`。
- 相同模式重複設定不呼叫 native API。
- 加入 style 不移除既有 bits；移除 style 只移除指定 flags。
- x64 pointer-sized style 往返轉換。
- native 讀取失敗轉為有操作名稱及 inner exception 的明確例外。
- frame refresh 失敗時回復原 style。
- HWND 尚未初始化時拒絕操作。
- 視窗關閉後拒絕操作且不再呼叫 native API。
- 模式切換不停止、重啟或額外觸發 Dashboard 採樣。

自動測試不能證明：實際 topmost、工作列／Alt+Tab 呈現、foreground/focus、跨 process 滑鼠穿透、拖曳、透明視覺、DPI、多螢幕、全螢幕、休眠喚醒或效能目標。

## 14. 人工測試清單

### 啟動與一般視窗行為

- 在其他程式的文字欄位持續輸入時啟動 App，確認 Overlay 出現但原程式仍接收鍵盤輸入。
- 確認工作列沒有 RunCatDashboard 按鈕。
- 確認 Alt+Tab 不出現一般應用程式項目。
- 確認 Overlay 位於一般非 topmost 視窗上方。
- 確認背景透明、內容半透明、文字仍清晰，透明區域沒有黑框。
- 確認只有一個 MainWindow，切換時沒有閃爍、消失或建立第二個工作列項目。

### Interactive

- 確認模式標籤為 `Interactive`。
- 點擊兩個模式按鈕與關閉按鈕，確認可操作。
- 從 header 拖曳視窗，確認其他控制項不會誤啟動拖曳。
- 確認使用者主動點擊後可以取得焦點，但 App 不會自行重複搶回焦點。
- 快速重複切換模式，確認沒有例外、重複 handler 或 Dashboard 採樣重啟。

### Click-through

- 將 Overlay 放在可點擊的其他程式控制項上，切換為 `Click-through`。
- 確認 Overlay 仍可見且模式標籤已更新。
- 點擊 Overlay 的內容區、按鈕區與透明邊緣，確認下方程式收到滑鼠操作。
- 確認點擊不會啟用 Overlay，也不會把鍵盤輸入從目前程式搶走。
- 若 Overlay 仍持有焦點，按 Esc 確認可回到 `Interactive`。
- 不按鍵，確認約 10 秒後自動回到 `Interactive`，並可再次操作。
- 確認切換期間 CPU／記憶體數值與歷史仍按原採樣節奏更新。

### DPI 與多螢幕

- 分別在 100%、125%、150% 縮放檢查尺寸、字體、圓角與 hit target。
- 在不同 DPI 的兩個螢幕間來回拖曳，確認不崩潰、視覺不持續錯誤縮放。
- 測試主螢幕左側／上方的負座標螢幕。
- 測試不同解析度、portrait monitor 與工作列位於不同邊緣。
- 拔除或停用 Overlay 所在螢幕；本輪可能不會自動搬回，記錄 Issue #7 所需 fallback。

### 全螢幕與系統轉換

- 測試無邊框全螢幕影片、簡報與遊戲，記錄 Overlay 是否遮蓋內容。
- 若可用，測試獨佔全螢幕應用程式；不預期 topmost 保證生效。
- 測試 Alt+Tab、Win+D、顯示桌面、鎖定／解鎖、睡眠／喚醒與 remote desktop reconnect；本輪只記錄結果。
- 關閉視窗後確認程序正常結束，沒有 native handle 錯誤或持續採樣。

## 15. Issue #7 正式實作建議

1. 保留 `OverlayInteractionMode`、位元運算、x64 P/Invoke、error conversion 與 controller 測試；將研究命名收斂為正式 windowing service。
2. 保留「WPF 管固定外觀、Win32 管動態輸入 styles」的責任界線。不要以 P/Invoke 重做所有 WPF 屬性。
3. 將 HWND 的 attach/detach 明確化；若正式需求需要 DPI、hotkey 或 display-change messages，再由 View 建立最小 `HwndSourceHook` 並在關閉時解除。
4. 正式預設應是不干擾桌面的 `ClickThrough`。切換入口必須是另一個已核准 Issue 所提供的全域快捷鍵或系統匣命令；不得保留本輪 10 秒自動回復。
5. 將 mode request、native applied mode 與 fault state 明確區分。若 native update 與 rollback 都失敗，停用後續一般切換、顯示可診斷錯誤並提供安全重建視窗或結束 App 的路徑。
6. 實作 monitor/DPI positioning service，集中 DIPs、pixels、work area 與負座標轉換；位置保存應等設定 Issue 一併定義，不放進 ViewModel。
7. 量測 `AllowsTransparency=true` 的 collapsed/expanded CPU、working set 與 GPU composition。若只需矩形半透明面板，評估一般無邊框視窗以降低 layered rendering 成本。
8. 提供可選的「全螢幕時隱藏」政策，但放在獨立、可取消且低頻率的 Windows-specific service；不可用遊戲 hook 或注入。
9. 將研究 UI、說明文字、Close experiment 按鈕與安全 timer 移除，換成正式 Dashboard／RunCat 呈現；不要讓 windowing service 依賴視覺控制項。

## 16. Issue #7 建議驗收標準

### 可自動驗證

- 初始模式與產品規格一致，mode state transition 與 native applied state 可查詢且一致。
- 同一模式重複設定不產生額外 native write、frame refresh、event handler 或採樣迴圈。
- 每次 style 更新保留非 service 管理的所有 extended style bits。
- native get/set/refresh 各失敗路徑均有可辨識操作與 error code；refresh 失敗會回復，回復失敗會進入明確 fault state。
- 未 attach、重複 attach、detach 後操作、重複 detach 皆有定義且有測試。
- Overlay 模式、monitor/DPI state 與 sampling lifecycle 互不改變彼此的 start/stop 次數。
- 像素與 DIP 轉換、work area clamp、負座標、monitor 消失 fallback 有純函式測試。
- 所有 timer、hook、hotkey registration、cancellation source 與 native handle ownership 都能重複安全釋放。

### 必須人工／整合驗證

- Windows 10 與 Windows 11 x64 上不顯示於工作列及 Alt+Tab，且一般桌面中保持 topmost。
- App 啟動、背景更新與切換到 `ClickThrough` 不搶焦點；`Interactive` 只在使用者明確操作時取得焦點。
- Overlay 全區域能將滑鼠輸入交給不同 process 的下方程式，正式回復入口在穿透後仍可靠。
- 100%、125%、150% 與 mixed-DPI monitors 的大小、位置、跨螢幕拖曳及重新連接行為符合規格。
- 負座標、portrait、工作列多方向、解析度改變與 monitor 移除後，視窗仍可見於有效 work area。
- 無邊框全螢幕與獨佔全螢幕行為有明確結果；若有全螢幕隱藏選項，其進出不閃爍且可恢復。
- 透明 rendering 在目標硬體達到專案 CPU 與 working set 工程目標，長時間執行無成長趨勢。
- 關閉、睡眠喚醒、鎖定解鎖與 Explorer restart 後沒有失效 handle、殘留 hook 或失控 topmost window。

## 17. 實驗程式的保留、重構與移除

### 保留

- `OverlayInteractionMode` 與 controller 的 idempotent state transition。
- `NativeWindowStyleBits`、pointer-sized P/Invoke 與 `Win32Exception` 轉換。
- `SourceInitialized` attach 與 `OnClosed` detach 原則。
- 不建立真實 WPF Window 的 controller／ViewModel 測試。

### 重構

- 視正式 Issue 邊界決定 `IOverlayWindowController.Initialize(nint)` 是否拆成 View-only handle adapter 與不暴露 HWND 的 mode service。
- 將 native fault state 與產品錯誤呈現正式化。
- 將 DPI／monitor message hook 納入獨立 Windows-specific component，但只在確有訊息需求時新增。
- 依效能量測決定是否繼續使用 `AllowsTransparency=true`。

### 移除

- 10 秒自動回復 timer 與 Esc 研究回復說明。
- `Overlay behavior research` 標題、模式研究按鈕、`Close experiment` 與其他研究文案。
- 本輪固定研究尺寸及臨時配色；正式視覺由其對應 Issue 決定。

## 18. API 契約參考

正式實作與驗收前應以目標 .NET 與 Windows 版本的 Microsoft Learn 頁面再次核對：

- Extended Window Styles: <https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles>
- Layered Windows: <https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows>
- `GetWindowLongPtrW`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowlongptrw>
- `SetWindowLongPtrW`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowlongptrw>
- `SetWindowPos`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos>
- WPF `Window.AllowsTransparency`: <https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency>
- WPF `Window.ShowActivated`: <https://learn.microsoft.com/dotnet/api/system.windows.window.showactivated>
- WPF `WindowInteropHelper`: <https://learn.microsoft.com/dotnet/api/system.windows.interop.windowinterophelper>
- WPF `Window.SourceInitialized`: <https://learn.microsoft.com/dotnet/api/system.windows.window.sourceinitialized>

本文件的結論仍以目標 Windows 10／11 x64 實機結果為最終依據，尤其是焦點、滑鼠穿透、DPI、多螢幕、全螢幕與 rendering 成本。
