using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace TaikoDiveLauncher.Pages;

public sealed partial class DemoSongsPage : Page, IUnsavedChangesAware
{
    private static readonly string[] SubPositions = ["左上", "右上", "左下", "右下"];
    private sealed class EntryEditor(DemoSongEntry entry)
    {
        internal DemoSongEntry Entry = entry;
        internal TextBlock Heading = new(), Help = new();
        internal Button Main = new();
        internal ToggleSwitch Auto = new();
        internal Grid Manual = new();
        internal Button[] Subs = new Button[4];
        internal Button Remove = new();
        internal Border Card = new();
        internal Grid Handle = new();
        internal Border DropIndicator = new();
    }
    private readonly DemoSongSettingsStore _store = new();
    private readonly List<DemoSongEntry> _entries = [];
    private readonly List<EntryEditor> _editors = [];
    private readonly ObservableCollection<Border> _cards = [];
    private EntryEditor? _dragEditor, _dropEditor;
    private ScrollViewer? _dragScroller;
    private Point _dragStart, _dragPoint;
    private uint _dragPointer;
    private int _dropIndex;
    private bool _dragActive;
    private double _dragScrollStart;
    private readonly DispatcherTimer _dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private IReadOnlyList<SelectedDemoSong> _catalog = [];
    private readonly Dictionary<string, SelectedDemoSong> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly MediaPlayer _player = new() { AutoPlay = false, Volume = 0.5 };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(4.73) };
    private readonly CancellationTokenSource _cancellation = new();
    private bool _started, _disposed, _dirty, _saving, _canSave, _settingsFailed, _previewRequested;
    private bool _loading = true;
    private double _previewStart;
    private TextBlock? _pickerPreviewText;
    public bool HasUnsavedChanges => _dirty;
    public string UnsavedChangesName => "タイトルの紹介曲";
    private bool UseCustom => !StandardToggle.IsOn;
    private TaikoDiveInstallation Installation => ((App)Application.Current).Context.Installation
        ?? throw new InvalidOperationException("ゲーム本体が見つかりません。");

    public DemoSongsPage()
    {
        InitializeComponent();
        _dragScrollTimer.Tick += (_, _) =>
        {
            if (!_dragActive || _dragScroller is null || !InsideList(_dragPoint)) return;
            double step = _dragPoint.Y < 48 ? -16 : _dragPoint.Y > CardsList.ActualHeight - 48 ? 16 : 0;
            if (step == 0) return;
            _dragScroller.ChangeView(null, Math.Clamp(_dragScroller.VerticalOffset + step, 0, _dragScroller.ScrollableHeight), null, true);
            UpdateDragPosition();
        };
        CardsList.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape && _dragEditor is not null) { EndDrag(false); e.Handled = true; } };
        _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_previewRequested) return;
            double start = _previewStart;
            if (_player.PlaybackSession.NaturalDuration > TimeSpan.Zero && start >= _player.PlaybackSession.NaturalDuration.TotalSeconds) start = 0;
            _player.PlaybackSession.Position = TimeSpan.FromSeconds(start); _player.Play(); _previewTimer.Start();
        });
        _player.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            string message = "試聴できません: " + e.ErrorMessage;
            StopPreview();
            if (_pickerPreviewText is not null) _pickerPreviewText.Text = message; else Message(message, true);
        });
        _previewTimer.Tick += (_, _) => StopPreview();
        Loaded += async (_, _) => { if (!_started) { _started = true; await Load(); } };
        Unloaded += (_, _) => { _disposed = true; EndDrag(false); _cancellation.Cancel(); StopPreview(); _player.Dispose(); _cancellation.Dispose(); };
        UpdateControls();
    }
    private void Message(string text, bool error = false)
    {
        Status.ActionButton = null;
        Status.Message = text; Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Status.IsOpen = true;
    }
    private async Task Load()
    {
        var settingsTask = _store.LoadAsync(Installation);
        var songsTask = SongLibraryService.ScanAsync(Installation.SongsDirectory, _cancellation.Token);
        try
        {
            DemoSongSettings? settings = null;
            try { settings = await settingsTask; }
            catch (Exception ex)
            {
                if (_disposed) return;
                _settingsFailed = true;
                Message("保存済みの設定を読み込めませんでした。標準設定に戻すと、保存し直せます。\n" + ex.Message, true);
                var recover = new Button { Content = "標準設定に戻す", IsEnabled = false };
                recover.Click += (_, _) =>
                {
                    _settingsFailed = false; _canSave = true; _entries.Clear(); RebuildCards(); StandardToggle.IsOn = true;
                    MarkChanged(); Message("標準の紹介に戻しました。「変更を保存」で確定してください。");
                };
                Status.ActionButton = recover;
            }
            IReadOnlyList<LibrarySong> songs = [];
            try { songs = await songsTask; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { if (!_disposed) Message("楽曲一覧を読み込めませんでした。" + ex.Message, true); }
            if (_disposed) return;
            _catalog = songs.Select(song => new SelectedDemoSong(Path.GetRelativePath(Installation.SongsDirectory, song.ChartPath).Replace('\\', '/'), song)).ToArray();
            foreach (var song in _catalog) _byPath.TryAdd(song.RelativePath, song);
            if (settings is not null) { StandardToggle.IsOn = !settings.Enabled; _entries.AddRange(settings.Entries); _canSave = true; }
            RebuildCards();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_disposed) Message("楽曲一覧を読み込めませんでした。" + ex.Message, true); }
        finally { if (!_disposed) { _loading = false; _dirty = false; UpdateControls(); } }
    }
    private void MarkChanged()
    {
        if (!_loading) { _dirty = true; if (Status.Severity != InfoBarSeverity.Error) Status.IsOpen = false; }
        UpdateControls();
    }
    private SelectedDemoSong? Find(string path) => _byPath.GetValueOrDefault(path.Replace('\\', '/'));
    private void SetSongButton(Button button, string path, string role)
    {
        var song = Find(path);
        string title = path.Length == 0 ? "曲を選ぶ" : song?.Label ?? "見つからない曲";
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (path.Length > 0)
        {
            if (song is not null) content.Children.Add(new TextBlock { Text = song.Description, Style = (Style)Application.Current.Resources["PageSubtitleStyle"], TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            content.Children.Add(new TextBlock { Text = path, Style = (Style)Application.Current.Resources["PageSubtitleStyle"], FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        }
        var layout = new Grid { ColumnSpacing = 12 }; layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.Children.Add(content);
        var edit = new FontIcon { Glyph = char.ConvertFromUtf32((int)Symbol.Edit), Style = (Style)Resources["SongPickerIconStyle"], VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; Grid.SetColumn(edit, 1); layout.Children.Add(edit);
        button.Content = layout; AutomationProperties.SetName(button, role + ": " + title);
        ToolTipService.SetToolTip(button, path.Length == 0 ? role + "を選ぶ" : path);
    }
    private void UpdateControls()
    {
        bool ready = !_loading && !_saving;
        if (Status.ActionButton is Control recovery) recovery.IsEnabled = ready;
        StandardToggle.IsEnabled = ready && !_settingsFailed;
        ModeHelp.Text = UseCustom ? "各枠のメイン曲を選び、サブ曲は枠ごとにおまかせ／手動を切り替えられます。" : "標準の楽曲紹介を使います。設定した枠は残したまま切り替えられます。";
        ModeHelp.Visibility = UseCustom ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility = _loading && UseCustom ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = UseCustom ? Visibility.Visible : Visibility.Collapsed;
        object? cardsSource = UseCustom ? _cards : null;
        if (!ReferenceEquals(CardsList.ItemsSource, cardsSource)) CardsList.ItemsSource = cardsSource;
        AddEntryButton.IsEnabled = ready && _entries.Count < DemoSongSettingsStore.MaximumSongs; BackButton.IsEnabled = !_saving;
        EntryCount.Text = "紹介する枠  " + _entries.Count + " / 13"; EmptyHelp.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int index = 0; index < _editors.Count; index++)
        {
            var editor = _editors[index]; var entry = editor.Entry; string number = (index + 1) + "曲目";
            editor.Heading.Text = number; editor.Main.IsEnabled = editor.Auto.IsEnabled = ready;
            editor.Remove.IsEnabled = ready;
            AutomationProperties.SetName(editor.Remove, number + "を削除");
            ToolTipService.SetToolTip(editor.Remove, number + "を削除");
            string label = number + " メイン曲: " + (Find(entry.MainSong)?.Label ?? "曲を選ぶ");
            AutomationProperties.SetName(editor.Card, label);
            if (CardsList.ContainerFromItem(editor.Card) is ListViewItem container) AutomationProperties.SetName(container, label);
            AutomationProperties.SetName(editor.Auto, number + " サブ曲は自動でおまかせ");
            SetSongButton(editor.Main, entry.MainSong, number + " メイン曲");
            editor.Manual.Visibility = entry.AutoSubSongs ? Visibility.Collapsed : Visibility.Visible;
            editor.Help.Text = entry.AutoSubSongs ? "Songsからメイン以外の4曲を自動で選びます。" : "画面下の左上・右上・左下・右下に、この4曲を表示します。";
            for (int sub = 0; sub < 4; sub++) { editor.Subs[sub].IsEnabled = ready; SetSongButton(editor.Subs[sub], sub < entry.SubSongs.Count ? entry.SubSongs[sub] : "", number + " サブ" + (sub + 1) + "（" + SubPositions[sub] + "）"); }
        }
        string? problem = DemoSongSettingsStore.SelectionProblem(_entries);
        SaveButton.IsEnabled = ready && _canSave && _dirty && (!UseCustom || problem is null);
        SaveState.Text = _saving ? "保存しています…" : _loading ? "設定と楽曲を読み込んでいます…" : _settingsFailed ? "保存済み設定の確認が必要です" : UseCustom && problem is not null ? problem : _dirty ? "変更はまだ保存されていません" : "保存済みの設定です";
    }
    private AppBarButton Command(string label, Symbol icon, RoutedEventHandler action)
    {
        var button = new AppBarButton { Label = label, Icon = new SymbolIcon(icon), Style = (Style)Application.Current.Resources["VisibleAppBarButtonStyle"] }; button.Click += action; return button;
    }
    private Button SongButton(bool main = false) => new() { Style = (Style)Resources[main ? "MainSongButtonStyle" : "SubSongButtonStyle"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 48, Padding = new Thickness(12, 8, 12, 8) };
    private StackPanel RoleLabel(string text, Symbol symbol, bool main = false)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(new FontIcon { Glyph = char.ConvertFromUtf32((int)symbol), Style = (Style)Resources[main ? "MainRoleIconStyle" : "SubRoleIconStyle"], VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        label.Children.Add(new TextBlock { Text = text, Style = (Style)Resources[main ? "MainFieldLabelStyle" : "SubFieldLabelStyle"], VerticalAlignment = VerticalAlignment.Center });
        return label;
    }
    private Grid SubLabel(int index)
    {
        var position = new Grid { Width = 20, Height = 20, RowSpacing = 3, ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        for (int axis = 0; axis < 2; axis++) { position.ColumnDefinitions.Add(new()); position.RowDefinitions.Add(new()); }
        for (int cell = 0; cell < 4; cell++) { var square = new Border { Style = (Style)Resources[cell == index ? "SubPositionActiveCellStyle" : "SubPositionCellStyle"] }; Grid.SetRow(square, cell / 2); Grid.SetColumn(square, cell % 2); position.Children.Add(square); }
        var label = new Grid { ColumnSpacing = 8 }; label.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); label.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); label.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        label.Children.Add(position);
        var number = new TextBlock { Text = "サブ" + (index + 1), Style = (Style)Resources["SubFieldLabelStyle"], VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(number, 1); label.Children.Add(number);
        var corner = new TextBlock { Text = SubPositions[index], Style = (Style)Resources["SubPositionLabelStyle"], VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(corner, 2); label.Children.Add(corner);
        return label;
    }
    private void RebuildCards()
    {
        _cards.Clear(); _editors.Clear();
        foreach (var entry in _entries)
        {
            var editor = new EntryEditor(entry); editor.Heading.Style = (Style)Application.Current.Resources["SectionTitleStyle"];
            editor.Main = SongButton(true); editor.Main.Click += async (_, _) => await Run(() => Pick(editor, null));
            editor.Auto.Header = RoleLabel("サブ曲は自動でおまかせ", Symbol.Shuffle); editor.Auto.OnContent = "オン"; editor.Auto.OffContent = "オフ"; editor.Auto.IsOn = entry.AutoSubSongs;
            editor.Auto.Toggled += (_, _) => { entry.AutoSubSongs = editor.Auto.IsOn; StopPreview(); MarkChanged(); };
            editor.Help.Style = (Style)Application.Current.Resources["PageSubtitleStyle"];
            editor.Manual.RowSpacing = editor.Manual.ColumnSpacing = 12;
            for (int axis = 0; axis < 2; axis++) { editor.Manual.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); editor.Manual.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            for (int sub = 0; sub < 4; sub++)
            {
                int subIndex = sub; editor.Subs[sub] = SongButton(); editor.Subs[sub].Click += async (_, _) => await Run(() => Pick(editor, subIndex));
                var row = new StackPanel { Spacing = 8 }; row.Children.Add(SubLabel(sub)); row.Children.Add(editor.Subs[sub]);
                Grid.SetRow(row, sub / 2); Grid.SetColumn(row, sub % 2); editor.Manual.Children.Add(row);
            }
            editor.Remove = new Button { Style = (Style)Resources["DeleteEntryButtonStyle"], Content = new FontIcon { Glyph = "\uE74D", Style = (Style)Resources["DeleteEntryIconStyle"] } };
            editor.Remove.Click += (_, _) => { StopPreview(); _entries.Remove(entry); RebuildCards(); MarkChanged(); };
            var header = new Grid { ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.Children.Add(editor.Heading); Grid.SetColumn(editor.Remove, 1); header.Children.Add(editor.Remove);
            var grip = new FontIcon { Glyph = "\uE76F", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(grip, "ドラッグして紹介順を変更");
            editor.Heading.VerticalAlignment = VerticalAlignment.Center;
            editor.Handle.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            editor.Handle.Width = 44; editor.Handle.VerticalAlignment = VerticalAlignment.Stretch; editor.Handle.ManipulationMode = ManipulationModes.None;
            editor.Handle.Children.Add(grip);
            ToolTipService.SetToolTip(editor.Handle, "ドラッグして紹介順を変更");
            editor.Handle.PointerPressed += (_, e) => BeginDrag(editor, e);
            editor.Handle.PointerMoved += (_, e) => ContinueDrag(e);
            editor.Handle.PointerReleased += (_, e) => ReleaseDrag(e);
            editor.Handle.PointerCanceled += (_, e) => { if (_dragEditor == editor && e.Pointer.PointerId == _dragPointer) EndDrag(false); };
            editor.Handle.PointerCaptureLost += (_, e) => { if (_dragEditor == editor && e.Pointer.PointerId == _dragPointer) EndDrag(false); };
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(header); body.Children.Add(RoleLabel("メイン曲", Symbol.Favorite, true)); body.Children.Add(editor.Main); body.Children.Add(editor.Auto); body.Children.Add(editor.Help); body.Children.Add(editor.Manual);
            editor.DropIndicator.Style = (Style)Resources["DropIndicatorStyle"];
            editor.DropIndicator.Margin = new Thickness(-8, -16, -16, -16);
            var layout = new Grid { ColumnSpacing = 12 }; layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            layout.Children.Add(editor.Handle); Grid.SetColumn(body, 1); layout.Children.Add(body);
            var content = new Grid(); content.Children.Add(layout); content.Children.Add(editor.DropIndicator);
            editor.Card = new Border { Style = (Style)Resources["ShowcaseCardStyle"], Padding = new Thickness(8, 16, 16, 16), Child = content }; _cards.Add(editor.Card); _editors.Add(editor);
        }
        UpdateControls();
    }
    private void BeginDrag(EntryEditor editor, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(editor.Handle);
        if (_loading || _saving || _dragEditor is not null || !e.Pointer.IsInContact
            || point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed) return;
        CardsList.Focus(FocusState.Programmatic);
        if (!editor.Handle.CapturePointer(e.Pointer)) return;
        StopPreview();
        _dragEditor = editor; _dragPointer = e.Pointer.PointerId;
        _dragStart = _dragPoint = e.GetCurrentPoint(CardsList).Position;
        _dropIndex = _editors.IndexOf(editor); _dragScroller = FindScroller(CardsList);
        _dragScrollStart = _dragScroller?.VerticalOffset ?? 0;
        e.Handled = true;
    }
    private void ContinueDrag(PointerRoutedEventArgs e)
    {
        if (_dragEditor is null || e.Pointer.PointerId != _dragPointer) return;
        _dragPoint = e.GetCurrentPoint(CardsList).Position;
        if (!_dragActive && Math.Abs(_dragPoint.Y - _dragStart.Y) >= 6)
        {
            _dragActive = true; _dragScrollTimer.Start(); _dragEditor.Card.Opacity = 0.7;
            if (CardsList.ContainerFromItem(_dragEditor.Card) is UIElement container) Canvas.SetZIndex(container, 10);
        }
        if (_dragActive) UpdateDragPosition();
        e.Handled = true;
    }
    private bool InsideList(Point point) => point.X >= 0 && point.X <= CardsList.ActualWidth && point.Y >= 0 && point.Y <= CardsList.ActualHeight;
    private void UpdateDragPosition()
    {
        if (_dragEditor is null) return;
        _dragEditor.Card.RenderTransform = new TranslateTransform { Y = _dragPoint.Y - _dragStart.Y + (_dragScroller?.VerticalOffset ?? 0) - _dragScrollStart };
        if (_dropEditor is not null) _dropEditor.DropIndicator.Visibility = Visibility.Collapsed;
        _dropEditor = null;
        if (!InsideList(_dragPoint)) { SaveState.Text = "枠の一覧の外で離すとキャンセルします"; return; }
        int source = _editors.IndexOf(_dragEditor), insertion = _editors.Count;
        EntryEditor? previous = null;
        foreach (var editor in _editors)
        {
            if (editor == _dragEditor) continue;
            var bounds = editor.Card.TransformToVisual(CardsList).TransformBounds(new Rect(0, 0, editor.Card.ActualWidth, editor.Card.ActualHeight));
            if (_dragPoint.Y < bounds.Y + bounds.Height / 2)
            {
                insertion = _editors.IndexOf(editor); _dropEditor = editor;
                editor.DropIndicator.VerticalAlignment = VerticalAlignment.Top; break;
            }
            previous = editor;
        }
        _dropIndex = Math.Clamp(insertion > source ? insertion - 1 : insertion, 0, _editors.Count - 1);
        if (_dropIndex == source) { _dropEditor = null; SaveState.Text = "元の位置で離すと順番を保ちます"; return; }
        if (_dropEditor is null && previous is not null) { _dropEditor = previous; previous.DropIndicator.VerticalAlignment = VerticalAlignment.Bottom; }
        if (_dropEditor is not null) _dropEditor.DropIndicator.Visibility = Visibility.Visible;
        SaveState.Text = "離すと" + (_dropIndex + 1) + "曲目に移動します";
    }
    private void ReleaseDrag(PointerRoutedEventArgs e)
    {
        if (_dragEditor is null || e.Pointer.PointerId != _dragPointer) return;
        _dragPoint = e.GetCurrentPoint(CardsList).Position;
        if (_dragActive) UpdateDragPosition();
        EndDrag(_dragActive && InsideList(_dragPoint)); e.Handled = true;
    }
    private void EndDrag(bool commit)
    {
        _dragScrollTimer.Stop();
        var editor = _dragEditor; if (editor is null) return;
        int source = _editors.IndexOf(editor), target = _dropIndex;
        _dragEditor = null; _dragActive = false;
        if (_dropEditor is not null) _dropEditor.DropIndicator.Visibility = Visibility.Collapsed;
        _dropEditor = null; editor.Card.Opacity = 1; editor.Card.ClearValue(UIElement.RenderTransformProperty);
        if (CardsList.ContainerFromItem(editor.Card) is UIElement container) Canvas.SetZIndex(container, 0);
        editor.Handle.ReleasePointerCaptures();
        if (commit && source >= 0 && target != source)
        {
            _entries.RemoveAt(source); _entries.Insert(target, editor.Entry);
            _editors.RemoveAt(source); _editors.Insert(target, editor); _cards.Move(source, target);
            MarkChanged();
        }
        else if (!_disposed) UpdateControls();
    }
    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        if (root is ScrollViewer scroller) return scroller;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindScroller(VisualTreeHelper.GetChild(root, index)) is ScrollViewer child) return child;
        return null;
    }
    private void Reveal(EntryEditor editor)
    {
        // 再生成したカードのレイアウト完了後に移動し、追加先が画面外に残るのを防ぐ。
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            editor.Card.Loaded -= loaded;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_disposed || !_editors.Contains(editor)) return;
                editor.Card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                editor.Main.Focus(FocusState.Programmatic);
            });
        };
        editor.Card.Loaded += loaded;
        CardsList.ScrollIntoView(editor.Card, ScrollIntoViewAlignment.Leading);
    }
    private async Task Pick(EntryEditor editor, int? subIndex)
    {
        string role = (_entries.IndexOf(editor.Entry) + 1) + "曲目 " + (subIndex.HasValue ? "サブ" + (subIndex.Value + 1) + "（" + SubPositions[subIndex.Value] + "）" : "メイン曲");
        var search = new TextBox { PlaceholderText = "曲名・ジャンル・フォルダー名で検索" }; AutomationProperties.SetName(search, "紹介する曲を検索");
        var count = new TextBlock { Style = (Style)Application.Current.Resources["PageSubtitleStyle"] };
        var list = new ListView { Height = XamlRoot.Size.Height < 750 ? 180 : 260, SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = (DataTemplate)Resources["CandidateTemplate"] }; AutomationProperties.SetName(list, "Songsの曲一覧");
        var hint = new TextBlock { Text = "一覧から曲を選んでください。", TextWrapping = TextWrapping.Wrap }; var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var preview = Command("試聴", Symbol.Play, async (_, _) => { try { await Preview(list.SelectedItem as SelectedDemoSong); } catch (Exception ex) { previewText.Text = ex.Message; } });
        var stop = Command("試聴を停止", Symbol.Stop, (_, _) => StopPreview());
        var commands = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) }; commands.PrimaryCommands.Add(preview); commands.PrimaryCommands.Add(stop);
        var body = new StackPanel { Spacing = 8 }; body.Children.Add(search); body.Children.Add(count); body.Children.Add(list); body.Children.Add(hint); body.Children.Add(commands); body.Children.Add(previewText);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = role + "を選ぶ", Content = body, PrimaryButtonText = "この曲を選ぶ", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        void Filter()
        {
            string query = search.Text.Trim(); var matches = _catalog.Where(row => query.Length == 0 || (row.Label + " " + row.Description + " " + row.RelativePath).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray(); list.ItemsSource = matches; count.Text = matches.Length + "曲 / 全" + _catalog.Count + "曲";
        }
        search.TextChanged += (_, _) => { StopPreview(); Filter(); };
        list.SelectionChanged += (_, _) => { StopPreview(); var selected = list.SelectedItem as SelectedDemoSong; dialog.IsPrimaryButtonEnabled = selected?.CanUse == true; preview.IsEnabled = selected?.Song?.AudioPath is not null; hint.Text = selected is null ? "一致する曲がない場合は検索する言葉を変えてください。" : selected.Problem.Length > 0 ? selected.Problem : selected.Label; };
        preview.IsEnabled = false; Filter(); _pickerPreviewText = previewText;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is SelectedDemoSong { CanUse: true } selected)
            {
                if (subIndex.HasValue) { while (editor.Entry.SubSongs.Count < 4) editor.Entry.SubSongs.Add(""); editor.Entry.SubSongs[subIndex.Value] = selected.RelativePath; } else editor.Entry.MainSong = selected.RelativePath;
                MarkChanged();
            }
        }
        finally { StopPreview(); _pickerPreviewText = null; }
    }
    private async Task Run(Func<Task> action) { try { await action(); } catch (Exception ex) { if (!_disposed) Message(ex.Message, true); } finally { if (!_disposed) UpdateControls(); } }
    private Task Preview(SelectedDemoSong? row)
    {
        if (GameProcessService.IsRunning()) throw new InvalidOperationException("ゲームを終了してから試聴してください。");
        if (row?.Song?.AudioPath is not string audio || !File.Exists(audio)) throw new InvalidOperationException("音源のある曲を選択してください。");
        StopPreview(); _previewStart = row.Song.DemoStart; _previewRequested = true; _player.Source = MediaSource.CreateFromUri(new Uri(audio));
        PreviewState.Text = "試聴中: " + row.Label + "（約5秒）"; if (_pickerPreviewText is not null) _pickerPreviewText.Text = PreviewState.Text; PreviewPanel.Visibility = Visibility.Visible; return Task.CompletedTask;
    }
    private void StopPreview() { _previewRequested = false; _previewTimer.Stop(); _player.Pause(); PreviewPanel.Visibility = Visibility.Collapsed; if (_pickerPreviewText is not null) _pickerPreviewText.Text = ""; }
    private async Task Save()
    {
        if (!_canSave) throw new InvalidOperationException("設定の読み込み状態を確認してください。");
        _saving = true; StopPreview(); UpdateControls();
        try
        {
            await _store.SaveAsync(Installation, new() { Enabled = UseCustom, Entries = _entries.ToList() }); _dirty = false;
            bool titleShown = (await new GameSettingsStore().LoadAsync(Installation)).TitleShow;
            Message(UseCustom && !titleShown ? "保存しました。「起動構成」で「タイトル画面を表示」をオンにすると紹介を再生します。" : UseCustom ? "紹介枠を保存しました。次のゲーム起動から、メイン曲と各枠のサブ曲を反映します。" : "標準の紹介を使う設定を保存しました。");
        }
        finally { _saving = false; }
    }
    private void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_entries.Count >= DemoSongSettingsStore.MaximumSongs) return;
        _entries.Add(new()); RebuildCards(); MarkChanged(); Reveal(_editors[^1]);
    }
    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty) { var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "紹介曲の変更を保存していません", Content = "変更を破棄して楽曲画面へ戻りますか？", PrimaryButtonText = "破棄して戻る", CloseButtonText = "このページに戻る", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; }
        Frame.Navigate(typeof(SongsPage));
    }
    private void Standard_Toggled(object sender, RoutedEventArgs e) { if (_started && !_loading) { StopPreview(); MarkChanged(); } }
    private void StopPreview_Click(object sender, RoutedEventArgs e) => StopPreview();
    private async void Save_Click(object sender, RoutedEventArgs e) => await Run(Save);
    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SaveButton is null) return;
        bool compact = e.NewSize.Width < 700;
        PageRoot.Padding = (Thickness)Application.Current.Resources[compact ? "PageNarrowPadding" : "PageWidePadding"];
        IntroductionLayout.RowSpacing = compact ? 16 : 0;
        Grid.SetRow(IntroductionText, compact ? 1 : 0); Grid.SetColumn(IntroductionText, compact ? 0 : 1); Grid.SetColumnSpan(IntroductionText, compact ? 2 : 1);
        Grid.SetRow(SaveButton, compact ? 2 : 1); Grid.SetColumn(SaveButton, compact ? 0 : 1); Grid.SetColumnSpan(SaveButton, compact ? 2 : 1); SaveButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }
}
