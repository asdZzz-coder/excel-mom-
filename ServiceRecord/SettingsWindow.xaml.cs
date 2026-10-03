using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ServiceRecord.Models;
using ServiceRecord.Services;

namespace ServiceRecord
{
    /// <summary>設定視窗：個案、服務項目（新增 / 刪除 / 修改 / 排序）、居服員姓名、實領比例。按「儲存」才會生效。</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ObservableCollection<Client> _clients;
        private readonly ObservableCollection<ServiceItem> _items;

        public AppSettings Result => _settings;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
            _settings = settings;
            _clients = new ObservableCollection<Client>(settings.Clients);
            _items = new ObservableCollection<ServiceItem>(settings.ServiceItems);
            ClientGrid.ItemsSource = _clients;
            ItemGrid.ItemsSource = _items;
            WorkerBox.Text = settings.WorkerName;
            RatioBox.Text = settings.ShareRatio.ToString("0.###", CultureInfo.InvariantCulture);
            WorkerBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();
        }

        private void UpdatePreview() =>
            FileNamePreview.Text = "匯出檔名例如：" + ExcelExporter.DefaultFileName(
                new MonthRecord { Year = DateTime.Today.Year, Month = DateTime.Today.Month }, WorkerBox.Text);

        // ---------- 個案 ----------

        private void AddClient_Click(object sender, RoutedEventArgs e)
        {
            CommitAll();
            var client = new Client { Name = "" };
            _clients.Add(client);
            BeginEditNew(ClientGrid, client);
        }

        private void DeleteClient_Click(object sender, RoutedEventArgs e)
        {
            if (ClientGrid.SelectedItem is not Client client) return;
            CommitAll();
            var name = client.Name.Length == 0 ? "（未命名）" : client.Label;
            var ok = MessageBox.Show(
                $"要刪除個案「{name}」嗎？\n\n之後的新月份不會再有這位個案；已經有紀錄的月份不受影響（儲存時會問要不要套用到目前的月份）。",
                "刪除個案", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ok == MessageBoxResult.Yes) _clients.Remove(client);
        }

        private void MoveClientUp_Click(object sender, RoutedEventArgs e) => Move(ClientGrid, _clients, -1);
        private void MoveClientDown_Click(object sender, RoutedEventArgs e) => Move(ClientGrid, _clients, 1);

        // ---------- 服務項目 ----------

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            CommitAll();
            var item = new ServiceItem();
            _items.Add(item);
            BeginEditNew(ItemGrid, item);
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (ItemGrid.SelectedItem is not ServiceItem item) return;
            CommitAll();
            var name = item.Code.Length == 0 ? "（未填代碼）" : $"{item.Code} {item.Name}";
            var ok = MessageBox.Show(
                $"要刪除服務項目「{name}」嗎？\n\n之後的新月份不會再有這個項目；已經有紀錄的月份不受影響（儲存時會問要不要套用到目前的月份）。",
                "刪除項目", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ok == MessageBoxResult.Yes) _items.Remove(item);
        }

        private void MoveItemUp_Click(object sender, RoutedEventArgs e) => Move(ItemGrid, _items, -1);
        private void MoveItemDown_Click(object sender, RoutedEventArgs e) => Move(ItemGrid, _items, 1);

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            CommitAll();
            int added = 0;
            foreach (var d in ServiceItem.Defaults())
            {
                if (_items.Any(i => string.Equals(i.Code.Trim(), d.Code, StringComparison.OrdinalIgnoreCase))) continue;
                _items.Add(d);
                added++;
            }
            MessageBox.Show(added == 0 ? "預設項目都已經在清單裡了。" : $"加回了 {added} 個預設項目（放在清單最後面）。", "服務項目");
        }

        // ---------- 共用 ----------

        private void Move<T>(DataGrid grid, ObservableCollection<T> list, int delta)
        {
            if (grid.SelectedItem is not T item) return;
            CommitAll();
            int from = list.IndexOf(item), to = from + delta;
            if (from < 0 || to < 0 || to >= list.Count) return;
            list.Move(from, to);
            grid.SelectedItem = item;
            grid.ScrollIntoView(item);
        }

        private static void BeginEditNew(DataGrid grid, object item)
        {
            grid.SelectedItem = item;
            grid.ScrollIntoView(item);
            grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
            grid.Focus();
            grid.BeginEdit();
        }

        private void CommitAll()
        {
            foreach (var grid in new[] { ClientGrid, ItemGrid })
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CommitAll();
            var error = Validate(out var ratio);
            if (error != null)
            {
                MessageBox.Show(error, "設定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var c in _clients) { c.Name = c.Name.Trim(); c.Location = c.Location.Trim(); }
            foreach (var i in _items) { i.Code = i.Code.Trim(); i.Name = i.Name.Trim(); }
            _settings.Clients = _clients.ToList();
            _settings.ServiceItems = _items.ToList();
            _settings.WorkerName = WorkerBox.Text.Trim();
            _settings.ShareRatio = ratio;
            DialogResult = true;
        }

        /// <summary>檢查輸入；有問題回傳要顯示的訊息，並切到那一頁。</summary>
        private string? Validate(out decimal ratio)
        {
            ratio = 0;

            // 完全空白的列（按了新增但沒填）直接拿掉
            foreach (var c in _clients.Where(c => c.Name.Trim().Length == 0 && c.Location.Trim().Length == 0).ToList()) _clients.Remove(c);
            foreach (var i in _items.Where(i => i.Code.Trim().Length == 0 && i.Name.Trim().Length == 0 && i.Price == 0).ToList()) _items.Remove(i);

            if (_clients.Any(c => c.Name.Trim().Length == 0))
                return Fail(0, "有個案還沒填姓名。");
            var dupClient = _clients.GroupBy(c => c.Name.Trim()).FirstOrDefault(g => g.Count() > 1);
            if (dupClient != null)
                return Fail(0, $"個案「{dupClient.Key}」重複了。如果是不同的人，請在姓名後面加註區分。");

            if (_items.Any(i => i.Code.Trim().Length == 0))
                return Fail(1, "有服務項目還沒填代碼。");
            var dupCode = _items.GroupBy(i => i.Code.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dupCode != null)
                return Fail(1, $"代碼「{dupCode.Key}」重複了，每個服務項目的代碼要不一樣。");
            var noName = _items.FirstOrDefault(i => i.Name.Trim().Length == 0);
            if (noName != null)
                return Fail(1, $"服務項目「{noName.Code}」還沒填名稱。");
            var badPrice = _items.FirstOrDefault(i => i.Price < 0);
            if (badPrice != null)
                return Fail(1, $"服務項目「{badPrice.Code}」的單價不能是負數。");

            if (WorkerBox.Text.Trim().Length == 0)
                return Fail(2, "請填居服員姓名（匯出檔名會用到）。");
            if (!decimal.TryParse(RatioBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out ratio) || ratio <= 0 || ratio > 1)
                return Fail(2, "實領比例要是 0 到 1 之間的數字，例如 0.6。");

            return null;
        }

        private string Fail(int tab, string message)
        {
            Tabs.SelectedIndex = tab;
            return message;
        }
    }
}
