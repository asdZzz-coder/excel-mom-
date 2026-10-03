using ServiceRecord.Services;

namespace ServiceRecord.Tests
{
    /// <summary>字體放大縮小：倍率範圍、設定檔 zoom.txt 的讀寫。</summary>
    public class ZoomServiceTests
    {
        [Theory]
        [InlineData("1.2", 1.2)]
        [InlineData(" 1.5\r\n", 1.5)]
        [InlineData("0.5", ZoomService.Min)]
        [InlineData("9", ZoomService.Max)]
        [InlineData("", 1.0)]
        [InlineData(null, 1.0)]
        [InlineData("abc", 1.0)]
        [InlineData("NaN", 1.0)]
        [InlineData("Infinity", 1.0)]
        public void Parse_ReadsSettingOrFallsBackTo100Percent(string? text, double expected) =>
            Assert.Equal(expected, ZoomService.Parse(text));

        [Theory]
        [InlineData(0.8)]
        [InlineData(1.0)]
        [InlineData(1.3)]
        [InlineData(2.0)]
        public void SettingText_RoundTrips(double scale) =>
            Assert.Equal(scale, ZoomService.Parse(ZoomService.ToSettingText(scale)));

        [Fact]
        public void Clamp_RemovesStepRoundingErrors()
        {
            // 從 80% 一路按「放大」到 200%，每一步都是整齊的 10%
            var s = ZoomService.Min;
            var steps = new List<double>();
            while (s < ZoomService.Max)
            {
                s = ZoomService.Clamp(s + ZoomService.Step);
                steps.Add(s);
            }
            Assert.Equal([0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.0], steps);
        }

        [Fact]
        public void SettingText_UsesDotEvenOnCommaLocales()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("1.2", ZoomService.ToSettingText(1.2));
                Assert.Equal(1.2, ZoomService.Parse("1.2"));
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }
    }
}
