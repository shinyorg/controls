namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Base for the trigger actions that control a <see cref="Keyframes"/> or <see cref="Choreography"/>
/// from XAML — in a <c>DataTrigger</c>'s or <c>EventTrigger</c>'s enter and exit actions.
/// </summary>
[ContentProperty(nameof(Animation))]
public abstract class AnimationTriggerAction : TriggerAction<VisualElement>
{
    /// <summary>The animation to control. Usually a <c>{StaticResource}</c>, so the begin and the stop
    /// actions name the same instance.</summary>
    public IAnimationDefinition? Animation { get; set; }

    /// <summary>Optional <c>x:Name</c> of the element to act on, instead of the one the trigger is on.</summary>
    public string? TargetName { get; set; }

    /// <inheritdoc />
    protected sealed override void Invoke(VisualElement sender)
    {
        var target = sender;
        if (!string.IsNullOrWhiteSpace(TargetName))
        {
            target = NameScope.Find(sender, TargetName)
                ?? throw new InvalidOperationException($"Could not find an element named '{TargetName}' for {GetType().Name}.");
        }

        Invoke(target, Animation);
    }

    /// <summary>Acts on the resolved target.</summary>
    protected abstract void Invoke(VisualElement target, IAnimationDefinition? animation);

    /// <summary>The players this action applies to: the named animation's, or every one on the element.</summary>
    protected static IEnumerable<Player> PlayersFor(VisualElement target, IAnimationDefinition? animation)
    {
        // An iterator rather than an array or list: on Windows, CsWinRT treats a returned collection as
        // something that may cross the WinRT ABI and demands unsafe marshalling code for it.
        if (animation is null)
        {
            foreach (var running in AnimationRuns.FindAll(target))
                yield return running;
        }
        else if (AnimationRuns.Find(target, animation) is { } player)
        {
            yield return player;
        }
    }
}

/// <summary>Starts an animation. Starting one already running restarts it from the current frame.</summary>
/// <example>
/// <code language="xml">
/// &lt;DataTrigger TargetType="Button" Binding="{Binding IsBusy}" Value="True"&gt;
///   &lt;DataTrigger.EnterActions&gt;&lt;kf:BeginAnimation Animation="{StaticResource Pulse}" /&gt;&lt;/DataTrigger.EnterActions&gt;
///   &lt;DataTrigger.ExitActions&gt;&lt;kf:StopAnimation Animation="{StaticResource Pulse}" /&gt;&lt;/DataTrigger.ExitActions&gt;
/// &lt;/DataTrigger&gt;
/// </code>
/// </example>
public sealed class BeginAnimation : AnimationTriggerAction
{
    /// <summary>What happens if the animation is already running on the element.</summary>
    public HandoffBehavior Handoff { get; set; } = HandoffBehavior.Replace;

    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        if (animation is null)
            throw new InvalidOperationException("BeginAnimation needs an Animation.");

        AnimationRuns.Begin(target, animation, Handoff);
    }
}

/// <summary>
/// Stops an animation — or, with no <see cref="AnimationTriggerAction.Animation"/>, every animation
/// started on the element.
/// </summary>
public sealed class StopAnimation : AnimationTriggerAction
{
    /// <summary>
    /// What happens to the animated values. <see cref="StopBehavior.Release"/>, the default, hands each
    /// property back to its binding, style or visual state; <see cref="StopBehavior.Hold"/> keeps the
    /// current frame on screen.
    /// </summary>
    public StopBehavior Behavior { get; set; } = StopBehavior.Release;

    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        if (animation is null)
            AnimationRuns.StopAll(target, Behavior);
        else
            AnimationRuns.Stop(target, animation, Behavior);
    }
}

/// <summary>Pauses an animation where it is.</summary>
public sealed class PauseAnimation : AnimationTriggerAction
{
    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        foreach (var player in PlayersFor(target, animation))
            player.Pause();
    }
}

/// <summary>Resumes a paused animation from where it stopped.</summary>
public sealed class ResumeAnimation : AnimationTriggerAction
{
    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        foreach (var player in PlayersFor(target, animation).Where(p => p.State == PlaybackState.Paused))
            player.Resume();
    }
}

/// <summary>
/// Jumps an animation to its end state. A looping animation has no end, so it is stopped and released
/// instead.
/// </summary>
public sealed class SkipAnimationToEnd : AnimationTriggerAction
{
    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        foreach (var player in PlayersFor(target, animation))
        {
            if (player.Duration == TimeSpan.MaxValue)
                player.Stop(StopBehavior.Release);
            else
                player.Finish();
        }
    }
}

/// <summary>Moves an animation to a point, 0 to 1 across its whole length. Seeking is direct
/// manipulation, so reduced motion never affects it.</summary>
public sealed class SeekAnimation : AnimationTriggerAction
{
    /// <summary>Where to move to, 0 to 1.</summary>
    public double Progress { get; set; }

    /// <inheritdoc />
    protected override void Invoke(VisualElement target, IAnimationDefinition? animation)
    {
        foreach (var player in PlayersFor(target, animation))
            player.SeekProgress(Progress);
    }
}
