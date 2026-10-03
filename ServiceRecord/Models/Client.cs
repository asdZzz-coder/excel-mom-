using System.Text.Json.Serialization;

namespace ServiceRecord.Models
{
    /// <summary>個案：姓名與服務地點（例如 慶東街）。Id 固定不變，改名後紀錄仍對得上。</summary>
    public class Client
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";

        /// <summary>Excel 上的寫法：慶東街(王林妙芳)；沒有地點時只有姓名。</summary>
        [JsonIgnore]
        public string Label => Location.Length == 0 ? Name : $"{Location}({Name})";

        public Client Clone() => new() { Id = Id, Name = Name, Location = Location };

        public override string ToString() => Label;
    }
}
