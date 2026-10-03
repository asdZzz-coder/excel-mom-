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

資料存在 `%AppData%\ServiceRecord`（`settings.json`、`months\2026-06.json`）。

## 安裝（給使用的人）

1. 到 [Releases](https://github.com/asdZzz-coder/excel-mom-/releases) 下載 `ServiceRecord-ClickOnce.zip`。
2. 整個解壓縮後，執行「安裝.cmd」，在出現的安裝視窗按「安裝」。
3. 安裝完成後，開始功能表和桌面會有「居服紀錄表」。之後有新版本時，打開程式會詢問是否更新。

安裝方式跟 [password](https://github.com/asdZzz-coder/password) 相同：ClickOnce（自帶 .NET 執行環境，不用另外安裝），由「安裝.cmd」先複製到 `%LOCALAPPDATA%\ServiceRecord-Setup` 再安裝，避免 ClickOnce「已從其他位置安裝」的錯誤。

## 開發

```
dotnet build excel-mom.slnx
dotnet test ServiceRecord.Tests/ServiceRecord.Tests.csproj
```

- `ServiceRecord/`：WPF 程式（.NET 10）。
- `ServiceRecord.Tests/`：xunit 測試。用原本 Excel 驗證的測試需要 `example/` 裡的範例檔；範例檔含個案資料，不放上 GitHub，沒有時會自動略過。
- 發佈：推送 `v*` 標籤（例如 `v1.0.0`），GitHub Actions 會跑測試、用 ClickOnce 打包、建立 Release（見 `.github/workflows/release.yml`）。
  本機開著 Windows「智慧型應用程式控制」時，ClickOnce 產生資訊清單會被擋（MSB3171），所以一律由 GitHub Actions 打包。
