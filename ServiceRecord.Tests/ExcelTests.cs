using System.IO;
using ClosedXML.Excel;
using ServiceRecord.Models;
using ServiceRecord.Services;

namespace ServiceRecord.Tests
{
    /// <summary>
    /// 只有在 example 資料夾有原本的 Excel 時才跑（範例檔含個案資料，不放上 GitHub，所以 CI 上會略過）。
    /// </summary>
    public sealed class ExampleFactAttribute : FactAttribute
    {
        public ExampleFactAttribute(string fileName)
        {
            if (ExcelTests.ExampleFile(fileName) == null) Skip = $"找不到範例檔 {fileName}";
        }
    }

    public sealed class ExcelTests : IDisposable
    {
        public const string June = "2026-06份 洪淑瑩靜鑫服務紀錄表.xlsx";
        public const string December = "2026-12份 洪淑瑩靜鑫服務紀錄表.xlsx";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ServiceRecord-Tests-" + Guid.NewGuid().ToString("N"));

        public ExcelTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        /// <summary>往上層找 example\檔名（測試從 bin\Debug\… 執行）。</summary>
        public static string? ExampleFile(string fileName)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "example", fileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static MonthRecord Import(string fileName, AppSettings? settings = null) =>
            ExcelImporter.ToMonthRecord(ExcelImporter.Read(ExampleFile(fileName)!), settings ?? new AppSettings()).Record;

        private static Client ClientNamed(MonthRecord record, string name) => record.Clients.Single(c => c.Name == name);

        // ---------- 匯入原本的檔案 ----------

        [ExampleFact(June)]
        public void ImportJune_MatchesExcelTotals()
        {
            var record = Import(June);

            Assert.Equal(2026, record.Year);
            Assert.Equal(6, record.Month);
            Assert.Equal(0.6m, record.ShareRatio);
            Assert.Equal(["王林妙芳", "黃景", "林美麗"], record.Clients.Select(c => c.Name).ToArray());

            var wang = ClientNamed(record, "王林妙芳");
            Assert.Equal("慶東街", wang.Location);
            Assert.Equal(39, record.CountOf(wang.Id, "BA13"));
            Assert.Equal(21, record.CountOf(wang.Id, "BA16"));
            Assert.Equal(14705, PayCalculator.Subtotal(record, wang.Id));
            Assert.Equal(8823m, PayCalculator.NetPay(record, wang.Id));
            Assert.Equal(16932m, PayCalculator.NetPay(record, ClientNamed(record, "黃景").Id));
            Assert.Equal(852m, PayCalculator.NetPay(record, ClientNamed(record, "林美麗").Id));

            // 原本 Excel 的月薪是 24423，少算了黃景的 BA01（見下一個測試），正確是 26607
            Assert.Equal(26607m, PayCalculator.MonthlyPay(record));
        }

        [ExampleFact(June)]
        public void ImportJune_DifferenceFromExcelIsOnlyTheSkippedFirstRow()
        {
            // 原本的檔案：(黃景)、(林美麗) 兩張的項目從第 4 列開始，合計公式卻是 SUM(AJ5:AJ34)，
            // 漏掉第 4 列的 BA01 基本身體清潔。程式照每一列加總，所以只差在這一列。
            var record = Import(June);
            using var wb = new XLWorkbook(ExampleFile(June)!);
            Assert.Equal("(SUM(AJ5:AJ34)*0.6)", wb.Worksheet("(黃景)").Cell("AJ35").FormulaA1);

            foreach (var (sheet, cell, name) in new[] { ("(王林妙芳)", "C3", "王林妙芳"), ("(黃景)", "C2", "黃景"), ("(林美麗)", "C2", "林美麗") })
            {
                var client = ClientNamed(record, name);
                var excel = (decimal)wb.Worksheet(sheet).Cell(cell).CachedValue.GetNumber();
                var skipped = sheet == "(王林妙芳)" ? 0 : record.CountOf(client.Id, "BA01") * 260 * 0.6m;
                Assert.Equal(excel + skipped, PayCalculator.NetPay(record, client.Id));
            }
        }

        [ExampleFact(June)]
        public void ImportJune_StaleLocationLabelIsNotUsed()
        {
            // 林美麗那張的 A1 寫的是「裕善街(徐春華)」，姓名對不上就不採用地點
            var record = Import(June);
            Assert.Equal("", ClientNamed(record, "林美麗").Location);
            Assert.Equal("東門路", ClientNamed(record, "黃景").Location);
        }

        [ExampleFact(December)]
        public void ImportDecemberTemplate_IsEmpty()
        {
            var record = Import(December);

            Assert.Equal(2026, record.Year);
            Assert.Equal(12, record.Month);
            Assert.Equal(3, record.Clients.Count);
            Assert.False(record.HasAnyCounts);
            Assert.Equal(0m, PayCalculator.MonthlyPay(record));
        }

        [ExampleFact(June)]
        public void ImportTwice_ReusesExistingClients()
        {
            var settings = new AppSettings();
            var first = ExcelImporter.ToMonthRecord(ExcelImporter.Read(ExampleFile(June)!), settings);
            var second = ExcelImporter.ToMonthRecord(ExcelImporter.Read(ExampleFile(December)!), settings);

            Assert.Equal(3, first.AddedClients);
            Assert.Equal(0, second.AddedClients);
            Assert.Equal(3, settings.Clients.Count);
            Assert.Equal(first.Record.Clients.Select(c => c.Id), second.Record.Clients.Select(c => c.Id));
        }

        // ---------- 匯出 ----------

        private static MonthRecord Sample(int year = 2026, int month = 6)
        {
            var settings = new AppSettings
            {
                Clients =
                [
                    new Client { Name = "甲", Location = "慶東街" },
                    new Client { Name = "乙", Location = "東門路" },
                ],
            };
            var record = MonthRecord.CreateFrom(settings, year, month);
            var a = record.Clients[0].Id;
            var b = record.Clients[1].Id;
            record.GetDays(a, "BA13")[1] = 3;   // 2 日 3 次
            record.GetDays(a, "BA13")[4] = 5;
            record.GetDays(a, "BA14")[1] = 1;
            record.GetDays(b, "BA16")[0] = 2;
            record.GetDays(b, "AA09")[19] = 1;
            return record;
        }

        private string ExportToTemp(MonthRecord record)
        {
            var path = Path.Combine(_root, ExcelExporter.DefaultFileName(record, "洪淑瑩靜鑫"));
            ExcelExporter.Export(record, path);
            return path;
        }

        [Fact]
        public void Export_MarksHolidaysAndWeekends()
        {
            var path = ExportToTemp(Sample(2026, 6));

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var pink = XLColor.FromHtml("#FDECEC");
            int Col(int day) => ExcelExporter.FirstDayCol + day - 1;

            // 6/19 端午節（週五）：上底色、紅字、註解寫節日名稱
            Assert.Equal(pink, ws.Cell(5, Col(19)).Style.Fill.BackgroundColor);
            Assert.True(ws.Cell(3, Col(19)).HasComment);
            Assert.Contains("端午節", ws.Cell(3, Col(19)).GetComment().Text);
            // 6/20 週六：上底色、沒有註解
            Assert.Equal(pink, ws.Cell(5, Col(20)).Style.Fill.BackgroundColor);
            Assert.False(ws.Cell(3, Col(20)).HasComment);
            // 6/18 平日
            Assert.NotEqual(pink, ws.Cell(5, Col(18)).Style.Fill.BackgroundColor);
        }

        [Fact]
        public void DefaultFileName_MatchesOriginalNaming()
        {
            Assert.Equal("2026-06份 洪淑瑩靜鑫服務紀錄表.xlsx", ExcelExporter.DefaultFileName(Sample(), "洪淑瑩靜鑫"));
        }

        [Fact]
        public void Export_ThenImport_RoundTripsCounts()
        {
            var record = Sample();
            var path = ExportToTemp(record);

            var back = ExcelImporter.ToMonthRecord(ExcelImporter.Read(path), new AppSettings()).Record;

            Assert.Equal(record.Year, back.Year);
            Assert.Equal(record.Month, back.Month);
            Assert.Equal(record.ShareRatio, back.ShareRatio);
            Assert.Equal(record.Items.Select(i => (i.Code, i.Name, i.Price)), back.Items.Select(i => (i.Code, i.Name, i.Price)));
            for (int i = 0; i < record.Clients.Count; i++)
            {
                Assert.Equal(record.Clients[i].Name, back.Clients[i].Name);
                Assert.Equal(record.Clients[i].Location, back.Clients[i].Location);
                foreach (var item in record.Items)
                    Assert.Equal(record.GetDays(record.Clients[i].Id, item.Code), back.GetDays(back.Clients[i].Id, item.Code));
            }
        }

        [Fact]
        public void Export_FormulasCalculateSameTotals()
        {
            var record = Sample();
            using var wb = new XLWorkbook(ExportToTemp(record));

            var first = wb.Worksheet("(甲)");
            var second = wb.Worksheet("(乙)");
            Assert.Equal((double)PayCalculator.NetPay(record, record.Clients[0].Id), first.Cell("C3").Value.GetNumber());
            Assert.Equal((double)PayCalculator.NetPay(record, record.Clients[1].Id), second.Cell("C3").Value.GetNumber());
            Assert.Equal((double)PayCalculator.MonthlyPay(record), first.Cell("C1").Value.GetNumber());
            Assert.Equal("慶東街(甲)", first.Cell("A2").GetString());
        }

        [Fact]
        public void Export_June_Has30DateColumns()
        {
            var record = Sample(2026, 6);
            using var wb = new XLWorkbook(ExportToTemp(record));
            var ws = wb.Worksheet("(甲)");

            Assert.Equal(new DateTime(2026, 6, 1), ws.Cell(3, ExcelExporter.FirstDayCol).Value.GetDateTime());
            Assert.True(ws.Cell(3, ExcelExporter.FirstDayCol + 29).HasFormula);   // 30 日
            Assert.True(ws.Cell(3, ExcelExporter.FirstDayCol + 30).IsEmpty());    // 沒有 31 日
        }

        [Fact]
        public void LeapFebruary_Has29Days()
        {
            var record = Sample(2028, 2);
            Assert.Equal(29, record.DaysInMonth);
            record.GetDays(record.Clients[0].Id, "BA01")[29] = 9; // 30 日不存在，不算
            Assert.Equal(0, record.CountOf(record.Clients[0].Id, "BA01"));
        }

        // ---------- 新增 / 刪除服務項目 ----------

        [Fact]
        public void AddedAndRemovedItems_AreExportedWithCorrectSumRange()
        {
            var record = Sample();
            record.RemoveItem("BA01");
            record.RemoveItem("BA02");
            record.AddItem(new ServiceItem { Code = "ZZ01", Name = "自訂項目", Price = 100 });
            record.GetDays(record.Clients[0].Id, "ZZ01")[2] = 4;

            using var wb = new XLWorkbook(ExportToTemp(record));
            var ws = wb.Worksheet("(甲)");
            int total = ExcelExporter.TotalRow(record);

            Assert.Equal(record.Items.Count, total - ExcelExporter.FirstItemRow);
            Assert.Equal("ZZ01", ws.Cell(total - 1, 1).GetString());
            Assert.Equal($"(SUM(AJ5:AJ{total - 1})*0.6)", ws.Cell(total, ExcelExporter.AmountCol).FormulaA1);
            Assert.Equal((double)PayCalculator.NetPay(record, record.Clients[0].Id), ws.Cell("C3").Value.GetNumber());
        }

        [Fact]
        public void AddItem_RejectsDuplicateCode()
        {
            var record = Sample();
            Assert.Throws<InvalidOperationException>(() => record.AddItem(new ServiceItem { Code = "ba13", Name = "重複", Price = 1 }));
        }

        [Fact]
        public void ChangingSettings_DoesNotAffectExistingMonth()
        {
            var settings = new AppSettings { Clients = [new Client { Name = "甲" }] };
            var june = MonthRecord.CreateFrom(settings, 2026, 6);
            june.GetDays(june.Clients[0].Id, "BA13")[0] = 2;
            var before = PayCalculator.MonthlyPay(june);

            settings.ServiceItems.Single(i => i.Code == "BA13").Price = 999;
            settings.ServiceItems.RemoveAll(i => i.Code == "BA14");

            Assert.Equal(before, PayCalculator.MonthlyPay(june));
            Assert.True(june.HasItem("BA14"));
        }

        [Fact]
        public void ApplySettings_KeepsOrRemovesItemsThatHaveCounts()
        {
            var settings = new AppSettings { Clients = [new Client { Name = "甲" }] };
            var keep = MonthRecord.CreateFrom(settings, 2026, 6);
            var remove = MonthRecord.CreateFrom(settings, 2026, 6);
            foreach (var r in new[] { keep, remove }) r.GetDays(r.Clients[0].Id, "BA13")[0] = 2;

            settings.ServiceItems.RemoveAll(i => i.Code is "BA13" or "BA14");
            settings.ServiceItems.Add(new ServiceItem { Code = "ZZ01", Name = "新項目", Price = 50 });

            Assert.Single(keep.RemovedWithCounts(settings).Items);
            keep.ApplySettings(settings, removeWithCounts: false);
            remove.ApplySettings(settings, removeWithCounts: true);

            Assert.True(keep.HasItem("BA13"));      // 有次數，保留
            Assert.False(keep.HasItem("BA14"));     // 沒次數，直接拿掉
            Assert.True(keep.HasItem("ZZ01"));
            Assert.Equal(2, keep.CountOf(keep.Clients[0].Id, "BA13"));
            Assert.False(remove.HasItem("BA13"));
            Assert.Equal(0, remove.TotalCountOfClient(remove.Clients[0].Id));
        }

        // ---------- 代班 ----------

        /// <summary>Sample() 再加一位代班個案「丙」：替王小明代班，3 日 BA13 2 次、4 日 BA01 1 次。</summary>
        private static MonthRecord SampleWithSubstitute(AppSettings? settings = null)
        {
            var record = Sample();
            var sub = new Client { Name = "丙", Location = "裕善街", CoverFor = "王小明", Note = "6/3～6/5" };
            record.AddSubstitute(sub);
            settings?.RememberSubstitute(sub);
            record.GetDays(sub.Id, "BA13")[2] = 2;
            record.GetDays(sub.Id, "BA01")[3] = 1;
            return record;
        }

        [Fact]
        public void Substitutes_ArePaidSeparately()
        {
            var plain = Sample();
            var record = SampleWithSubstitute();
            var sub = record.Substitutes.Single();

            Assert.Equal(PayCalculator.MonthlyPay(plain), PayCalculator.RegularPay(record)); // 正常個案不受影響
            Assert.Equal(PayCalculator.NetPay(record, sub.Id), PayCalculator.SubstitutePay(record));
            Assert.True(PayCalculator.SubstitutePay(record) > 0);
            Assert.Equal(PayCalculator.RegularPay(record) + PayCalculator.SubstitutePay(record), PayCalculator.MonthlyPay(record));
            Assert.DoesNotContain(record.Clients, c => c.Id == sub.Id);
            Assert.True(record.IsSubstitute(sub.Id));
            Assert.Equal(3, record.TotalCountOfClient(sub.Id));
        }

        [Fact]
        public void Substitutes_StayInTheirOwnMonth()
        {
            var settings = new AppSettings { Clients = [new Client { Name = "甲" }] };
            var june = MonthRecord.CreateFrom(settings, 2026, 6);
            var sub = new Client { Name = "丙" };
            june.AddSubstitute(sub);
            settings.RememberSubstitute(sub);
            june.GetDays(sub.Id, "BA13")[0] = 2;

            // 新月份不會帶入代班；套用設定也不會動到代班
            Assert.Empty(MonthRecord.CreateFrom(settings, 2026, 7).Substitutes);
            Assert.DoesNotContain(settings.Clients, c => c.Name == "丙");
            settings.Clients.Add(new Client { Name = "乙" });
            june.ApplySettings(settings, removeWithCounts: true);
            Assert.Single(june.Substitutes);
            Assert.Equal(2, june.CountOf(sub.Id, "BA13"));
            Assert.True(june.MatchesSettings(settings));

            // 刪除項目時，代班的次數也算在內（會先問）
            Assert.Equal(2, june.TotalCountOf("BA13"));
            Assert.True(june.HasAnyCounts);

            june.RemoveSubstitute(sub.Id);
            Assert.Empty(june.Substitutes);
            Assert.False(june.Counts.ContainsKey(sub.Id));
            Assert.Single(settings.SubstituteHistory); // 記錄還在，下次可以直接挑
        }

        [Fact]
        public void AddSubstitute_RejectsSamePersonTwice()
        {
            var record = SampleWithSubstitute();
            Assert.Throws<InvalidOperationException>(() => record.AddSubstitute(record.Substitutes[0]));
        }

        [Fact]
        public void Export_PutsSubstitutesOnTheirOwnSheets()
        {
            var record = SampleWithSubstitute();
            using var wb = new XLWorkbook(ExportToTemp(record));

            Assert.Equal(["(甲)", "(乙)", "代班(丙)", "服物地點", "服務項目選項"], wb.Worksheets.Select(w => w.Name));
            var ws = wb.Worksheet("代班(丙)");
            Assert.Equal("裕善街(丙)", ws.Cell("A2").GetString());
            Assert.Equal("代班", ws.Cell(2, ExcelExporter.FirstDayCol).GetString());
            Assert.Contains(ws.Row(2).CellsUsed(), c => c.GetString() == "原居服員：王小明");
            Assert.Contains(ws.Row(2).CellsUsed(), c => c.GetString() == "備註：6/3～6/5");
            Assert.Equal((double)PayCalculator.SubstitutePay(record), ws.Cell("C3").Value.GetNumber());

            // 第一張：月薪含代班，旁邊分開寫個案、代班合計
            var first = wb.Worksheet("(甲)");
            Assert.Equal((double)PayCalculator.MonthlyPay(record), first.Cell("C1").Value.GetNumber());
            var row1 = first.Row(1).CellsUsed().ToList();
            int regular = row1.FindIndex(c => c.GetString() == "個案");
            int substitute = row1.FindIndex(c => c.GetString() == "代班");
            Assert.Equal((double)PayCalculator.RegularPay(record), row1[regular + 1].Value.GetNumber());
            Assert.Equal((double)PayCalculator.SubstitutePay(record), row1[substitute + 1].Value.GetNumber());

            Assert.Contains(wb.Worksheet("服物地點").CellsUsed(), c => c.GetString() == "裕善街(丙)");
        }

        [Fact]
        public void Export_WithoutSubstitutes_KeepsOriginalLayout()
        {
            using var wb = new XLWorkbook(ExportToTemp(Sample()));
            var first = wb.Worksheet("(甲)");
            Assert.Equal(["月薪", ""], first.Row(1).CellsUsed().Select(c => c.HasFormula ? "" : c.GetString()));
            Assert.True(first.Cell(2, ExcelExporter.FirstDayCol).IsEmpty());
        }

        [Fact]
        public void Export_ThenImport_RoundTripsSubstitutes()
        {
            var original = new AppSettings();
            var record = SampleWithSubstitute(original);
            var path = ExportToTemp(record);
            var sub = record.Substitutes.Single();

            // 同一台電腦再匯入：沿用原本的 Id
            var (back, added) = ExcelImporter.ToMonthRecord(ExcelImporter.Read(path), original);
            Assert.Equal(2, added);                                // 只有甲、乙加到設定
            Assert.DoesNotContain(original.Clients, c => c.Name == "丙");
            Assert.Equal(sub.Id, back.Substitutes.Single().Id);
            Assert.Single(original.SubstituteHistory);

            // 別台電腦（沒有紀錄）匯入：還是認得出是代班
            var fresh = new AppSettings();
            var other = ExcelImporter.ToMonthRecord(ExcelImporter.Read(path), fresh).Record;
            var s = other.Substitutes.Single();
            Assert.Equal(("丙", "裕善街", "王小明", "6/3～6/5"), (s.Name, s.Location, s.CoverFor, s.Note));
            Assert.Equal(["甲", "乙"], other.Clients.Select(c => c.Name));
            Assert.Equal(record.GetDays(sub.Id, "BA13"), other.GetDays(s.Id, "BA13"));
            Assert.Equal(PayCalculator.SubstitutePay(record), PayCalculator.SubstitutePay(other));
            Assert.Equal(PayCalculator.MonthlyPay(record), PayCalculator.MonthlyPay(other));
            Assert.Equal("丙", fresh.SubstituteHistory.Single().Name);
        }

        [Fact]
        public void DataStore_ReadsOldMonthWithoutSubstitutes()
        {
            DataStore.DataDirectory = Path.Combine(_root, "data");
            Directory.CreateDirectory(DataStore.MonthsDirectory);
            File.WriteAllText(Path.Combine(DataStore.MonthsDirectory, "2026-05.json"),
                """{ "Year": 2026, "Month": 5, "ShareRatio": 0.6, "Clients": [ { "Id": "a", "Name": "甲", "Location": "" } ], "Items": [], "Counts": {} }""");

            var may = DataStore.LoadMonth(2026, 5)!;
            Assert.Empty(may.Substitutes);
            Assert.Equal("甲", may.Clients.Single().Name);

            var record = SampleWithSubstitute();
            DataStore.SaveMonth(record);
            var loaded = DataStore.LoadMonth(2026, 6)!;
            Assert.Equal(record.Substitutes.Single().Id, loaded.Substitutes.Single().Id);
            Assert.Equal("王小明", loaded.Substitutes.Single().CoverFor);
            Assert.Equal(PayCalculator.SubstitutePay(record), PayCalculator.SubstitutePay(loaded));
        }

        // ---------- 存檔 ----------

        [Fact]
        public void DataStore_SavesAndLoadsMonth()
        {
            DataStore.DataDirectory = Path.Combine(_root, "data");
            var record = Sample();

            DataStore.SaveMonth(record);
            var loaded = DataStore.LoadMonth(2026, 6)!;

            Assert.True(DataStore.MonthExists(2026, 6));
            Assert.Null(DataStore.LoadMonth(2026, 7));
            Assert.Equal(PayCalculator.MonthlyPay(record), PayCalculator.MonthlyPay(loaded));
            Assert.Equal(record.Clients.Select(c => c.Id), loaded.Clients.Select(c => c.Id));
        }
    }
}
