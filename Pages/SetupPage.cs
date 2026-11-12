using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Pages;

public sealed partial class SetupPage : ToolkitPageBase, IUnsavedChangesAware
{
    private readonly StackPanel _stepBody = new() { Spacing = 12 };
    private UserProfile? _profile;
    private GameSettings? _settings;
    private int _step;
    private bool _dirty;
    public bool HasUnsavedChanges
    {
        get => _dirty || _stepBody.Children.OfType<Frame>().Any(f => f.Content is IUnsavedChangesAware { HasUnsavedChanges: true });
        private set => _dirty = value;
    }
    public string UnsavedChangesName => "セットアップ";

    public SetupPage() : base("初回セットアップ", "名前 → 入力 → 音量 → 画面の順に設定します。既存の設定から始めます。")
    {
        Body.Children.Add(_stepBody);
        Loaded += async (_, _) =>
        {
            try { _profile = (await new UserProfileStore().LoadAsync(Installation))[0]; _settings = await new GameSettingsStore().LoadAsync(Installation); ShowStep(); }
            catch (Exception ex) { Message(ex.Message, true); }
        };
    }

    private void ShowStep()
    {
        if (_profile is null || _settings is null) return;
        _stepBody.Children.Clear(); _stepBody.Children.Add(Text($"{_step + 1} / 4", 20));
        if (_step == 0)
        {
            var name = new TextBox { Header = "ユーザー1の名前", Text = _profile.Name, MaxLength = 40 };
            name.TextChanged += (_, _) => { _profile.Name = name.Text; HasUnsavedChanges = true; };
            var saveScores = new ToggleSwitch { Header = "このユーザーの成績を保存（ゲストモードを解除）", IsOn = !_settings.GuestMode };
            saveScores.Toggled += (_, _) => { _settings.GuestMode = !saveScores.IsOn; HasUnsavedChanges = true; };
            _stepBody.Children.Add(name); _stepBody.Children.Add(saveScores);
        }
        else if (_step == 1)
        {
            _stepBody.Children.Add(Text("キー・コントローラーを割り当て、下の入力設定画面の「保存」を押してください。既存の割り当てを使う場合はそのまま次へ進めます。"));
            var frame = new Frame { Height = 500 }; frame.Navigate(typeof(InputSettingsPage)); _stepBody.Children.Add(frame);
        }
        else if (_step == 2)
        {
            var volume = new Slider { Header = "マスター音量", Minimum = 0, Maximum = 100, StepFrequency = 1, Value = _settings.MasterVolume };
            volume.ValueChanged += (_, _) => { _settings.MasterVolume = (int)volume.Value; HasUnsavedChanges = true; };
            _stepBody.Children.Add(volume); _stepBody.Children.Add(Text("音量は次回のゲーム起動から反映します。"));
        }
        else
        {
            var full = new ToggleSwitch { Header = "フルスクリーン", IsOn = _settings.FullScreen };
            full.Toggled += (_, _) => { _settings.FullScreen = full.IsOn; HasUnsavedChanges = true; };
            var resolution = new ComboBox { Header = "画面幅", ItemsSource = new[] { 1280, 1600, 1920, 2560, 3840 }, SelectedItem = _settings.ScreenWidth };
            resolution.SelectionChanged += (_, _) => { if (resolution.SelectedItem is int width) { _settings.ScreenWidth = width; HasUnsavedChanges = true; } };
            _stepBody.Children.Add(full); _stepBody.Children.Add(resolution);
        }
        if (_step > 0) Action("前へ", () => { _step--; ShowStep(); return Task.CompletedTask; }, _stepBody);
        Action(_step == 3 ? "保存して完了" : "次へ", async () =>
        {
            if (_step == 0 && string.IsNullOrWhiteSpace(_profile.Name)) throw new InvalidOperationException("名前を入力してください。");
            if (_step == 1 && _stepBody.Children.OfType<Frame>().FirstOrDefault()?.Content is IUnsavedChangesAware { HasUnsavedChanges: true })
                throw new InvalidOperationException("入力設定の保存を押してから次へ進んでください。");
            if (_step < 3) { _step++; ShowStep(); return; }
            await new UserProfileStore().SaveAsync(Installation, _profile);
            await new GameSettingsStore().SaveAsync(Installation, _settings);
            Host.Context.Preferences.SetupCompleted = true; await Host.Context.SavePreferencesAsync(); HasUnsavedChanges = false;
            MainPage.Current?.NavigateTo("home");
        }, _stepBody);
    }
}
