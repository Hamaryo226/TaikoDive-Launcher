using Newtonsoft.Json;

namespace TaikoDiveLauncher.Models
{
    public class GameSettings
    {
        [JsonProperty("guestMode")]
        public bool GuestMode { get; set; }

        [JsonProperty("b2PMode")]
        public bool B2PMode { get; set; }

        [JsonProperty("fullScreen")]
        public bool FullScreen { get; set; }

        [JsonProperty("borderlessWindow")]
        public bool BorderlessWindow { get; set; }

        [JsonProperty("fullHD")]
        public bool FullHD { get; set; }

        [JsonProperty("verticalSync")]
        public bool VerticalSync { get; set; }

        [JsonProperty("titleShow")]
        public bool TitleShow { get; set; }

        [JsonProperty("collaboBack")]
        public bool CollaboBack { get; set; }

        [JsonProperty("soundType")]
        public string SoundType { get; set; } = "Wasapi";

        [JsonProperty("fontOedo")]
        public string FontOedo { get; set; } = "";

        [JsonProperty("fontDFGothic")]
        public string FontDFGothic { get; set; } = "";

        [JsonProperty("fontSeurat")]
        public string FontSeurat { get; set; } = "";

        [JsonProperty("fontDomCasual")]
        public string FontDomCasual { get; set; } = "";

        [JsonProperty("fontFallback")]
        public string FontFallback { get; set; } = "";

        [JsonProperty("keybinds")]
        public string Keybinds { get; set; } = "d,f,j,k";
    }
}
