using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace TaikoDiveLauncher.Pages;

// FrameはXAMLメタデータを持つページを生成する。追加画面は明示的に構築し、
// トリミング後も各コンストラクターを保持する。
public sealed partial class ToolkitHostPage : Page, Models.IUnsavedChangesAware
{
    public ToolkitHostPage() => InitializeComponent();
    public bool HasUnsavedChanges => Content is Models.IUnsavedChangesAware { HasUnsavedChanges: true };
    public string UnsavedChangesName => (Content as Models.IUnsavedChangesAware)?.UnsavedChangesName ?? "設定";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Content = e.Parameter as string switch
        {
            "library" => new LibraryPage(),
            "records" => new RecordsPage(),
            "setup" => new SetupPage(),
            _ => new ToolkitPage(),
        };
    }
}
