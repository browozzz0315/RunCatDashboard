# 跑貓動畫決策（Issue #8）

## 美術資源

正式動畫使用本專案原創、面向右側的黑色像素貓，保留小型黃色眼睛，不直接複製 RunCatNeo 或其他第三方素材。跑步循環固定為 6 幀：

- 每幀為具有真正 alpha transparency 的 `48 × 32` RGBA PNG。
- 貓本體約為 28～32 px 寬、16～20 px 高。
- 所有幀使用相同 head/body anchor 與 baseline，避免切幀時上下或左右漂移。
- WPF 使用 `RenderOptions.BitmapScalingMode="NearestNeighbor"` 顯示。

固定動作順序為：

1. 前腳著地、後腳收攏。
2. 身體壓低承重。
3. 後腳向後推蹬。
4. 身體完全伸展騰空。
5. 四肢向身體收攏。
6. 前腳向下伸出，準備再次著地。

六幀依上述 gait phase 構成固定順序，並使用一致 canvas 與 baseline，避免播放時產生非預期位移。

正式資源位於 `src/RunCatDashboard.App/Assets/RunCat/`，且唯一 runtime source 是依固定順序命名為 `cat-frame-01.png` 至 `cat-frame-06.png` 的六張單幀。Sprite sheet 不屬於 App runtime 或測試契約。六張 frame 在 View converter 初始化時各載入及 decode 一次，使用 `BitmapCacheOption.OnLoad` 後凍結並重用；animation tick 只變更 frame index，不讀取磁碟、assembly resource 或建立新的 `BitmapImage`。

`cats/` 是使用者提供的姿態參考資料，root `assets/` 是本機圖片工作資料；兩者都不屬於 runtime resource，也不納入 Git。正式 runtime PNG 由使用者提供並視為不可修改的美術來源；repository 不保留會重新生成或覆蓋它們的腳本。資源測試只驗證檔名、順序、PNG 尺寸、alpha、可見內容、assembly 載入與 converter cache 等技術契約，不綁定固定 hash、像素位置或姿勢。

後續更新圖片時，由美術人員直接準備六張 `48 × 32`、具有 alpha channel 的 PNG，以固定檔名覆蓋正式 frame，再執行 build、test 與人工動畫驗證；不需要修改 hash、bounding box 或姿勢測試。

## CPU 基準與速度映射

動畫使用 CPU history 中最近最多 3 筆有效 sample 的平均值：

- 依 history 的舊到新順序處理，只保留最新 3 筆。
- `NaN`、正無限與負無限不納入平均。
- 小於 0 的有限值 clamp 為 0；大於 100 的有限值 clamp 為 100。
- 沒有有效 sample 時不產生平均值，速度 mapper 使用安全預設。

速度使用線性映射：

```text
interval = 250 ms - (200 ms × cpu / 100)
```

- CPU 0% → 250 ms/frame。
- CPU 100% → 50 ms/frame。
- 任何輸出都限制在 50～250 ms/frame。
- null、`NaN`、正無限或負無限 → 250 ms/frame。

本 Issue 不加入 easing、非線性曲線、自訂速度或 FPS 設定。

## Timer 與 lifecycle

動畫使用可變 interval 的 WPF dispatcher timer，不使用 `CompositionTarget.Rendering`、固定 60 FPS、GIF 或持續 rendering loop。Controller 只管理 frame sequence、target interval、Start／Stop／Dispose 與可診斷 fault，不操作 WPF `Image`、不讀取 CPU provider，也不載入圖片。

- 重複 Start 不建立第二個 timer。
- Stop 與 Dispose 可重複呼叫。
- lifecycle generation 阻止 Stop／Dispose 後的 delayed callback 發布 frame。
- subscriber 或 timer callback exception 不越過 dispatcher callback boundary，錯誤保留在 controller／ViewModel 的診斷狀態。
- 單 frame controller 不啟動不必要的週期 timer。

Overlay 因 fullscreen policy 隱藏時只停止跑貓 timer；metrics sampling、CPU history、global hotkey、interaction mode 與 HWND 均保持不變。Overlay 恢復顯示後從目前 frame 繼續，且不呼叫 `Activate` 或 `Focus`。MainWindow 關閉時停止動畫，ViewModel 解除 controller events 並 dispose controller；DI 再次 dispose 時仍安全。

## 範圍界線

- 收合／展開與 compact mode 由 Issue #18 負責；本 Issue 只提供固定尺寸、可重用的跑貓顯示區塊。
- system tray 專用小尺寸 sprite 不屬於本 Issue。
- 不包含 runner 選擇、多動物、主題、自訂動畫速度或設定保存。
- WPF rendering、焦點、fullscreen Hide／Show、DPI 與長時間資源趨勢仍須在目標 Windows 環境人工驗證，不能只由單元測試宣稱完成。
