namespace Shiny.Controls.Gamepad.Tests;


/// <summary>
/// A clock the test moves by hand. Timers fire synchronously inside <see cref="Advance"/>, so a turbo
/// repeat happens exactly when the test says and never races the assertion after it.
/// </summary>
sealed class ManualTimeProvider : TimeProvider
{
    readonly List<ManualTimer> timers = [];
    long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => this.ticks;

    public int ActiveTimers => this.timers.Count(x => !x.Disposed);


    public void Advance(TimeSpan by)
    {
        var end = this.ticks + by.Ticks;
        while (true)
        {
            var next = this.timers.Where(x => !x.Disposed && x.Due <= end).OrderBy(x => x.Due).FirstOrDefault();
            if (next == null)
                break;

            this.ticks = next.Due;
            next.Due = next.Period > 0 ? next.Due + next.Period : long.MaxValue;
            next.Callback(next.State);
        }
        this.ticks = end;
    }


    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state)
        {
            Due = this.ticks + dueTime.Ticks,
            Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks
        };
        this.timers.Add(timer);
        return timer;
    }


    sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long Due { get; set; }
        public long Period { get; set; }
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() => this.Disposed = true;
        public ValueTask DisposeAsync()
        {
            this.Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
