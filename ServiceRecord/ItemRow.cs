using System.ComponentModel;
using ServiceRecord.Models;

namespace ServiceRecord
{
    /// <summary>
    /// 表格的一列：某位個案、某個服務項目這個月每天的次數。
    /// 日期欄用索引器繫結（Binding "[0]" 是 1 號），0 顯示成空白，輸入空白也當作 0。
    /// </summary>
    public sealed class ItemRow : INotifyPropertyChanged
    {
        public const int MaxPerDay = 99;

        private readonly MonthRecord _record;
        private readonly string _clientId;
        private readonly Action _changed;

        public ItemRow(MonthRecord record, string clientId, ServiceItem item, Action changed)
        {
            _record = record;
            _clientId = clientId;
            Service = item;
            _changed = changed;
        }

        public ServiceItem Service { get; }
        public string Code => Service.Code;
        public string Name => Service.Name;
        public int Price => Service.Price;

        public string this[int index]
        {
            get
            {
                var n = Get(index);
                return n == 0 ? "" : n.ToString();
            }
            set
            {
                var text = (value ?? "").Trim();
                if (text.Length == 0) Set(index, 0);
                else if (int.TryParse(text, out var n) && n >= 0 && n <= MaxPerDay) Set(index, n);
                else Raise("Item[]"); // 輸入的不是 0~99 的整數：還原成原本的值
            }
        }

        public int Get(int index) =>
            _record.Counts.TryGetValue(_clientId, out var byCode) && byCode.TryGetValue(Service.Code, out var days) && index < days.Length
                ? days[index]
                : 0;

        public void Set(int index, int value)
        {
            value = Math.Clamp(value, 0, MaxPerDay);
            if (Get(index) == value) return;
            // 只有真的填了次數才建立陣列，沒填的項目不會在存檔裡留一堆 0
            _record.GetDays(_clientId, Service.Code)[index] = value;
            Raise("Item[]");
            Raise(nameof(Count));
            Raise(nameof(CountText));
            Raise(nameof(AmountText));
            _changed();
        }

        private bool _isCrossRow;
        /// <summary>十字標示：目前選到的格子在這一列，整列（服務項目）上底色。</summary>
        public bool IsCrossRow
        {
            get => _isCrossRow;
            set
            {
                if (_isCrossRow == value) return;
                _isCrossRow = value;
                Raise(nameof(IsCrossRow));
            }
        }

        private int _crossDay = -1;
        /// <summary>十字標示：目前選到的是哪一天（0 是 1 號，-1 表示沒有選日期）。每一列都一樣，日期欄的格子依此上底色。</summary>
        public int CrossDay
        {
            get => _crossDay;
            set
            {
                if (_crossDay == value) return;
                _crossDay = value;
                Raise(nameof(CrossDay));
            }
        }

        public int Count => _record.CountOf(_clientId, Service.Code);
        public string CountText => Count == 0 ? "" : Count.ToString();
        public string AmountText => Count == 0 ? "" : (Count * Price).ToString("#,##0");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>十字標示：目前選到的格子在哪一欄（日期）。附加在 DataGridColumn 上，格子和欄標題的樣式依此上底色。</summary>
    public static class CrossHair
    {
        public static readonly System.Windows.DependencyProperty IsActiveProperty =
            System.Windows.DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(CrossHair),
                new System.Windows.PropertyMetadata(false));

        public static bool GetIsActive(System.Windows.DependencyObject d) => (bool)d.GetValue(IsActiveProperty);
        public static void SetIsActive(System.Windows.DependencyObject d, bool value) => d.SetValue(IsActiveProperty, value);
    }

    /// <summary>左側個案清單的一列：名稱 + 本月實領。</summary>
    public sealed class ClientRow : INotifyPropertyChanged
    {
        public ClientRow(Client client) => Client = client;

        public Client Client { get; }
        public string Label => Client.Label;

        private string _netPay = "";
        public string NetPayText
        {
            get => _netPay;
            set { _netPay = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetPayText))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
