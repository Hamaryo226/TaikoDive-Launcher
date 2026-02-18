using Newtonsoft.Json;
using System.Collections.Generic;

namespace TaikoDiveLauncher.Models
{
    public class PlayerProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("namePlateType")]
        public int NamePlateType { get; set; }
    }

    public class ICCardUsersData
    {
        [JsonProperty("cardToProfile")]
        public Dictionary<string, PlayerProfile> CardToProfile { get; set; } = new();
    }

    // UI表示用のラッパークラス
    public class PlayerEntry
    {
        public string CardId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public int NamePlateType { get; set; }
    }
}
