namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// Reports what the <b>current platform</b> will actually do with an effect. Reached through
/// <see cref="CameraView.GetEffectSupport"/>.
/// </summary>
/// <remarks>
/// Deliberately static rather than a handler method: an app needs to know whether to show a Comic button
/// <i>before</i> the camera is connected, not after.
/// </remarks>
static class CameraEffectSupport
{
    public static EffectSupport Resolve(ICameraEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var descriptor = (effect as INativeEffect)?.Descriptor;
        return CameraEffectChain.ResolveSupport(
            effect,
            HasNativeProgram(descriptor),
            FiltersPreview,
            // every MAUI head runs the managed pixel pass on captured stills
            hasStillFallback: descriptor?.Managed is not null);
    }

#if IOS || MACCATALYST || MACOS

    // Core Image runs the preview, the still and the recording composite, so anything with a CI filter name
    // is honoured everywhere.
    const bool FiltersPreview = true;

    static bool HasNativeProgram(NativeEffectDescriptor? d) => d?.CoreImageFilterName is not null;

#elif ANDROID

    // The preview effect is RenderEffect, which is API 31+; the AGSL shader path needs API 33+. Blur is the
    // exception — it maps to RenderEffect.CreateBlurEffect and so needs only 31.
    static bool FiltersPreview => OperatingSystem.IsAndroidVersionAtLeast(31);

    static bool HasNativeProgram(NativeEffectDescriptor? d)
    {
        if (d is null)
            return false;

        if (d.AndroidBlurRadius is > 0)
            return OperatingSystem.IsAndroidVersionAtLeast(31);

        return d.AgslShader is not null && OperatingSystem.IsAndroidVersionAtLeast(33);
    }

#else

    // Windows and the bare net11.0 head: stills go through the managed pixel path, the preview is untouched.
    const bool FiltersPreview = false;

    static bool HasNativeProgram(NativeEffectDescriptor? d) => d?.Managed is not null;

#endif
}
