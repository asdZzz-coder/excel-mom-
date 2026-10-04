using System.Windows;
using System.Windows.Controls;
using ServiceRecord.Models;
using ServiceRecord.Services;

namespace ServiceRecord
{
    /// <summary>新增或修改這個月的代班個案：姓名、地點、原居服員（替誰代班）、備註。可以從以前代過班的個案直接挑。</summary>
    public partial class SubstituteWindow : Window
    {
        private readonly MonthRecord _record;
        private readonly Client? _editing;
        /// <summary>從清單挑的那位（沿用他的 Id，紀錄才接得起來）。</summary>
        private Client? _picked;

        public Client? Result { get; private set; }

        public SubstituteWindow(MonthRecord record, AppSettings settings, Client? editing = null)
        {
            InitializeComponent();
            ZoomService.FitWindow(this);       // 字體放大時視窗也跟著放大（不超過螢幕）
            ThemeService.TrackTitleBar(this); // 標題列跟著深淺色變
            _record = record;
            _editing = editing;

            if (editing != null)
            {
                Title = "修改代班";
                OkButton.Content = "儲存";
                IntroText.Text = $"{record.Year} 年 {record.Month} 月的代班個案。";
                HistoryPanel.Visibility = Visibility.Collapsed;
                NameBox.Text = editing.Name;
                LocationBox.Text = editing.Location;
                CoverForBox.Text = editing.CoverFor;
                NoteBox.Text = editing.Note;
            }
            else
            {
                IntroText.Text = $"加到 {record.Year} 年 {record.Month} 月的代班（只有這個月，不會加到設定的個案名單，金額也跟正常個案分開算）。";
                var candidates = settings.SubstituteHistory.Where(c => !record.IsSubstitute(c.Id)).ToList();
                HistoryBox.ItemsSource = candidates;
                HistoryPanel.Visibility = candidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            Loaded += (_, _) => (HistoryPanel.Visibility == Visibility.Visible ? (Control)HistoryBox : NameBox).Focus();
        }

        private void HistoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryBox.SelectedItem is not Client client) return;
            _picked = client;
            NameBox.Text = client.Name;
            LocationBox.Text = client.Location;
            CoverForBox.Text = client.CoverFor;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            // 修改時沿用原本的 Id；新增時挑了清單裡的人、姓名也沒改，就沿用那個人的 Id
            var id = _editing?.Id ?? (_picked != null && _picked.Name.Trim() == name ? _picked.Id : Guid.NewGuid().ToString("N"));
            string? error =
                name.Length == 0 ? "請填個案姓名。" :
                _record.Substitutes.Any(c => c.Id != id && c.Name.Trim() == name) ? $"這個月已經有代班個案「{name}」了。" :
                null;
            if (error != null)
            {
                MessageBox.Show(error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new Client
            {
                Id = id,
                Name = name,
                Location = LocationBox.Text.Trim(),
                CoverFor = CoverForBox.Text.Trim(),
                Note = NoteBox.Text.Trim(),
            };
            DialogResult = true;
        }
    }
}
