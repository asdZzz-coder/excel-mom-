using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using ServiceRecord.Services;

namespace ServiceRecord.Tests
{
    /// <summary>
    /// 主題（跟隨系統 / 淺色 / 深色）的自動測試：切換邏輯、設定檔，以及 XAML 與配色表是否一致。
    /// </summary>
    public class ThemeServiceTests
    {
        private static string SourceDir([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here)!, "..", "ServiceRecord");

        private static string ReadXaml(string name) => File.ReadAllText(Path.Combine(SourceDir(), name));

        public static TheoryData<string> XamlFiles => new() { "App.xaml", "MainWindow.xaml", "SettingsWindow.xaml", "ItemEditWindow.xaml" };

        // ---------- 切換邏輯 ----------

        [Fact]
        public void Next_CyclesSystemLightDarkAndBack()
        {
            Assert.Equal(AppTheme.Light, ThemeService.Next(AppTheme.System));
            Assert.Equal(AppTheme.Dark, ThemeService.Next(AppTheme.Light));
            Assert.Equal(AppTheme.System, ThemeService.Next(AppTheme.Dark));
        }

        [Theory]
        [InlineData(AppTheme.Light, false, false)]
        [InlineData(AppTheme.Light, true, false)]
        [InlineData(AppTheme.Dark, false, true)]
        [InlineData(AppTheme.Dark, true, true)]
        [InlineData(AppTheme.System, false, false)]
        [InlineData(AppTheme.System, true, true)]
        public void ResolveIsDark_FollowsChoiceOrSystem(AppTheme mode, bool systemIsDark, bool expected) =>
            Assert.Equal(expected, ThemeService.ResolveIsDark(mode, systemIsDark));

        [Fact]
        public void DisplayName_IsChinese()
        {
            Assert.Equal("跟隨系統", ThemeService.DisplayName(AppTheme.System));
            Assert.Equal("淺色", ThemeService.DisplayName(AppTheme.Light));
            Assert.Equal("深色", ThemeService.DisplayName(AppTheme.Dark));
        }

        // ---------- 設定檔 theme.txt ----------

        [Theory]
        [InlineData("light", AppTheme.Light)]
        [InlineData("dark", AppTheme.Dark)]
        [InlineData("system", AppTheme.System)]
        [InlineData(" Dark\r\n", AppTheme.Dark)]
        [InlineData("", AppTheme.System)]
        [InlineData(null, AppTheme.System)]
        [InlineData("purple", AppTheme.System)]
        public void Parse_ReadsSettingOrFallsBackToSystem(string? text, AppTheme expected) =>
            Assert.Equal(expected, ThemeService.Parse(text));

        [Theory]
        [InlineData(AppTheme.System)]
        [InlineData(AppTheme.Light)]
        [InlineData(AppTheme.Dark)]
        public void SettingText_RoundTrips(AppTheme mode) =>
            Assert.Equal(mode, ThemeService.Parse(ThemeService.ToSettingText(mode)));

        // ---------- 配色表 ----------

        [Fact]
        public void Palette_KeysAreUniqueAndColorsValid()
        {
            var keys = ThemeService.Palette.Select(p => p.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            foreach (var (_, light, dark) in ThemeService.Palette)
            {
                ThemeService.ParseColor(light);
                ThemeService.ParseColor(dark);
            }
        }

        [Fact]
        public void Palette_LightColorsMatchAppXamlDefaults()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xamlBrushes = XDocument.Parse(ReadXaml("App.xaml")).Descendants()
                .Where(e => e.Name.LocalName == "SolidColorBrush" && e.Attribute(x + "Key") != null)
                .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Attribute("Color")!.Value.ToUpperInvariant());

            var palette = ThemeService.Palette.ToDictionary(p => p.Key, p => p.Light.ToUpperInvariant());
            Assert.Equal(xamlBrushes.Keys.Order(), palette.Keys.Order());
            foreach (var (key, color) in xamlBrushes)
                Assert.True(palette[key] == color, $"{key}: App.xaml 是 {color}，Palette 是 {palette[key]}");
        }

        [Theory]
        [MemberData(nameof(XamlFiles))]
        public void Xaml_UsesOnlyDynamicPaletteBrushes(string file)
        {
            var xaml = ReadXaml(file);
            var known = ThemeService.Palette.Select(p => p.Key).Append("AccentGradient").ToHashSet();

            // 顏色一律用 DynamicResource，否則切換主題時不會更新
            Assert.DoesNotMatch(@"StaticResource \w*(Brush|Gradient)\}", xaml);
            foreach (Match m in Regex.Matches(xaml, @"DynamicResource (\w*(?:Brush|Gradient))\}"))
                Assert.True(known.Contains(m.Groups[1].Value), $"{file} 用到 {m.Groups[1].Value}，但 Palette 沒有深色版本");
        }

        [Theory]
        [InlineData("MainWindow.xaml")]
        [InlineData("SettingsWindow.xaml")]
        [InlineData("ItemEditWindow.xaml")]
        public void WindowXaml_HasNoHardcodedColors(string file)
        {
            // 不允許寫死顏色（月薪卡片上的字用 White）
            var allowed = Array.Empty<string>();
            var found = Regex.Matches(ReadXaml(file), "#[0-9A-Fa-f]{6}\\b").Select(m => m.Value.ToUpperInvariant());
            Assert.All(found, c => Assert.Contains(c, allowed));
        }

        [Fact]
        public void Code_LooksUpBrushesAsResourceReferences()
        {
            // 程式裡用 FindResource 取筆刷只會拿到當下的顏色，切換主題不會更新，要改用 SetResourceReference
            var code = File.ReadAllText(Path.Combine(SourceDir(), "MainWindow.xaml.cs"));
            Assert.DoesNotMatch(@"FindResource\(""\w*Brush""\)", code);
        }

        [Fact]
        public void NumberInputs_DisableChineseInputMethod()
        {
            // 注音輸入法開著時，數字鍵會變成注音符號（5 → ㄓ），次數、單價、比例都打不進去
            Assert.Contains("x:Name=\"ItemsGrid\"", ReadXaml("MainWindow.xaml"));
            Assert.Matches(@"x:Name=""ItemsGrid""[^>]*InputMethod\.IsInputMethodEnabled=""False""", ReadXaml("MainWindow.xaml"));
            Assert.Matches(@"x:Key=""CellEditBox""[^>]*>\s*<Setter Property=""InputMethod\.IsInputMethodEnabled"" Value=""False""/>", ReadXaml("App.xaml"));
            Assert.Matches(@"x:Name=""PriceBox""[^>]*InputMethod\.IsInputMethodEnabled=""False""", ReadXaml("ItemEditWindow.xaml"));
            Assert.Matches(@"x:Name=""RatioBox""[^>]*InputMethod\.IsInputMethodEnabled=""False""", ReadXaml("SettingsWindow.xaml"));
            // 姓名、地點等文字格要能打中文
            Assert.Matches(@"x:Key=""CellEditBoxLeft""[\s\S]*?InputMethod\.IsInputMethodEnabled"" Value=""True""", ReadXaml("App.xaml"));
        }

        [Theory]
        [MemberData(nameof(XamlFiles))]
        public void Windows_ScaleWithZoom(string file)
        {
            if (file == "App.xaml") return;
            Assert.Contains("LayoutTransform=\"{DynamicResource UiScale}\"", ReadXaml(file));
        }

        // ---------- 可讀性：文字與背景的對比（WCAG AAA：7:1，老花也看得清楚） ----------

        /// <summary>每一種文字色，和它會出現的每一種底色。</summary>
        public static TheoryData<string, string> TextOnBackground => new()
        {
            // 一般文字：卡片、視窗、輸入框、表格各種底色
            { "TextBrush", "CardBrush" }, { "TextBrush", "AppBgBrush" }, { "TextBrush", "InputBgBrush" },
            { "TextBrush", "AltRowBrush" }, { "TextBrush", "CellSelectedBrush" }, { "TextBrush", "WeekendBrush" },
            { "TextBrush", "TotalBrush" }, { "TextBrush", "AccentSoftBrush" }, { "TextBrush", "HoverBrush" },
            { "TextBrush", "GhostHoverBrush" }, { "TextBrush", "CrossBrush" }, { "TextBrush", "CrossHeaderBrush" },
            // 灰色說明文字、星期
            { "MutedBrush", "CardBrush" }, { "MutedBrush", "AppBgBrush" }, { "MutedBrush", "AccentSoftBrush" },
            { "MutedBrush", "CrossHeaderBrush" },
            // 紅字：假日日期、本月假日、刪除按鈕
            { "DangerBrush", "CardBrush" }, { "DangerBrush", "AccentSoftBrush" }, { "DangerBrush", "DangerSoftBrush" },
            { "DangerBrush", "CrossHeaderBrush" },
            // 藍字：次數、實領、選到的分頁
            { "AccentTextBrush", "CardBrush" }, { "AccentTextBrush", "AccentSoftBrush" }, { "AccentTextBrush", "TotalBrush" },
            { "AccentTextBrush", "CrossBrush" }, { "AccentTextBrush", "CellSelectedBrush" },
        };

        [Theory]
        [MemberData(nameof(TextOnBackground))]
        public void Palette_TextIsClearlyReadableInBothThemes(string fg, string bg) => AssertContrast(fg, bg, 7.0);

        [Theory]
        // 非文字：強調色線條（選取框、勾選框）、表格格線要看得出來
        [InlineData("AccentBrush", "CardBrush", 3.0)]
        [InlineData("GridLineBrush", "CardBrush", 1.5)]
        [InlineData("GridLineBrush", "AltRowBrush", 1.5)]
        [InlineData("GridLineBrush", "WeekendBrush", 1.4)]
        public void Palette_LinesAreVisibleInBothThemes(string fg, string bg, double minRatio) => AssertContrast(fg, bg, minRatio);

        private static void AssertContrast(string fg, string bg, double minRatio)
        {
            var p = ThemeService.Palette.ToDictionary(e => e.Key);
            foreach (var dark in new[] { false, true })
            {
                var f = ThemeService.ParseColor(dark ? p[fg].Dark : p[fg].Light);
                var b = ThemeService.ParseColor(dark ? p[bg].Dark : p[bg].Light);
                var ratio = Contrast(f, b);
                Assert.True(ratio >= minRatio, $"{(dark ? "深色" : "淺色")} {fg} / {bg} 對比 {ratio:F2} < {minRatio}");
            }
        }

        [Fact]
        public void WhiteText_OnButtonsAndPayCard_IsClearlyReadable()
        {
            // 主要按鈕（匯出 Excel、儲存）和月薪卡片（漸層）上的白字
            var p = ThemeService.Palette.ToDictionary(e => e.Key);
            var g = ThemeService.Gradient;
            foreach (var hex in new[] { p["PrimaryBrush"].Light, p["PrimaryBrush"].Dark, p["AccentHoverBrush"].Light, p["AccentHoverBrush"].Dark,
                                        p["AccentPressedBrush"].Light, p["AccentPressedBrush"].Dark, g.Light1, g.Light2, g.Dark1, g.Dark2 })
            {
                var ratio = Contrast(Colors.White, ThemeService.ParseColor(hex));
                Assert.True(ratio >= 7.0, $"白字 / {hex} 對比 {ratio:F2} < 7");
            }
        }

        private static double Contrast(Color a, Color b)
        {
            static double Lum(Color c)
            {
                static double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
                return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
            }
            var (l1, l2) = (Lum(a), Lum(b));
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }
    }
}
