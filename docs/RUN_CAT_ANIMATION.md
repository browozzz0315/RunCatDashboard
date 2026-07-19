# 跑貓動畫決策（Issue #8）

## 美術資源

正式動畫使用 CC0 授權的 Pet Cats Pack 中 Cat-2 Run animation。來源為橫向 8 幀的 `Cat-2/Cat-2-Run.png`，從左至右依序匯入：

第三方素材與授權摘要記錄於 [`THIRD_PARTY_ASSETS.md`](THIRD_PARTY_ASSETS.md)。

- 每幀為具有 alpha channel 的 `50 × 50` RGBA PNG。
- 固定檔名為 `cat-frame-01.png` 至 `cat-frame-08.png`。
- 每幀直接裁切自來源 strip 的對應 `50 × 50` 區域，不縮放、重新取樣或改變像素。
- WPF 使用 `RenderOptions.BitmapScalingMode="NearestNeighbor"` 顯示。
- WPF Image 使用 `Stretch="Uniform"`，保留正方形 sprite 比例。

正式資源位於 `src/RunCatDashboard.App/Assets/RunCat/`，且唯一 runtime source 是上述八張單幀。來源 strip 只存在 root `assets/` 本機匯入目錄，不納入 App resource 或 Git。八張 frame 在 View converter 初始化時各載入及 decode 一次，使用 `BitmapCacheOption.OnLoad` 後凍結並重用；animation tick 只變更 frame index，不讀取磁碟、assembly resource 或建立新的 `BitmapImage`。

## 素材匯入

使用 repository 內的單用途腳本匯入：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Import-PetCatRunAssets.ps1 `
  -SourcePath '.\assets\Pet Cats Pack\Cat-2\Cat-2-Run.png' `
  -OutputPath '.\src\RunCatDashboard.App\Assets\RunCat'
```

腳本只接受明確的來源與輸出路徑，驗證來源必須是 `400 × 50`、8-bit RGBA PNG，精確切成八張 `50 × 50` PNG，並在替換正式輸出前逐幀比對 decoded pixels。來源或既有輸出規格異常時會明確失敗，不下載或處理其他圖片。

未來純替換圖片仍須符合八張 `50 × 50` RGBA PNG、固定檔名與順序的正式 frame contract，並執行 build、test、來源逐像素比對與人工動畫驗證；測試不綁定固定 hash、bounding box 或姿勢。

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
