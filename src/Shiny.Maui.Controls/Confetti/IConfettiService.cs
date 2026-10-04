namespace Shiny.Maui.Controls;

/// <summary>
/// Throws confetti over the page that is showing. Registered by <c>UseShinyControls()</c>. To fire
/// it from a tap without any code, use the <see cref="Confetti"/> attached properties instead.
/// </summary>
public interface IConfettiService
{
    /// <summary>
    /// Fires a custom burst. Null fires canvas-confetti's defaults from the centre of the page.
    /// Completes once every particle has faded out.
    /// </summary>
    Task FireAsync(ConfettiOptions? options = null);

    /// <summary>
    /// Fires a preset. <paramref name="origin"/> is a fraction of the page (0-1 on each axis) and
    /// defaults to (0.5, 0.6); <see cref="ConfettiPreset.Fireworks"/> and
    /// <see cref="ConfettiPreset.SideCannons"/> ignore it.
    /// </summary>
    Task FireAsync(ConfettiPreset preset, Point? origin = null);

    /// <summary>Fires a preset from the centre of <paramref name="element"/>, on whatever page it is on.</summary>
    Task FireFromAsync(VisualElement element, ConfettiPreset preset = ConfettiPreset.Burst);

    /// <summary>Fires a custom burst from the centre of <paramref name="element"/>. Its origin is replaced.</summary>
    Task FireFromAsync(VisualElement element, ConfettiOptions options);

    /// <summary>Removes everything in the air, and anything a preset still had queued, from the page that is showing.</summary>
    void Clear();
}


public class ConfettiService : IConfettiService
{
    public Task FireAsync(ConfettiOptions? options = null)
        => ConfettiHost.FireAsync(null, [new ConfettiShot(TimeSpan.Zero, options?.Clone() ?? new ConfettiOptions())]);

    public Task FireAsync(ConfettiPreset preset, Point? origin = null)
        => ConfettiHost.FireAsync(null, ConfettiHost.Shots(preset, null, origin));

    public Task FireFromAsync(VisualElement element, ConfettiPreset preset = ConfettiPreset.Burst)
    {
        ArgumentNullException.ThrowIfNull(element);
        return ConfettiHost.FireAsync(element, ConfettiHost.Shots(preset, null, ConfettiHost.NormalizedCenter(element)));
    }

    public Task FireFromAsync(VisualElement element, ConfettiOptions options)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(options);
        return ConfettiHost.FireAsync(element, ConfettiHost.Shots(ConfettiPreset.Burst, options, ConfettiHost.NormalizedCenter(element)));
    }

    public void Clear() => ConfettiHost.Clear();
}
