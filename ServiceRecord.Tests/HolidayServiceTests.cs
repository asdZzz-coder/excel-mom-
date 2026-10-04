using System.IO;
using System.Reflection;
using ServiceRecord.Services;

namespace ServiceRecord.Tests
{
    /// <summary>台灣國定假日：內附的官方日曆、讀檔檢查、沒有官方日曆時自己算的結果。</summary>
    public class HolidayServiceTests
    {
        private static DateOnly D(int y, int m, int d) => new(y, m, d);

        private static string Bundled(int year)
        {
            using var s = typeof(HolidayService).Assembly.GetManifestResourceStream($"ServiceRecord.Holidays.{year}.json")!;
            return new StreamReader(s).ReadToEnd();
        }

        // ---------- 官方日曆（內附 2026、2027） ----------

        [Theory]
        [InlineData(2026, 6, 19, true, "端午節")]          // 週五國定假日
        [InlineData(2026, 6, 20, true, null)]              // 一般週六
        [InlineData(2026, 6, 22, false, null)]             // 平日
        [InlineData(2026, 2, 20, true, "補假")]            // 春節補假
        [InlineData(2026, 10, 9, true, "補假")]            // 國慶日（週六）提前一天補假
        [InlineData(2026, 9, 28, true, "孔子誕辰紀念日/教師節")]
        [InlineData(2027, 2, 5, true, "農曆除夕")]
        public void Official2026And2027(int y, int m, int d, bool off, string? name)
        {
            var date = D(y, m, d);
            Assert.True(HolidayService.IsOfficial(y));
            Assert.Equal(off, HolidayService.IsOffDay(date));
            Assert.Equal(name, HolidayService.NameOf(date));
        }

        [Fact]
        public void NamedHolidaysIn_June2026()
        {
            Assert.Equal([(D(2026, 6, 19), "端午節")], HolidayService.NamedHolidaysIn(2026, 6));
            Assert.Empty(HolidayService.NamedHolidaysIn(2026, 7));
        }

        [Theory]
        [InlineData(2026)]
        [InlineData(2027)]
        public void Parse_AcceptsBundledYear(int year)
        {
            var cal = HolidayService.Parse(Bundled(year), year)!;
            Assert.True(cal.Official);
            Assert.Equal(365, cal.Days.Count);
        }

        [Fact]
        public void Parse_RejectsBrokenOrWrongYear()
        {
            Assert.Null(HolidayService.Parse(Bundled(2026), 2027)); // 年份不對
            Assert.Null(HolidayService.Parse("not json", 2026));
            Assert.Null(HolidayService.Parse("[]", 2026));          // 不是整年
            Assert.Null(HolidayService.Parse("""[{"date":"20260101","isHoliday":true,"description":"開國紀念日"}]""", 2026));
        }

        // ---------- 沒有官方日曆時自己算 ----------

        [Theory]
        [InlineData(2026)]
        [InlineData(2027)]
        public void Computed_MatchesOfficialNamedHolidays(int year)
        {
            // 自己算的應該和官方日曆的國定假日一樣，只差在補假
            var official = HolidayService.Parse(Bundled(year), year)!.Days
                .Where(p => p.Value.IsOff && p.Value.Name.Length > 0 && p.Value.Name != "補假")
                .ToDictionary(p => p.Key, p => p.Value.Name);
            var computed = HolidayService.Computed(year).Days
                .Where(p => p.Value.Name.Length > 0)
                .ToDictionary(p => p.Key, p => p.Value.Name);

            Assert.Equal(official.OrderBy(p => p.Key), computed.OrderBy(p => p.Key));
        }

        [Fact]
        public void Computed_MarksAllWeekends()
        {
            var cal = HolidayService.Computed(2030);
            Assert.False(cal.Official);
            for (var d = D(2030, 1, 1); d.Year == 2030; d = d.AddDays(1))
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    Assert.True(cal.Days[d].IsOff, d.ToString());
        }

        [Theory]
        [InlineData(2024, 2, 10)] // 春節
        [InlineData(2025, 1, 29)]
        [InlineData(2026, 2, 17)]
        [InlineData(2028, 1, 26)]
        public void Lunar_NewYear(int y, int m, int d) => Assert.Equal(D(y, m, d), HolidayService.Lunar(y, 1, 1));

        [Theory]
        [InlineData(2023, 6, 22)] // 2023 有閏二月，端午不能算錯一個月
        [InlineData(2025, 5, 31)] // 2025 有閏六月
        public void Lunar_DragonBoatWithLeapMonth(int y, int m, int d) => Assert.Equal(D(y, m, d), HolidayService.Lunar(y, 5, 5));

        [Fact]
        public void Lunar_MidAutumnAfterLeapMonth() => Assert.Equal(D(2025, 10, 6), HolidayService.Lunar(2025, 8, 15));

        [Theory]
        [InlineData(2024, 4)]
        [InlineData(2025, 4)]
        [InlineData(2026, 5)]
        [InlineData(2027, 5)]
        [InlineData(2028, 4)]
        public void QingMing(int year, int day) => Assert.Equal(D(year, 4, day), HolidayService.QingMing(year));
    }
}
