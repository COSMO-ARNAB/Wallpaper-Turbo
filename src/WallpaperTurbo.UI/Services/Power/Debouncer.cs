using System;
using System.Threading;

namespace WallpaperTurbo.UI.Services.Power;

/// <summary>
/// Trailing-edge debouncer: every <see cref="Schedule"/> call restarts the window, and the
/// action runs once the window elapses with no further calls.
/// </summary>
/// <remarks>
/// This <b>defers</b> notifications, it does not drop them. The distinction matters for power
/// events: <c>PowerModeChanged(StatusChange)</c> arrives in bursts and
/// <c>SystemInformation.PowerStatus.PowerLineStatus</c> can still report the previous value on the
/// first event of a burst. A "skip if the last run was recent" filter would latch that stale
/// reading and never revisit it; coalescing to the trailing edge reads the settled value exactly
/// once.
/// <para>
/// Built on <see cref="TimeProvider"/> so tests can drive it with a fake clock instead of sleeping.
/// </para>
/// </remarks>
internal sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _action;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public Debouncer(TimeSpan delay, Action action, TimeProvider timeProvider)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Debounce delay cannot be negative.");
        }

        _delay = delay;
        _action = action ?? throw new ArgumentNullException(nameof(action));
        ArgumentNullException.ThrowIfNull(timeProvider);

        // Created idle; Schedule() arms it.
        _timer = timeProvider.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Restarts the debounce window. Safe to call from any thread.</summary>
    public void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        // Invoked outside the lock: the action does real work (process probes, IPC) and must
        // never be able to deadlock against Schedule/Dispose.
        _action();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }
}
