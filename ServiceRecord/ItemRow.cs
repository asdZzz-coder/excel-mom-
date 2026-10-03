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

        public int Count => _record.CountOf(_clientId, Service.Code);
        public string CountText => Count == 0 ? "" : Count.ToString();
        public string AmountText => Count == 0 ? "" : (Count * Price).ToString("#,##0");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
