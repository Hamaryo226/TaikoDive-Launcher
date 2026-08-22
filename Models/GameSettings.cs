namespace TaikoDiveLauncher.Models;

public sealed class GameSettings
{
    public bool GuestMode { get; set; } = true;

    public bool TwoPlayerMode { get; set; }

    public bool FullScreen { get; set; }

    public bool BorderlessWindow { get; set; }

    public int ScreenWidth { get; set; } = 1920;

    public bool VerticalSync { get; set; } = true;

    public bool SaveBestReplay { get; set; } = true;

    public int MasterVolume { get; set; } = 100;

    public int MusicVolume { get; set; } = 100;

    public int SoundEffectVolume { get; set; } = 100;

    public string SoundType { get; set; } = "DirectSound";

    public int SoundBufferSamples { get; set; }

    public bool ReduceTextureColorTo16bit { get; set; }

    public int CharaAnimationFrameSkip { get; set; } = 3;

    public bool ReduceCharaTextureColorTo16bit { get; set; }

    public bool UseCompressedSongSound { get; set; } = true;
}
