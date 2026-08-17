# 跑貓動畫與自訂 sprite sheet

## 內建動畫

內建預設動畫 ID 為 `builtin.cat2-run`。Dashboard 使用既有 Cat-2 Run 的八張
50 x 50 RGBA PNG；Light 使用黑貓資源，Dark 使用白貓資源。資源在啟動時以
`BitmapCacheOption.OnLoad` 載入、freeze 後重用，animation tick 只更新 frame index。

系統匣維持既有八張內建黑／白 `.ico`。它依 resolved theme 選擇資源，與 Dashboard
共用 controller 的 frame index，但不讀取自訂 PNG，也不產生 PNG-to-ICO。當 Dashboard
使用自訂動畫時，tray 仍顯示內建動畫。

## 自訂動畫 contract

自訂動畫只影響 Dashboard，與 theme 無關。匯入像素不重著色、不產生 Light／Dark
變體，也不要求成對資產。只接受副檔名大小寫不拘的 PNG，且必須是單一橫向 row、
等寬 frame，frame count 由使用者輸入。

驗證規則如下：

- frame count 為 1 至 64。
- source width 與 height 各不超過 4096 px。
- source width 必須可被 frame count 整除。
- derived frame width 與 frame height 各不超過 1024 px。
- source decoded pixel budget 不超過 8,388,608 pixels。
- PNG 必須能由 WPF `PngBitmapDecoder` 解碼；alpha／transparency 保留。

輸出一律使用 `frame-000.png`、`frame-001.png` 依序命名。每張 frame 以
`BitmapCacheOption.OnLoad` 載入並 freeze；runtime 不在 tick 時讀取檔案，載入完成後
不保留檔案 stream。

每個自訂動畫使用 `custom-<lowercase-guid-without-hyphens>` stable ID。display name、
stable ID 與實體資料夾名稱是不同概念，display name 不會用來組合路徑。manifest
format version 為 1，base frame interval 固定為 250 ms。

## Import transaction 與 preview

匯入由 `AnimationImportWindow` 處理。ViewModel 只使用 file-picker、parser 與 import
service，不建立 `OpenFileDialog`、不操作 filesystem，也不 decode／write PNG。

流程為：

1. file picker 選擇 PNG。
2. parser decode source 並驗證尺寸、frame count 與 pixel budget。
3. 使用者輸入 display name 與 frame count；畫面顯示 source／derived dimensions、
   slicing preview 與狀態錯誤。
4. preview 使用 250 ms、Normal 1.00x，不使用 CPU mapping；preview timer 是
   import window 自己的 disposable lifecycle，關閉視窗即停止。
5. Confirm 在 `Animations/.import-<guid>.tmp` 建立 temporary directory，寫入 normalized
   PNG 與 manifest。
6. 從 temporary directory 重新讀取並完整驗證 manifest 與 frames。
7. 用同一 parent 的 directory move 原子發布到 `Animations/custom-.../`，destination
   已存在時失敗且不覆寫。
8. refresh catalog；任何 publish 前失敗都清理 temporary directory。啟動時也會清理
   可安全辨識的 stale `.import-*.tmp` directory。

完成 publish 與 settings persistence 是兩個交易。publish 成功後若選取或保存設定
失敗，保留有效的 library item，runtime 維持上一個成功套用的選取值。

## Catalog、fallback 與 delete

catalog 永遠包含 built-in default，並提供 custom list、stable ID lookup、refresh、
resolve 與 delete。內建項目不可刪除。custom manifest parse、format version、ID／資料夾、
frame sequence、frame dimensions、PNG decode 或 manifest dimensions 任一失敗時，項目
會標記為 corrupt，不會靜默刪除資料。

settings reference 的 custom ID missing 或 corrupt 時，啟動繼續，runtime 立即切換
內建 default，發布 animation diagnostic，並用 settings service 的 safe/debounced
update pattern 修回 `builtin.cat2-run`。修復保存失敗時仍維持內建 runtime，不阻止啟動。

刪除非選取 custom 只移除 library item。刪除目前選取項目時，先套用並保存 built-in
fallback，再刪除資料夾。刪除失敗時 runtime 仍是 built-in，custom data 與 catalog
entry 保留到可處理失敗為止。Settings Cancel 不會刪除匯入後的 library item。

## CPU mapping、base interval 與 playback speed

CPU sampling／averaging 仍由 MainWindowViewModel 管理，controller 不取樣也不計算
CPU mapping。最近最多三筆有效 CPU sample 先取平均，既有 mapper 維持：

```text
cpuMappedMs = clamp(250 ms - (200 ms * cpu / 100), 50 ms, 250 ms)
```

播放偏好只有三種：Slow = 0.75x、Normal = 1.00x、Fast = 1.25x。每個 animation
manifest 的 `baseFrameIntervalMilliseconds` 在 V1 固定為 250 ms。有效 interval 為：

```text
effectiveMs = clamp(
    baseFrameIntervalMs * (cpuMappedMs / 250.0) / speedMultiplier,
    50 ms,
    250 ms)
```

因此 250 ms + Normal 完全保留既有 CPU mapping；更大的 multiplier 會產生更小的
interval，除非被 50／250 ms 邊界限制。設定只保存 `speedPreference`，不保存 derived
interval。

## Controller 與 frame-set lifecycle

App 存活期間只有一個 `IRunCatAnimationController`、一個 timer 與一條 `FrameChanged`
event stream。controller 支援 immutable active frame-set replacement：切換時在同一個
lifecycle 中更新 frame count、reset index 至 0，先替換 frame source，再立即發布 frame 0，
並重算目前 CPU average、base interval 與 speed preference。它不建立第二個 timer，
不重複訂閱 event；generation 仍防止 Stop／Dispose 後的 delayed callback。

MainWindow 與 animated tray 仍共用 production frame index／timer。tray 永遠以明確的
`frameIndex % 8` 映射到既有八張 icon，因此 custom frame count 與 8 不同時不會越界。
只有真正退出才停止並 dispose controller、timer、tray resources、loaded frame sets 與
相關 cancellation sources。

WPF rendering、實際 DPI、多螢幕、tray shell recovery、長時間 working set／CPU 與真實
PNG 匯入畫面仍需在 Windows 環境人工驗證；unit tests 不宣稱涵蓋這些事項。
