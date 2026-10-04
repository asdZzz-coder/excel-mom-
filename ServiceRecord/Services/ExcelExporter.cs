using ClosedXML.Excel;
using ServiceRecord.Models;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 匯出成原本「服務紀錄表」的格式（含公式），每位個案一張工作表：
    ///   A1 月薪  C1 = 各表 C3 加總（只在第一張）
    ///   A2 地點(姓名)
    ///   A3 實領  C3 = 合計列的 AJ；D3 起是日期（1 日、=D3+1 …）
    ///   C4 費用  D4 起 =TEXT(D3,"aaaa") 顯示星期
    ///   第 5 列起：A 代碼、B 名稱、C 單價、D..AH 每日次數、AI =SUM(D:AH)、AJ =AI*C
    ///   合計列：AJ =SUM(AJ5:AJn)*比例
    /// 另附「服物地點」和「服務項目選項」兩張表。
    /// </summary>
    public static class ExcelExporter
    {
        public const int HeaderRows = 4;
        public const int FirstItemRow = 5;
        public const int CodeCol = 1, NameCol = 2, PriceCol = 3, FirstDayCol = 4;
        public const int CountCol = FirstDayCol + MonthRecord.MaxDays; // AI
        public const int AmountCol = CountCol + 1;                     // AJ

        private static readonly XLColor WeekendFill = XLColor.FromHtml("#FDECEC");
        private static readonly XLColor HolidayFont = XLColor.FromHtml("#8B1515");
        private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF0FF");
        private static readonly XLColor TotalFill = XLColor.FromHtml("#FFF7D6");
        private static readonly XLColor SubstituteFont = XLColor.FromHtml("#9A3412");
        private static readonly XLColor SubstituteFill = XLColor.FromHtml("#FFEDD5");

        /// <summary>代班個案的工作表名稱前綴、D2 的標記，以及原居服員、備註的寫法（匯入時也用這些認）。</summary>
        public const string SubstitutePrefix = "代班";
        public const string SubstituteMark = "代班";
        public const string CoverForLabel = "原居服員：";
        public const string NoteLabel = "備註：";

        public static string DefaultFileName(MonthRecord record, string workerName) =>
            $"{record.Year:D4}-{record.Month:D2}份 {workerName.Trim()}服務紀錄表.xlsx";

        /// <summary>某個案工作表的合計列（實領）在第幾列。</summary>
        public static int TotalRow(MonthRecord record) => FirstItemRow + record.Items.Count;

        public static void Export(MonthRecord record, string path)
        {
            using var wb = new XLWorkbook();
            wb.Style.Font.FontName = "Microsoft JhengHei";
            wb.Style.Font.FontSize = 11;
            // 讓 Excel 開檔時重新計算全部公式（星期、加總）
            wb.FullCalculationOnLoad = true;

            // 正常個案在前，代班個案接在後面，工作表名稱前面加「代班」
            var clients = record.AllClients.ToList();
            var names = new List<string>();
            foreach (var client in clients)
                names.Add(UniqueSheetName((record.IsSubstitute(client.Id) ? SubstitutePrefix : "") + "(" + client.Name + ")", names));

            for (int i = 0; i < clients.Count; i++)
            {
                var ws = wb.Worksheets.Add(names[i]);
                WriteClientSheet(ws, record, clients[i], i == 0 ? names : null);
                if (record.IsSubstitute(clients[i].Id)) WriteSubstituteMark(ws, clients[i]);
            }
            if (record.Substitutes.Count > 0) WritePayBreakdown(wb.Worksheet(names[0]), record, names);

            WriteLocations(wb.Worksheets.Add(UniqueSheetName("服物地點", names)), record);
            WritePriceList(wb.Worksheets.Add(UniqueSheetName("服務項目選項", names)), record);
            wb.SaveAs(path);
        }

        private static void WriteClientSheet(IXLWorksheet ws, MonthRecord record, Client client, List<string>? allSheetsForMonthlyPay)
        {
            int days = record.DaysInMonth;
            int totalRow = TotalRow(record);
            int lastItemRow = totalRow - 1;
            string amount = Col(AmountCol), count = Col(CountCol);

            // 第 1 列：月薪（只放在第一張）
            if (allSheetsForMonthlyPay != null)
            {
                ws.Cell(1, 1).Value = "月薪";
                ws.Cell(1, PriceCol).FormulaA1 = string.Join("+", allSheetsForMonthlyPay.Select((n, i) => i == 0 ? "C3" : $"{QuoteSheet(n)}!C3"));
                ws.Range(1, 1, 1, 2).Merge();
                ws.Range(1, 1, 1, PriceCol).Style.Font.Bold = true;
            }

            // 第 2 列：地點(姓名)
            ws.Cell(2, 1).Value = client.Label;
            ws.Cell(2, 1).Style.Font.Bold = true;

            // 第 3 列：實領 + 日期；第 4 列：費用 + 星期
            ws.Cell(3, 1).Value = "實領";
            ws.Range(3, 1, 3, 2).Merge();
            ws.Cell(3, PriceCol).FormulaA1 = $"{amount}{totalRow}";
            ws.Cell(4, PriceCol).Value = "費用";
            var first = new DateTime(record.Year, record.Month, 1);
            for (int d = 1; d <= days; d++)
            {
                var date = ws.Cell(3, FirstDayCol + d - 1);
                if (d == 1) date.Value = first;
                else date.FormulaA1 = $"{Col(FirstDayCol + d - 2)}3+1";
                date.Style.NumberFormat.Format = "m/d";

                var weekday = ws.Cell(4, FirstDayCol + d - 1);
                weekday.FormulaA1 = $"TEXT({Col(FirstDayCol + d - 1)}3,\"aaaa\")";
            }
            ws.Cell(4, CountCol).Value = "次數";
            ws.Cell(4, AmountCol).Value = "金額";
            ws.Range(3, 1, 4, AmountCol).Style.Fill.BackgroundColor = HeaderFill;
            ws.Range(3, 1, 4, AmountCol).Style.Font.Bold = true;

            // 服務項目
            for (int i = 0; i < record.Items.Count; i++)
            {
                var item = record.Items[i];
                int r = FirstItemRow + i;
                ws.Cell(r, CodeCol).Value = item.Code;
                ws.Cell(r, NameCol).Value = item.Name;
                ws.Cell(r, PriceCol).Value = item.Price;
                var dayCounts = record.GetDays(client.Id, item.Code);
                for (int d = 1; d <= days; d++)
                    if (dayCounts[d - 1] != 0)
                        ws.Cell(r, FirstDayCol + d - 1).Value = dayCounts[d - 1];
                ws.Cell(r, CountCol).FormulaA1 = $"SUM({Col(FirstDayCol)}{r}:{Col(CountCol - 1)}{r})";
                ws.Cell(r, AmountCol).FormulaA1 = $"{count}{r}*{Col(PriceCol)}{r}";
            }

            // 合計列（實領）
            ws.Cell(totalRow, CountCol).Value = "實領";
            ws.Cell(totalRow, AmountCol).FormulaA1 = lastItemRow >= FirstItemRow
                ? $"(SUM({amount}{FirstItemRow}:{amount}{lastItemRow})*{record.ShareRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : "0";
            ws.Range(totalRow, CountCol, totalRow, AmountCol).Style.Fill.BackgroundColor = TotalFill;
            ws.Range(totalRow, CountCol, totalRow, AmountCol).Style.Font.Bold = true;
            ws.Cell(3, PriceCol).Style.Font.Bold = true;

            // 放假日（週末、國定假日、補假）上底色、日期紅字；節日名稱放在日期的註解
            for (int d = 1; d <= days; d++)
            {
                var date = new DateOnly(record.Year, record.Month, d);
                if (!HolidayService.IsOffDay(date)) continue;
                int col = FirstDayCol + d - 1;
                ws.Range(3, col, Math.Max(lastItemRow, 4), col).Style.Fill.BackgroundColor = WeekendFill;
                ws.Range(3, col, 4, col).Style.Font.FontColor = HolidayFont;
                if (HolidayService.NameOf(date) is { } name)
                    ws.Cell(3, col).CreateComment().AddText(name);
            }

            // 框線、欄寬、凍結窗格、列印
            var table = ws.Range(3, 1, Math.Max(lastItemRow, 4), AmountCol);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(3, FirstDayCol, Math.Max(lastItemRow, 4), AmountCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(CodeCol).Width = 8;
            ws.Column(NameCol).Width = 22;
            ws.Column(PriceCol).Width = 8;
            for (int c = FirstDayCol; c < CountCol; c++) ws.Column(c).Width = 5.5;
            ws.Column(CountCol).Width = 6;
            ws.Column(AmountCol).Width = 10;
            ws.SheetView.Freeze(HeaderRows, PriceCol);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
        }

        /// <summary>代班個案的工作表：D2「代班」，後面寫原居服員和備註（匯入時靠這幾格認出是代班）。</summary>
        private static void WriteSubstituteMark(IXLWorksheet ws, Client client)
        {
            var mark = ws.Cell(2, FirstDayCol);
            mark.Value = SubstituteMark;
            mark.Style.Font.Bold = true;
            mark.Style.Font.FontColor = SubstituteFont;
            mark.Style.Fill.BackgroundColor = SubstituteFill;
            if (client.CoverFor.Length > 0) ws.Cell(2, FirstDayCol + 2).Value = CoverForLabel + client.CoverFor;
            if (client.Note.Length > 0) ws.Cell(2, FirstDayCol + 8).Value = NoteLabel + client.Note;
        }

        /// <summary>有代班時，第一張表的月薪旁邊再分開寫「個案」和「代班」各自的合計。</summary>
        private static void WritePayBreakdown(IXLWorksheet ws, MonthRecord record, List<string> names)
        {
            var clients = record.AllClients.ToList();
            string Sum(bool substitute) =>
                string.Join("+", clients.Select((c, i) => (c, i)).Where(x => record.IsSubstitute(x.c.Id) == substitute)
                    .Select(x => x.i == 0 ? "C3" : $"{QuoteSheet(names[x.i])}!C3")) is { Length: > 0 } f ? f : "0";

            void Write(int col, string label, string formula)
            {
                ws.Cell(1, col).Value = label;
                var value = ws.Range(1, col + 1, 1, col + 3).Merge();
                value.FirstCell().FormulaA1 = formula;
                value.Style.NumberFormat.Format = "#,##0.##";
                ws.Range(1, col, 1, col + 3).Style.Font.Bold = true;
            }
            Write(FirstDayCol + 1, "個案", Sum(false));
            Write(FirstDayCol + 6, SubstituteMark, Sum(true));
            ws.Cell(1, FirstDayCol + 6).Style.Font.FontColor = SubstituteFont;
        }

        private static void WriteLocations(IXLWorksheet ws, MonthRecord record)
        {
            ws.Cell(1, 1).Value = "服物地點";
            ws.Cell(1, 1).Style.Font.Bold = true;
            for (int i = 0; i < record.Clients.Count; i++)
                ws.Cell(i + 2, 1).Value = record.Clients[i].Label;
            ws.Column(1).Width = 24;
            if (record.Substitutes.Count == 0) return;

            // 代班個案另外列一段
            int row = record.Clients.Count + 3;
            ws.Cell(row, 1).Value = SubstituteMark;
            ws.Cell(row, 1).Style.Font.Bold = true;
            foreach (var s in record.Substitutes)
            {
                row++;
                ws.Cell(row, 1).Value = s.Label;
                ws.Cell(row, 2).Value = s.SubstituteInfo;
            }
            ws.Column(2).Width = 30;
        }

        private static void WritePriceList(IXLWorksheet ws, MonthRecord record)
        {
            ws.Cell(1, 1).Value = "服務項目代號";
            ws.Cell(1, 2).Value = "內容說明";
            ws.Cell(1, 3).Value = "支付金額";
            ws.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < record.Items.Count; i++)
            {
                ws.Cell(i + 2, 1).Value = record.Items[i].Code;
                ws.Cell(i + 2, 2).Value = record.Items[i].Name;
                ws.Cell(i + 2, 3).Value = record.Items[i].Price;
            }
            ws.Column(1).Width = 14;
            ws.Column(2).Width = 24;
            ws.Column(3).Width = 10;
        }

        /// <summary>欄號轉成 Excel 欄名：1 → A、36 → AJ。</summary>
        public static string Col(int column) => XLHelper.GetColumnLetterFromNumber(column);

        private static string QuoteSheet(string name) => "'" + name.Replace("'", "''") + "'";

        /// <summary>工作表名稱：去掉 Excel 不允許的字元、最長 31 字、不重複。</summary>
        private static string UniqueSheetName(string wanted, List<string> used)
        {
            var name = new string(wanted.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
            if (name.Length == 0) name = "個案";
            if (name.Length > 31) name = name[..31];
            var candidate = name;
            for (int n = 2; used.Contains(candidate, StringComparer.OrdinalIgnoreCase); n++)
            {
                var suffix = $"-{n}";
                candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] : name) + suffix;
            }
            return candidate;
        }
    }
}
