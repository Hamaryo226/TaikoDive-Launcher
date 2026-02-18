using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using TaikoDiveLauncher.Helpers;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher
{
    public partial class MainWindow : Window
    {
        private readonly string _basePath;
        private readonly string _settingsPath;
        private readonly string _playersPath;
        private readonly string _gamePath;
        private readonly string _launcherSettingsPath;
        private readonly string _scoreDataDir;

        private GameSettings? _settings;
        private ICCardUsersData? _playersData;
        private LauncherSettings? _launcherSettings;

        // 設定画面 UIコントロール参照
        private CheckBox? _chkGuestMode, _chkB2PMode, _chkFullScreen, _chkBorderless;
        private CheckBox? _chkFullHD, _chkVSync, _chkTitleShow, _chkCollaboBack;
        private ComboBox? _cmbSoundType;
        private TextBox? _txtFontOedo, _txtFontDFGothic, _txtFontSeurat, _txtFontDomCasual, _txtFontFallback;
        private TextBox? _txtKeybinds;

        // ランチャー設定UIコントロール参照
        private CheckBox? _chkUseMica;

        // スコアビューア UIコントロール参照
        private ComboBox? _cmbScorePlayer;
        private StackPanel? _scoreListPanel;
        private readonly Dictionary<string, List<SongScore>> _scoreCache = new();
        private List<SongScore>? _currentPlayedScores;
        private int _scoreDisplayCount;

        // プレイヤーデータ UIコントロール参照
        private StackPanel? _playerListPanel;

        public MainWindow()
        {
            InitializeComponent();

            _basePath = FindProjectRoot();
            _settingsPath = Path.Combine(_basePath, "Setting.json");
            _playersPath = Path.Combine(_basePath, "Info", "ICCardUsers.json");
            _gamePath = Path.Combine(_basePath, "TaikoDive.exe");
            _launcherSettingsPath = Path.Combine(_basePath, "LauncherSetting.json");
            _scoreDataDir = Path.Combine(_basePath, "Info", "ScoreData");

            BuildSettingsUI();
            BuildPlayerDataUI();
            BuildScoreViewerUI();
            BuildLauncherSettingsUI();

            // ランチャー設定を読み込み、Mica を適用
            LoadLauncherSettings();
            ShowPage("home");

            // ウィンドウが表示された後に Mica を適用
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_launcherSettings?.UseMica == true)
            {
                ApplyMicaBackdrop(true);
            }
        }

        /// <summary>
        /// プロジェクトルート（TaikoDive.exe がある場所）を探す。
        /// dotnet run 実行時は bin/Debug/... から起動されるため、
        /// 親ディレクトリを遡って TaikoDive.exe を探す。
        /// </summary>
        private string FindProjectRoot()
        {
            // 実行ファイルのディレクトリから上位へ探索
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TaikoDive.exe")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            // 見つからなければカレントディレクトリを試す
            var currentDir = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(currentDir, "TaikoDive.exe")))
                return currentDir;

            // 最終手段: BaseDirectoryを返す
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        #region Mica バックドロップ

        /// <summary>
        /// Mica バックドロップの適用/解除とUIの透明度を切り替え。
        /// </summary>
        private void ApplyMicaBackdrop(bool enable)
        {
            if (enable && MicaHelper.IsWindows11OrLater)
            {
                bool applied = MicaHelper.ApplyMica(this, useDarkMode: true);
                if (applied)
                {
                    // 背景を半透明にしてMicaを透過させる
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0f, 0x0f, 0x17));
                    SidebarBorder.Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x11, 0x11, 0x19));
                    StatusBarBorder.Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x11, 0x11, 0x19));
                    return;
                }
            }

            // Mica 無効化 or 非対応: 不透明背景に戻す
            MicaHelper.RemoveMica(this);
            RootBorder.Background = FindResource("BgGradient") as Brush;
            SidebarBorder.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x19));
            StatusBarBorder.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x19));
        }

        #endregion

        #region ナビゲーション

        private void ShowPage(string page)
        {
            HomePage.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
            PlayerDataPage.Visibility = page == "playerdata" ? Visibility.Visible : Visibility.Collapsed;
            ScoreViewerPage.Visibility = page == "scoreviewer" ? Visibility.Visible : Visibility.Collapsed;
            LauncherSettingsPage.Visibility = page == "launchersettings" ? Visibility.Visible : Visibility.Collapsed;

            // アクティブボタンのスタイル更新
            UpdateNavButtonStyle(BtnHome, page == "home");
            UpdateNavButtonStyle(BtnSettings, page == "settings");
            UpdateNavButtonStyle(BtnPlayerData, page == "playerdata");
            UpdateNavButtonStyle(BtnScoreViewer, page == "scoreviewer");
            UpdateNavButtonStyle(BtnLauncherSettings, page == "launchersettings");
        }

        private void UpdateNavButtonStyle(Button btn, bool isActive)
        {
            if (isActive)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x38));
                btn.Foreground = FindResource("TextPrimaryBrush") as Brush;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = FindResource("TextSecondaryBrush") as Brush;
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e) => ShowPage("home");
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            ShowPage("settings");
        }
        private void BtnPlayerData_Click(object sender, RoutedEventArgs e)
        {
            LoadPlayerData();
            ShowPage("playerdata");
        }
        private void BtnScoreViewer_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("scoreviewer");
        }
        private void BtnLauncherSettings_Click(object sender, RoutedEventArgs e)
        {
            LoadLauncherSettings();
            ShowPage("launchersettings");
        }

        #endregion

        #region ウィンドウコントロール

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region マウス追従グローエフェクト

        private void HomePage_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(HomePage);

            // メイングロー: マウス位置の中心に配置
            Canvas.SetLeft(GlowOrb, pos.X - GlowOrb.Width / 2);
            Canvas.SetTop(GlowOrb, pos.Y - GlowOrb.Height / 2);

            // セカンダリグロー: 少しオフセットして深みを出す
            Canvas.SetLeft(GlowOrb2, pos.X - GlowOrb2.Width / 2 + 60);
            Canvas.SetTop(GlowOrb2, pos.Y - GlowOrb2.Height / 2 - 40);

            // マウスが戻ったら表示
            if (GlowOrb.Opacity < 0.15)
            {
                GlowOrb.Opacity = 0.15;
                GlowOrb2.Opacity = 0.08;
            }
        }

        private void HomePage_MouseLeave(object sender, MouseEventArgs e)
        {
            // ウィンドウ外に出たらグローをフェードアウト
            GlowOrb.Opacity = 0;
            GlowOrb2.Opacity = 0;
        }

        #endregion

        #region ゲーム起動

        private void BtnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(_gamePath))
                {
                    var msg = $"TaikoDive.exe が見つかりません。\nパス: {_gamePath}";
                    MessageBox.Show(msg, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _gamePath,
                    WorkingDirectory = Path.GetDirectoryName(_gamePath)!,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                TxtStatus.Text = "ゲームを起動しました...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ゲームの起動に失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 設定画面の構築

        private void BuildSettingsUI()
        {
            SettingsContent.Children.Clear();

            // タイトル
            SettingsContent.Children.Add(CreatePageTitle("ゲーム設定"));

            // --- ゲームモード設定 ---
            var modeCard = CreateCard("ゲームモード");
            var modeStack = GetCardContent(modeCard);

            _chkGuestMode = CreateToggle("ゲストモード");
            _chkB2PMode = CreateToggle("2Pモード");
            modeStack.Children.Add(_chkGuestMode);
            modeStack.Children.Add(_chkB2PMode);
            SettingsContent.Children.Add(modeCard);

            // --- 画面設定 ---
            var displayCard = CreateCard("画面設定");
            var displayStack = GetCardContent(displayCard);

            _chkFullScreen = CreateToggle("フルスクリーン");
            _chkBorderless = CreateToggle("ボーダーレスウィンドウ");
            _chkFullHD = CreateToggle("フルHD (1920x1080)");
            _chkVSync = CreateToggle("垂直同期 (VSync)");
            _chkTitleShow = CreateToggle("タイトル表示");
            _chkCollaboBack = CreateToggle("コラボ背景");

            displayStack.Children.Add(_chkFullScreen);
            displayStack.Children.Add(_chkBorderless);
            displayStack.Children.Add(_chkFullHD);
            displayStack.Children.Add(_chkVSync);
            displayStack.Children.Add(_chkTitleShow);
            displayStack.Children.Add(_chkCollaboBack);
            SettingsContent.Children.Add(displayCard);

            // --- サウンド設定 ---
            var soundCard = CreateCard("サウンド設定");
            var soundStack = GetCardContent(soundCard);

            var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            soundRow.Children.Add(new TextBlock
            {
                Text = "サウンドタイプ",
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 160,
                FontFamily = new FontFamily("Segoe UI")
            });
            _cmbSoundType = new ComboBox
            {
                Width = 200,
                Style = FindResource("ModernComboBox") as Style,
                IsEditable = false
            };
            _cmbSoundType.Items.Add("Wasapi");
            _cmbSoundType.Items.Add("ASIO");
            _cmbSoundType.Items.Add("DirectSound");
            soundRow.Children.Add(_cmbSoundType);
            soundStack.Children.Add(soundRow);
            SettingsContent.Children.Add(soundCard);

            // --- フォント設定 ---
            var fontCard = CreateCard("フォント設定");
            var fontStack = GetCardContent(fontCard);

            _txtFontOedo = CreateTextInput(fontStack, "大江戸勘亭流");
            _txtFontDFGothic = CreateTextInput(fontStack, "DF太丸ゴシック");
            _txtFontSeurat = CreateTextInput(fontStack, "スーラ");
            _txtFontDomCasual = CreateTextInput(fontStack, "Dom Casual");
            _txtFontFallback = CreateTextInput(fontStack, "フォールバック");
            SettingsContent.Children.Add(fontCard);

            // --- キーバインド設定 ---
            var keyCard = CreateCard("キーバインド");
            var keyStack = GetCardContent(keyCard);

            _txtKeybinds = CreateTextInput(keyStack, "キーバインド (カンマ区切り)");
            SettingsContent.Children.Add(keyCard);

            // --- ボタン ---
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 24)
            };
            var btnReset = new Button
            {
                Content = "リセット",
                Style = FindResource("SecondaryButton") as Style,
                Margin = new Thickness(0, 0, 12, 0)
            };
            btnReset.Click += (s, e) => LoadSettings();

            var btnSave = new Button
            {
                Content = "保存",
                Style = FindResource("AccentButton") as Style,
                Padding = new Thickness(32, 12, 32, 12),
                FontSize = 14
            };
            btnSave.Click += BtnSaveSettings_Click;

            buttonPanel.Children.Add(btnReset);
            buttonPanel.Children.Add(btnSave);
            SettingsContent.Children.Add(buttonPanel);
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonConvert.DeserializeObject<GameSettings>(json) ?? new GameSettings();
                }
                else
                {
                    _settings = new GameSettings();
                }

                // UIに反映
                _chkGuestMode!.IsChecked = _settings.GuestMode;
                _chkB2PMode!.IsChecked = _settings.B2PMode;
                _chkFullScreen!.IsChecked = _settings.FullScreen;
                _chkBorderless!.IsChecked = _settings.BorderlessWindow;
                _chkFullHD!.IsChecked = _settings.FullHD;
                _chkVSync!.IsChecked = _settings.VerticalSync;
                _chkTitleShow!.IsChecked = _settings.TitleShow;
                _chkCollaboBack!.IsChecked = _settings.CollaboBack;
                _cmbSoundType!.SelectedItem = _settings.SoundType;
                _txtFontOedo!.Text = _settings.FontOedo;
                _txtFontDFGothic!.Text = _settings.FontDFGothic;
                _txtFontSeurat!.Text = _settings.FontSeurat;
                _txtFontDomCasual!.Text = _settings.FontDomCasual;
                _txtFontFallback!.Text = _settings.FontFallback;
                _txtKeybinds!.Text = _settings.Keybinds;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の読み込みに失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings = new GameSettings
                {
                    GuestMode = _chkGuestMode!.IsChecked == true,
                    B2PMode = _chkB2PMode!.IsChecked == true,
                    FullScreen = _chkFullScreen!.IsChecked == true,
                    BorderlessWindow = _chkBorderless!.IsChecked == true,
                    FullHD = _chkFullHD!.IsChecked == true,
                    VerticalSync = _chkVSync!.IsChecked == true,
                    TitleShow = _chkTitleShow!.IsChecked == true,
                    CollaboBack = _chkCollaboBack!.IsChecked == true,
                    SoundType = _cmbSoundType!.SelectedItem?.ToString() ?? "Wasapi",
                    FontOedo = _txtFontOedo!.Text,
                    FontDFGothic = _txtFontDFGothic!.Text,
                    FontSeurat = _txtFontSeurat!.Text,
                    FontDomCasual = _txtFontDomCasual!.Text,
                    FontFallback = _txtFontFallback!.Text,
                    Keybinds = _txtKeybinds!.Text
                };

                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);

                TxtStatus.Text = "設定を保存しました";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region ランチャー設定画面の構築

        private void BuildLauncherSettingsUI()
        {
            LauncherSettingsContent.Children.Clear();

            // タイトル
            LauncherSettingsContent.Children.Add(CreatePageTitle("ランチャー設定"));

            // --- 外観設定 ---
            var appearanceCard = CreateCard("外観");
            var appearanceStack = GetCardContent(appearanceCard);

            _chkUseMica = CreateToggle("Mica バックドロップ");
            _chkUseMica.ToolTip = "Windows 11 の Mica エフェクトを使用します (Windows 11 以降のみ)";
            appearanceStack.Children.Add(_chkUseMica);

            // Mica 非対応の場合の注意書き
            if (!MicaHelper.IsWindows11OrLater)
            {
                var warningText = new TextBlock
                {
                    Text = "⚠ Mica は Windows 11 以降でのみ利用できます",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)),
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontFamily = new FontFamily("Segoe UI")
                };
                appearanceStack.Children.Add(warningText);
            }

            LauncherSettingsContent.Children.Add(appearanceCard);

            // --- ボタン ---
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 24)
            };

            var btnSave = new Button
            {
                Content = "保存",
                Style = FindResource("AccentButton") as Style,
                Padding = new Thickness(32, 12, 32, 12),
                FontSize = 14
            };
            btnSave.Click += BtnSaveLauncherSettings_Click;

            buttonPanel.Children.Add(btnSave);
            LauncherSettingsContent.Children.Add(buttonPanel);
        }

        private void LoadLauncherSettings()
        {
            try
            {
                if (File.Exists(_launcherSettingsPath))
                {
                    var json = File.ReadAllText(_launcherSettingsPath);
                    _launcherSettings = JsonConvert.DeserializeObject<LauncherSettings>(json) ?? new LauncherSettings();
                }
                else
                {
                    _launcherSettings = new LauncherSettings();
                }

                // UIに反映
                if (_chkUseMica != null)
                {
                    _chkUseMica.IsChecked = _launcherSettings.UseMica;
                }
            }
            catch (Exception ex)
            {
                _launcherSettings = new LauncherSettings();
                MessageBox.Show($"ランチャー設定の読み込みに失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveLauncherSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool previousMica = _launcherSettings?.UseMica ?? false;

                _launcherSettings = new LauncherSettings
                {
                    UseMica = _chkUseMica!.IsChecked == true
                };

                var json = JsonConvert.SerializeObject(_launcherSettings, Formatting.Indented);
                File.WriteAllText(_launcherSettingsPath, json);

                // Mica 設定が変更された場合、即座に適用
                if (previousMica != _launcherSettings.UseMica)
                {
                    ApplyMicaBackdrop(_launcherSettings.UseMica);
                }

                TxtStatus.Text = "ランチャー設定を保存しました";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ランチャー設定の保存に失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region プレイヤーデータ画面の構築

        private void BuildPlayerDataUI()
        {
            PlayerDataContent.Children.Clear();

            // タイトル
            PlayerDataContent.Children.Add(CreatePageTitle("プレイヤーデータ"));

            // プレイヤーリスト
            _playerListPanel = new StackPanel();
            PlayerDataContent.Children.Add(_playerListPanel);

            // 追加・保存ボタン
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 24)
            };

            var btnAdd = new Button
            {
                Content = "プレイヤー追加",
                Style = FindResource("SecondaryButton") as Style,
                Margin = new Thickness(0, 0, 12, 0)
            };
            btnAdd.Click += BtnAddPlayer_Click;

            var btnSave = new Button
            {
                Content = "保存",
                Style = FindResource("AccentButton") as Style,
                Padding = new Thickness(32, 12, 32, 12),
                FontSize = 14
            };
            btnSave.Click += BtnSavePlayerData_Click;

            buttonPanel.Children.Add(btnAdd);
            buttonPanel.Children.Add(btnSave);
            PlayerDataContent.Children.Add(buttonPanel);
        }

        private void LoadPlayerData()
        {
            try
            {
                if (File.Exists(_playersPath))
                {
                    var json = File.ReadAllText(_playersPath);
                    _playersData = JsonConvert.DeserializeObject<ICCardUsersData>(json) ?? new ICCardUsersData();
                }
                else
                {
                    _playersData = new ICCardUsersData();
                }

                RefreshPlayerList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プレイヤーデータの読み込みに失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPlayerList()
        {
            _playerListPanel!.Children.Clear();

            if (_playersData?.CardToProfile == null || _playersData.CardToProfile.Count == 0)
            {
                var emptyMsg = new TextBlock
                {
                    Text = "プレイヤーデータがありません。「プレイヤー追加」で新しいプレイヤーを追加してください。",
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    FontSize = 14,
                    Margin = new Thickness(0, 20, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                };
                _playerListPanel.Children.Add(emptyMsg);
                return;
            }

            foreach (var kvp in _playersData.CardToProfile)
            {
                var card = CreatePlayerCard(kvp.Key, kvp.Value);
                _playerListPanel.Children.Add(card);
            }
        }

        private Border CreatePlayerCard(string cardId, PlayerProfile profile)
        {
            var card = new Border
            {
                Style = FindResource("CardPanel") as Style
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();

            // カードID表示
            var idLabel = new TextBlock
            {
                Text = "ICカードID",
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 0, 0, 2)
            };
            stack.Children.Add(idLabel);

            var txtCardId = new TextBox
            {
                Text = cardId,
                Style = FindResource("ModernTextBox") as Style,
                Margin = new Thickness(0, 0, 0, 12),
                Tag = "cardId",
                IsReadOnly = false,
                FontSize = 11,
                Foreground = FindResource("TextSecondaryBrush") as Brush
            };
            stack.Children.Add(txtCardId);

            // 名前
            var nameRow = CreateLabeledInput("名前", profile.Name);
            stack.Children.Add(nameRow);

            // 称号
            var titleRow = CreateLabeledInput("称号", profile.Title);
            stack.Children.Add(titleRow);

            // ネームプレートタイプ
            var plateRow = CreateLabeledInput("ネームプレートタイプ", profile.NamePlateType.ToString());
            stack.Children.Add(plateRow);

            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            // 削除ボタン（小さめ、二段階確認）
            var btnDelete = new Button
            {
                Content = "削除",
                Style = FindResource("SecondaryButton") as Style,
                VerticalAlignment = VerticalAlignment.Top,
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(12, 0, 0, 0),
                MinWidth = 0,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50)),
                ToolTip = "このプレイヤーを削除"
            };
            btnDelete.Tag = new object[] { card, false }; // [card, isConfirming]
            btnDelete.Click += BtnDeletePlayer_Click;
            Grid.SetColumn(btnDelete, 1);
            grid.Children.Add(btnDelete);

            card.Child = grid;
            card.Tag = cardId; // 元のカードIDを保持
            return card;
        }

        private StackPanel CreateLabeledInput(string label, string value)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                FontFamily = new FontFamily("Segoe UI")
            });
            row.Children.Add(new TextBox
            {
                Text = value,
                Style = FindResource("ModernTextBox") as Style,
                Tag = label,
                MaxWidth = 400,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300
            });
            return row;
        }

        private void BtnAddPlayer_Click(object sender, RoutedEventArgs e)
        {
            _playersData ??= new ICCardUsersData();

            var newCardId = "NEW_CARD_" + DateTime.Now.Ticks;
            var newProfile = new PlayerProfile
            {
                Name = "新しいプレイヤー",
                Title = "",
                NamePlateType = 0
            };

            _playersData.CardToProfile[newCardId] = newProfile;
            var card = CreatePlayerCard(newCardId, newProfile);
            _playerListPanel!.Children.Add(card);
        }

        private void BtnDeletePlayer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not object[] tagData || tagData.Length < 2)
                return;

            var card = tagData[0] as Border;
            var isConfirming = (bool)tagData[1];

            if (!isConfirming)
            {
                // 第1段階: 確認モードに切り替え
                btn.Content = "本当に？";
                btn.Background = new SolidColorBrush(Color.FromRgb(0x80, 0x20, 0x20));
                btn.Foreground = Brushes.White;
                tagData[1] = true;

                // 2秒後に自動リセット
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (tagData[1] is true)
                    {
                        btn.Content = "削除";
                        btn.Background = FindResource("BgCardBrush") as Brush;
                        btn.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
                        tagData[1] = false;
                    }
                };
                timer.Start();
            }
            else
            {
                // 第2段階: 実際に削除
                if (card != null)
                {
                    var cardId = card.Tag?.ToString();
                    if (cardId != null && _playersData != null)
                    {
                        _playersData.CardToProfile.Remove(cardId);
                    }
                    _playerListPanel!.Children.Remove(card);
                }
            }
        }

        private void BtnSavePlayerData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UIからデータを収集
                var newData = new ICCardUsersData();

                foreach (var child in _playerListPanel!.Children)
                {
                    if (child is Border card && card.Child is Grid grid)
                    {
                        var stack = grid.Children.OfType<StackPanel>().FirstOrDefault();
                        if (stack == null) continue;

                        var textBoxes = FindAllTextBoxes(stack);
                        var cardIdBox = textBoxes.FirstOrDefault(t => t.Tag?.ToString() == "cardId");
                        var nameBox = textBoxes.FirstOrDefault(t => t.Tag?.ToString() == "名前");
                        var titleBox = textBoxes.FirstOrDefault(t => t.Tag?.ToString() == "称号");
                        var plateBox = textBoxes.FirstOrDefault(t => t.Tag?.ToString() == "ネームプレートタイプ");

                        if (cardIdBox != null)
                        {
                            var profile = new PlayerProfile
                            {
                                Name = nameBox?.Text ?? "",
                                Title = titleBox?.Text ?? "",
                                NamePlateType = int.TryParse(plateBox?.Text, out var pt) ? pt : 0
                            };
                            newData.CardToProfile[cardIdBox.Text] = profile;
                        }
                    }
                }

                _playersData = newData;
                var json = JsonConvert.SerializeObject(_playersData, Formatting.Indented);

                // ディレクトリが存在しない場合は作成
                var dir = Path.GetDirectoryName(_playersPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_playersPath, json);
                TxtStatus.Text = "プレイヤーデータを保存しました";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プレイヤーデータの保存に失敗しました。\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<TextBox> FindAllTextBoxes(DependencyObject parent)
        {
            var list = new List<TextBox>();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb)
                    list.Add(tb);
                else
                    list.AddRange(FindAllTextBoxes(child));
            }

            // LogicalChildren もチェック（StackPanelの子要素など）
            if (parent is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBox tb && !list.Contains(tb))
                        list.Add(tb);
                    else if (child is Panel subPanel)
                        list.AddRange(FindAllTextBoxes(subPanel).Where(t => !list.Contains(t)));
                }
            }

            return list;
        }

        #endregion

        #region UI ヘルパー

        private TextBlock CreatePageTitle(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 24),
                FontFamily = new FontFamily("Segoe UI")
            };
        }

        private Border CreateCard(string title)
        {
            var card = new Border
            {
                Style = FindResource("CardPanel") as Style
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 16),
                FontFamily = new FontFamily("Segoe UI")
            });

            card.Child = stack;
            return card;
        }

        private StackPanel GetCardContent(Border card)
        {
            return (StackPanel)card.Child;
        }

        private CheckBox CreateToggle(string label)
        {
            return new CheckBox
            {
                Content = label,
                Style = FindResource("ToggleSwitch") as Style,
                Margin = new Thickness(0, 6, 0, 6)
            };
        }

        private TextBox CreateTextInput(StackPanel parent, string label)
        {
            var row = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                FontFamily = new FontFamily("Segoe UI")
            });
            var textBox = new TextBox
            {
                Style = FindResource("ModernTextBox") as Style,
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 400
            };
            row.Children.Add(textBox);
            parent.Children.Add(row);
            return textBox;
        }

        #endregion

        #region スコア記録ビューア

        private const int ScorePageSize = 50;

        private void BuildScoreViewerUI()
        {
            ScoreViewerContent.Children.Clear();
            ScoreViewerContent.Children.Add(CreatePageTitle("スコア記録"));

            // プレイヤー選択
            var selectorCard = CreateCard("プレイヤー選択");
            var selectorStack = GetCardContent(selectorCard);

            _cmbScorePlayer = new ComboBox
            {
                Width = 300,
                Style = FindResource("ModernComboBox") as Style,
                IsEditable = false,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var players = ScoreDataParser.GetPlayerNames(_scoreDataDir);
            foreach (var p in players)
                _cmbScorePlayer.Items.Add(p);
            _cmbScorePlayer.SelectionChanged += CmbScorePlayer_SelectionChanged;
            selectorStack.Children.Add(_cmbScorePlayer);

            if (players.Count == 0)
            {
                selectorStack.Children.Add(new TextBlock
                {
                    Text = "スコアデータが見つかりません",
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    FontSize = 13, Margin = new Thickness(0, 8, 0, 0),
                    FontFamily = new FontFamily("Segoe UI")
                });
            }
            ScoreViewerContent.Children.Add(selectorCard);

            _scoreListPanel = new StackPanel();
            ScoreViewerContent.Children.Add(_scoreListPanel);

            if (players.Count > 0)
                _cmbScorePlayer.SelectedIndex = 0;
        }

        private async void CmbScorePlayer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cmbScorePlayer?.SelectedItem == null || _scoreListPanel == null) return;

            var playerName = _cmbScorePlayer.SelectedItem.ToString()!;
            _scoreListPanel.Children.Clear();

            // ローディング表示
            var loadingText = new TextBlock
            {
                Text = "読み込み中...",
                FontSize = 14,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                FontFamily = new FontFamily("Segoe UI")
            };
            _scoreListPanel.Children.Add(loadingText);
            TxtStatus.Text = $"{playerName} のスコアを読み込み中...";

            // キャッシュ or 非同期読み込み
            List<SongScore> scores;
            if (_scoreCache.TryGetValue(playerName, out var cached))
            {
                scores = cached;
            }
            else
            {
                var dir = _scoreDataDir;
                scores = await Task.Run(() => ScoreDataParser.LoadPlayerScores(dir, playerName));
                _scoreCache[playerName] = scores;
            }

            _scoreListPanel.Children.Clear();

            // 統計データ収集
            var allDiffs = scores.SelectMany(s => s.Difficulties).Where(d => d.IsPlayed).ToList();
            var playedCount = scores.Count(s => s.BestPlayedDifficulty != null);
            var totalCount = scores.Count;
            int noClearCount = allDiffs.Count(d => d.Crown == "NoClear");
            int clearCount = allDiffs.Count(d => d.Crown == "Clear");
            int fullComboCount = allDiffs.Count(d => d.Crown == "FullCombo");
            int allPerfectCount = allDiffs.Count(d => d.Crown == "AllPerfect");

            // ═══ 統計サマリー ═══
            var summaryCard = CreateCard($"📊 {playerName} の記録");
            var summaryStack = GetCardContent(summaryCard);
            var statsGrid = new Grid();
            for (int i = 0; i < 4; i++)
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddStatItem(statsGrid, 0, "プレイ曲数", $"{playedCount}/{totalCount}");
            AddStatItem(statsGrid, 1, "クリア", $"{clearCount}");
            AddStatItem(statsGrid, 2, "フルコンボ", $"{fullComboCount}");
            AddStatItem(statsGrid, 3, "全良", $"{allPerfectCount}");
            summaryStack.Children.Add(statsGrid);
            _scoreListPanel.Children.Add(summaryCard);

            // ═══ グラフエリア ═══
            var chartsGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Crown 分布チャート
            var crownChart = CreateBarChart("クラウン分布",
                new[] { "未クリア", "クリア", "フルコン", "全良" },
                new[] { noClearCount, clearCount, fullComboCount, allPerfectCount },
                new[] { "#EF4444", "#FBBF24", "#4ADE80", "#A855F7" });
            Grid.SetColumn(crownChart, 0);
            chartsGrid.Children.Add(crownChart);

            // 難易度別プレイ数チャート
            string[] diffNames = { "Easy", "Normal", "Hard", "Oni", "Edit" };
            string[] diffColors = { "#4ADE80", "#FBBF24", "#EF4444", "#A855F7", "#3B82F6" };
            var diffCounts = diffNames.Select(d => allDiffs.Count(x => x.Difficulty == d)).ToArray();
            var diffChart = CreateBarChart("難易度別プレイ数", diffNames, diffCounts, diffColors);
            Grid.SetColumn(diffChart, 1);
            chartsGrid.Children.Add(diffChart);

            _scoreListPanel.Children.Add(chartsGrid);

            // スコア分布チャート（横幅フル）
            var bestScores = scores.Where(s => s.BestPlayedDifficulty != null)
                                   .Select(s => s.BestPlayedDifficulty!.Score).ToList();
            if (bestScores.Count > 0)
            {
                var scoreDistChart = CreateScoreDistributionChart("スコア分布", bestScores);
                _scoreListPanel.Children.Add(scoreDistChart);
            }

            // ═══ 楽曲リスト（ページネーション） ═══
            _currentPlayedScores = scores.Where(s => s.BestPlayedDifficulty != null)
                                          .OrderByDescending(s => s.BestPlayedDifficulty!.Score)
                                          .ToList();
            _scoreDisplayCount = 0;

            if (_currentPlayedScores.Count == 0)
            {
                _scoreListPanel.Children.Add(new TextBlock
                {
                    Text = "プレイ済みの楽曲がありません",
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    FontSize = 14, Margin = new Thickness(0, 20, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                });
            }
            else
            {
                LoadMoreScoreCards();
            }

            TxtStatus.Text = $"{playerName}: {playedCount}曲プレイ済み";
        }

        private void LoadMoreScoreCards()
        {
            if (_currentPlayedScores == null || _scoreListPanel == null) return;

            // 「もっと読み込む」ボタンがあれば削除
            if (_scoreListPanel.Children.Count > 0 &&
                _scoreListPanel.Children[_scoreListPanel.Children.Count - 1] is Button)
            {
                _scoreListPanel.Children.RemoveAt(_scoreListPanel.Children.Count - 1);
            }

            var batch = _currentPlayedScores.Skip(_scoreDisplayCount).Take(ScorePageSize).ToList();
            foreach (var song in batch)
            {
                _scoreListPanel.Children.Add(CreateSongScoreCard(song));
            }
            _scoreDisplayCount += batch.Count;

            // まだ残りがあれば「もっと読み込む」ボタン
            if (_scoreDisplayCount < _currentPlayedScores.Count)
            {
                var remaining = _currentPlayedScores.Count - _scoreDisplayCount;
                var btnMore = new Button
                {
                    Content = $"もっと読み込む (残り {remaining} 曲)",
                    Style = FindResource("ModernButton") as Style,
                    Padding = new Thickness(32, 12, 32, 12),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 24)
                };
                btnMore.Click += (_, _) => LoadMoreScoreCards();
                _scoreListPanel.Children.Add(btnMore);
            }
        }

        // --- チャート描画 ---

        private Border CreateBarChart(string title, string[] labels, int[] values, string[] colors)
        {
            var card = new Border
            {
                Style = FindResource("CardPanel") as Style
            };
            var stack = new StackPanel();

            // タイトル
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = new FontFamily("Segoe UI")
            });

            int maxVal = values.Length > 0 ? values.Max() : 1;
            if (maxVal == 0) maxVal = 1;

            for (int i = 0; i < labels.Length; i++)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                // ラベル
                var lbl = new TextBlock
                {
                    Text = labels[i], FontSize = 11,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(lbl, 0);
                rowGrid.Children.Add(lbl);

                // バー
                var barColor = (Color)ColorConverter.ConvertFromString(colors[i]);
                double barWidth = (double)values[i] / maxVal;
                var barContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Height = 20,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var bar = new Border
                {
                    Background = new SolidColorBrush(barColor),
                    CornerRadius = new CornerRadius(4),
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MinWidth = values[i] > 0 ? 4 : 0,
                    Opacity = 0.85
                };
                bar.Loaded += (s, _) =>
                {
                    var b = (Border)s;
                    var parent = b.Parent as Border;
                    if (parent != null && parent.ActualWidth > 0)
                        b.Width = parent.ActualWidth * barWidth;
                };
                barContainer.Child = bar;
                Grid.SetColumn(barContainer, 1);
                rowGrid.Children.Add(barContainer);

                // 数値
                var valText = new TextBlock
                {
                    Text = values[i].ToString(),
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(barColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(valText, 2);
                rowGrid.Children.Add(valText);

                stack.Children.Add(rowGrid);
            }

            card.Child = stack;
            return card;
        }

        private Border CreateScoreDistributionChart(string title, List<int> scores)
        {
            var card = new Border { Style = FindResource("CardPanel") as Style };
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = new FontFamily("Segoe UI")
            });

            // スコア範囲のバケット
            var buckets = new (string label, int min, int max, string color)[]
            {
                ("0-400K",     0,      400000, "#EF4444"),
                ("400K-600K",  400000, 600000, "#F97316"),
                ("600K-800K",  600000, 800000, "#FBBF24"),
                ("800K-900K",  800000, 900000, "#4ADE80"),
                ("900K-950K",  900000, 950000, "#22D3EE"),
                ("950K-1M",    950000, 1000000, "#818CF8"),
                ("1M+",        1000000, int.MaxValue, "#A855F7")
            };

            var labels = buckets.Select(b => b.label).ToArray();
            var values = buckets.Select(b => scores.Count(s => s >= b.min && s < b.max)).ToArray();
            var colors = buckets.Select(b => b.color).ToArray();

            int maxVal = values.Length > 0 ? values.Max() : 1;
            if (maxVal == 0) maxVal = 1;

            for (int i = 0; i < labels.Length; i++)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                var lbl = new TextBlock
                {
                    Text = labels[i], FontSize = 11,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(lbl, 0);
                rowGrid.Children.Add(lbl);

                var barColor = (Color)ColorConverter.ConvertFromString(colors[i]);
                double barWidth = (double)values[i] / maxVal;
                var barContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Height = 20, VerticalAlignment = VerticalAlignment.Center
                };
                var bar = new Border
                {
                    Background = new SolidColorBrush(barColor),
                    CornerRadius = new CornerRadius(4),
                    Height = 20, HorizontalAlignment = HorizontalAlignment.Left,
                    MinWidth = values[i] > 0 ? 4 : 0, Opacity = 0.85
                };
                bar.Loaded += (s, _) =>
                {
                    var b = (Border)s;
                    var parent = b.Parent as Border;
                    if (parent != null && parent.ActualWidth > 0)
                        b.Width = parent.ActualWidth * barWidth;
                };
                barContainer.Child = bar;
                Grid.SetColumn(barContainer, 1);
                rowGrid.Children.Add(barContainer);

                var valText = new TextBlock
                {
                    Text = values[i].ToString(),
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(barColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Grid.SetColumn(valText, 2);
                rowGrid.Children.Add(valText);

                stack.Children.Add(rowGrid);
            }

            card.Child = stack;
            return card;
        }

        // --- スコアカード ---

        private void AddStatItem(Grid grid, int col, string label, string value)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = FindResource("AccentOrangeBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            });
            Grid.SetColumn(stack, col);
            grid.Children.Add(stack);
        }

        private Border CreateSongScoreCard(SongScore song)
        {
            var card = new Border { Style = FindResource("CardPanel") as Style };
            var mainStack = new StackPanel();

            mainStack.Children.Add(new TextBlock
            {
                Text = song.SongName, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 10),
                FontFamily = new FontFamily("Segoe UI")
            });

            var diffGrid = new Grid();
            for (int i = 0; i < 5; i++)
                diffGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string[] cardDiffColors = { "#4ADE80", "#FBBF24", "#EF4444", "#A855F7", "#3B82F6" };
            for (int i = 0; i < song.Difficulties.Count; i++)
            {
                var diffPanel = CreateDifficultyPanel(song.Difficulties[i], cardDiffColors[i]);
                Grid.SetColumn(diffPanel, i);
                diffGrid.Children.Add(diffPanel);
            }

            mainStack.Children.Add(diffGrid);
            card.Child = mainStack;
            return card;
        }

        private Border CreateDifficultyPanel(DifficultyScore diff, string colorHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var isPlayed = diff.IsPlayed;

            var panel = new Border
            {
                Background = isPlayed
                    ? new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B))
                    : new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(2, 0, 2, 0)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            stack.Children.Add(new TextBlock
            {
                Text = diff.Difficulty, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = isPlayed ? new SolidColorBrush(color) : FindResource("TextSecondaryBrush") as Brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            });

            if (isPlayed)
            {
                var crownText = diff.Crown switch
                {
                    "AllPerfect" => "👑",
                    "FullCombo" => "🥇",
                    "Clear" => "✅",
                    _ => "❌"
                };
                stack.Children.Add(new TextBlock
                {
                    Text = crownText, FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2)
                });
                stack.Children.Add(new TextBlock
                {
                    Text = diff.Score.ToString("N0"), FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = FindResource("TextPrimaryBrush") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                });
                stack.Children.Add(new TextBlock
                {
                    Text = $"{diff.MaxCombo}combo", FontSize = 10,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                });
                stack.Children.Add(new TextBlock
                {
                    Text = $"良{diff.Great} 可{diff.Good} 不可{diff.Miss}",
                    FontSize = 9,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    FontFamily = new FontFamily("Segoe UI")
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "―", FontSize = 14,
                    Foreground = FindResource("TextSecondaryBrush") as Brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontFamily = new FontFamily("Segoe UI")
                });
            }

            panel.Child = stack;
            return panel;
        }

        #endregion
    }
}
