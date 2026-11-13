using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.SignaturePad;

public partial class SignatureView
{
    public static readonly BindableProperty StrokeColorProperty = BindableProperty.Create(
        nameof(StrokeColor), typeof(Color), typeof(SignatureView), Colors.Black,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.drawable.StrokeColor = (Color)n;
            view.surface.Invalidate();
        }));

    /// <summary>The ink. Black by default.</summary>
    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(double), typeof(SignatureView), 3.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.drawable.StrokeWidth = (float)(double)n;
            view.surface.Invalidate();
        }));

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public static readonly BindableProperty SignatureBackgroundColorProperty = BindableProperty.Create(
        nameof(SignatureBackgroundColor), typeof(Color), typeof(SignatureView), Colors.White,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.drawable.BackgroundColor = (Color)n;
            view.surface.Invalidate();
        }));

    /// <summary>The paper. White by default, and not themed: it is part of the exported image.</summary>
    public Color SignatureBackgroundColor
    {
        get => (Color)GetValue(SignatureBackgroundColorProperty);
        set => SetValue(SignatureBackgroundColorProperty, value);
    }

    public static readonly BindableProperty PlaceholderTextProperty = BindableProperty.Create(
        nameof(PlaceholderText), typeof(string), typeof(SignatureView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.drawable.PlaceholderText = (string?)n;
            view.surface.Invalidate();
        }));

    /// <summary>Hint shown while the surface is empty ("Sign here"). Never exported.</summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly BindableProperty ShowSignatureLineProperty = BindableProperty.Create(
        nameof(ShowSignatureLine), typeof(bool), typeof(SignatureView), false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.drawable.ShowSignatureLine = (bool)n;
            view.surface.Invalidate();
        }));

    /// <summary>Draws a "✕ ____" baseline to sign on. Never exported.</summary>
    public bool ShowSignatureLine
    {
        get => (bool)GetValue(ShowSignatureLineProperty);
        set => SetValue(ShowSignatureLineProperty, value);
    }

    public static readonly BindableProperty ShowClearButtonProperty = BindableProperty.Create(
        nameof(ShowClearButton), typeof(bool), typeof(SignatureView), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SignatureView), () => ((SignatureView)b).SyncState()));

    /// <summary>Shows a Clear button in the top corner once something is drawn.</summary>
    public bool ShowClearButton
    {
        get => (bool)GetValue(ShowClearButtonProperty);
        set => SetValue(ShowClearButtonProperty, value);
    }

    public static readonly BindableProperty ClearButtonTextProperty = BindableProperty.Create(
        nameof(ClearButtonText), typeof(string), typeof(SignatureView), "Clear",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () => ((SignatureView)b).clearButton.Text = (string)n));

    public string ClearButtonText
    {
        get => (string)GetValue(ClearButtonTextProperty);
        set => SetValue(ClearButtonTextProperty, value);
    }

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly), typeof(bool), typeof(SignatureView), false,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
        {
            var view = (SignatureView)b;
            view.CancelStroke();
            view.SyncState();
            view.UpdateEdgeGestures();
        }));

    /// <summary>Shows the signature but takes no strokes, and hides the clear button.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly BindableProperty ShowBorderProperty = BindableProperty.Create(
        nameof(ShowBorder), typeof(bool), typeof(SignatureView), true,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () => ((SignatureView)b).border.StrokeThickness = (bool)n ? 1 : 0));

    /// <summary>
    /// Draws the outline. Turn it off when the view fills a container that already draws one (a card,
    /// a <see cref="Border"/>, a <see cref="SignaturePad"/>), so the two edges do not double up.
    /// </summary>
    public bool ShowBorder
    {
        get => (bool)GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(SignatureView), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () => ((SignatureView)b).ApplyBorderColor((Color?)n)));

    /// <summary>Leave unset to follow the theme's outline-variant colour.</summary>
    public Color? BorderColor
    {
        get => (Color?)GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(SignatureView), 8.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(SignatureView), () =>
            ((SignatureView)b).border.StrokeShape = new RoundRectangle { CornerRadius = (double)n }));

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty ExportWidthProperty = BindableProperty.Create(
        nameof(ExportWidth), typeof(int), typeof(SignatureView), 600);

    /// <summary>The default width of <see cref="ExportToPng"/>, in pixels.</summary>
    public int ExportWidth
    {
        get => (int)GetValue(ExportWidthProperty);
        set => SetValue(ExportWidthProperty, value);
    }

    public static readonly BindableProperty ExportHeightProperty = BindableProperty.Create(
        nameof(ExportHeight), typeof(int), typeof(SignatureView), 200);

    /// <summary>The default height of <see cref="ExportToPng"/>, in pixels.</summary>
    public int ExportHeight
    {
        get => (int)GetValue(ExportHeightProperty);
        set => SetValue(ExportHeightProperty, value);
    }

    static readonly BindablePropertyKey HasSignaturePropertyKey = BindableProperty.CreateReadOnly(
        nameof(HasSignature), typeof(bool), typeof(SignatureView), false);

    public static readonly BindableProperty HasSignatureProperty = HasSignaturePropertyKey.BindableProperty;

    /// <summary>Whether at least one stroke has been drawn. Bind a Submit button's IsEnabled to it.</summary>
    public bool HasSignature => (bool)GetValue(HasSignatureProperty);

    static readonly BindablePropertyKey StrokeCountPropertyKey = BindableProperty.CreateReadOnly(
        nameof(StrokeCount), typeof(int), typeof(SignatureView), 0);

    public static readonly BindableProperty StrokeCountProperty = StrokeCountPropertyKey.BindableProperty;

    /// <summary>How many strokes are drawn - what <see cref="Undo"/> walks back through.</summary>
    public int StrokeCount => (int)GetValue(StrokeCountProperty);

    public static readonly BindableProperty StrokeCompletedCommandProperty = BindableProperty.Create(
        nameof(StrokeCompletedCommand), typeof(ICommand), typeof(SignatureView));

    /// <summary>Executed alongside <see cref="StrokeCompleted"/>.</summary>
    public ICommand? StrokeCompletedCommand
    {
        get => (ICommand?)GetValue(StrokeCompletedCommandProperty);
        set => SetValue(StrokeCompletedCommandProperty, value);
    }

    public static readonly BindableProperty ClearedCommandProperty = BindableProperty.Create(
        nameof(ClearedCommand), typeof(ICommand), typeof(SignatureView));

    /// <summary>Executed alongside <see cref="Cleared"/>.</summary>
    public ICommand? ClearedCommand
    {
        get => (ICommand?)GetValue(ClearedCommandProperty);
        set => SetValue(ClearedCommandProperty, value);
    }
}
