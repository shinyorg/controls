namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Where a view actually is, in the coordinate space of an ancestor layout.
/// </summary>
/// <remarks>
/// MAUI gives a view its <see cref="VisualElement.Bounds"/> relative to its immediate parent and
/// nothing else, so anything that has to draw <em>at</em> another view — a tooltip tail, a walkthrough
/// spotlight — has to add the offsets up itself. The one non-obvious step is a
/// <see cref="ScrollView"/>: its content is laid out in content space, so the scroll offset has to
/// come back off again or the highlight sits where the target was before the user scrolled.
/// </remarks>
static class ViewGeometry
{
    /// <summary>
    /// The target's rectangle in <paramref name="root"/>'s coordinate space, or null when the target
    /// is not underneath that root, is hidden, or has not been laid out yet.
    /// </summary>
    public static Rect? BoundsIn(VisualElement target, Element root)
    {
        if (!IsEffectivelyVisible(target))
            return null;

        if (target.Width <= 0 || target.Height <= 0)
            return null;

        double x = 0;
        double y = 0;
        var inItemsView = false;
        Element? current = target;

        while (current is not null && !ReferenceEquals(current, root))
        {
            if (current is VisualElement ve)
            {
                x += ve.X;
                y += ve.Y;

                // Stepping out of a scroll view: its child was positioned in content space, and the
                // scroll offset is what maps that back onto the screen.
                if (current is ScrollView scroll)
                {
                    x -= scroll.ScrollX;
                    y -= scroll.ScrollY;
                }
            }

            // An item template of a CollectionView / CarouselView sits at 0,0 in its own native cell,
            // so the sum above says nothing about where the item is in the list.
            if (current is ItemsView)
                inItemsView = true;

            current = current.Parent;
        }

        // Never reached the root — the target lives on some other page, or in a layer of its own.
        if (current is null)
            return null;

        if (inItemsView && NativeOffset(target, root) is { } native)
            return new Rect(native.X, native.Y, target.Width, target.Height);

        return new Rect(x, y, target.Width, target.Height);
    }


    /// <summary>The target's top-left in <paramref name="root"/>, measured on the native views.</summary>
    static Point? NativeOffset(VisualElement target, Element root)
    {
#if WINDOWS
        if (target.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement { XamlRoot: not null } view &&
            root.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement host)
        {
            var p = view.TransformToVisual(host).TransformPoint(new Windows.Foundation.Point(0, 0));
            return new Point(p.X, p.Y);
        }
#elif ANDROID
        if (target.Handler?.PlatformView is Android.Views.View { IsAttachedToWindow: true } view &&
            root.Handler?.PlatformView is Android.Views.View host)
        {
            var a = new int[2];
            var b = new int[2];
            view.GetLocationInWindow(a);
            host.GetLocationInWindow(b);
            var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            return new Point((a[0] - b[0]) / density, (a[1] - b[1]) / density);
        }
#elif IOS || MACCATALYST
        if (target.Handler?.PlatformView is UIKit.UIView { Window: not null } view &&
            root.Handler?.PlatformView is UIKit.UIView host)
        {
            var p = view.ConvertPointToView(CoreGraphics.CGPoint.Empty, host);
            return new Point(p.X, p.Y);
        }
#endif
        return null;
    }


    /// <summary>Visible in its own right and not inside something hidden.</summary>
    public static bool IsEffectivelyVisible(VisualElement target)
    {
        Element? current = target;
        while (current is not null)
        {
            if (current is VisualElement ve && !ve.IsVisible)
                return false;

            current = current.Parent;
        }
        return true;
    }


    /// <summary>The nearest scrollable ancestor, which is what a "scroll the target into view" has to drive.</summary>
    public static ScrollView? EnclosingScrollView(Element target)
    {
        var current = target.Parent;
        while (current is not null)
        {
            if (current is ScrollView scroll)
                return scroll;

            current = current.Parent;
        }
        return null;
    }


    /// <summary>Grows a rect by a uniform padding, clamped so it never inverts.</summary>
    public static Rect Inflate(Rect rect, double padding)
    {
        if (padding == 0)
            return rect;

        var result = new Rect(
            rect.X - padding,
            rect.Y - padding,
            rect.Width + (padding * 2),
            rect.Height + (padding * 2)
        );

        if (result.Width < 0)
            result = new Rect(rect.Center.X, result.Y, 0, result.Height);

        if (result.Height < 0)
            result = new Rect(result.X, rect.Center.Y, result.Width, 0);

        return result;
    }


    /// <summary>Straight-line interpolation between two rects, for animating a highlight between targets.</summary>
    public static Rect Lerp(Rect from, Rect to, double t) => new(
        from.X + ((to.X - from.X) * t),
        from.Y + ((to.Y - from.Y) * t),
        from.Width + ((to.Width - from.Width) * t),
        from.Height + ((to.Height - from.Height) * t)
    );
}
