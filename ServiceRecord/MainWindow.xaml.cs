using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using ServiceRecord.Models;
using ServiceRecord.Services;

namespace ServiceRecord
{
    public partial class MainWindow : Window
    {
        private const string AppTitle = "居服紀錄表";
        private const string ExcelFilter = "Excel 活頁簿 (*.xlsx)|*.xlsx";

        private readonly UpdateService _updater = new();
        private AppSettings _settings;
        private MonthRecord _record = null!;
        /// <summary>這個月是否已經存過檔。還沒存過的月份只在記憶體裡，改設定時直接重建。</summary>
        private bool _saved;
        private string? _selectedClientId;
        private readonly List<ClientRow> _clientRows = new();
        private readonly List<ClientRow> _substituteRows = new();
        private ListCollectionView? _rowsView;
        /// <summary>日期欄 → 第幾天（0 是 1 號）。</summary>
        private readonly Dictionary<DataGridColumn, int> _dayColumns = new();

        public MainWindow()
        {
            InitializeComponent();
            // 小螢幕（例如筆電）上不要讓視窗超出可用範圍
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 20);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);

            _settings = DataStore.LoadSettings();
            Title = _updater.IsInstalled ? $"{AppTitle} v{_updater.CurrentVersion}" : $"{AppTitle} (開發版)";
            StatusText.Text = $"版本 {_updater.CurrentVersion}　·　填寫內容會自動儲存";
            ShowMonth(DateTime.Today.Year, DateTime.Today.Month);

            ThemeService.TrackTitleBar(this); // 標題列跟著深淺色變
            ThemeService.ThemeChanged += UpdateThemeButton;
            ZoomService.ScaleChanged += UpdateZoomButtons;
            HolidayService.YearUpdated += OnHolidaysUpdated;
            Closed += (_, _) =>
            {
                ThemeService.ThemeChanged -= UpdateThemeButton;
                ZoomService.ScaleChanged -= UpdateZoomButtons;
                HolidayService.YearUpdated -= OnHolidaysUpdated;
            };
            UpdateThemeButton();
            UpdateZoomButtons();
        }

        // ---------- 主題：跟隨系統 / 淺色 / 深色 ----------

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Cycle();
            StatusText.Text = $"外觀：{ThemeService.DisplayName(ThemeService.Mode)}";
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Text = ThemeService.Mode switch
            {
                AppTheme.Light => "", // 太陽
                AppTheme.Dark => "",  // 月亮
                _ => "",              // 電腦（跟隨系統）
            };
            ThemeText.Text = ThemeService.DisplayName(ThemeService.Mode);
            ThemeButton.ToolTip = $"外觀：{ThemeService.DisplayName(ThemeService.Mode)}（按一下切換：跟隨系統 → 淺色 → 深色）";
        }

        // ---------- 字體放大縮小 ----------

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomService.ZoomIn();
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomService.ZoomOut();
        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomService.Reset();

        private void UpdateZoomButtons()
        {
            ZoomText.Text = $"{ZoomService.Percent}%";
            ZoomInButton.IsEnabled = ZoomService.CanZoomIn;
            ZoomOutButton.IsEnabled = ZoomService.CanZoomOut;
        }

        /// <summary>Ctrl + / Ctrl − / Ctrl 0 調整字體（先於表格的 + − 加減次數處理）。</summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (e.Key)
            {
                case Key.Add:
                case Key.OemPlus:
                    ZoomService.ZoomIn();
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    ZoomService.ZoomOut();
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    ZoomService.Reset();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>按住 Ctrl 滾滑鼠滾輪調整字體。</summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Delta > 0) ZoomService.ZoomIn();
            else if (e.Delta < 0) ZoomService.ZoomOut();
            e.Handled = true;
        }

        // ---------- 月份 ----------

        private string MonthLabel => $"{_record.Year} 年 {_record.Month} 月";

        private void PrevMonth_Click(object sender, RoutedEventArgs e) => MoveMonth(-1);
        private void NextMonth_Click(object sender, RoutedEventArgs e) => MoveMonth(1);
        private void ThisMonth_Click(object sender, RoutedEventArgs e) => ShowMonth(DateTime.Today.Year, DateTime.Today.Month);

        private void MoveMonth(int delta)
        {
            var target = new DateTime(_record.Year, _record.Month, 1).AddMonths(delta);
            ShowMonth(target.Year, target.Month);
        }

        /// <summary>顯示某個月：有存過就讀檔，沒有就用目前的設定建一份新的（還不存檔，等到第一次填寫才存）。</summary>
        private void ShowMonth(int year, int month)
        {
            CommitEdits();
            var loaded = DataStore.LoadMonth(year, month);
            ShowRecord(loaded ?? MonthRecord.CreateFrom(_settings, year, month), saved: loaded != null);
        }

        private void ShowRecord(MonthRecord record, bool saved)
        {
            _record = record;
            _saved = saved;
            MonthText.Text = MonthLabel;

            _clientRows.Clear();
            _clientRows.AddRange(record.Clients.Select(c => new ClientRow(c)));
            _substituteRows.Clear();
            _substituteRows.AddRange(record.Substitutes.Select(c => new ClientRow(c)));
            _switchingList = true;
            ClientList.ItemsSource = null;
            ClientList.ItemsSource = _clientRows;
            SubstituteList.ItemsSource = null;
            SubstituteList.ItemsSource = _substituteRows;
            _switchingList = false;
            UpdateClientListState();

            BuildColumns();
            UpdateHolidayText();
            _ = HolidayService.RefreshAsync(record.Year); // 背景下載這一年最新的官方日曆，有變動會重畫
            SelectClient(_selectedClientId);
            RefreshTotals();
        }

        // ---------- 個案（正常個案和代班個案分兩個清單，同時只會選到一位） ----------

        private bool AnyClients => _clientRows.Count + _substituteRows.Count > 0;

        private void UpdateClientListState()
        {
            NoClientHint.Visibility = _clientRows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            NoClientHint.Text = _substituteRows.Count > 0
                ? "這個月沒有正常個案（只有代班）。\n\n要新增個案請按右上角「設定」。"
                : "這個月還沒有個案。\n\n按右上角「設定」新增個案，或用「匯入 Excel」讀入原本的服務紀錄表。";
            NoSubstituteHint.Visibility = _substituteRows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            SubstituteList.Visibility = _substituteRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AddItemButton.IsEnabled = AnyClients;
            UsedOnlyBox.IsEnabled = AnyClients;
        }

        /// <summary>選某一位（正常或代班）；找不到就選第一位正常個案，沒有的話選第一位代班。</summary>
        private void SelectClient(string? clientId)
        {
            var row = _clientRows.Concat(_substituteRows).FirstOrDefault(r => r.Client.Id == clientId)
                      ?? _clientRows.FirstOrDefault() ?? _substituteRows.FirstOrDefault();
            _switchingList = true;
            ClientList.SelectedItem = _clientRows.Contains(row!) ? row : null;
            SubstituteList.SelectedItem = _substituteRows.Contains(row!) ? row : null;
            _switchingList = false;
            CommitEdits();
            _selectedClientId = row?.Client.Id;
            ShowClient(row?.Client);
        }

        private bool _switchingList;

        private void ClientList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_switchingList || sender is not ListBox list || list.SelectedItem is not ClientRow row) return;
            // 選了一邊，另一邊取消選取
            _switchingList = true;
            (list == ClientList ? SubstituteList : ClientList).SelectedItem = null;
            _switchingList = false;
            CommitEdits();
            _selectedClientId = row.Client.Id;
            ShowClient(row.Client);
        }

        private void ShowClient(Client? client)
        {
            bool substitute = client != null && _record.IsSubstitute(client.Id);
            SubstituteBadge.Visibility = substitute ? Visibility.Visible : Visibility.Collapsed;
            SubstituteActions.Visibility = substitute ? Visibility.Visible : Visibility.Collapsed;
            SubstituteInfoText.Text = substitute ? client!.SubstituteInfo : "";
            SubstituteInfoText.Visibility = SubstituteInfoText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (client == null)
            {
                ClientTitle.Text = "";
                ItemsGrid.ItemsSource = null;
                _rowsView = null;
                UpdateGridHint();
                RefreshTotals();
                return;
            }

            ClientTitle.Text = $"{client.Label}　{MonthLabel}";
            _crossRow = null; // 換成新的列
            var rows = _record.Items.Select(i => new ItemRow(_record, client.Id, i, OnCountsChanged)).ToList();
            _rowsView = new ListCollectionView(rows);
            ApplyRowFilter();
            ItemsGrid.ItemsSource = _rowsView;
            RefreshTotals();
        }

        private Client? SelectedClient => ((ClientList.SelectedItem ?? SubstituteList.SelectedItem) as ClientRow)?.Client;

        private void UsedOnly_Changed(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            ApplyRowFilter();
        }

        private void ApplyRowFilter()
        {
            if (_rowsView == null) return;
            _rowsView.Filter = UsedOnlyBox.IsChecked == true ? o => o is ItemRow r && r.Count > 0 : null;
            UpdateGridHint();
        }

        private void UpdateGridHint()
        {
            if (!AnyClients)
            {
                NoDataHint.Text = "還沒有個案。請按右上角「設定」新增個案，或用「匯入 Excel」讀入原本的服務紀錄表。";
                NoDataHint.Visibility = Visibility.Visible;
            }
            else if (_record.Items.Count == 0)
            {
                NoDataHint.Text = "這個月沒有任何服務項目。按左下角「新增項目」加入。";
                NoDataHint.Visibility = Visibility.Visible;
            }
            else if (_rowsView != null && _rowsView.Count == 0)
            {
                NoDataHint.Text = "這位個案這個月還沒有填任何次數。\n取消勾選「只顯示有次數的項目」就能看到全部項目。";
                NoDataHint.Visibility = Visibility.Visible;
            }
            else
            {
                NoDataHint.Visibility = Visibility.Collapsed;
            }
            ItemsGrid.Visibility = NoDataHint.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        }

        // ---------- 國定假日 ----------

        /// <summary>表格上方列出本月的國定假日（例如「本月假日：6/19（五）端午節」）。</summary>
        private void UpdateHolidayText()
        {
            var holidays = HolidayService.NamedHolidaysIn(_record.Year, _record.Month);
            HolidayText.Text = holidays.Count == 0 ? "" :
                "本月假日：" + string.Join("、", holidays.Select(h => $"{h.Date.Month}/{h.Date.Day}（{WeekdayNames[(int)h.Date.DayOfWeek]}）{h.Name}"));
            HolidayText.Visibility = holidays.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            HolidayText.ToolTip = HolidayService.IsOfficial(_record.Year)
                ? "依政府行政機關辦公日曆表（人事行政總處公告）"
                : $"{_record.Year} 年的辦公日曆還沒公告或還沒下載，先用固定的國定假日（沒有補假）";
        }

        /// <summary>背景下載到新的官方日曆：如果是正在看的年份，重畫日期欄。</summary>
        private void OnHolidaysUpdated(int year) => Dispatcher.BeginInvoke(() =>
        {
            if (year != _record.Year) return;
            CommitEdits();
            BuildColumns();
            UpdateHolidayText();
        });

        // ---------- 十字標示 ----------

        private ItemRow? _crossRow;
        private DataGridColumn? _crossColumn;

        private void ItemsGrid_CurrentCellChanged(object? sender, EventArgs e) => UpdateCrossHair();

        /// <summary>選到的格子所在的服務項目（整列）和日期（整欄）上底色，方便對照要填哪一天、哪一項。</summary>
        private void UpdateCrossHair()
        {
            var row = ItemsGrid.CurrentCell.Item as ItemRow;
            var column = ItemsGrid.CurrentCell.Column;
            if (column != null && !_dayColumns.ContainsKey(column)) column = null; // 只有日期欄才標整欄

            if (_crossRow != row)
            {
                if (_crossRow != null) _crossRow.IsCrossRow = false;
                if (row != null) row.IsCrossRow = true;
                _crossRow = row;
            }
            if (_crossColumn != column)
            {
                if (_crossColumn != null) CrossHair.SetIsActive(_crossColumn, false); // 日期標題
                if (column != null) CrossHair.SetIsActive(column, true);
                _crossColumn = column;
            }
            int day = column != null ? _dayColumns[column] : -1;
            if (_rowsView != null)
                foreach (ItemRow r in _rowsView.SourceCollection) r.CrossDay = day;
        }

        /// <summary>日期欄的格子樣式：原本的樣式（平日 / 假日）＋「選到這一天時整欄上底色」。</summary>
        private Style DayCellStyle(int dayIndex, Style basedOn)
        {
            var style = new Style(typeof(DataGridCell), basedOn);
            var cross = new DataTrigger { Binding = new Binding(nameof(ItemRow.CrossDay)), Value = dayIndex };
            cross.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("CrossBrush")));
            style.Triggers.Add(cross);
            // 十字交叉的那一格：選取色＋粗框（要放在最後，才會蓋過上面的整欄底色；和 XAML 的 CrossCell 一樣）
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("CellSelectedBrush")));
            selected.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
            selected.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(2)));
            selected.Setters.Add(new Setter(PaddingProperty, new Thickness(2, 0, 2, 0)));
            style.Triggers.Add(selected);
            return style;
        }

        // ---------- 表格欄位 ----------

        private static readonly string[] WeekdayNames = ["日", "一", "二", "三", "四", "五", "六"];

        private void BuildColumns()
        {
            ItemsGrid.Columns.Clear();
            _dayColumns.Clear();
            _crossColumn = null; // 舊的欄位已經不用了

            ItemsGrid.Columns.Add(ReadOnlyColumn("代碼", nameof(ItemRow.Code), 74, "LeftText"));
            ItemsGrid.Columns.Add(ReadOnlyColumn("服務項目", nameof(ItemRow.Name), 196, "LeftText"));
            ItemsGrid.Columns.Add(ReadOnlyColumn("單價", nameof(ItemRow.Price), 62, "RightText"));

            var offDayCell = (Style)FindResource("WeekendCell");
            var dayCell = (Style)FindResource("CrossCell");
            for (int d = 1; d <= _record.DaysInMonth; d++)
            {
                var date = new DateOnly(_record.Year, _record.Month, d);
                // 週末、國定假日、補假都標紅（官方日曆裡補行上班的週六不標）
                bool isOff = HolidayService.IsOffDay(date);
                var holidayName = HolidayService.NameOf(date);
                var header = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
                var dayNumber = new TextBlock { Text = d.ToString(), HorizontalAlignment = HorizontalAlignment.Center };
                var weekday = new TextBlock
                {
                    Text = WeekdayNames[(int)date.DayOfWeek],
                    FontSize = 13,
                    FontWeight = FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                // 用資源參照（等同 DynamicResource），切換深淺色時才會跟著變
                if (isOff) dayNumber.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                weekday.SetResourceReference(TextBlock.ForegroundProperty, isOff ? "DangerBrush" : "MutedBrush");
                header.Children.Add(dayNumber);
                header.Children.Add(weekday);
                if (holidayName != null) header.ToolTip = $"{date.Month}/{d} {holidayName}";

                var column = new DataGridTextColumn
                {
                    Header = header,
                    Width = 44,
                    Binding = new Binding($"[{d - 1}]"),
                    ElementStyle = (Style)FindResource("DayText"),
                    EditingElementStyle = (Style)FindResource("CellEditBox"),
                    CellStyle = DayCellStyle(d - 1, isOff ? offDayCell : dayCell),
                };
                _dayColumns[column] = d - 1;
                ItemsGrid.Columns.Add(column);
            }

            var total = (Style)FindResource("TotalCell");
            var count = ReadOnlyColumn("次數", nameof(ItemRow.CountText), 60, "CountText");
            count.CellStyle = total;
            ItemsGrid.Columns.Add(count);
            var amount = ReadOnlyColumn("金額", nameof(ItemRow.AmountText), 90, "RightText");
            amount.CellStyle = total;
            ItemsGrid.Columns.Add(amount);
            ItemsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Width = 40,
                CellTemplate = (DataTemplate)FindResource("DeleteCellTemplate"),
                IsReadOnly = true,
            });
        }

        private DataGridTextColumn ReadOnlyColumn(string header, string path, double width, string styleKey) => new()
        {
            Header = header,
            Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = width,
            IsReadOnly = true,
            ElementStyle = (Style)FindResource(styleKey),
        };

        // ---------- 填寫次數 ----------

        private void OnCountsChanged()
        {
            SaveMonth();
            RefreshTotals();
        }

        private static bool IsEditingText => Keyboard.FocusedElement is TextBox;

        /// <summary>沒在編輯時：數字鍵開始填寫，+ / − 加減一次，Delete / Backspace 清除。</summary>
        private void ItemsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsEditingText) return;
            if (ItemsGrid.CurrentCell.Item is not ItemRow row || ItemsGrid.CurrentCell.Column is not { } column
                || !_dayColumns.TryGetValue(column, out var day))
                return;

            // 中文輸入法（注音）開著時，按鍵會先被輸入法拿走（Key.ImeProcessed），實際的鍵在 ImeProcessedKey
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            // 數字鍵：自己開始編輯並填入這個數字。不靠 DataGrid 的打字開始編輯，
            // 因為注音模式下第一個數字會被輸入法吃掉（變成注音符號或不見）
            if (Keyboard.Modifiers == ModifierKeys.None && DigitOf(key) is { } digit)
            {
                e.Handled = true; // 同時讓這個鍵不再產生文字輸入，數字才不會重複
                // 當作「打了這個數字」開始編輯：DataGrid 會把數字放進輸入框、游標放在後面（和平常打字開始編輯一樣）
                var typed = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                    new TextComposition(InputManager.Current, ItemsGrid, digit.ToString()))
                {
                    RoutedEvent = TextCompositionManager.TextInputEvent,
                };
                ItemsGrid.BeginEdit(typed);
                return;
            }

            switch (key)
            {
                case Key.Add:
                case Key.OemPlus:
                    row.Set(day, row.Get(day) + 1);
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    row.Set(day, Math.Max(0, row.Get(day) - 1));
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    row.Set(day, 0);
                    e.Handled = true;
                    break;
            }
        }

        internal static int? DigitOf(Key key) => key switch
        {
            >= Key.D0 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => null,
        };

        /// <summary>沒在編輯時只讓數字開始編輯，其他字（例如 + −）不要被當成輸入內容。</summary>
        private void ItemsGrid_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsEditingText) return;
            if (e.Text.Length > 0 && !e.Text.All(char.IsAsciiDigit)) e.Handled = true;
        }

        private void CommitEdits()
        {
            if (ItemsGrid.ItemsSource == null) return;
            ItemsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ItemsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void Window_Closing(object? sender, CancelEventArgs e) => CommitEdits();

        // ---------- 合計 ----------

        private void RefreshTotals()
        {
            foreach (var row in _clientRows.Concat(_substituteRows))
                row.NetPayText = PayCalculator.Money(PayCalculator.NetPay(_record, row.Client.Id));

            MonthlyPayText.Text = PayCalculator.Money(PayCalculator.MonthlyPay(_record));
            // 有代班時，月薪下面分開列「個案合計」「代班合計」
            PayBreakdown.Visibility = _record.Substitutes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RegularPayText.Text = PayCalculator.Money(PayCalculator.RegularPay(_record));
            SubstitutePayText.Text = PayCalculator.Money(PayCalculator.SubstitutePay(_record));
            var ratio = _record.ShareRatio.ToString("0.###");
            RatioHintText.Text = $"每位個案金額 × {ratio} 後加總";
            RatioText.Text = $"× {ratio}";

            var client = SelectedClient;
            SubtotalText.Text = client == null ? "0" : PayCalculator.Subtotal(_record, client.Id).ToString("#,##0");
            NetPayText.Text = client == null ? "0" : PayCalculator.Money(PayCalculator.NetPay(_record, client.Id));
        }

        private void SaveMonth()
        {
            try
            {
                DataStore.SaveMonth(_record);
                _saved = true;
                StatusText.Text = $"版本 {_updater.CurrentVersion}　·　已自動儲存 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"存檔失敗：{ex.Message}", "存檔", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool SaveSettings()
        {
            try
            {
                DataStore.SaveSettings(_settings);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"設定存檔失敗：{ex.Message}", "設定", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ---------- 這個月的服務項目：新增 / 刪除 ----------

        private void AddItem_Click(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var dlg = new ItemEditWindow(_record, _settings) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Result == null) return;

            _record.AddItem(dlg.Result);
            if (dlg.AddToSettings && !_settings.ServiceItems.Any(i => string.Equals(i.Code, dlg.Result.Code, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.ServiceItems.Add(dlg.Result.Clone());
                SaveSettings();
            }
            SaveMonth();
            ShowClient(SelectedClient);

            // 捲到新項目
            var added = _rowsView?.Cast<ItemRow>().FirstOrDefault(r => r.Code == dlg.Result.Code);
            if (added != null) ItemsGrid.ScrollIntoView(added);
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ItemRow row }) return;
            CommitEdits();

            int total = _record.TotalCountOf(row.Code);
            var message = total > 0
                ? $"「{row.Code} {row.Name}」這個月已經填了 {total} 次（所有個案合計）。\n\n刪除後這些次數也會一起刪掉，確定要從 {MonthLabel} 刪除這個項目嗎？"
                : $"要從 {MonthLabel} 刪除「{row.Code} {row.Name}」嗎？\n\n只會影響這個月；之後的月份要不要有這個項目，請到「設定 → 服務項目」調整。";
            var answer = MessageBox.Show(message, "刪除項目", MessageBoxButton.YesNo,
                total > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _record.RemoveItem(row.Code);
            SaveMonth();
            ShowClient(SelectedClient);
        }

        // ---------- 代班：新增 / 修改 / 刪除（只影響這個月） ----------

        private void AddSubstitute_Click(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var dlg = new SubstituteWindow(_record, _settings) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Result == null) return;

            _record.AddSubstitute(dlg.Result);
            _settings.RememberSubstitute(dlg.Result); // 下次新增代班可以直接挑
            SaveSettings();
            SaveMonth();
            _selectedClientId = dlg.Result.Id;
            ShowRecord(_record, saved: true);
        }

        private void EditSubstitute_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedClient is not { } client || !_record.IsSubstitute(client.Id)) return;
            CommitEdits();
            var dlg = new SubstituteWindow(_record, _settings, client) { Owner = this };
            if (dlg.ShowDialog() != true || dlg.Result == null) return;

            client.Name = dlg.Result.Name;
            client.Location = dlg.Result.Location;
            client.CoverFor = dlg.Result.CoverFor;
            client.Note = dlg.Result.Note;
            _settings.RememberSubstitute(client);
            SaveSettings();
            SaveMonth();
            ShowRecord(_record, saved: true);
        }

        private void DeleteSubstitute_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedClient is not { } client || !_record.IsSubstitute(client.Id)) return;
            CommitEdits();

            int total = _record.TotalCountOfClient(client.Id);
            var message = total > 0
                ? $"代班個案「{client.Label}」這個月已經填了 {total} 次（實領 {PayCalculator.Money(PayCalculator.NetPay(_record, client.Id))}）。\n\n刪除後這些次數也會一起刪掉，確定要從 {MonthLabel} 刪除嗎？"
                : $"要從 {MonthLabel} 刪除代班個案「{client.Label}」嗎？";
            var answer = MessageBox.Show(message, "刪除代班", MessageBoxButton.YesNo,
                total > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _record.RemoveSubstitute(client.Id);
            SaveMonth();
            _selectedClientId = null;
            ShowRecord(_record, saved: true);
        }

        // ---------- 設定 ----------

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var dlg = new SettingsWindow(_settings.Clone()) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _settings = dlg.Result;
            SaveSettings();

            // 還沒填過的月份：直接照新設定重建
            if (!_saved)
            {
                ShowRecord(MonthRecord.CreateFrom(_settings, _record.Year, _record.Month), saved: false);
                return;
            }
            if (_record.MatchesSettings(_settings)) return;

            var apply = MessageBox.Show(
                $"設定已儲存，之後的新月份都會用新的設定。\n\n{MonthLabel} 已經有紀錄，要讓這個月也改用新的設定嗎？\n" +
                "（新增或刪除的個案、服務項目，以及修改過的名稱、單價、比例）\n\n選「否」：這個月維持原樣。",
                "套用設定", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (apply != MessageBoxResult.Yes) return;

            var (items, clients) = _record.RemovedWithCounts(_settings);
            bool remove = false;
            if (items.Count + clients.Count > 0)
            {
                var list = string.Join("\n",
                    clients.Select(c => $"・個案 {c.Label}（{_record.TotalCountOfClient(c.Id)} 次）")
                        .Concat(items.Select(i => $"・{i.Code} {i.Name}（{_record.TotalCountOf(i.Code)} 次）")));
                remove = MessageBox.Show(
                    $"下列項目在設定裡已經刪除，但 {MonthLabel} 已經填了次數：\n\n{list}\n\n要連同次數從這個月一起刪掉嗎？\n選「否」會保留在這個月。",
                    "套用設定", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            }

            _record.ApplySettings(_settings, remove);
            SaveMonth();
            ShowRecord(_record, saved: true);
        }

        // ---------- Excel 匯入 / 匯出 ----------

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            var dlg = new OpenFileDialog { Filter = ExcelFilter, Title = "選擇服務紀錄表" };
            if (dlg.ShowDialog() != true) return;

            ImportedMonth imported;
            try
            {
                imported = ExcelImporter.Read(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"無法讀取這個檔案：{ex.Message}", "匯入 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var existing = DataStore.LoadMonth(imported.Year, imported.Month);
            if (existing != null && existing.HasAnyCounts)
            {
                var overwrite = MessageBox.Show(
                    $"{imported.Year} 年 {imported.Month} 月已經有紀錄了。\n\n匯入會用 Excel 的內容取代這個月原本的紀錄，確定要匯入嗎？",
                    "匯入 Excel", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (overwrite != MessageBoxResult.Yes) return;
            }

            var (record, added) = ExcelImporter.ToMonthRecord(imported, _settings);
            if (added > 0 || record.Substitutes.Count > 0) SaveSettings(); // 新個案、代過班的個案
            _record = record;
            SaveMonth();
            _selectedClientId = null;
            ShowRecord(record, saved: true);

            MessageBox.Show(
                $"已匯入 {MonthLabel}，共 {record.Clients.Count} 位個案" +
                (record.Substitutes.Count > 0 ? $"、{record.Substitutes.Count} 位代班個案。" : "。") +
                (added > 0 ? $"\n新增了 {added} 位個案到設定。" : "") +
                $"\n\n本月月薪：{PayCalculator.Money(PayCalculator.MonthlyPay(record))}",
                "匯入 Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            CommitEdits();
            if (!_record.AllClients.Any())
            {
                MessageBox.Show("這個月還沒有個案，沒有東西可以匯出。", "匯出 Excel");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = ExcelFilter,
                FileName = ExcelExporter.DefaultFileName(_record, _settings.WorkerName),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelExporter.Export(_record, dlg.FileName);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"無法寫入檔案，可能正在 Excel 裡開著，請先關閉再試。\n\n{ex.Message}", "匯出 Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出失敗：{ex.Message}", "匯出 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var open = MessageBox.Show($"已匯出：\n{dlg.FileName}\n\n要現在打開嗎？", "匯出 Excel", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
                catch (Win32Exception ex) { MessageBox.Show($"無法開啟：{ex.Message}", "匯出 Excel"); }
            }
        }

        // ---------- 啟動時清理、檢查更新 ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CleanupService.RunInBackground(); // 清掉更新後遺留的舊檔
            await CheckForUpdateAsync(manual: false);
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShortcutService.CreateDesktop(_updater.IsInstalled, Environment.ProcessPath ?? InstallService.InstalledExe);
                MessageBox.Show("已在桌面建立「居服紀錄表」捷徑。", "桌面捷徑", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                MessageBox.Show($"建立捷徑失敗：{ex.Message}", "桌面捷徑", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

        private async Task CheckForUpdateAsync(bool manual)
        {
            if (!_updater.IsInstalled)
            {
                if (manual) MessageBox.Show("目前是開發版（不是從安裝檔安裝的），不檢查更新。", "檢查更新");
                return;
            }

            try
            {
                var info = await _updater.CheckAsync();
                if (info == null)
                {
                    if (manual) MessageBox.Show($"已經是最新版（{_updater.CurrentVersion}）。", "檢查更新");
                    return;
                }

                var answer = MessageBox.Show(
                    $"有新版本 {info.Version}（目前 {_updater.CurrentVersion}）。\n\n要現在下載並更新嗎？資料不會受影響。",
                    "有新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                CommitEdits();
                StatusText.Text = "正在下載更新…";
                await _updater.DownloadAndLaunchAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = $"正在下載更新… {p}%"));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動重新開啟
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"版本 {_updater.CurrentVersion}";
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show($"檢查更新失敗：{ex.Message}", "檢查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
