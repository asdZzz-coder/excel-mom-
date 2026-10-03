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

        public AppSettings Clone() => new()
        {
            WorkerName = WorkerName,
            ShareRatio = ShareRatio,
            Clients = Clients.Select(c => c.Clone()).ToList(),
            ServiceItems = ServiceItems.Select(i => i.Clone()).ToList(),
        };
    }
}
