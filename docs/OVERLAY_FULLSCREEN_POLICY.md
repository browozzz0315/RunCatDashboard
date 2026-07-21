# Overlay 全螢幕顯示政策決策（Issue #16）

## 決策

Overlay 提供三態、僅限本次執行期間的顯示政策：

| 政策 | 可見性 | Topmost | Fullscreen 行為 |
|---|---|---|---|
| `AlwaysOnTop` | 保持顯示 | `true` | 不因偵測結果隱藏 |
| `HideOverFullscreenApps` | 一般情況顯示 | `true` | 只有 foreground fullscreen 與 Overlay 位於同一 monitor 時隱藏 |
| `NeverTopmost` | 保持顯示 | `false` | 不因偵測結果隱藏 |

預設為 `HideOverFullscreenApps`。Issue #10 已建立 settings 基礎，但 fullscreen
policy 明確仍不持久化；應用程式重啟後回到預設值。

## 責任分界

- Fullscreen detector 只觀測 foreground window、window bounds 與 monitor，不操作 WPF Window。
- 純政策 coordinator 根據 requested policy 與 observation 計算 visibility 與 topmost。
- `MainWindow` lifecycle 負責 Dispatcher marshaling 與實際設定 `Topmost`；fullscreen 結果先進入統一 visibility coordinator，再由 lifecycle 套用 `Show()`／`Hide()`。
- `OverlayWindowController` 繼續只管理 Interactive／ClickThrough extended styles。
- ViewModel 只保存可顯示的 requested／applied 狀態與診斷，不接觸 HWND、`HwndSource` 或 P/Invoke。

最終可見性為 user-requested visibility 與 fullscreen policy visibility 的 AND。政策 Hide／Show 不停止 metrics sampling、不解除 global hotkey、不重建 HWND、不改變 requested interaction mode，也不重新初始化 `MainWindow`。恢復顯示不呼叫 `Activate` 或 `Focus`；使用者手動隱藏後，fullscreen 結束不會自行顯示。

Issue #10 只保存 user-requested visibility。hidden startup 會先建立 HWND 並完成
tray/hotkey/sampling/native initialization，再以此 AND 結果決定是否第一次 Show；
fullscreen observation 不會把暫時 actual hidden 寫回 settings。

## Fullscreen observation

1. 以 Overlay HWND 的 `MonitorFromWindow` 決定 Overlay monitor。
2. 排除無 foreground HWND、Overlay 自身、不可見或 minimized foreground window。
3. 在 geometry 判定前排除 `GetShellWindow()` HWND，以及 class name 為 `Progman`／`WorkerW` 的 Windows desktop shell 視窗；不依 process name 排除一般檔案總管視窗。
4. 優先讀取 DWM extended frame bounds。
5. DWM 失敗時 fallback `GetWindowRect`。
6. 以 foreground HWND 決定 foreground monitor。
7. 使用完整 `rcMonitor` 比較 bounds，不使用定位用途的 `rcWork`。
8. 邊界容許值固定為 2 physical pixels，支援負座標與多 monitor。

DWM 與 fallback 均失敗、monitor 查詢失敗或 callback／timer／dispatcher／政策套用失敗時，保留可診斷 fault 並採 fail-visible。後續成功 observation 會清除 transient observation/application fault，恢復正常 applied state。

## 監測 lifecycle

- `EVENT_SYSTEM_FOREGROUND` WinEvent hook 在 foreground 改變時立即重新評估。
- 1 秒 reconciliation timer 補足同一 HWND 內的 F11、borderless、bounds 或 style 變化。
- 政策切換、Overlay 位置／monitor 改變及 `DisplaySettingsChanged` 立即重新評估。
- `Start` 不重複註冊；`Stop`／`Dispose` 可重複呼叫。
- 關閉時清理 hook、timer、native callback 與既有 static display-settings event。
- native callback 不操作 WPF Window，且不讓例外跨越 unmanaged callback boundary。
- 監測停止後不再發布狀態。

## 已知限制

- 不保證 Overlay 能覆蓋 exclusive fullscreen。
- 不解決部分遊戲攔截或不轉送 `RegisterHotKey` 的情況。
- 不使用 low-level keyboard hook、Raw Input、遊戲注入或 anti-cheat workaround。
- 不強制要求管理員權限。
- 真實 DPI、mixed-DPI、多螢幕、負座標、Topmost、焦點、遊戲及顯示器重接仍需人工驗證。

## API 參考

- `DwmGetWindowAttribute`: <https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute>
- `GetWindowRect`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrect>
- `SetWinEventHook`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook>
- `MonitorFromWindow`: <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfromwindow>
- `MONITORINFO`: <https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfo>
