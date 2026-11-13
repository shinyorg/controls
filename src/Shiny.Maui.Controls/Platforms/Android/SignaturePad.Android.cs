using AndroidX.ViewPager2.Widget;
using AView = Android.Views.View;

namespace Shiny.Maui.Controls.SignaturePad;

public partial class SignaturePad
{
    ViewPager2? lockedPager;
    bool pagerWasInputEnabled;

    // The Android 10+ (API 29+) back edge-swipe is already kept off the surface by SignatureView's
    // own gesture exclusion; what is left is a hosting TabbedPage, whose ViewPager2 would take a
    // horizontal stroke as a swipe to the next/previous tab.
    partial void SetBackGestureEnabled(bool enabled)
    {
        if (signatureView?.Surface.Handler?.PlatformView is not AView native)
            return;

        if (!enabled)
        {
            lockedPager = FindViewPager(native);
            if (lockedPager is not null)
            {
                pagerWasInputEnabled = lockedPager.UserInputEnabled;
                lockedPager.UserInputEnabled = false;
            }
        }
        else if (lockedPager is not null)
        {
            lockedPager.UserInputEnabled = pagerWasInputEnabled;
            lockedPager = null;
        }
    }

    static ViewPager2? FindViewPager(AView view)
    {
        var parent = view.Parent;
        while (parent is not null)
        {
            if (parent is ViewPager2 pager)
                return pager;

            parent = (parent as AView)?.Parent;
        }
        return null;
    }
}
