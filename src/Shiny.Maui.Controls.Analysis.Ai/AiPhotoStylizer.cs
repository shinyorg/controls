using Microsoft.Extensions.AI;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// An <see cref="ICaptureEffect"/> that redraws a captured photo through an image-generation model — the
/// "turn my selfie into a comic" flow the photo-toy apps ship. Add it to <c>CameraView.Effects</c> and
/// <c>CapturePhotoAsync</c> returns the stylized image.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>capture</b> effect, not a live one, and deliberately so: a round-trip to a hosted model is
/// seconds of latency and a per-image cost, so it can never sit on a frame loop. Present it as
/// capture → busy indicator → reveal. If you want a live comic <i>viewfinder</i> as well, add
/// <see cref="CameraEffects.Comic"/> to the chain — it is procedural, offline and free, and the two compose:
/// the user previews a comic look and gets a generated one on the shutter.
/// </para>
/// <para>
/// Provider-agnostic via Microsoft.Extensions.AI's <see cref="IImageGenerator"/> — anything that implements it
/// (Azure OpenAI, OpenAI, a local model) works unchanged. Note that not every chat provider offers image
/// <i>generation</i>; this needs an image-to-image capable one.
/// </para>
/// <para>
/// Failure is never allowed to cost the user their photo: any error surfaces on <see cref="Error"/> and the
/// original capture is returned untouched.
/// </para>
/// <para>
/// <b>MEAI001:</b> <c>IImageGenerator</c> is still marked evaluation-only by Microsoft.Extensions.AI, so
/// constructing one in your own project raises <c>MEAI001</c>. Add
/// <c>&lt;NoWarn&gt;$(NoWarn);MEAI001&lt;/NoWarn&gt;</c> to suppress it until that API ships stable.
/// </para>
/// </remarks>
public class AiPhotoStylizer : ICaptureEffect
{
    readonly IImageGenerator generator;

    /// <param name="generator">The MEAI image generator the photo is sent to. Must support image-to-image editing.</param>
    public AiPhotoStylizer(IImageGenerator generator)
        => this.generator = generator ?? throw new ArgumentNullException(nameof(generator));

    /// <inheritdoc/>
    public string Id { get; set; } = "shiny.camera.ai.stylize";

    /// <inheritdoc/>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The instruction describing the style to redraw the photo in. The default asks for inked comic-book art;
    /// change it for watercolour, pixel art, oil painting, and so on.
    /// </summary>
    public string Prompt { get; set; } =
        "Redraw this photo as a comic-book illustration: bold black ink outlines, flat cel shading, " +
        "halftone-style shadows and punchy saturated colour. Keep the subject, composition and pose exactly " +
        "as they are — restyle the image, do not reimagine the scene.";

    /// <summary>Optional MEAI <see cref="ImageGenerationOptions"/> (model id, size, response format, …).</summary>
    public ImageGenerationOptions? Options { get; set; }

    /// <summary>
    /// How long to wait for the model before giving up and returning the original photo. Default 60 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Raised when the model call fails or times out. The capture still succeeds — the unstyled photo is
    /// returned — so treat this as "the style didn't apply", not "the photo was lost".
    /// </summary>
    public event EventHandler<Exception>? Error;

    /// <summary>Raised when a stylized image comes back, with the byte count of the result.</summary>
    public event EventHandler<int>? Stylized;

    /// <inheritdoc/>
    public async ValueTask<byte[]> ApplyAsync(byte[] jpeg, CancellationToken ct)
    {
        if (jpeg is not { Length: > 0 })
            return jpeg;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (this.Timeout > TimeSpan.Zero)
            timeout.CancelAfter(this.Timeout);

        try
        {
            var response = await this.generator
                .EditImageAsync(new DataContent(jpeg, "image/jpeg"), this.Prompt, this.Options, timeout.Token)
                .ConfigureAwait(false);

            var result = ExtractImage(response);
            if (result is not { Length: > 0 })
            {
                this.Error?.Invoke(this, new InvalidOperationException(
                    "The image generator returned no inline image data. Providers that reply with a URL instead " +
                    "of bytes need ImageGenerationOptions.ResponseFormat set to request the data directly."));
                return jpeg;
            }

            this.Stylized?.Invoke(this, result.Length);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller cancelled the capture — let that propagate
        }
        catch (OperationCanceledException)
        {
            this.Error?.Invoke(this, new TimeoutException($"Image stylization timed out after {this.Timeout}."));
            return jpeg;
        }
        catch (Exception ex)
        {
            this.Error?.Invoke(this, ex);
            return jpeg;
        }
    }

    // Providers return either inline bytes or a hosted URL; only the former is usable without a second fetch,
    // which would need an HttpClient this type deliberately does not own.
    static byte[]? ExtractImage(ImageGenerationResponse response)
    {
        foreach (var content in response.Contents)
        {
            if (content is DataContent data && data.Data.Length > 0)
                return data.Data.ToArray();
        }

        return null;
    }
}
