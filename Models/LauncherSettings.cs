using Newtonsoft.Json;

namespace TaikoDiveLauncher.Models
{
    public class LauncherSettings
    {
        [JsonProperty("useMica")]
        public bool UseMica { get; set; } = false;
    }
}
