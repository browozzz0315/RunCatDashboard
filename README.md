# RunCatDashboard

Windows 上的低資源常駐桌面小工具。

V1 目標是建立具有跑貓動畫、系統資訊與半透明 Dashboard 的 WPF 應用程式，同時確保它不妨礙日常工作。

## 技術選擇

- .NET 10
- WPF
- C#
- MVVM
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- NLog（本機 rolling diagnostic file）
- Win32 Interop
- xUnit

## 支援平台

- Windows 10 / 11
- x64
- 不支援 Linux 或 macOS
- V1 使用 WPF 與 Win32 API，跨平台不在目前範圍內

## V1 範圍

### 顯示功能

- 跑貓動畫
- CPU 即時使用率
- 記憶體即時使用率
- CPU 短期歷史折線圖
- CatOnly、Compact、Standard、Expanded 四種 Overlay 尺寸模式
- 可選擇 CPU、Memory、歷史、狀態、互動模式與快捷鍵提示欄位
- 半透明介面

### Windows 整合

- 永遠置頂
- 不顯示於工作列
- 不搶輸入焦點
- 滑鼠穿透
- 可分別自訂全域快捷鍵切換互動模式與顯示／隱藏 Dashboard
- 系統匣選單
- 記住視窗位置
- 單一執行個體
- 可選擇開機啟動

### 工程品質

- 結構化設定
- 基本錯誤紀錄
- 單元測試
- Release 發布
- 發布檔實機測試
- CPU、記憶體與長時間執行測試

## V1 不包含

- Claude、Codex 或 Headroom 使用量整合
- 插件系統
- 使用者遙測
- 自動更新
- Microsoft Store 發布
- 多套外觀主題
- GPU 溫度與硬體感測器整合

## 專案結構

```text
src/
  RunCatDashboard.App/

tests/
  RunCatDashboard.Tests/

docs/
scripts/
assets/
```

## 建置

```powershell
dotnet restore
dotnet build
```

## 測試

```powershell
dotnet test
```

## 診斷日誌

正式診斷日誌位於 `%LocalAppData%\RunCatDashboard\Logs`，採每日與 5 MiB 單檔
rolling、保留 7 天且 active file在內最多 14 個檔案。日誌不寫入 repository、執行或安裝
目錄，也不會上傳。等級、短期 Trace、隱私與 failure policy 詳見
[`docs/DIAGNOSTIC_LOGGING.md`](docs/DIAGNOSTIC_LOGGING.md)。

## 執行

```powershell
dotnet run --project ".\src\RunCatDashboard.App"
```

## Codex

```powershell
codex -C "D:\.repos\RunCatDashboard"
```

## Headroom

```powershell
Set-Location "D:\.repos\RunCatDashboard"
headroom wrap codex
```

專案開發規則請閱讀根目錄的 `AGENTS.md`。
