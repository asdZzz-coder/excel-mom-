# 居服紀錄表

取代每個月手動填寫的 Excel「服務紀錄表」：選個案、點日期填次數，自動算出每位個案的實領和整個月的月薪，也能匯出成跟原本一樣格式的 Excel。

## 功能

- **每月紀錄**：每位個案一張表，列出服務項目 × 當月每天（實際天數，週末上色），直接打數字，Enter 往下一格；`+` / `−` 加減一次，`Delete` 清除。填寫內容會自動儲存。
- **自動計算**：金額 = 次數 × 單價，實領 = 金額小計 × 比例（預設 0.6），月薪 = 所有個案實領加總。
- **服務項目可以新增、刪除、修改**：
  - 「設定 → 服務項目」是新月份預設的清單（代碼、名稱、單價、順序）。
  - 主畫面的「新增項目」和每列的刪除鈕，只調整目前這個月。
  - 已經有紀錄的月份各自保存當時的項目、單價和比例，改設定不會動到舊月份；改完設定時會問要不要套用到目前的月份。
- **匯出 Excel**：`2026-06份 洪淑瑩靜鑫服務紀錄表.xlsx`，每位個案一張工作表（含 `SUM`、`TEXT(…,"aaaa")` 等公式），另附「服物地點」「服務項目選項」。
- **匯入 Excel**：可以讀入原本的服務紀錄表（各工作表差一列也讀得到），個案會自動加到設定。
- **國定假日標紅**：週末、國定假日、補假的日期是紅色，表格上方列出「本月假日：6/19（五）端午節」，匯出的 Excel 也一樣上底色（節日名稱在日期的註解）。
  - 資料是人事行政總處公告的「政府行政機關辦公日曆表」（JSON 版本來自 [ruyut/TaiwanCalendar](https://github.com/ruyut/TaiwanCalendar)）。程式內附 2026、2027 年，其他年份會自動下載（存在 `holidays\`，30 天更新一次）。
  - 沒有網路又沒有那一年的日曆時，用固定的國定假日＋農曆春節、端午、中秋自己算（沒有補假）。
- **字大、格線清楚**：預設字體比一般程式大，表格格線較深，次數用粗體，捲軸也比較粗好拉。
- **外觀**：右下角按一下切換「跟隨系統 → 淺色 → 深色」。跟隨系統時，Windows 改深淺色，程式馬上跟著變。
- **字體大小**：右下角「字體 − 100% +」，或 `Ctrl +` / `Ctrl −` / `Ctrl 0`、按住 Ctrl 滾滑鼠滾輪；80%～200%，所有視窗一起縮放。

資料存在 `%AppData%\ServiceRecord`（`settings.json`、`months\2026-06.json`；外觀和字體大小在 `theme.txt`、`zoom.txt`）。

## 安裝（給使用的人）

1. 到 [Releases](https://github.com/asdZzz-coder/excel-mom-/releases) 下載 `ServiceRecord-Setup.zip`。
2. **整個解壓縮**後，執行「安裝.cmd」。
3. 程式會自動打開，開始功能表和桌面會有「居服紀錄表」。之後有新版本時，打開程式會詢問是否更新。

- 安裝在 `%LOCALAPPDATA%\Programs\ServiceRecord`，不需要系統管理員權限，自帶 .NET 執行環境。
- 解除安裝：Windows「設定 → 應用程式」找「居服紀錄表」。紀錄（`%AppData%\ServiceRecord`）會保留。
- 不用 ClickOnce：ClickOnce 一定要搭配每個程式各自產生、沒有簽章的 `Launcher.exe`，開著 Windows「智慧型應用程式控制」的電腦會擋下它，安裝時出現「部署中某些檔案已損毀」。

## 開發

```
dotnet build excel-mom.slnx
dotnet test ServiceRecord.Tests/ServiceRecord.Tests.csproj
```

- `ServiceRecord/`：WPF 程式（.NET 10）。
- `ServiceRecord.Tests/`：xunit 測試。用原本 Excel 驗證的測試需要 `example/` 裡的範例檔；範例檔含個案資料，不放上 GitHub，沒有時會自動略過。
- 發佈：推送 `v*` 標籤（例如 `v1.0.1`），GitHub Actions 會跑測試、`dotnet publish`、把「安裝.cmd + app 資料夾」壓成 `ServiceRecord-Setup.zip` 並建立 Release（見 `.github/workflows/release.yml`）。
