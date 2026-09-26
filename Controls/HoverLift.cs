using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace TaikoDiveLauncher.Controls;

/// <summary>
/// Lifts an element with a theme shadow while the pointer is over it.
/// Respects the Windows "animation effects" setting.
/// </summary>
public static class HoverLift
{
    private static readonly Vector3 LiftedTranslation = new(0, -3, 16);
    private static readonly UISettings UiSettings = new();
    private static readonly PointerEventHandler EnteredHandler = OnPointerEntered;
    private static readonly PointerEventHandler ExitedHandler = OnPointerExited;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(HoverLift),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(UIElement element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(UIElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.RemoveHandler(UIElement.PointerEnteredEvent, EnteredHandler);
        element.RemoveHandler(UIElement.PointerExitedEvent, ExitedHandler);
        element.RemoveHandler(UIElement.PointerCanceledEvent, ExitedHandler);
        element.Translation = Vector3.Zero;

        if (e.NewValue is not true)
        {
            return;
        }

        element.TranslationTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) };
        element.Shadow ??= new ThemeShadow();
        element.AddHandler(UIElement.PointerEnteredEvent, EnteredHandler, true);
        element.AddHandler(UIElement.PointerExitedEvent, ExitedHandler, true);
        element.AddHandler(UIElement.PointerCanceledEvent, ExitedHandler, true);
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element && UiSettings.AnimationsEnabled)
        {
            element.Translation = LiftedTranslation;
        }
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.Translation = Vector3.Zero;
        }
    }
}
