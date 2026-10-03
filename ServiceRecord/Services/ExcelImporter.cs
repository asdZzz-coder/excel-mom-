using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ServiceRecord.Models;

namespace ServiceRecord.Services
{
    public class ImportedClient
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public Dictionary<string, int[]> Counts { get; } = new();
    }

    public class ImportedMonth
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal? ShareRatio { get; set; }
        public List<ServiceItem> Items { get; } = new();
        public List<ImportedClient> Clients { get; } = new();
    }

    /// <summary>
    /// 讀取原本的「服務紀錄表」Excel（也能讀本程式匯出的檔）。
    /// 不寫死列號：每張工作表找「D 欄是日期」的那一列當日期列，再往下找「A 欄有代碼、C 欄是單價」的列當服務項目，
    /// 所以第一張和其他張差一列（原本的檔案就是這樣）也讀得到。沒有日期列或沒有服務項目的工作表（價目表、空白表）會略過。
    /// </summary>
    public static partial class ExcelImporter
    {
        private const int CodeCol = 1, NameCol = 2, PriceCol = 3, FirstDayCol = 4;
        private const int AmountCol = FirstDayCol + MonthRecord.MaxDays + 1; // AJ

        public static ImportedMonth Read(string path)
        {
            // 允許讀取「正在被 Excel 開啟」的檔案，否則會因檔案被鎖定而失敗
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);

            var result = new ImportedMonth();
            DateTime? month = null;
            foreach (var ws in wb.Worksheets)
            {
                var sheet = ReadSheet(ws);
                if (sheet == null) continue;
                var (first, client, items, ratio) = sheet.Value;

                month ??= first;
                if (first.Year != month.Value.Year || first.Month != month.Value.Month) continue; // 別的月份的表不混進來
                result.ShareRatio ??= ratio;
                foreach (var item in items)
                    if (!result.Items.Any(i => i.Code == item.Code))
                        result.Items.Add(item);
                result.Clients.Add(client);
            }

            if (month == null || result.Clients.Count == 0)
                throw new InvalidDataException("這個檔案裡找不到服務紀錄表（需要有日期列和服務代碼）。");
            result.Year = month.Value.Year;
            result.Month = month.Value.Month;
            return result;
        }

        private static (DateTime First, ImportedClient Client, List<ServiceItem> Items, decimal? Ratio)? ReadSheet(IXLWorksheet ws)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow == 0) return null;

            // 日期列：前 10 列裡 D 欄是日期的那一列
            int dateRow = 0;
            DateTime first = default;
            for (int r = 1; r <= Math.Min(10, lastRow); r++)
            {
                if (AsDate(ValueOf(ws.Cell(r, FirstDayCol))) is DateTime d) { dateRow = r; first = d; break; }
            }
            if (dateRow == 0) return null;

            var client = new ImportedClient { Name = ClientNameFromSheet(ws.Name) };
            var items = new List<ServiceItem>();
            for (int r = dateRow + 1; r <= lastRow; r++)
            {
                var code = ValueOf(ws.Cell(r, CodeCol)).ToString(CultureInfo.InvariantCulture).Trim();
                var price = AsNumber(ValueOf(ws.Cell(r, PriceCol)));
                if (code.Length == 0 || price == null) continue;

                // 同一張表重複的代碼：第二個起加上 (2)、(3)… 分開記
                var unique = code;
                for (int n = 2; items.Any(i => i.Code == unique); n++) unique = $"{code}({n})";

                items.Add(new ServiceItem
                {
                    Code = unique,
                    Name = ValueOf(ws.Cell(r, NameCol)).ToString(CultureInfo.InvariantCulture).Trim(),
                    Price = (int)Math.Round(price.Value),
                });

                var days = new int[MonthRecord.MaxDays];
                for (int c = FirstDayCol; c < FirstDayCol + MonthRecord.MaxDays; c++)
                {
                    var date = first.AddDays(c - FirstDayCol);
                    if (date.Month != first.Month) continue; // 超出當月的欄（例如 6 月的第 31 欄）
                    if (AsNumber(ValueOf(ws.Cell(r, c))) is double n && n > 0)
                        days[date.Day - 1] += (int)Math.Round(n);
                }
                client.Counts[unique] = days;
            }
            if (items.Count == 0) return null;

            // 地點：表頭裡「慶東街(王林妙芳)」這種寫法，括號內的姓名要跟工作表名稱一樣才採用
            for (int r = 1; r <= dateRow; r++)
            {
                var label = ValueOf(ws.Cell(r, CodeCol)).ToString(CultureInfo.InvariantCulture).Trim();
                var m = LabelPattern().Match(label);
                if (m.Success && m.Groups["name"].Value.Trim() == client.Name)
                {
                    client.Location = m.Groups["loc"].Value.Trim();
                    break;
                }
            }

            // 比例：合計列的公式 (SUM(AJ5:AJ35)*0.6)
            decimal? ratio = null;
            for (int r = dateRow; r <= lastRow + 2 && ratio == null; r++)
            {
                var cell = ws.Cell(r, AmountCol);
                if (!cell.HasFormula) continue;
                var m = RatioPattern().Match(cell.FormulaA1);
                if (m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 && v <= 1)
                    ratio = v;
            }

            return (first, client, items, ratio);
        }

        /// <summary>
        /// 把匯入的內容變成本程式的月份紀錄：個案依姓名對到設定裡的個案，設定裡沒有的會新增到設定。
        /// 回傳新增了幾位個案。
        /// </summary>
        public static (MonthRecord Record, int AddedClients) ToMonthRecord(ImportedMonth imported, AppSettings settings)
        {
            var record = new MonthRecord
            {
                Year = imported.Year,
                Month = imported.Month,
                ShareRatio = imported.ShareRatio ?? settings.ShareRatio,
                Items = imported.Items.Select(i => i.Clone()).ToList(),
            };
            int added = 0;
            foreach (var ic in imported.Clients)
            {
                var client = settings.Clients.FirstOrDefault(c => c.Name.Trim() == ic.Name);
                if (client == null)
                {
                    client = new Client { Name = ic.Name, Location = ic.Location };
                    settings.Clients.Add(client);
                    added++;
                }
                else if (client.Location.Length == 0 && ic.Location.Length > 0)
                {
                    client.Location = ic.Location;
                }

                if (record.Clients.Any(c => c.Id == client.Id)) continue; // 同一個人出現兩張表，只取第一張
                record.Clients.Add(client.Clone());
                foreach (var (code, days) in ic.Counts)
                    Array.Copy(days, record.GetDays(client.Id, code), MonthRecord.MaxDays);
            }
            return (record, added);
        }

        // ---------- 讀值 ----------

        /// <summary>公式格用 Excel 存檔時算好的值，不在這裡重算（例如 TEXT(…,"aaaa") 這類函數）。</summary>
        private static XLCellValue ValueOf(IXLCell cell) => cell.HasFormula ? cell.CachedValue : cell.Value;

        private static double? AsNumber(XLCellValue v)
        {
            if (v.IsNumber) return v.GetNumber();
            if (v.IsText && double.TryParse(v.GetText().Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return n;
            return null;
        }

        /// <summary>日期格：可能是日期型別，也可能只是日期序號（例如 46174 = 2026/6/1）。</summary>
        private static DateTime? AsDate(XLCellValue v)
        {
            if (v.IsDateTime) return v.GetDateTime().Date;
            if (v.IsNumber && v.GetNumber() is >= 40000 and <= 60000) return DateTime.FromOADate(v.GetNumber()).Date;
            return null;
        }

        private static string ClientNameFromSheet(string sheetName) =>
            sheetName.Trim().Trim('(', ')', '（', '）').Trim();

        [GeneratedRegex(@"^(?<loc>[^(（]*)[(（](?<name>[^)）]+)[)）]")]
        private static partial Regex LabelPattern();

        [GeneratedRegex(@"SUM\([^)]*\)\s*\*\s*([0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase)]
        private static partial Regex RatioPattern();
    }
}
