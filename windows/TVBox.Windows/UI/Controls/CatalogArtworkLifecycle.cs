using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;

namespace TVBoxForWindows.UI.Controls;

/// <summary>Cached frames are hidden rather than unloaded; stop their artwork requests explicitly.</summary>
public static class CatalogArtworkLifecycle
{
    public static void Watch(Frame frame)
    {
        void Refresh() => SetActive(frame.Content as DependencyObject, frame.Visibility == Visibility.Visible);
        frame.Navigating += (_, _) => SetActive(frame.Content as DependencyObject, false);
        frame.Navigated += (_, _) => Refresh();
        frame.NavigationStopped += (_, _) => Refresh();
        frame.NavigationFailed += (_, _) => Refresh();
        frame.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Refresh());
    }
    public static bool IsActive(DependencyObject node)
    {
        for (var current = node; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement element && element.Visibility != Visibility.Visible) return false;
        return true;
    }
    public static void SetActive(DependencyObject root, bool active)
    {
        if (root == null) return;
        if (root is PosterImage poster) poster.SetActive(active);
        if (root is WorkTitleArt title) title.SetActive(active);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) SetActive(VisualTreeHelper.GetChild(root, i), active);
    }
}
