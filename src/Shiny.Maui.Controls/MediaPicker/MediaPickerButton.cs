using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Shiny.Maui.Controls.CarouselGallery;
using Shiny.Maui.Controls.Collections;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.ImageEditor;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;
using ImageEditorControl = Shiny.Maui.Controls.ImageEditor.ImageEditor;

namespace Shiny.Maui.Controls.Media;

/// <summary>
/// A button that lets the user add photos from the gallery and/or camera (via the built-in
/// <see cref="Microsoft.Maui.Media.MediaPicker"/>), compresses/converts them, caps the count,
/// and shows the collected photos inline as a carousel (tap to view/edit) or via a pinch/zoom overlay.
/// </summary>
public class MediaPickerButton : ContentView
{
    readonly VerticalStackLayout root;
    readonly ContentView displayHost;   // swaps between empty view / carousel / compact preview
    readonly Border addTrigger;
    readonly Label addTriggerLabel;
    readonly Label permissionLabel;
    readonly CarouselGallery.CarouselGallery carousel;
    readonly ImageViewer viewer;

    View? noImagesView;
    Label? navLabel;
    int currentIndex;
    Layout? editorOverlayParent;
    View? editorOverlay;

    public MediaPickerButton()
    {
        this.carousel = new CarouselGallery.CarouselGallery
        {
            ItemHeight = 220,
            ItemWidth = 180,
            HeightRequest = 240
        };
        this.carousel.ItemSelected += OnCarouselItemSelected;

        this.displayHost = new ContentView();

        this.addTriggerLabel = new Label
        {
            Text = "➕  Add Photo",
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        this.addTriggerLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.addTrigger = new Border
        {
            Padding = new Thickness(16, 12),
            StrokeDashArray = new DoubleCollection { 4, 2 },
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = this.addTriggerLabel,
            HorizontalOptions = LayoutOptions.Fill
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        this.addTrigger.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        var addTap = new TapGestureRecognizer();
        addTap.Tapped += (_, _) => _ = OnAddTappedAsync();
        this.addTrigger.GestureRecognizers.Add(addTap);

        this.permissionLabel = new Label
        {
            IsVisible = false,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        this.permissionLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.Error);

        // Shared, hidden viewer — it injects its full-screen overlay into the page host on open.
        this.viewer = new ImageViewer { IsVisible = false, OpenViewerOnTap = false };
        this.viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImageViewer.IsOpen) && !this.viewer.IsOpen)
                this.viewer.Source = null;
        };

        this.root = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { this.displayHost, this.addTrigger, this.permissionLabel, this.viewer }
        };
        this.Content = this.root;

        this.Photos = new ObservableCollection<MediaPickerItem>();
        UpdateView();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(MediaPickerButton));
    }

    #region Bindable Properties

    public static readonly BindableProperty AllowGalleryProperty = BindableProperty.Create(
        nameof(AllowGallery), typeof(bool), typeof(MediaPickerButton), true);
    public bool AllowGallery
    {
        get => (bool)GetValue(AllowGalleryProperty);
        set => SetValue(AllowGalleryProperty, value);
    }

    public static readonly BindableProperty AllowCameraProperty = BindableProperty.Create(
        nameof(AllowCamera), typeof(bool), typeof(MediaPickerButton), true);
    public bool AllowCamera
    {
        get => (bool)GetValue(AllowCameraProperty);
        set => SetValue(AllowCameraProperty, value);
    }

    public static readonly BindableProperty AllowPhotoEditProperty = BindableProperty.Create(
        nameof(AllowPhotoEdit), typeof(bool), typeof(MediaPickerButton), false);
    public bool AllowPhotoEdit
    {
        get => (bool)GetValue(AllowPhotoEditProperty);
        set => SetValue(AllowPhotoEditProperty, value);
    }

    public static readonly BindableProperty PermissionDeniedTextProperty = BindableProperty.Create(
        nameof(PermissionDeniedText), typeof(string), typeof(MediaPickerButton),
        "Permission denied. Please enable access in Settings.");
    public string PermissionDeniedText
    {
        get => (string)GetValue(PermissionDeniedTextProperty);
        set => SetValue(PermissionDeniedTextProperty, value);
    }

    public static readonly BindableProperty NoImagesTemplateProperty = BindableProperty.Create(
        nameof(NoImagesTemplate), typeof(DataTemplate), typeof(MediaPickerButton), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).ApplyNoImagesTemplate();
            }));
    public DataTemplate? NoImagesTemplate
    {
        get => (DataTemplate?)GetValue(NoImagesTemplateProperty);
        set => SetValue(NoImagesTemplateProperty, value);
    }

    public static readonly BindableProperty ShowAsCarouselInViewProperty = BindableProperty.Create(
        nameof(ShowAsCarouselInView), typeof(bool), typeof(MediaPickerButton), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).UpdateView();
            }));
    public bool ShowAsCarouselInView
    {
        get => (bool)GetValue(ShowAsCarouselInViewProperty);
        set => SetValue(ShowAsCarouselInViewProperty, value);
    }

    public static readonly BindableProperty MaxPhotosProperty = BindableProperty.Create(
        nameof(MaxPhotos), typeof(int), typeof(MediaPickerButton), 1,
        validateValue: (_, v) => (int)v >= 1,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).UpdateView();
            }));
    public int MaxPhotos
    {
        get => (int)GetValue(MaxPhotosProperty);
        set => SetValue(MaxPhotosProperty, value);
    }

    /// <summary>JPEG/PNG encoder quality as a percentage (1..100). Default 92.</summary>
    public static readonly BindableProperty CompressionQualityProperty = BindableProperty.Create(
        nameof(CompressionQuality), typeof(int), typeof(MediaPickerButton), 92,
        validateValue: (_, v) => (int)v is >= 1 and <= 100);
    public int CompressionQuality
    {
        get => (int)GetValue(CompressionQualityProperty);
        set => SetValue(CompressionQualityProperty, value);
    }

    /// <summary>If &gt; 0, the longest edge of each saved photo is capped to this many pixels.</summary>
    public static readonly BindableProperty MaxImageDimensionProperty = BindableProperty.Create(
        nameof(MaxImageDimension), typeof(int), typeof(MediaPickerButton), 0);
    public int MaxImageDimension
    {
        get => (int)GetValue(MaxImageDimensionProperty);
        set => SetValue(MaxImageDimensionProperty, value);
    }

    public static readonly BindableProperty OutputFormatProperty = BindableProperty.Create(
        nameof(OutputFormat), typeof(ImageExportFormat), typeof(MediaPickerButton), ImageExportFormat.Jpeg);
    public ImageExportFormat OutputFormat
    {
        get => (ImageExportFormat)GetValue(OutputFormatProperty);
        set => SetValue(OutputFormatProperty, value);
    }

    public static readonly BindableProperty PhotosProperty = BindableProperty.Create(
        nameof(Photos), typeof(IList<MediaPickerItem>), typeof(MediaPickerButton), null,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).OnPhotosChanged(o as IList<MediaPickerItem>, n as IList<MediaPickerItem>);
            }));
    public IList<MediaPickerItem> Photos
    {
        get => (IList<MediaPickerItem>)GetValue(PhotosProperty);
        set => SetValue(PhotosProperty, value);
    }

    /// <summary>Custom thumbnail template for the inline carousel (item = <see cref="MediaPickerItem"/>).</summary>
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(MediaPickerButton), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).ApplyItemTemplate();
            }));
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly BindableProperty AddButtonTextProperty = BindableProperty.Create(
        nameof(AddButtonText), typeof(string), typeof(MediaPickerButton), "➕  Add Photo",
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(MediaPickerButton), () =>
            {
                ((MediaPickerButton)b).addTriggerLabel.Text = (string)n;
            }));
    public string AddButtonText
    {
        get => (string)GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    public static readonly BindableProperty GalleryActionTextProperty = BindableProperty.Create(
        nameof(GalleryActionText), typeof(string), typeof(MediaPickerButton), "Choose from Gallery");
    public string GalleryActionText
    {
        get => (string)GetValue(GalleryActionTextProperty);
        set => SetValue(GalleryActionTextProperty, value);
    }

    public static readonly BindableProperty CameraActionTextProperty = BindableProperty.Create(
        nameof(CameraActionText), typeof(string), typeof(MediaPickerButton), "Take Photo");
    public string CameraActionText
    {
        get => (string)GetValue(CameraActionTextProperty);
        set => SetValue(CameraActionTextProperty, value);
    }

    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback), typeof(bool), typeof(MediaPickerButton), true);
    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }

    public static readonly BindableProperty PhotosChangedCommandProperty = BindableProperty.Create(
        nameof(PhotosChangedCommand), typeof(ICommand), typeof(MediaPickerButton), null);
    public ICommand? PhotosChangedCommand
    {
        get => (ICommand?)GetValue(PhotosChangedCommandProperty);
        set => SetValue(PhotosChangedCommandProperty, value);
    }

    #endregion

    #region Events

    public event EventHandler<MediaPickerItem>? PhotoAdded;
    public event EventHandler<MediaPickerItem>? PhotoRemoved;
    public event EventHandler? PhotosChanged;
    public event EventHandler<string>? PermissionDenied;

    #endregion

    #region Photos collection wiring

    IDisposable? photosSubscription;

    void OnPhotosChanged(IList<MediaPickerItem>? oldValue, IList<MediaPickerItem>? newValue)
    {
        // Weak: a bound collection can outlive the page.
        this.photosSubscription?.Dispose();
        this.photosSubscription = WeakEventSubscription.CollectionChanged(newValue, OnPhotosCollectionChanged);
        this.carousel.ItemsSource = newValue as System.Collections.IEnumerable;
        UpdateView();
    }

    void OnPhotosCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => OnPhotosCollectionChanged(sender, e));
            return;
        }
        RaisePhotosChanged();
        UpdateView();
    }

    void RaisePhotosChanged()
    {
        PhotosChanged?.Invoke(this, EventArgs.Empty);
        if (PhotosChangedCommand?.CanExecute(this.Photos) == true)
            PhotosChangedCommand.Execute(this.Photos);
    }

    #endregion

    #region Add / capture flow

    async Task OnAddTappedAsync()
    {
        HidePermissionMessage();

        if (this.Photos.Count >= this.MaxPhotos)
            return;

        if (this.UseFeedback)
            FeedbackHelper.Execute(this, "Add");

        var useCamera = false;
        if (this.AllowGallery && this.AllowCamera)
        {
            var page = GetPage();
            if (page == null)
                return;

            var choice = await page.DisplayActionSheetAsync(null, "Cancel", null, this.GalleryActionText, this.CameraActionText);
            if (choice == this.CameraActionText)
                useCamera = true;
            else if (choice != this.GalleryActionText)
                return; // cancelled
        }
        else if (this.AllowCamera)
        {
            useCamera = true;
        }
        else if (!this.AllowGallery)
        {
            return; // neither source allowed
        }

        FileResult? file;
        try
        {
            // Single-pick is intentional — MaxPhotos is enforced by adding one at a time.
#pragma warning disable CS0618 // PickPhotoAsync is single-select, which is exactly what we want here
            file = useCamera
                ? await Microsoft.Maui.Media.MediaPicker.Default.CapturePhotoAsync()
                : await Microsoft.Maui.Media.MediaPicker.Default.PickPhotoAsync();
#pragma warning restore CS0618
        }
        catch (PermissionException)
        {
            ShowPermissionMessage();
            return;
        }
        catch (FeatureNotSupportedException)
        {
            ShowPermissionMessage();
            return;
        }

        if (file == null)
            return; // user cancelled

        await using var stream = await file.OpenReadAsync();
        var quality = MediaImageProcessor.NormalizeQuality(this.CompressionQuality);
        var item = await MediaImageProcessor.ProcessAsync(stream, this.OutputFormat, quality, this.MaxImageDimension);
        if (item == null)
            return;

        this.Photos.Add(item);
        PhotoAdded?.Invoke(this, item);

        // Non-observable lists won't fire CollectionChanged; refresh explicitly.
        if (this.Photos is not INotifyCollectionChanged)
        {
            RaisePhotosChanged();
            UpdateView();
        }
    }

    void ShowPermissionMessage()
    {
        this.permissionLabel.Text = this.PermissionDeniedText;
        this.permissionLabel.IsVisible = true;
        PermissionDenied?.Invoke(this, this.PermissionDeniedText);
    }

    void HidePermissionMessage() => this.permissionLabel.IsVisible = false;

    #endregion

    #region Display

    void UpdateView()
    {
        var count = this.Photos?.Count ?? 0;

        if (count == 0)
        {
            this.displayHost.Content = ResolveNoImagesView();
        }
        else if (this.ShowAsCarouselInView)
        {
            EnsureCarouselTemplate();
            this.displayHost.Content = this.carousel;
        }
        else
        {
            this.displayHost.Content = BuildCompactPreview();
        }

        this.addTrigger.IsVisible = count < this.MaxPhotos;
    }

    View ResolveNoImagesView()
    {
        this.noImagesView ??= NoImagesTemplate?.CreateContent() as View ?? CreateDefaultNoImagesView();
        return this.noImagesView;
    }

    void ApplyNoImagesTemplate()
    {
        this.noImagesView = null;
        UpdateView();
    }

    static View CreateDefaultNoImagesView()
    {
        var label = new Label
        {
            Text = "No photos yet",
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 24)
        };
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        return label;
    }

    void EnsureCarouselTemplate()
        => this.carousel.ItemTemplate = this.ItemTemplate ?? BuildDefaultItemTemplate();

    void ApplyItemTemplate()
    {
        if (this.ShowAsCarouselInView)
            EnsureCarouselTemplate();
    }

    DataTemplate BuildDefaultItemTemplate() => new(() =>
    {
        var image = new Image { Aspect = Aspect.AspectFill };
        image.SetBinding(Image.SourceProperty, nameof(MediaPickerItem.Thumbnail));

        var removeButton = new Button
        {
            Text = "✕",
            Padding = 0,
            WidthRequest = 28,
            HeightRequest = 28,
            CornerRadius = 14,
            BackgroundColor = Color.FromRgba(0, 0, 0, 0.5),
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(6)
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        removeButton.Clicked += (s, _) =>
        {
            if (((BindableObject)s!).BindingContext is MediaPickerItem item)
                RemovePhoto(item);
        };

        var border = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = new Grid { Children = { image, removeButton } }
        };
        return border;
    });

    View BuildCompactPreview()
    {
        var count = this.Photos.Count;
        var first = this.Photos[0];

        var image = new Image
        {
            Source = first.Thumbnail,
            Aspect = Aspect.AspectFill,
            HeightRequest = 120,
            WidthRequest = 120
        };

        var countBadge = new Border
        {
            Padding = new Thickness(8, 2),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            BackgroundColor = Color.FromRgba(0, 0, 0, 0.6),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(6),
            IsVisible = count > 1,
            Content = new Label { Text = $"+{count - 1}", TextColor = Colors.White}.WithFontSize(ShinyThemeKeys.Type.BodySmallSize)
        };

        var border = new Border
        {
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = new Grid { Children = { image, countBadge } }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OpenViewer(0);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    #endregion

    #region Viewer / paging / edit

    void OnCarouselItemSelected(object? sender, CollectionItemEventArgs e) => OpenViewer(e.Index);

    void OpenViewer(int index)
    {
        if (this.Photos.Count == 0)
            return;

        this.currentIndex = Math.Clamp(index, 0, this.Photos.Count - 1);
        this.viewer.Source = this.Photos[this.currentIndex].Thumbnail;
        this.viewer.HeaderTemplate = this.Photos.Count > 1 ? BuildNavHeaderTemplate() : null;
        this.viewer.FooterTemplate = this.AllowPhotoEdit ? BuildEditFooterTemplate() : null;
        this.viewer.IsOpen = true;
    }

    DataTemplate BuildNavHeaderTemplate() => new(() =>
    {
        var prev = MakeChromeButton("‹");
        prev.Clicked += (_, _) => Page(-1);

        var next = MakeChromeButton("›");
        next.Clicked += (_, _) => Page(1);

        this.navLabel = new Label
        {
            Text = $"{this.currentIndex + 1} / {this.Photos.Count}",
            TextColor = Colors.White,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        return new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 50, 16, 0),
            HorizontalOptions = LayoutOptions.Center,
            Children = { prev, this.navLabel, next }
        };
    });

    DataTemplate BuildEditFooterTemplate() => new(() =>
    {
        var edit = new Button
        {
            Text = "✎  Edit",
            CornerRadius = 20,
            HeightRequest = 44,
            Padding = new Thickness(20, 0),
            Margin = new Thickness(0, 0, 0, 40),
            HorizontalOptions = LayoutOptions.Center
        }.Neutralize();
        edit.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        edit.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        edit.Clicked += (_, _) =>
        {
            this.viewer.IsOpen = false;
            OpenEditor(this.currentIndex);
        };
        return edit;
    });

    void Page(int delta)
    {
        if (this.Photos.Count == 0)
            return;

        this.currentIndex = (this.currentIndex + delta + this.Photos.Count) % this.Photos.Count;
        this.viewer.Source = this.Photos[this.currentIndex].Thumbnail;
        if (this.navLabel != null)
            this.navLabel.Text = $"{this.currentIndex + 1} / {this.Photos.Count}";
    }

    static Button MakeChromeButton(string text) => new Button
    {
        Text = text,
        WidthRequest = 44,
        HeightRequest = 44,
        Padding = 0,
        CornerRadius = 22,
        BackgroundColor = Color.FromRgba(0, 0, 0, 0.5),
        TextColor = Colors.White
    }.Neutralize().WithFontSize(ShinyThemeKeys.Type.TitleLargeSize);

    void OpenEditor(int index)
    {
        this.editorOverlayParent = FindOverlayParent();
        if (this.editorOverlayParent == null || index < 0 || index >= this.Photos.Count)
            return;

        var editor = new ImageEditorControl
        {
            Source = this.Photos[index].Thumbnail,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            SaveText = "✓ Save"
        };
        editor.SaveCommand = new Command<EditedImage>(async edited => await ApplyEditAsync(index, edited));

        var cancel = MakeChromeButton("✕");
        cancel.Margin = new Thickness(0, 50, 16, 0);
        cancel.HorizontalOptions = LayoutOptions.End;
        cancel.VerticalOptions = LayoutOptions.Start;
        cancel.Clicked += (_, _) => CloseEditor();

        var backdrop = new BoxView();
        backdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);

        this.editorOverlay = new Grid
        {
            Children = { backdrop, editor, cancel }
        };
        this.editorOverlayParent.Children.Add(this.editorOverlay);
    }

    async Task ApplyEditAsync(int index, EditedImage edited)
    {
        var quality = MediaImageProcessor.NormalizeQuality(this.CompressionQuality);
        await using var stream = await edited.ToStreamAsync(this.OutputFormat, quality);
        var item = await MediaImageProcessor.ProcessAsync(stream, this.OutputFormat, quality, this.MaxImageDimension);
        CloseEditor();

        if (item == null || index < 0 || index >= this.Photos.Count)
            return;

        this.Photos[index] = item;
        if (this.UseFeedback)
            FeedbackHelper.Execute(this, "Edited");

        if (this.Photos is not INotifyCollectionChanged)
        {
            RaisePhotosChanged();
            UpdateView();
        }
    }

    void CloseEditor()
    {
        if (this.editorOverlay != null)
        {
            this.editorOverlayParent?.Children.Remove(this.editorOverlay);
            this.editorOverlay = null;
        }
        this.editorOverlayParent = null;
    }

    void RemovePhoto(MediaPickerItem item)
    {
        var idx = this.Photos.IndexOf(item);
        if (idx < 0)
            return;

        this.Photos.RemoveAt(idx);
        PhotoRemoved?.Invoke(this, item);

        if (this.Photos is not INotifyCollectionChanged)
        {
            RaisePhotosChanged();
            UpdateView();
        }
    }

    #endregion

    #region Helpers

    Page? GetPage()
    {
        Element? current = this.Parent;
        while (current != null)
        {
            if (current is Page page)
                return page;
            current = current.Parent;
        }
        return null;
    }

    Layout? FindOverlayParent()
    {
        Element? current = this.Parent;
        while (current != null)
        {
            if (current is OverlayHost host)
                return host;
            current = current.Parent;
        }

        current = this.Parent;
        while (current != null)
        {
            if (current is Page page)
            {
                if (page is ShinyContentPage scp)
                    return scp.OverlayHost;
                if (page is ContentPage cp && cp.Content is Grid grid)
                    return grid;
                break;
            }
            current = current.Parent;
        }
        return null;
    }

    #endregion
}
