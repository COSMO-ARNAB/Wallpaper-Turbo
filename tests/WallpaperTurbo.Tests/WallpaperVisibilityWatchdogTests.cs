using WallpaperTurbo.Core.Services.Watchdog;

namespace WallpaperTurbo.Tests;

public class WallpaperVisibilityWatchdogTests
{
    // ── Pure decision logic (IsWallpaperVisible) ─────────────────────────────

    private static RenderWindowCandidate Candidate(
        bool visible = true,
        string? parentClass = "WorkerW",
        double coverage = 1.0)
    {
        var monitor = new WindowRectInfo(0, 0, 100, 100);
        var windowRect = new WindowRectInfo(0, 0, (int)(100 * coverage), 100);
        return new RenderWindowCandidate(
            Hwnd: new IntPtr(0x1234),
            IsVisible: visible,
            ParentClassName: parentClass,
            WindowRect: windowRect,
            MonitorRects: new List<WindowRectInfo> { monitor });
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsFalse_WhenNoRenderWindows()
    {
        bool result = WallpaperVisibilityWatchdog.IsOnScreen(new List<RenderWindowCandidate>());
        Assert.False(result);
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsFalse_WhenWindowNotVisible()
    {
        var candidates = new List<RenderWindowCandidate> { Candidate(visible: false) };
        Assert.False(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsFalse_WhenNotAttachedToDesktop()
    {
        var candidates = new List<RenderWindowCandidate> { Candidate(parentClass: "SomeAppWindow") };
        Assert.False(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsFalse_WhenCoverageBelowThreshold()
    {
        var candidates = new List<RenderWindowCandidate> { Candidate(coverage: 0.5) };
        Assert.False(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsTrue_WhenVisibleAttachedAndCoveringMonitor()
    {
        var candidates = new List<RenderWindowCandidate> { Candidate() };
        Assert.True(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsTrue_WhenParentIsProgman()
    {
        var candidates = new List<RenderWindowCandidate> { Candidate(parentClass: "Progman") };
        Assert.True(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    [Fact]
    public void IsWallpaperVisible_ReturnsTrue_WhenAnyCandidatePasses()
    {
        var candidates = new List<RenderWindowCandidate>
        {
            Candidate(parentClass: "SomeAppWindow"),
            Candidate()
        };
        Assert.True(WallpaperVisibilityWatchdog.IsOnScreen(candidates));
    }

    // ── State machine (poll loop) ────────────────────────────────────────────

    private sealed class ScriptedEnumerationSource : IWindowEnumerationSource
    {
        private readonly Queue<Func<IReadOnlyList<RenderWindowCandidate>>> _states = new();

        public ScriptedEnumerationSource(params bool[] visibleStates)
        {
            foreach (bool visible in visibleStates)
            {
                Enqueue(visible);
            }
        }

        public void Enqueue(bool visible)
        {
            _states.Enqueue(() => visible
                ? new List<RenderWindowCandidate> { Candidate() }
                : new List<RenderWindowCandidate>());
        }

        public IReadOnlyList<RenderWindowCandidate> GetCandidates()
            => _states.Count > 0 ? _states.Dequeue()() : new List<RenderWindowCandidate>();
    }

    private sealed class WatchdogFixture : IDisposable
    {
        public ScriptedEnumerationSource Source { get; } = new();
        public WallpaperVisibilityWatchdog Watchdog { get; }

        public int WallpaperLostRaises { get; set; }
        public List<bool> VisibilityChanges { get; } = new();

        public WatchdogFixture()
        {
            Watchdog = new WallpaperVisibilityWatchdog(Source, pollIntervalMs: 50);
            Watchdog.WallpaperLost += (_, _) => WallpaperLostRaises++;
            Watchdog.VisibilityChanged += (_, visible) => VisibilityChanges.Add(visible);
        }

        public void Dispose() => Watchdog.Stop();
    }

    [Fact]
    public void WallpaperLost_NotRaised_WhenEngineNotExpected()
    {
        using var fixture = new WatchdogFixture();
        fixture.Source.Enqueue(true);
        fixture.Source.Enqueue(false);

        fixture.Watchdog.SetEngineExpected(false);
        fixture.Watchdog.PollOnce();
        fixture.Watchdog.PollOnce();

        Assert.Equal(0, fixture.WallpaperLostRaises);
    }

    [Fact]
    public void WallpaperLost_RaisedOnce_PerLostTransition()
    {
        using var fixture = new WatchdogFixture();
        // A single missed poll must NOT raise (debounce). It takes LostPollThreshold
        // consecutive misses to declare the wallpaper lost. Below we use 3 falses per
        // lost segment, matching the threshold.
        fixture.Source.Enqueue(true);   // poll 1: visible
        fixture.Source.Enqueue(true);   // poll 2: still visible
        fixture.Source.Enqueue(false);  // poll 3: miss 1
        fixture.Source.Enqueue(false);  // poll 4: miss 2
        fixture.Source.Enqueue(false);  // poll 5: miss 3 -> LOST raise (1)
        fixture.Source.Enqueue(true);   // poll 6: visible again (reset)
        fixture.Source.Enqueue(false);  // poll 7: miss 1
        fixture.Source.Enqueue(false);  // poll 8: miss 2
        fixture.Source.Enqueue(false);  // poll 9: miss 3 -> LOST raise (2)

        fixture.Watchdog.SetEngineExpected(true);
        for (int i = 0; i < 9; i++)
        {
            fixture.Watchdog.PollOnce();
        }

        Assert.Equal(2, fixture.WallpaperLostRaises);
    }

    [Fact]
    public void WallpaperLost_NotRaised_OnTransientSingleMiss()
    {
        using var fixture = new WatchdogFixture();
        // A momentary disappearance (e.g. AppRunner restart) lasting fewer than the
        // debounce threshold must NOT trigger recovery.
        fixture.Source.Enqueue(true);   // poll 1: visible
        fixture.Source.Enqueue(false);  // poll 2: single miss (below threshold)
        fixture.Source.Enqueue(true);   // poll 3: visible again

        fixture.Watchdog.SetEngineExpected(true);
        fixture.Watchdog.PollOnce();
        fixture.Watchdog.PollOnce();
        fixture.Watchdog.PollOnce();

        Assert.Equal(0, fixture.WallpaperLostRaises);
    }

    [Fact]
    public void WallpaperLost_NotRaised_BeforeWallpaperEverVisible()
    {
        using var fixture = new WatchdogFixture();
        // The engine is expected but the render window has not appeared yet (AppRunner
        // spends several seconds recreating the WorkerW shell at startup). "Lost" must
        // NOT fire until the wallpaper has actually been observed on screen once,
        // otherwise the watchdog relaunches the engine mid-startup and crashes it.
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);

        fixture.Watchdog.SetEngineExpected(true);
        for (int i = 0; i < 4; i++)
        {
            fixture.Watchdog.PollOnce();
        }

        Assert.Equal(0, fixture.WallpaperLostRaises);
    }

    [Fact]
    public void VisibilityChanged_Raised_OnEachTransition()
    {
        using var fixture = new WatchdogFixture();
        fixture.Source.Enqueue(true);   // poll 1: visible (transition false->true)
        fixture.Source.Enqueue(true);   // poll 2: no transition
        fixture.Source.Enqueue(false);  // poll 3: hidden (transition true->false)

        fixture.Watchdog.PollOnce();
        fixture.Watchdog.PollOnce();
        fixture.Watchdog.PollOnce();

        Assert.Equal(new List<bool> { true, false }, fixture.VisibilityChanges);
    }

    // ── WaitForVisibleAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task WaitForVisibleAsync_ReturnsTrue_WhenWallpaperBecomesVisible()
    {
        using var fixture = new WatchdogFixture();
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(true);
        fixture.Source.Enqueue(true);

        bool result = await fixture.Watchdog.WaitForVisibleAsync(TimeSpan.FromSeconds(5));

        Assert.True(result);
    }

    [Fact]
    public async Task WaitForVisibleAsync_ReturnsFalse_OnTimeout()
    {
        using var fixture = new WatchdogFixture();
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);
        fixture.Source.Enqueue(false);

        bool result = await fixture.Watchdog.WaitForVisibleAsync(TimeSpan.FromMilliseconds(300));

        Assert.False(result);
    }
}
