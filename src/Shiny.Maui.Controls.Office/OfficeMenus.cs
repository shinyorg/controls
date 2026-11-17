using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The insert and highlight menus, shared by the document and slide toolbars.
/// </summary>
/// <remarks>
/// <para>
/// Action sheets rather than the popovers the Blazor toolbars use. MAUI has no popover primitive in
/// the box, and the alternatives — a <c>Popup</c> from the Community Toolkit, or a hand-rolled
/// absolute-positioned overlay — would either add a dependency this package does not otherwise need
/// or reimplement dismissal, focus and safe-area behaviour that the platform already gets right.
/// An action sheet is also the gesture a phone user expects, and these toolbars run on phones.
/// </para>
/// <para>
/// The gallery contents are deliberately the same as the Blazor ones. A shape offered on one host and
/// not the other would be a difference with nothing behind it.
/// </para>
/// </remarks>
static class OfficeMenus
{
    /// <summary>
    /// The nearest page, for the action sheet to be presented from.
    /// </summary>
    /// <remarks>
    /// Walked up from the view rather than taken from <c>Application.Current</c>: in a multi-window
    /// desktop app the current application has several pages and only one of them is the one this
    /// toolbar is in.
    /// </remarks>
    public static Page? PageOf(Element element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current is Page page)
                return page;
        }

        return null;
    }

    /// <summary>
    /// Asks for a highlight colour. Returns false when the user cancelled.
    /// </summary>
    /// <remarks>
    /// Two out-parameters in effect — "did they choose" and "what did they choose" — because null is a
    /// real answer here: it means remove the highlight, which is not the same as backing out.
    /// </remarks>
    public static async Task<(bool Chosen, ArgbColor? Color)> PickHighlightAsync(Page? page)
    {
        if (page is null)
            return (false, null);

        const string none = "No colour";

        var names = HighlightPalette.Swatches.Select(x => x.DisplayName).Prepend(none).ToArray();
        var picked = await page.DisplayActionSheet("Highlight", "Cancel", null, names);

        if (string.IsNullOrEmpty(picked) || picked == "Cancel")
            return (false, null);

        if (picked == none)
            return (true, null);

        foreach (var swatch in HighlightPalette.Swatches)
        {
            if (swatch.DisplayName == picked)
                return (true, swatch.Color);
        }

        return (false, null);
    }

    /// <summary>The shapes on offer. Shared with the Blazor gallery so the two cannot drift.</summary>
    public static IReadOnlyList<(ShapeGeometry Geometry, string Name)> Shapes => ShapeNames.All;

    public static async Task<ShapeGeometry?> PickShapeAsync(Page? page)
    {
        if (page is null)
            return null;

        var picked = await page.DisplayActionSheet("Shape", "Cancel", null, Shapes.Select(x => x.Name).ToArray());

        if (string.IsNullOrEmpty(picked) || picked == "Cancel")
            return null;

        foreach (var (geometry, name) in Shapes)
        {
            if (name == picked)
                return geometry;
        }

        return null;
    }

    /// <summary>
    /// The table sizes on offer.
    /// </summary>
    /// <remarks>
    /// A fixed list rather than the drag-a-grid picker the Blazor toolbar has, because an action sheet
    /// cannot express a two-dimensional gesture. These are the sizes people actually insert; anything
    /// else is reachable by adding rows once the table is there.
    /// </remarks>
    static readonly (int Rows, int Columns)[] TableSizes =
    [
        (2, 2), (2, 3), (3, 2), (3, 3), (3, 4), (4, 3), (4, 4), (5, 3), (5, 5), (6, 4)
    ];

    public static async Task<(int Rows, int Columns)?> PickTableAsync(Page? page)
    {
        if (page is null)
            return null;

        var labels = TableSizes.Select(x => $"{x.Rows} × {x.Columns}").ToArray();
        var picked = await page.DisplayActionSheet("Table size", "Cancel", null, labels);

        if (string.IsNullOrEmpty(picked) || picked == "Cancel")
            return null;

        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] == picked)
                return TableSizes[i];
        }

        return null;
    }

    /// <summary>
    /// Asks which page margins to apply. Null when the user backed out.
    /// </summary>
    /// <remarks>
    /// The presets come from <see cref="PageMarginPresets"/> rather than being listed here, so this
    /// sheet and the Blazor gallery offer the same four with the same measurements. The measurements
    /// are on the label because "Moderate" says nothing on its own.
    /// </remarks>
    public static async Task<PageMargins?> PickPageMarginsAsync(Page? page)
    {
        if (page is null)
            return null;

        var labels = PageMarginPresets.All.Select(x => $"{x.Name} — {x.Description}").ToArray();
        var picked = await page.DisplayActionSheet("Page margins", "Cancel", null, labels);

        if (string.IsNullOrEmpty(picked) || picked == "Cancel")
            return null;

        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] == picked)
                return PageMarginPresets.All[i].Margins;
        }

        return null;
    }

    /// <summary>
    /// Asks for an image file and reads it.
    /// </summary>
    /// <remarks>
    /// <see cref="FilePickerFileType.Images"/> rather than a list of this package's own extensions:
    /// the platform type list is what makes the picker show the photo library on a phone rather than
    /// a file browser. Whatever comes back is still checked against what OOXML can actually store,
    /// since "image" on iOS includes HEIC and neither format can hold one.
    /// </remarks>
    /// <summary>
    /// Asks for a picture, from wherever the platform actually keeps pictures.
    /// </summary>
    /// <param name="page">The page to ask on, or null to go straight to the file browser.</param>
    /// <remarks>
    /// <para>
    /// A file browser is the right answer on a desktop, where a picture is a file in a folder. On a
    /// phone it is the wrong one twice over: photos live in the gallery rather than the filesystem,
    /// and the picture a user most often wants to put in a document is one that does not exist yet -
    /// they mean to take it. Sending them to Files to find a photo is a dead end.
    /// </para>
    /// <para>
    /// So on a phone the button asks first. Camera is offered only where
    /// <see cref="MediaPicker.IsCaptureSupported"/> says there is one, which correctly leaves it out
    /// on a simulator and on a desktop with no webcam. Browse stays on the list: a picture pulled off
    /// a share or a download is still a file.
    /// </para>
    /// </remarks>
    public static async Task<(OfficePickedImage? Image, OfficeDropRejected? Rejected)> PickImageAsync(Page? page = null)
    {
        FileResult? file;
        try
        {
            file = UsesGallery && page is not null
                ? await PickFromDeviceAsync(page)
                : await BrowseForImageAsync();
        }
        catch (Exception)
        {
            // Not every platform head has a picker implementation; a missing one is not a crash.
            return (null, null);
        }

        if (file is null)
            return (null, null);

        if (ImageContentTypes.Resolve(file.FileName, file.ContentType) is not { } contentType)
            return (null, new OfficeDropRejected(file.FileName, "That picture is in a format a document cannot store."));

        try
        {
            using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            if (buffer.Length > OfficeFileDrop.MaxBytes)
                return (null, new OfficeDropRejected(file.FileName, "The file is too large to embed."));

            return (new OfficePickedImage(file.FileName, contentType, buffer.ToArray()), null);
        }
        catch (Exception ex)
        {
            return (null, new OfficeDropRejected(file.FileName, ex.Message));
        }
    }

    /// <summary>
    /// The platforms where a picture means a photo rather than a file.
    /// </summary>
    /// <remarks>
    /// Mac Catalyst is deliberately not on this list: it runs the iOS code but presents as a desktop,
    /// where a Files browser is what a user reaches for and there is rarely a gallery worth offering.
    /// </remarks>
    static bool UsesGallery
        => DeviceInfo.Platform == DevicePlatform.iOS ||
           DeviceInfo.Platform == DevicePlatform.Android;

    static async Task<FileResult?> PickFromDeviceAsync(Page page)
    {
        var options = new List<string> { "Photo Library" };

        if (MediaPicker.Default.IsCaptureSupported)
            options.Insert(0, "Take Photo");

        options.Add("Browse Files");

        var choice = await page.DisplayActionSheetAsync("Add a picture", "Cancel", null, [.. options]);

        return choice switch
        {
            "Take Photo" => await MediaPicker.Default.CapturePhotoAsync(),
            "Photo Library" => await MediaPicker.Default.PickPhotoAsync(),
            "Browse Files" => await BrowseForImageAsync(),
            _ => null
        };
    }

    /// <summary>
    /// The platform's own open dialog, filtered to the formats a document can actually embed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="FilePickerFileType.Images"/>: that is the platform's idea of an image, which on
    /// Apple heads includes HEIC and RAW. Offering those and then refusing them after the user has
    /// chosen is worse than not offering them - the list is built from the same map the drop handler
    /// and the Blazor picker use, so all three agree on what an image is.
    /// </para>
    /// <para>
    /// AppKit is handled by hand because MAUI Essentials has no FilePicker for <c>net11.0-macos</c> -
    /// it covers iOS, Android, WinUI and Mac Catalyst. The call there threw, the catch above swallowed
    /// it, and the Picture button did nothing at all with no error to explain why.
    /// </para>
    /// </remarks>
    static async Task<FileResult?> BrowseForImageAsync()
    {
#if MACOS
        return await PickWithOpenPanelAsync();
#else
        return await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a picture",
            FileTypes = ImageFileTypes
        });
#endif
    }

#if !MACOS
    /// <summary>Exactly the formats <see cref="ImageContentTypes"/> knows how to store.</summary>
    static readonly FilePickerFileType ImageFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            // Windows wants extensions, Android MIME types, and Apple uniform type identifiers.
            [DevicePlatform.WinUI] = ImageContentTypes.ByExtension.Keys,
            [DevicePlatform.Android] = ImageContentTypes.ByExtension.Values.Distinct(),
            [DevicePlatform.iOS] = AppleImageTypes,
            [DevicePlatform.MacCatalyst] = AppleImageTypes
        });

    static readonly string[] AppleImageTypes =
    [
        "public.png",
        "public.jpeg",
        "com.compuserve.gif",
        "com.microsoft.bmp",
        "public.tiff",
        "org.webmproject.webp"
    ];
#endif

#if MACOS
    /// <summary>AppKit's open panel, which is the standard file picker on this head.</summary>
    static Task<FileResult?> PickWithOpenPanelAsync()
    {
        var completion = new TaskCompletionSource<FileResult?>();

        // The panel is UI: it has to be opened on the main thread whatever called this.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var panel = new AppKit.NSOpenPanel
                {
                    Title = "Choose a picture",
                    CanChooseFiles = true,
                    CanChooseDirectories = false,
                    AllowsMultipleSelection = false,
                    AllowedFileTypes = [.. ImageContentTypes.ByExtension.Keys.Select(x => x.TrimStart('.'))]
                };

                var result = panel.RunModal();
                var url = panel.Urls.FirstOrDefault();

                completion.TrySetResult(result == 1 && url?.Path is { } path
                    ? new FileResult(path)
                    : null);
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
        });

        return completion.Task;
    }
#endif
}
