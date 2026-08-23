using System;
using Microsoft.Extensions.Time.Testing;
using WallpaperTurbo.UI.Services.Power;
using Xunit;

namespace WallpaperTurbo.Tests;

public class DebouncerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void Schedule_DoesNotFire_BeforeTheWindowElapses()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        using var debouncer = new Debouncer(Window, () => fired++, time);

        debouncer.Schedule();

        time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, fired);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, fired);
    }

    /// <summary>
    /// The core property: a second notification inside the window <b>defers</b> the run rather than
    /// being dropped. PowerLineStatus can still report the stale value on the first event of a
    /// burst, so the settled value must be read on the trailing edge.
    /// </summary>
    [Fact]
    public void Schedule_RestartsTheWindow_InsteadOfDroppingTheNotification()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        using var debouncer = new Debouncer(Window, () => fired++, time);

        debouncer.Schedule();
        time.Advance(TimeSpan.FromMilliseconds(300));

        debouncer.Schedule(); // restarts the 500ms window
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, fired); // 600ms of wall clock, but only 300ms since the last notification

        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, fired); // ...and the deferred notification is honoured, not lost
    }

    [Fact]
    public void Schedule_CoalescesABurst_IntoASingleRun()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        using var debouncer = new Debouncer(Window, () => fired++, time);

        for (int i = 0; i < 5; i++)
        {
            debouncer.Schedule();
        }

        time.Advance(Window);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Schedule_AfterAFire_ArmsTheNextRun()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        using var debouncer = new Debouncer(Window, () => fired++, time);

        debouncer.Schedule();
        time.Advance(Window);
        Assert.Equal(1, fired);

        debouncer.Schedule();
        time.Advance(Window);
        Assert.Equal(2, fired);
    }

    [Fact]
    public void Dispose_PreventsAPendingRun()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        var debouncer = new Debouncer(Window, () => fired++, time);

        debouncer.Schedule();
        debouncer.Dispose();

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Schedule_AfterDispose_IsANoOp()
    {
        var time = new FakeTimeProvider();
        int fired = 0;
        var debouncer = new Debouncer(Window, () => fired++, time);
        debouncer.Dispose();

        debouncer.Schedule();
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var time = new FakeTimeProvider();
        var debouncer = new Debouncer(Window, () => { }, time);

        debouncer.Dispose();
        debouncer.Dispose();
    }

    [Fact]
    public void Constructor_RejectsInvalidArguments()
    {
        var time = new FakeTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => new Debouncer(TimeSpan.FromMilliseconds(-1), () => { }, time));
        Assert.Throws<ArgumentNullException>(() => new Debouncer(Window, null!, time));
        Assert.Throws<ArgumentNullException>(() => new Debouncer(Window, () => { }, null!));
    }
}
