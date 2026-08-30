namespace TaikoDiveLauncher.Models;

public sealed class GameSettings
{
    public bool GuestMode { get; set; } = true;

    public bool TwoPlayerMode { get; set; }

    public bool FullScreen { get; set; }

    public bool BorderlessWindow { get; set; }

    public int ScreenWidth { get; set; } = 1920;

    public bool VerticalSync { get; set; } = true;

    public bool TitleShow { get; set; } = true;

    public bool CollaboBack { get; set; }

    public bool SaveBestReplay { get; set; } = true;

    public bool FreePlay { get; set; }

    public int MasterVolume { get; set; } = 100;

    public int MusicVolume { get; set; } = 100;

    public int SoundEffectVolume { get; set; } = 100;

    public string BackgroundMovieLayoutMode { get; set; } = "FullScreen";

    public string SoundType { get; set; } = "DirectSound";

    public int SoundBufferSamples { get; set; }

    public string FontOedo { get; set; } = "FOT-大江戸勘亭流 Std E";

    public string FontDFGothic { get; set; } = "ＤＦ太丸ゴシック体 Pro-5";

    public string FontSeurat { get; set; } = "FOT-スーラ Pro B";

    public string FontDomCasual { get; set; } = "Dom Casual";

    public string FontFallback { get; set; } = "Comic Sans MS";

    public bool ReduceTextureColorTo16bit { get; set; }

    public int CharaAnimationFrameSkip { get; set; } = 3;

    public bool ReduceCharaTextureColorTo16bit { get; set; }

    public bool ReduceBgTextureColorTo16bit { get; set; }

    public bool UseCompressedSongSound { get; set; } = true;

    public int OnlinePort { get; set; } = 22047;

    public string LastJoinAddress { get; set; } = string.Empty;
}
