using ServiceRecord.Models;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 跟原本 Excel 一樣的算法：
    ///   每項金額 = 次數 × 單價（AJ = AI*C）
    ///   實領     = 金額小計 × 比例（SUM(AJ)*0.6）
    ///   月薪     = 所有個案的實領加總 = 正常個案合計 + 代班合計
    /// </summary>
    public static class PayCalculator
    {
        public static int Amount(MonthRecord record, string clientId, ServiceItem item) =>
            record.CountOf(clientId, item.Code) * item.Price;

        public static int Subtotal(MonthRecord record, string clientId) =>
            record.Items.Sum(i => Amount(record, clientId, i));

        public static decimal NetPay(MonthRecord record, string clientId) =>
            Subtotal(record, clientId) * record.ShareRatio;

        /// <summary>正常個案的實領加總。</summary>
        public static decimal RegularPay(MonthRecord record) =>
            record.Clients.Sum(c => NetPay(record, c.Id));

        /// <summary>代班個案的實領加總。</summary>
        public static decimal SubstitutePay(MonthRecord record) =>
            record.Substitutes.Sum(c => NetPay(record, c.Id));

        public static decimal MonthlyPay(MonthRecord record) =>
            RegularPay(record) + SubstitutePay(record);

        /// <summary>明細的一筆：哪一天、做了哪一項、幾次、金額（次數 × 單價）。</summary>
        public sealed record Entry(DateOnly Date, ServiceItem Item, int Count)
        {
            public int Amount => Count * Item.Price;
        }

        /// <summary>某個案這個月每一天做了什麼（只列有次數的），依日期、再依項目順序排。代班明細用。</summary>
        public static List<Entry> Entries(MonthRecord record, string clientId)
        {
            var entries = new List<Entry>();
            for (int d = 1; d <= record.DaysInMonth; d++)
                foreach (var item in record.Items)
                {
                    int count = record.CountOf(clientId, item.Code) == 0 ? 0 : record.GetDays(clientId, item.Code)[d - 1];
                    if (count != 0) entries.Add(new Entry(new DateOnly(record.Year, record.Month, d), item, count));
                }
            return entries;
        }

        /// <summary>金額顯示：整數不帶小數，有小數時最多兩位。</summary>
        public static string Money(decimal value) => value.ToString("#,##0.##");
    }
}
