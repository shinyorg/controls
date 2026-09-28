using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

public partial class Ribbon
{
    /// <summary>
    /// Where a ribbon's dropdowns are drawn.
    /// </summary>
    /// <remarks>
    /// Its own marker type, like every other overlay layer: the layer is looked up by type, so asking
    /// for a bare <c>Grid</c> would match whichever control's layer happened to be added first and the
    /// two would clear each other's children. A <c>Grid</c> rather than an <c>AbsoluteLayout</c> for the
    /// reason quick entry uses one — an auto-sized, proportionally-positioned absolute child comes back
    /// unmeasured on some heads and paints nothing.
    /// </remarks>
    internal sealed class RibbonMenuLayer : Grid, PageOverlay.IOverlayLayer;


    BoxView? menuBackdrop;
    Border? menuCard;
    RibbonMenuLayer? menuLayer;
    AndroidBackButton? menuBack;


    /// <summary>Whether a dropdown or a collapsed group's popup is open.</summary>
    public bool IsMenuOpen => this.menuCard is not null;


    /// <summary>Closes any open dropdown. Safe to call when nothing is open.</summary>
    public void CloseMenu()
    {
        var card = this.menuCard;
        var backdrop = this.menuBackdrop;

        this.menuCard = null;
        this.menuBackdrop = null;
        this.menuBack?.SetActive(false);

        if (card is not null)
        {
            // Unparent what was lent to the card first: the next open would otherwise find a view that
            // still has a parent, and MAUI throws.
            card.Content = null;
            this.menuLayer?.Children.Remove(card);
        }

        if (backdrop is not null)
            this.menuLayer?.Children.Remove(backdrop);
    }


    internal void OpenMenu(RibbonMenuButton button, View? anchor, Action onPicked)
    {
        var entries = button.VisibleMenu;
        if (entries.Count == 0)
            return;

        this.Present(anchor, null, this.BuildMenuBody(entries, onPicked));
    }


    internal void OpenGroupPopup(RibbonTab tab, RibbonGroup group, View anchor)
    {
        // A fresh expanded view of the same group. The collapsed button and the open box cannot be the
        // same view - one of them is in the bar and the other is over it.
        var body = new RibbonGroupView(this, tab, group, simplified: false)
        {
            Margin = new Thickness(4)
        };

        this.Present(anchor, null, body);

        // Hosted content (Word's find bar) is lent from the bar, where it was last measured hidden: on
        // iOS the popup sized itself from that stale measure and clipped the rest of the group away
        // (Find showed, Replace and Select did not). Re-measure once it is up, then re-place the card.
        if (this.menuCard is { } card && this.Dispatcher is { } dispatcher)
        {
            dispatcher.Dispatch(() =>
            {
                if (!ReferenceEquals(this.menuCard, card))
                    return;

                RibbonGroupView.InvalidateTree(card);

                dispatcher.Dispatch(() =>
                {
                    if (ReferenceEquals(this.menuCard, card) && PageOverlay.GetOrCreateRoot(this) is { } root)
                        this.PlaceCard(card, ViewGeometry.BoundsIn(anchor, root), root);
                });
            });
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Presentation
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Drops a card under an anchor, in the page's overlay layer rather than inside the ribbon.
    /// </summary>
    /// <remarks>
    /// A dropdown drawn inside the bar would be clipped by it: the body is only as tall as three rows
    /// of small buttons, and a menu is taller than that. The page overlay is the one place in the tree
    /// that is guaranteed to be above the page's own content and not clipped by anything in it.
    /// </remarks>
    internal void Present(View? anchor, Rect? anchorRect, View content)
    {
        this.CloseMenu();

        var layer = PageOverlay.GetOrCreateLayer<RibbonMenuLayer>(this, PageOverlay.Layers.RibbonMenu);
        this.menuLayer = layer;
        if (layer is null)
            return;

        var root = PageOverlay.GetOrCreateRoot(this);
        if (root is null)
            return;

        var card = new Border
        {
            Content = content,
            Padding = new Thickness(4),
            StrokeThickness = 1,
            Stroke = this.OutlineBrush,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius)
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        card.WithElevation(ShinyThemeKeys.Elevation.Level2);

        // Not a scrim: a dropdown that dims the document behind it reads as a modal, which it is not.
        // It still has to be painted rather than fully transparent, or some heads never hit-test it.
        var backdrop = new BoxView { Color = Color.FromRgba(0, 0, 0, 0.01) };
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(this.CloseMenu) });

        layer.Children.Add(backdrop);
        layer.Children.Add(card);

        this.menuBackdrop = backdrop;
        this.menuCard = card;

        // Android's back button puts the dropdown away rather than leaving the page under it.
        if (this.menuBack is null)
        {
            this.menuBack = new AndroidBackButton(this.CloseMenu);

            // Leaving the page with a dropdown open must not leave it holding the back button.
            this.Unloaded += (_, _) => this.CloseMenu();
        }

        this.menuBack.SetActive(true);

        this.PlaceCard(card, anchorRect ?? (anchor is null ? null : ViewGeometry.BoundsIn(anchor, root)), root);
    }


    /// <summary>
    /// Puts the card directly under its anchor, nudged back on screen if it would hang off the edge.
    /// </summary>
    void PlaceCard(Border card, Rect? bounds, PageOverlay.ShinyOverlayRoot root)
    {
        if (bounds is not { } rect)
        {
            // Not laid out yet - centre it rather than pinning it to a corner it does not belong in.
            card.HorizontalOptions = LayoutOptions.Center;
            card.VerticalOptions = LayoutOptions.Start;
            card.Margin = new Thickness(0, 8, 0, 0);
            return;
        }

        // Against the room there is: unconstrained, content that fills (a find box) reports a width
        // wider than the page, which pinned the card to the left edge far from its button.
        // A measure includes the margin, which on a re-place is the previous position.
        var margin = card.Margin.HorizontalThickness;
        var room = root.Width > 16 ? root.Width - 16 + margin : double.PositiveInfinity;
        var size = ((IView)card).Measure(room, double.PositiveInfinity);
        var width = double.IsFinite(size.Width) && size.Width - margin > 0 ? size.Width - margin : 220;

        var x = rect.X;
        if (root.Width > 0 && x + width > root.Width - 8)
            x = Math.Max(8, root.Width - width - 8);

        card.Margin = new Thickness(x, rect.Bottom + 2, 0, 0);
    }


    // ---------------------------------------------------------------------------------------------
    // Menu body
    // ---------------------------------------------------------------------------------------------

    View BuildMenuBody(IReadOnlyList<RibbonMenuEntry> entries, Action onPicked)
    {
        var stack = new VerticalStackLayout { Spacing = 0, MinimumWidthRequest = 180 };

        foreach (var entry in entries)
            stack.Children.Add(this.BuildMenuLine(entry, onPicked));

        return stack;
    }


    View BuildMenuLine(RibbonMenuEntry entry, Action onPicked)
    {
        if (entry.IsSeparator)
        {
            var rule = new BoxView { HeightRequest = 1, Margin = new Thickness(8, 4) };
            rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            return rule;
        }

        var row = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };

        // A fixed tick column, drawn or not, so the labels line up whether or not this menu is a
        // set of choices. A tick that shifts the text as it appears reads as the menu twitching.
        var tick = new Label
        {
            Text = entry.IsChecked ? "✓" : " ",
            WidthRequest = 14,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);
        tick.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.Primary);
        row.Children.Add(tick);

        if (entry.Icon is { } icon)
        {
            row.Children.Add(new Image
            {
                Source = icon,
                WidthRequest = 16,
                HeightRequest = 16,
                Aspect = Aspect.AspectFit,
                InputTransparent = true
            });
        }

        var label = new Label
        {
            Text = entry.Text,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        row.Children.Add(label);

        if (entry.HasChildren)
        {
            row.Children.Add(new Polyline
            {
                Points = new PointCollection { new(0, 0), new(4, 4), new(0, 8) },
                Stroke = this.ForegroundBrush,
                StrokeThickness = 1.4,
                StrokeLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                WidthRequest = 5,
                HeightRequest = 8,
                VerticalOptions = LayoutOptions.Center
            });
        }

        var border = new Border
        {
            Content = row,
            Padding = new Thickness(8, 6),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            Opacity = entry.IsEnabled ? 1d : 0.38d,
            InputTransparent = !entry.IsEnabled,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius)
        };

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
            border.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        pointer.PointerExited += (_, _) =>
        {
            border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            border.BackgroundColor = Colors.Transparent;
        };
        border.GestureRecognizers.Add(pointer);

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (entry.HasChildren)
                {
                    // Submenus replace the card rather than flying out beside it. A second floating
                    // panel needs its own dismissal, its own placement and its own edge clamping, and
                    // on a ribbon the menus are shallow enough that replacing reads fine.
                    //
                    // The line's rect has to be taken *before* presenting, because presenting closes
                    // this card first and an unparented view has no bounds to place the next one at.
                    var here = PageOverlay.GetOrCreateRoot(this) is { } overlayRoot
                        ? ViewGeometry.BoundsIn(border, overlayRoot)
                        : null;

                    this.Present(
                        null,
                        here,
                        this.BuildMenuBody(entry.Children.Where(x => x.IsVisible).ToList(), onPicked)
                    );
                    return;
                }

                entry.Invoke();
                this.CloseMenu();
                onPicked();
            })
        });

        SemanticProperties.SetDescription(border, entry.Text);
        return border;
    }
}
