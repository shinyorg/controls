using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Cover for the two things a stateful button gets wrong most easily: a state machine where the busy
/// flag and the state enum fight each other, and a <see cref="ICommand"/> integration that stomps a
/// consumer's own <see cref="VisualElement.IsEnabled"/>.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyButtonTests
{
    public ShinyButtonTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();

        // A fresh Application per test, not `Application.Current ?? new`. Application.Current is
        // process-wide, and the implicit style ConstructingBusy_DoesNotStartTheIndicator installs
        // would otherwise leak into every test that ran after it in this collection.
        _ = new Application();
    }

    static (Border Border, Grid Root, Grid Inner) Parts(ShinyButton button)
    {
        var border = (Border)button.Content!;
        var root = (Grid)border.Content!;
        var inner = root.Children.OfType<Grid>().First();
        return (border, root, inner);
    }

    static ContentView LeftHost(ShinyButton button)
    {
        var (_, _, inner) = Parts(button);
        return inner.Children.OfType<ContentView>().First();
    }


    /// <summary>
    /// Merges the Basic light pack into the current Application directly, rather than going through
    /// ShinyThemeManager. The manager caches the applied dictionary in a static and short-circuits on
    /// ReferenceEquals, so with a fresh Application per test it would skip the merge depending on test
    /// order - and no theme is selected in a headless host anyway.
    /// </summary>
    static void MergeTheme()
        => Application.Current!.Resources.MergedDictionaries.Add(new Themes.BasicLightTheme());

    static TestDispatcher.ControllableTimer LastTimer()
        => TestDispatcherProvider.Instance.Timers[^1];

    /// <summary>
    /// Completes with the state the button lands on after Busy. The button's own unwind runs as a
    /// continuation on the command's task, so a test that awaited that task directly would be racing
    /// its own assertion - watching StateChanged is the deterministic signal.
    /// </summary>
    static Task<ButtonState> Settled(ShinyButton button)
    {
        var tcs = new TaskCompletionSource<ButtonState>(TaskCreationOptions.RunContinuationsAsynchronously);
        button.StateChanged += (_, e) =>
        {
            if (e.To is not ButtonState.Busy)
                tcs.TrySetResult(e.To);
        };
        return tcs.Task;
    }


    // ---------------------------------------------------------------------------------------------
    // State machine
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void IsBusy_ProjectsOntoState()
    {
        var button = new ShinyButton { Text = "Save" };

        button.IsBusy = true;
        button.State.ShouldBe(ButtonState.Busy);

        button.IsBusy = false;
        button.State.ShouldBe(ButtonState.Normal);
    }

    [Fact]
    public void State_ProjectsOntoIsBusy()
    {
        var button = new ShinyButton { Text = "Save" };

        button.State = ButtonState.Busy;
        button.IsBusy.ShouldBeTrue();

        button.State = ButtonState.Success;
        button.IsBusy.ShouldBeFalse();
    }

    /// <summary>
    /// The case the projection exists to survive: a view model clears its busy flag in a finally
    /// block *after* setting Success, and the button must not throw the outcome away.
    /// </summary>
    [Fact]
    public void ClearingIsBusy_DoesNotCutSuccessShort()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };

        button.IsBusy = true;
        button.State = ButtonState.Success;
        button.IsBusy = false;

        button.State.ShouldBe(ButtonState.Success);
    }

    [Fact]
    public void ClearingIsBusy_DoesNotCutErrorShort()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };

        button.IsBusy = true;
        button.State = ButtonState.Error;
        button.IsBusy = false;

        button.State.ShouldBe(ButtonState.Error);
    }

    [Fact]
    public void StateChanged_ReportsBothEnds()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };
        var seen = new List<ButtonStateChangedEventArgs>();
        button.StateChanged += (_, e) => seen.Add(e);

        button.State = ButtonState.Busy;
        button.State = ButtonState.Success;

        seen.Count.ShouldBe(2);
        seen[0].From.ShouldBe(ButtonState.Normal);
        seen[0].To.ShouldBe(ButtonState.Busy);
        seen[1].From.ShouldBe(ButtonState.Busy);
        seen[1].To.ShouldBe(ButtonState.Success);
    }


    // ---------------------------------------------------------------------------------------------
    // Auto-revert
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Success_RevertsToNormalWhenTheTimerFires()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.FromSeconds(2) };

        button.State = ButtonState.Success;

        var timer = LastTimer();
        timer.Interval.ShouldBe(TimeSpan.FromSeconds(2));
        timer.IsRepeating.ShouldBeFalse();
        timer.IsRunning.ShouldBeTrue();

        timer.Fire();
        button.State.ShouldBe(ButtonState.Normal);
    }

    [Fact]
    public void ZeroRevertDelay_HoldsTheStateForever()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };

        button.State = ButtonState.Error;

        TestDispatcherProvider.Instance.Timers.ShouldBeEmpty();
        button.State.ShouldBe(ButtonState.Error);
    }

    /// <summary>
    /// A stale timer firing after the state has already moved on would drag the button back to
    /// Normal from wherever it now is.
    /// </summary>
    [Fact]
    public void LeavingTheStateEarly_StopsTheRevertTimer()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.FromSeconds(2) };

        button.State = ButtonState.Success;
        var stale = LastTimer();

        button.State = ButtonState.Busy;
        stale.IsRunning.ShouldBeFalse();

        stale.Fire();
        button.State.ShouldBe(ButtonState.Busy);
    }


    // ---------------------------------------------------------------------------------------------
    // Command state
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CommandThatCannotExecute_DisablesTheButton()
    {
        var command = new TogglingCommand { CanRun = false };
        var button = new ShinyButton { Text = "Save", Command = command };

        button.IsEnabled.ShouldBeFalse();

        command.CanRun = true;
        button.IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// The reason the button drives <c>IsEnabledCore</c> rather than writing <c>IsEnabled</c>: a
    /// command becoming executable must not re-enable a button the consumer switched off.
    /// </summary>
    [Fact]
    public void ConsumerDisabled_StaysDisabledWhenTheCommandBecomesExecutable()
    {
        var command = new TogglingCommand { CanRun = false };
        var button = new ShinyButton { Text = "Save", Command = command, IsEnabled = false };

        command.CanRun = true;

        button.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void SwappingCommand_UnsubscribesTheOldOne()
    {
        var first = new TogglingCommand { CanRun = true };
        var second = new TogglingCommand { CanRun = true };
        var button = new ShinyButton { Text = "Save", Command = first };

        button.Command = second;

        // The old command going non-executable must no longer reach the button.
        first.CanRun = false;
        button.IsEnabled.ShouldBeTrue();

        second.CanRun = false;
        button.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void BusyDisablesTheButton_UnlessAskedNotTo()
    {
        var button = new ShinyButton { Text = "Save" };

        button.State = ButtonState.Busy;
        button.IsEnabled.ShouldBeFalse();

        button.DisableWhileBusy = false;
        button.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void CommandParameterChange_ReevaluatesCanExecute()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            // Executable only for a non-null parameter.
            Command = new ParameterCommand()
        };

        button.IsEnabled.ShouldBeFalse();

        button.CommandParameter = "something";
        button.IsEnabled.ShouldBeTrue();
    }


    // ---------------------------------------------------------------------------------------------
    // AutoBusy
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AsyncCommand_DrivesBusyForItsDuration()
    {
        var gate = new TaskCompletionSource();
        var command = new FakeAsyncCommand(() => gate.Task);
        var button = new ShinyButton { Text = "Save", Command = command, StateRevertDelay = TimeSpan.Zero };
        var settled = Settled(button);

        button.Invoke();
        button.State.ShouldBe(ButtonState.Busy);

        gate.SetResult();

        (await settled).ShouldBe(ButtonState.Normal);
    }

    [Fact]
    public async Task FaultedAsyncCommand_EndsInError()
    {
        var command = new FakeAsyncCommand(() => Task.FromException(new InvalidOperationException("nope")));
        var button = new ShinyButton { Text = "Save", Command = command, StateRevertDelay = TimeSpan.Zero };
        var settled = Settled(button);

        button.Invoke();

        (await settled).ShouldBe(ButtonState.Error);
    }

    [Fact]
    public async Task ShowErrorOnFaultOff_EndsInNormal()
    {
        var command = new FakeAsyncCommand(() => Task.FromException(new InvalidOperationException("nope")));
        var button = new ShinyButton
        {
            Text = "Save",
            Command = command,
            ShowErrorOnFault = false,
            StateRevertDelay = TimeSpan.Zero
        };
        var settled = Settled(button);

        button.Invoke();

        (await settled).ShouldBe(ButtonState.Normal);
    }

    /// <summary>
    /// A command that reports its own outcome has the last word - the button must not overwrite a
    /// Success it set for itself with Normal when the task completes.
    /// </summary>
    [Fact]
    public async Task CommandThatSetsItsOwnState_IsNotOverwritten()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };
        var settled = Settled(button);
        var command = new FakeAsyncCommand(async () =>
        {
            await Task.Yield();
            button.State = ButtonState.Success;
        });
        button.Command = command;

        button.Invoke();

        (await settled).ShouldBe(ButtonState.Success);
        await command.ExecutionTask!;
        button.State.ShouldBe(ButtonState.Success);
    }

    [Fact]
    public void AutoBusyOff_LeavesStateAlone()
    {
        var gate = new TaskCompletionSource();
        var command = new FakeAsyncCommand(() => gate.Task);
        var button = new ShinyButton { Text = "Save", Command = command, AutoBusy = false };

        button.Invoke();

        button.State.ShouldBe(ButtonState.Normal);
        gate.SetResult();
    }

    [Fact]
    public void PlainCommand_IsNotTreatedAsAsync()
    {
        var command = new TogglingCommand { CanRun = true };
        var button = new ShinyButton { Text = "Save", Command = command };

        button.Invoke();

        command.Executions.ShouldBe(1);
        button.State.ShouldBe(ButtonState.Normal);
    }


    // ---------------------------------------------------------------------------------------------
    // Busy rendering
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ReplaceLeftIcon_RestoresTheOriginalIconOnExit()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            LeftIcon = "save.png",
            BusyMode = ButtonBusyMode.ReplaceLeftIcon,
            StateRevertDelay = TimeSpan.Zero
        };

        var host = LeftHost(button);
        var original = host.Content;
        original.ShouldBeOfType<Image>();

        button.State = ButtonState.Busy;
        host.Content.ShouldNotBe(original);

        button.State = ButtonState.Normal;
        host.Content.ShouldBe(original);
    }

    [Fact]
    public void ReplaceContent_HidesTheContentWithoutRemovingIt()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            BusyMode = ButtonBusyMode.ReplaceContent,
            StateRevertDelay = TimeSpan.Zero
        };

        var (_, _, inner) = Parts(button);

        button.State = ButtonState.Busy;
        // Opacity, not IsVisible: the content has to keep its layout space so the button holds the
        // width it had.
        inner.Opacity.ShouldBe(0);
        inner.IsVisible.ShouldBeTrue();

        button.State = ButtonState.Normal;
        inner.Opacity.ShouldBe(1);
    }

    [Fact]
    public void BusyText_StandsInForTextAndReverts()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            BusyText = "Saving...",
            StateRevertDelay = TimeSpan.Zero
        };

        var label = Parts(button).Inner.Children.OfType<Label>().Single();
        label.Text.ShouldBe("Save");

        button.State = ButtonState.Busy;
        label.Text.ShouldBe("Saving...");

        button.State = ButtonState.Normal;
        label.Text.ShouldBe("Save");
    }

    /// <summary>
    /// MAUI applies an implicit Style from its own base constructor, so a style setting State="Busy"
    /// lands before the button is parented. Starting a motion-icon ticker from there animates
    /// something nobody can see on a view with no window - and in a headless host it deadlocks
    /// outright. Playback waits for Loaded.
    /// </summary>
    [Fact]
    public void ConstructingBusy_DoesNotStartTheIndicator()
    {
        var style = new Style(typeof(ShinyButton))
        {
            Setters =
            {
                new Setter { Property = ShinyButton.StateProperty, Value = ButtonState.Busy },
                new Setter { Property = ShinyButton.BusyMotionIconProperty, Value = "loader" }
            }
        };

        var app = new Application();
        app.Resources.Add(style);

        // The assertion is that this returns at all - the indicator must be built without being told
        // to play.
        var button = new ShinyButton { Text = "Save" };

        button.State.ShouldBe(ButtonState.Busy);
        MotionIconIn(LeftHost(button)).IsPlaying.ShouldBeFalse();
    }

    static MotionIconView MotionIconIn(ContentView host)
        => (MotionIconView)host.Content!;

    // ---------------------------------------------------------------------------------------------
    // Review regressions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The state text used to be written straight into SemanticProperties.Description, which blanked
    /// it whenever there was no text - so an icon-only button lost the description the docs tell you
    /// to set, on its first state change.
    /// </summary>
    [Fact]
    public void StateChanges_DoNotWipeAConsumerSuppliedDescription()
    {
        var button = new ShinyButton { LeftIcon = "trash.png", StateRevertDelay = TimeSpan.Zero };
        SemanticProperties.SetDescription(button, "Delete item");

        button.State = ButtonState.Busy;
        button.State = ButtonState.Normal;

        SemanticProperties.GetDescription(button).ShouldBe("Delete item");
    }

    [Fact]
    public void TextDrivesTheDescriptionWhenTheConsumerHasNotSetOne()
    {
        var button = new ShinyButton { Text = "Save", BusyText = "Saving...", StateRevertDelay = TimeSpan.Zero };

        SemanticProperties.GetDescription(button).ShouldBe("Save");

        button.State = ButtonState.Busy;
        SemanticProperties.GetDescription(button).ShouldBe("Saving...");
    }

    /// <summary>
    /// A bound description used to lose to the text when the text arrived first - the order a
    /// DataTemplate applies them in when the text binding comes first. The text went in as a local
    /// value, which outranks the binding, so the bound description never showed.
    /// </summary>
    [Fact]
    public void ABoundDescription_IsNotHiddenByTextThatArrivesFirst()
    {
        var button = new ShinyButton();
        button.SetBinding(ShinyButton.TextProperty, "Glyph");
        button.SetBinding(SemanticProperties.DescriptionProperty, "Name");

        button.BindingContext = new { Glyph = "☆", Name = "Add to favourites" };

        SemanticProperties.GetDescription(button).ShouldBe("Add to favourites");
    }

    [Fact]
    public void ABoundDescription_IsNotHiddenByALiteralText()
    {
        var button = new ShinyButton();
        button.SetBinding(SemanticProperties.DescriptionProperty, "Name");
        button.Text = "☆";

        button.BindingContext = new { Name = "Add to favourites" };

        SemanticProperties.GetDescription(button).ShouldBe("Add to favourites");
    }

    /// <summary>
    /// The state glyphs are cached to keep their animation intact across transitions, so changing
    /// which glyph to use has to drop the cache or the button keeps reporting the first outcome.
    /// </summary>
    [Fact]
    public void ChangingTheErrorIcon_RebuildsTheGlyph()
    {
        var button = new ShinyButton { Text = "Save", StateRevertDelay = TimeSpan.Zero };
        var host = LeftHost(button);

        button.State = ButtonState.Error;
        var first = host.Content;
        first.ShouldNotBeNull();

        button.State = ButtonState.Normal;
        button.ErrorMotionIcon = "wifi";
        button.State = ButtonState.Error;

        host.Content.ShouldNotBe(first);
    }

    /// <summary>
    /// SuccessMotionIcon defaults to a name, so without the IsSet check an explicit SuccessIcon image
    /// could never be reached.
    /// </summary>
    [Fact]
    public void ExplicitSuccessImage_BeatsTheDefaultMotionIcon()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            SuccessIcon = "tick.png",
            StateRevertDelay = TimeSpan.Zero
        };

        button.State = ButtonState.Success;

        LeftHost(button).Content.ShouldBeOfType<Image>();
    }

    [Fact]
    public void ExplicitSuccessMotionIcon_BeatsAnImage()
    {
        var button = new ShinyButton
        {
            Text = "Save",
            SuccessIcon = "tick.png",
            SuccessMotionIcon = "check",
            StateRevertDelay = TimeSpan.Zero
        };

        button.State = ButtonState.Success;

        LeftHost(button).Content.ShouldNotBeOfType<Image>();
    }

    /// <summary>
    /// The stacked layouts used to column-span the label across the icons' three columns, which made
    /// MAUI's Grid distribute the label's width across them and pushed a single icon off centre.
    /// </summary>
    [Fact]
    public void StackedLayout_PutsTheIconsInOneCenteredCell()
    {
        var button = new ShinyButton { Text = "Upload", LeftIcon = "up.png", ContentLayout = ButtonContentLayout.Top };
        var (_, _, inner) = Parts(button);

        // One column, two rows - nothing to span, so nothing to distribute.
        inner.ColumnDefinitions.Count.ShouldBe(1);
        inner.RowDefinitions.Count.ShouldBe(2);

        var stack = inner.Children.OfType<HorizontalStackLayout>().Single();
        stack.HorizontalOptions.ShouldBe(LayoutOptions.Center);
        Grid.GetColumnSpan(Parts(button).Inner.Children.OfType<Label>().Single()).ShouldBe(1);
    }

    [Fact]
    public void SwitchingBackToSides_ReparentsTheIconHostsCleanly()
    {
        var button = new ShinyButton { Text = "Upload", LeftIcon = "up.png", ContentLayout = ButtonContentLayout.Top };

        button.ContentLayout = ButtonContentLayout.Sides;

        var (_, _, inner) = Parts(button);
        inner.ColumnDefinitions.Count.ShouldBe(4);
        // The hosts are back in the grid rather than orphaned in the icon stack.
        inner.Children.OfType<ContentView>().Count().ShouldBe(3);
        inner.Children.OfType<HorizontalStackLayout>().ShouldBeEmpty();
    }


    /// <summary>
    /// ApplyState runs on every Loaded, so a recycled list row went through it each time it scrolled
    /// back in - and every pass cleared the grid and added the same hosts again. A pass that changes
    /// nothing about the arrangement now leaves the grid alone.
    /// </summary>
    [Fact]
    public void ReapplyingTheSameState_LeavesTheGridAlone()
    {
        var button = new ShinyButton { Text = "Open", LeftIcon = "a.png" };
        var (_, _, inner) = Parts(button);
        var removed = 0;
        inner.ChildRemoved += (_, _) => removed++;

        // Each of these runs ApplyState without changing what is shown where.
        button.BusyMode = ButtonBusyMode.ReplaceContent;
        button.LeftIcon = "b.png";

        removed.ShouldBe(0);
        inner.Children.OfType<ContentView>().Count().ShouldBe(3);
        LeftHost(button).Content.ShouldNotBeNull();
    }

    [Fact]
    public void LosingTheText_StillRebuildsTheSpacing()
    {
        var button = new ShinyButton { Text = "Open", LeftIcon = "a.png" };
        var (_, _, inner) = Parts(button);
        inner.ColumnSpacing.ShouldBeGreaterThan(0);

        button.Text = null;

        inner.ColumnSpacing.ShouldBe(0);
    }


    /// <summary>
    /// Border.Stroke is a Brush and the theme tokens are Colors, so SetDynamicResource on
    /// StrokeProperty silently dropped the resource - an Outlined button had a stroke thickness and a
    /// null stroke, i.e. no visible border at all. The token is now bound on a SolidColorBrush.
    /// </summary>
    [Fact]
    public void OutlinedAppearance_ActuallyGetsAStroke()
    {
        MergeTheme();

        var button = new ShinyButton
        {
            Text = "Delete",
            Appearance = ButtonAppearance.Outlined,
            Type = ButtonType.Critical
        };
        var (border, _, _) = Parts(button);

        border.StrokeThickness.ShouldBe(1);

        // Not merely non-null: the token has to have actually resolved to the pack's Outline colour.
        // Two earlier attempts produced a brush that was silently transparent - DynamicResource on
        // Border.Stroke (Color into a Brush property) and then on a bare SolidColorBrush (no resource
        // chain). Asserting the concrete colour is what catches both.
        var brush = border.Stroke.ShouldBeOfType<SolidColorBrush>();
        var outline = (Color)Application.Current!.Resources[Themes.ShinyThemeKeys.Color.Outline];
        brush.Color.ShouldBe(outline);
    }

    [Fact]
    public void ExplicitBorderColor_WinsOverTheToken()
    {
        MergeTheme();

        var button = new ShinyButton
        {
            Text = "Brand",
            Appearance = ButtonAppearance.Outlined,
            BorderColor = Colors.Fuchsia
        };
        var (border, _, _) = Parts(button);

        border.Stroke.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Colors.Fuchsia);
    }

    [Fact]
    public void FilledAppearance_HasNoStroke()
    {
        MergeTheme();

        var button = new ShinyButton { Text = "Save", Appearance = ButtonAppearance.Filled };
        var (border, _, _) = Parts(button);

        border.StrokeThickness.ShouldBe(0);
        border.Stroke.ShouldBeNull();
    }


    /// <summary>
    /// The tap recognizer has to be on the button, not on its inner Border: a recognizer on a child is
    /// invisible to UI automation, so resolving the control by AutomationId and tapping it silently
    /// does nothing. Caught by DevFlow reporting success:false against a real simulator.
    /// </summary>
    [Fact]
    public void TapRecognizerIsOnTheButtonItself()
    {
        var button = new ShinyButton { Text = "Save" };

        button.GestureRecognizers.OfType<TapGestureRecognizer>().ShouldHaveSingleItem();

        var (border, _, _) = Parts(button);
        border.GestureRecognizers.ShouldBeEmpty();
    }

    [Fact]
    public void NoIcons_ReservesNoSpacing()
    {
        var button = new ShinyButton { Text = "Save" };

        Parts(button).Inner.ColumnSpacing.ShouldBe(0);
    }

    [Fact]
    public void IconAndText_SpacesTheTwo()
    {
        var button = new ShinyButton { Text = "Save", LeftIcon = "save.png", IconSpacing = 12 };

        Parts(button).Inner.ColumnSpacing.ShouldBe(12);
    }


    // ---------------------------------------------------------------------------------------------
    // AsyncCommandProbe
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Probe_FindsTheAsyncShape()
    {
        var command = new FakeAsyncCommand(() => Task.CompletedTask);

        AsyncCommandProbe.IsAsync(command).ShouldBeTrue();
        AsyncCommandProbe.IsRunning(command).ShouldBe(false);
    }

    [Fact]
    public void Probe_IgnoresAPlainCommand()
    {
        AsyncCommandProbe.IsAsync(new TogglingCommand()).ShouldBeFalse();
        AsyncCommandProbe.GetExecutionTask(new TogglingCommand()).ShouldBeNull();
        AsyncCommandProbe.IsRunning(new TogglingCommand()).ShouldBeNull();
    }

    [Fact]
    public void Probe_HandlesNull()
    {
        AsyncCommandProbe.IsAsync(null).ShouldBeFalse();
        AsyncCommandProbe.GetExecutionTask(null).ShouldBeNull();
        AsyncCommandProbe.IsRunning(null).ShouldBeNull();
    }


    // ---------------------------------------------------------------------------------------------
    // Doubles
    // ---------------------------------------------------------------------------------------------

    sealed class TogglingCommand : ICommand
    {
        bool canRun;

        public int Executions { get; private set; }

        public bool CanRun
        {
            get => this.canRun;
            set
            {
                this.canRun = value;
                this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => this.canRun;

        public void Execute(object? parameter) => this.Executions++;
    }

    /// <summary>Executable only when it is given a parameter.</summary>
    sealed class ParameterCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => parameter is not null;

        public void Execute(object? parameter)
        {
            _ = this.CanExecuteChanged;
        }
    }

    /// <summary>
    /// The shape of an async command - <c>ExecutionTask</c> plus <c>IsRunning</c> - without taking a
    /// dependency on an MVVM framework to get it. This is exactly what the probe looks for.
    /// </summary>
    sealed class FakeAsyncCommand(Func<Task> body) : ICommand
    {
        public Task? ExecutionTask { get; private set; }

        public bool IsRunning => this.ExecutionTask is { IsCompleted: false };

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _ = this.CanExecuteChanged;
            this.ExecutionTask = body();
        }
    }
}
