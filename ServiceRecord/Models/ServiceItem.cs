namespace ServiceRecord.Models
{
    /// <summary>一個服務項目：代碼（例如 BA13）、名稱、單價。</summary>
    public class ServiceItem
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public int Price { get; set; }

        public ServiceItem Clone() => new() { Code = Code, Name = Name, Price = Price };

        public override string ToString() => $"{Code} {Name}";

        /// <summary>預設的服務項目，取自原本 Excel 服務紀錄表的價目。</summary>
        public static List<ServiceItem> Defaults() =>
        [
            new() { Code = "BA01", Name = "基本身體清潔", Price = 260 },
            new() { Code = "BA02", Name = "基本日常照顧", Price = 195 },
            new() { Code = "BA03", Name = "測量生命徵象", Price = 35 },
            new() { Code = "BA04", Name = "協助餵食或灌食", Price = 130 },
            new() { Code = "BA05", Name = "餐食照顧", Price = 310 },
            new() { Code = "BA07", Name = "協助沐浴及洗頭", Price = 325 },
            new() { Code = "BA08", Name = "足部照顧", Price = 500 },
            new() { Code = "BA10", Name = "翻身拍背", Price = 155 },
            new() { Code = "BA11", Name = "肢體關節活動", Price = 195 },
            new() { Code = "BA12", Name = "協助上下樓梯", Price = 130 },
            new() { Code = "BA13", Name = "陪同外出", Price = 195 },
            new() { Code = "BA14", Name = "陪同就醫", Price = 685 },
            new() { Code = "BA15", Name = "家務協助", Price = 195 },
            new() { Code = "BA16", Name = "代購或代領或代送服務", Price = 130 },
            new() { Code = "BA17", Name = "協助執行輔助性醫療", Price = 65 },
            new() { Code = "BA17a", Name = "人工氣道管內分泌抽吸", Price = 75 },
            new() { Code = "BA17b", Name = "口鼻抽吸", Price = 65 },
            new() { Code = "BA17c", Name = "管路(尿管,鼻胃管)清潔", Price = 50 },
            new() { Code = "BA17d", Name = "甘油球通便,血糖機驗血", Price = 50 },
            new() { Code = "BA17e", Name = "依指示置入藥盒", Price = 50 },
            new() { Code = "BA18", Name = "安全看視", Price = 200 },
            new() { Code = "BA20", Name = "陪伴服務", Price = 175 },
            new() { Code = "BA22", Name = "巡視服務", Price = 130 },
            new() { Code = "BA23", Name = "協助洗頭", Price = 200 },
            new() { Code = "BA24", Name = "協助排泄", Price = 200 },
            new() { Code = "GA09", Name = "居家喘息服務-2小時", Price = 770 },
            new() { Code = "AA05", Name = "照顧困難", Price = 200 },
            new() { Code = "AA06", Name = "身體照顧困難", Price = 200 },
            new() { Code = "AA07", Name = "家庭照顧功能微弱", Price = 760 },
            new() { Code = "AA09", Name = "假日服務加給", Price = 770 },
            new() { Code = "RA14", Name = "自費陪同就醫", Price = 753 },
        ];
    }
}
