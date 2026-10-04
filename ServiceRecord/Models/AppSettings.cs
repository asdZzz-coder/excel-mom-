namespace ServiceRecord.Models
{
    /// <summary>
    /// 設定：居服員姓名、實領比例，以及「新月份」預設使用的個案和服務項目。
    /// 已經有紀錄的月份各自保存一份當時的個案、項目和比例（見 MonthRecord），改設定不會動到舊月份。
    /// </summary>
    public class AppSettings
    {
        public const decimal DefaultShareRatio = 0.6m;

        /// <summary>匯出檔名用：2026-06份 {WorkerName}服務紀錄表.xlsx</summary>
        public string WorkerName { get; set; } = "洪淑瑩靜鑫";

        /// <summary>實領 = 金額小計 × 比例。</summary>
        public decimal ShareRatio { get; set; } = DefaultShareRatio;

        public List<Client> Clients { get; set; } = new();

        public List<ServiceItem> ServiceItems { get; set; } = ServiceItem.Defaults();

        /// <summary>
        /// 代過班的個案（姓名、地點、原居服員）：只用來在新增代班時讓人直接挑，不會自動加到任何月份。
        /// 同一個人再次代班沿用同一個 Id。
        /// </summary>
        public List<Client> SubstituteHistory { get; set; } = new();

        public AppSettings Clone() => new()
        {
            WorkerName = WorkerName,
            ShareRatio = ShareRatio,
            Clients = Clients.Select(c => c.Clone()).ToList(),
            ServiceItems = ServiceItems.Select(i => i.Clone()).ToList(),
            SubstituteHistory = SubstituteHistory.Select(c => c.Clone()).ToList(),
        };

        /// <summary>把代班個案記下來（同 Id 的更新姓名、地點、原居服員；備註只屬於那個月，不記）。</summary>
        public void RememberSubstitute(Client client)
        {
            var known = SubstituteHistory.FirstOrDefault(c => c.Id == client.Id);
            if (known == null)
            {
                known = new Client { Id = client.Id };
                SubstituteHistory.Add(known);
            }
            known.Name = client.Name;
            known.Location = client.Location;
            known.CoverFor = client.CoverFor;
        }
    }
}
