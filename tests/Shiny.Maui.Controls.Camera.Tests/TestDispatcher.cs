using Microsoft.Maui.Dispatching;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// Enough of a dispatcher to construct controls that queue work onto one. Everything dispatched runs
/// inline; timers are created but never tick, so a test needing one to fire drives it rather than racing
/// the clock.
/// </summary>
sealed class TestDispatcher : IDispatcher
{
    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    /// <summary>Every timer this dispatcher has handed out, newest last.</summary>
    public List<ControllableTimer> Timers { get; } = [];

    public IDispatcherTimer CreateTimer()
    {
        var timer = new ControllableTimer();
        this.Timers.Add(timer);
        return timer;
    }

    public sealed class ControllableTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }

        public event EventHandler? Tick;

        public void Start() => this.IsRunning = true;

        public void Stop() => this.IsRunning = false;

        /// <summary>Fires the timer as the real dispatcher would once <see cref="Interval"/> elapsed.</summary>
        public void Fire()
        {
            if (!this.IsRepeating)
                this.IsRunning = false;

            this.Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}


sealed class TestDispatcherProvider : IDispatcherProvider
{
    /// <summary>The single dispatcher every test shares, so a test can reach the timers it created.</summary>
    public static readonly TestDispatcher Instance = new();

    public IDispatcher? GetForCurrentThread() => Instance;

    /// <summary>Idempotent — xUnit runs the classes that need this in the same process.</summary>
    public static void Install() => DispatcherProvider.SetCurrent(new TestDispatcherProvider());
}
