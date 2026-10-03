namespace ServiceRecord.Models
{
    /// <summary>
    /// 一個月的服務紀錄。Clients、Items、ShareRatio 是這個月自己的一份，
    /// 第一次建立時從設定複製過來；之後改設定（調價、刪項目）不會動到這個月，除非選擇套用。
    /// Counts[個案 Id][服務代碼] 是 31 格的陣列，第 0 格是 1 號。
    /// </summary>
    public class MonthRecord
    {
        public const int MaxDays = 31;

        public int Year { get; set; }
        public int Month { get; set; }
        public decimal ShareRatio { get; set; } = AppSettings.DefaultShareRatio;
        public List<Client> Clients { get; set; } = new();
        public List<ServiceItem> Items { get; set; } = new();
        public Dictionary<string, Dictionary<string, int[]>> Counts { get; set; } = new();

        public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

        public string Key => Format(Year, Month);

        public static string Format(int year, int month) => $"{year:D4}-{month:D2}";

        public static MonthRecord CreateFrom(AppSettings settings, int year, int month) => new()
        {
            Year = year,
            Month = month,
            ShareRatio = settings.ShareRatio,
            Clients = settings.Clients.Select(c => c.Clone()).ToList(),
            Items = settings.ServiceItems.Select(i => i.Clone()).ToList(),
        };

        // ---------- 次數 ----------

        /// <summary>某個案某項目的每日次數（沒有就建立一個全 0 的）。</summary>
        public int[] GetDays(string clientId, string code)
        {
            if (!Counts.TryGetValue(clientId, out var byCode))
                Counts[clientId] = byCode = new Dictionary<string, int[]>();
            if (!byCode.TryGetValue(code, out var days) || days.Length != MaxDays)
            {
                var fixedDays = new int[MaxDays];
                if (days != null) Array.Copy(days, fixedDays, Math.Min(days.Length, MaxDays));
                byCode[code] = days = fixedDays;
            }
            return days;
        }

        /// <summary>某個案某項目本月的總次數（只算當月實際有的日期）。</summary>
        public int CountOf(string clientId, string code) =>
            Counts.TryGetValue(clientId, out var byCode) && byCode.TryGetValue(code, out var days)
                ? days.Take(DaysInMonth).Sum()
                : 0;

        /// <summary>某項目本月所有個案加起來的次數。</summary>
        public int TotalCountOf(string code) => Clients.Sum(c => CountOf(c.Id, code));

        /// <summary>某個案本月所有項目加起來的次數。</summary>
        public int TotalCountOfClient(string clientId) => Items.Sum(i => CountOf(clientId, i.Code));

        public bool HasAnyCounts => Clients.Any(c => TotalCountOfClient(c.Id) > 0);

        // ---------- 新增 / 刪除 ----------

        public bool HasItem(string code) => Items.Any(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

        public void AddItem(ServiceItem item)
        {
            if (HasItem(item.Code)) throw new InvalidOperationException($"本月已經有 {item.Code} 這個項目。");
            Items.Add(item.Clone());
        }

        /// <summary>刪除這個月的某個項目，連同所有個案在這個項目的次數。</summary>
        public void RemoveItem(string code)
        {
            Items.RemoveAll(i => i.Code == code);
            foreach (var byCode in Counts.Values) byCode.Remove(code);
        }

        public void RemoveClient(string clientId)
        {
            Clients.RemoveAll(c => c.Id == clientId);
            Counts.Remove(clientId);
        }

        // ---------- 套用設定 ----------

        /// <summary>這個月的個案、項目和比例是否跟設定完全一樣（一樣就不必問要不要套用）。</summary>
        public bool MatchesSettings(AppSettings settings) =>
            ShareRatio == settings.ShareRatio
            && Clients.Select(c => (c.Id, c.Name, c.Location)).SequenceEqual(settings.Clients.Select(c => (c.Id, c.Name, c.Location)))
            && Items.Select(i => (i.Code, i.Name, i.Price)).SequenceEqual(settings.ServiceItems.Select(i => (i.Code, i.Name, i.Price)));

        /// <summary>套用設定時會被拿掉、但本月已經有次數的項目與個案（套用前要先問使用者）。</summary>
        public (List<ServiceItem> Items, List<Client> Clients) RemovedWithCounts(AppSettings settings)
        {
            var items = Items.Where(i => !settings.ServiceItems.Any(s => s.Code == i.Code) && TotalCountOf(i.Code) > 0).ToList();
            var clients = Clients.Where(c => !settings.Clients.Any(s => s.Id == c.Id) && TotalCountOfClient(c.Id) > 0).ToList();
            return (items, clients);
        }

        /// <summary>
        /// 讓這個月改用設定裡的個案、項目（名稱、單價、順序）和比例。
        /// 設定裡已經沒有的項目或個案：沒有次數的直接拿掉；有次數的看 removeWithCounts 決定刪掉或保留在最後面。
        /// </summary>
        public void ApplySettings(AppSettings settings, bool removeWithCounts)
        {
            ShareRatio = settings.ShareRatio;

            var keptItems = Items.Where(i => !settings.ServiceItems.Any(s => s.Code == i.Code)
                                             && !removeWithCounts && TotalCountOf(i.Code) > 0).ToList();
            foreach (var gone in Items.Where(i => !settings.ServiceItems.Any(s => s.Code == i.Code)).Except(keptItems).ToList())
                RemoveItem(gone.Code);
            Items = settings.ServiceItems.Select(i => i.Clone()).Concat(keptItems).ToList();

            var keptClients = Clients.Where(c => !settings.Clients.Any(s => s.Id == c.Id)
                                                 && !removeWithCounts && TotalCountOfClient(c.Id) > 0).ToList();
            foreach (var gone in Clients.Where(c => !settings.Clients.Any(s => s.Id == c.Id)).Except(keptClients).ToList())
                RemoveClient(gone.Id);
            Clients = settings.Clients.Select(c => c.Clone()).Concat(keptClients).ToList();
        }
    }
}
