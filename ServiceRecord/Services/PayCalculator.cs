using ServiceRecord.Models;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 跟原本 Excel 一樣的算法：
    ///   每項金額 = 次數 × 單價（AJ = AI*C）
    ///   實領     = 金額小計 × 比例（SUM(AJ)*0.6）
    ///   月薪     = 所有個案的實領加總
    /// </summary>
    public static class PayCalculator
    {
        public static int Amount(MonthRecord record, string clientId, ServiceItem item) =>
            record.CountOf(clientId, item.Code) * item.Price;

        public static int Subtotal(MonthRecord record, string clientId) =>
            record.Items.Sum(i => Amount(record, clientId, i));

        public static decimal NetPay(MonthRecord record, string clientId) =>
            Subtotal(record, clientId) * record.ShareRatio;

        public static decimal MonthlyPay(MonthRecord record) =>
            record.Clients.Sum(c => NetPay(record, c.Id));

        /// <summary>金額顯示：整數不帶小數，有小數時最多兩位。</summary>
        public static string Money(decimal value) => value.ToString("#,##0.##");
    }
}
