using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.Themes;
using TvTableView = Shiny.Maui.Controls.TableView;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;

namespace Shiny.Maui.Controls.Infrastructure;

static class SectionRenderer
{
    public static View Render(TvTableSection section, TvTableView parentTableView)
    {
        if (!section.IsVisible)
            return new ContentView { IsVisible = false };

        var sectionLayout = new VerticalStackLayout();

        // A section the TableView's ItemTemplate generated carries its item as BindingContext, but the
        // layout inherits the TableView's once it is added under it - and hands that down to every cell
        // and header/footer view in it, so a fixed cell in the template bound to the page's view model
        // instead of the item. Pinned, so the layout passes the item down instead.
        if (parentTableView.IsGeneratedSection(section))
            sectionLayout.BindingContext = section.BindingContext;

        // Header
        RenderHeader(sectionLayout, section, parentTableView);

        // Cells with separators
        var cells = section.GetVisibleCells();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            cell.ParentTableView = parentTableView;
            cell.ParentSection = section;
            cell.ApplyCascadedStyles();

            // a radio group's value can arrive before its cells exist - pick it up now
            (cell as Shiny.Maui.Controls.Cells.RadioCell)?.SyncFromGroup();

            if (section.UseDragSort)
            {
                sectionLayout.Children.Add(new DragSortRow(parentTableView, section, cell));
            }
            else
            {
                sectionLayout.Children.Add(cell);
            }

            // Separator between cells (not after last)
            if (i < cells.Count - 1)
            {
                sectionLayout.Children.Add(CreateSeparator(parentTableView));
            }
        }

        // Footer
        RenderFooter(sectionLayout, section, parentTableView);

        return sectionLayout;
    }

    static void RenderHeader(VerticalStackLayout layout, TvTableSection section, TvTableView tableView)
    {
        if (section.HeaderView != null)
        {
            layout.Children.Add(section.HeaderView);
            return;
        }

        if (string.IsNullOrEmpty(section.Title))
            return;

        var headerColor = section.HeaderBackgroundColor ?? tableView.HeaderBackgroundColor;
        var textColor = section.HeaderTextColor ?? tableView.HeaderTextColor;
        var fontSize = section.HeaderFontSize >= 0 ? section.HeaderFontSize
            : tableView.HeaderFontSize >= 0 ? tableView.HeaderFontSize
            : ThemeTokens.Unset;
        var fontFamily = section.HeaderFontFamily ?? tableView.HeaderFontFamily;
        var fontAttributes = section.HeaderFontAttributes ?? tableView.HeaderFontAttributes;

        var headerHeight = section.HeaderHeight >= 0 ? section.HeaderHeight
            : tableView.HeaderHeight >= 0 ? tableView.HeaderHeight
            : -1;

        var headerContainer = new ContentView { Padding = tableView.HeaderPadding };
        ApplyColor(headerContainer, VisualElement.BackgroundColorProperty, headerColor, ShinyThemeKeys.Color.SurfaceContainer);

        if (headerHeight >= 0)
            headerContainer.HeightRequest = headerHeight;

        var verticalAlign = headerHeight >= 0
            ? ToLayoutOptions(tableView.HeaderTextVerticalAlign)
            : LayoutOptions.Center;

        var headerLabel = new Label
        {
            Text = section.Title,
            FontAttributes = fontAttributes,
            VerticalOptions = verticalAlign,
            // Drawn uppercase, not stored uppercase: Title keeps whatever was bound to it.
            TextTransform = tableView.HeaderTextTransform,
            CharacterSpacing = tableView.HeaderCharacterSpacing
        };

        headerLabel.SetTokenOrValue(Label.FontSizeProperty, fontSize, ShinyThemeKeys.Type.BodySmallSize);
        ApplyColor(headerLabel, Label.TextColorProperty, textColor, ShinyThemeKeys.Color.OnSurfaceVariant);

        if (fontFamily != null)
            headerLabel.FontFamily = fontFamily;

        headerContainer.Content = headerLabel;
        layout.Children.Add(headerContainer);
    }

    static void RenderFooter(VerticalStackLayout layout, TvTableSection section, TvTableView tableView)
    {
        if (!section.FooterVisible)
            return;

        if (section.FooterView != null)
        {
            layout.Children.Add(section.FooterView);
            return;
        }

        if (string.IsNullOrEmpty(section.FooterText))
            return;

        var bgColor = section.FooterBackgroundColor ?? tableView.FooterBackgroundColor;
        var textColor = section.FooterTextColor ?? tableView.FooterTextColor;
        var fontSize = section.FooterFontSize >= 0 ? section.FooterFontSize
            : tableView.FooterFontSize >= 0 ? tableView.FooterFontSize
            : ThemeTokens.Unset;
        var fontFamily = section.FooterFontFamily ?? tableView.FooterFontFamily;
        var fontAttributes = section.FooterFontAttributes ?? tableView.FooterFontAttributes;

        var footerContainer = new ContentView
        {
            Padding = tableView.FooterPadding
        };
        if (bgColor != null)
            footerContainer.BackgroundColor = bgColor;

        var footerLabel = new Label
        {
            Text = section.FooterText,
            FontAttributes = fontAttributes
        };

        footerLabel.SetTokenOrValue(Label.FontSizeProperty, fontSize, ShinyThemeKeys.Type.BodySmallSize);
        ApplyColor(footerLabel, Label.TextColorProperty, textColor, ShinyThemeKeys.Color.OnSurfaceVariant);

        if (fontFamily != null)
            footerLabel.FontFamily = fontFamily;

        footerContainer.Content = footerLabel;
        layout.Children.Add(footerContainer);
    }

    /// <summary>
    /// Applies the consumer's colour, or binds the property to a theme token when they gave none.
    /// </summary>
    /// <remarks>
    /// These used to be literal iOS system greys picked from <c>Application.Current.RequestedTheme</c>
    /// at render time, which was wrong twice over. A theme pack restyled every other part of the
    /// table and left the section headers in iOS grey; and because the colour was read once while the
    /// section was being built, a theme swap - or an appearance flip arriving after the first render -
    /// left the old value on screen, so headers drifted out of step with the rows under them. A
    /// dynamic resource re-resolves on both.
    /// </remarks>
    static void ApplyColor(VisualElement element, BindableProperty property, Color? explicitColor, string themeKey)
    {
        if (explicitColor is not null)
            element.SetValue(property, explicitColor);
        else
            element.SetDynamicResource(property, themeKey);
    }

    static BoxView CreateSeparator(TvTableView tableView)
    {
        var separator = new BoxView
        {
            HeightRequest = tableView.SeparatorHeight >= 0 ? tableView.SeparatorHeight : 0.5,
            Margin = new Thickness(tableView.SeparatorPadding >= 0 ? tableView.SeparatorPadding : 16, 0, 0, 0)
        };

        // BoxView paints from Color, not BackgroundColor - see the AppKit note in styling.md.
        ApplyColor(separator, BoxView.ColorProperty, tableView.SeparatorColor, ShinyThemeKeys.Color.OutlineVariant);
        return separator;
    }

    static LayoutOptions ToLayoutOptions(LayoutAlignment alignment) => alignment switch
    {
        LayoutAlignment.Start => LayoutOptions.Start,
        LayoutAlignment.Center => LayoutOptions.Center,
        LayoutAlignment.End => LayoutOptions.End,
        LayoutAlignment.Fill => LayoutOptions.Fill,
        _ => LayoutOptions.End
    };
}