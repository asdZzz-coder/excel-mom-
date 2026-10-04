using System.Text.Json.Serialization;

namespace ServiceRecord.Models
{
    /// <summary>個案：姓名與服務地點（例如 慶東街）。Id 固定不變，改名後紀錄仍對得上。</summary>
    public class Client
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";

        /// <summary>代班個案才有：原本服務這位個案的居服員（替誰代班）。</summary>
        public string CoverFor { get; set; } = "";

        /// <summary>代班個案才有：備註（例如代班期間、原因）。</summary>
        public string Note { get; set; } = "";

        /// <summary>Excel 上的寫法：慶東街(王林妙芳)；沒有地點時只有姓名。</summary>
        [JsonIgnore]
        public string Label => Location.Length == 0 ? Name : $"{Location}({Name})";

        /// <summary>代班說明：「替 王小明 代班　6/3～6/10」；沒填就是空字串。</summary>
        [JsonIgnore]
        public string SubstituteInfo => string.Join("　",
            new[] { CoverFor.Length > 0 ? $"替 {CoverFor} 代班" : "", Note }.Where(s => s.Length > 0));

        public Client Clone() => new() { Id = Id, Name = Name, Location = Location, CoverFor = CoverFor, Note = Note };

        public override string ToString() => Label;
    }
}
