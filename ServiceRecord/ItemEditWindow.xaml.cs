using System.Windows;
using System.Windows.Controls;
using ServiceRecord.Models;
using ServiceRecord.Services;

namespace ServiceRecord
{
    /// <summary>在目前的月份加一個服務項目：可以從設定的清單選，也可以自己填新的代碼、名稱、單價。</summary>
    public partial class ItemEditWindow : Window
    {
        private readonly MonthRecord _record;
        private readonly AppSettings _settings;

        public ServiceItem? Result { get; private set; }
        public bool AddToSettings => AddToSettingsBox.IsChecked == true && AddToSettingsBox.IsEnabled;

        public ItemEditWindow(MonthRecord record, AppSettings settings)
        {
            InitializeComponent();
            ZoomService.FitWindow(this);       // 字體放大時視窗也跟著放大（不超過螢幕）
            ThemeService.TrackTitleBar(this); // 標題列跟著深淺色變
            _record = record;
            _settings = settings;
            IntroText.Text = $"加到 {record.Year} 年 {record.Month} 月（所有個案都會多這一列）。";

            // 清單裡這個月還沒有的項目（設定的項目 + 預設項目）
            var candidates = settings.ServiceItems.Concat(ServiceItem.Defaults())
                .Where(i => !record.HasItem(i.Code))
                .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToList();
            PresetBox.ItemsSource = candidates;
            PresetBox.IsEnabled = candidates.Count > 0;
            CodeBox.TextChanged += (_, _) => UpdateSettingsBox();
            UpdateSettingsBox();
            Loaded += (_, _) => (candidates.Count > 0 ? (Control)PresetBox : CodeBox).Focus();
        }

        private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetBox.SelectedItem is not ServiceItem item) return;
            CodeBox.Text = item.Code;
            NameBox.Text = item.Name;
            PriceBox.Text = item.Price.ToString();
        }

        /// <summary>設定裡已經有這個代碼時，不需要再加一次。</summary>
        private void UpdateSettingsBox()
        {
            bool inSettings = _settings.ServiceItems.Any(i => string.Equals(i.Code, CodeBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            AddToSettingsBox.IsEnabled = !inSettings;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var code = CodeBox.Text.Trim();
            var name = NameBox.Text.Trim();
            string? error =
                code.Length == 0 ? "請填代碼，例如 BA13。" :
                _record.HasItem(code) ? $"這個月已經有 {code} 了。" :
                name.Length == 0 ? "請填服務項目名稱。" :
                !int.TryParse(PriceBox.Text.Trim(), out var price) || price < 0 ? "單價要是 0 以上的整數。" :
                null;
            if (error != null)
            {
                MessageBox.Show(error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new ServiceItem { Code = code, Name = name, Price = int.Parse(PriceBox.Text.Trim()) };
            DialogResult = true;
        }
    }
}
