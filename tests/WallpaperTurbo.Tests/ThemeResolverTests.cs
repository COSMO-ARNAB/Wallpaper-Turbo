using WallpaperTurbo.UI.Services;
using WallpaperTurbo.UI.Services.Theme;
using Xunit;

namespace WallpaperTurbo.Tests;

public class ThemeResolverTests
{
    private readonly ThemeResolver _sut = new();
    private readonly IThemeResolver _sutViaInterface = new ThemeResolver();

    // -----------------------------------------------------------------
    // Mica -> Tabbed (4), Glass -> Transient (3), None -> None (1),
    // Tabbed -> Tabbed, unknown -> Tabbed fallback (when visible)
    // -----------------------------------------------------------------
    [Theory]
    [InlineData("Mica", WindowBackdropMode.Tabbed)]
    [InlineData("Glass", WindowBackdropMode.Transient)]
    [InlineData("None", WindowBackdropMode.None)]
    [InlineData("Tabbed", WindowBackdropMode.Tabbed)]
    [InlineData("unknown", WindowBackdropMode.Tabbed)]
    [InlineData("RandomValue", WindowBackdropMode.Tabbed)]
    [InlineData("", WindowBackdropMode.Tabbed)]
    [InlineData(" ", WindowBackdropMode.Tabbed)]
    public void Resolve_Visible_Maps_BackdropPreference_To_Expected_Mode(string preference, WindowBackdropMode expected)
    {
        var inputs = new ThemeInputs(IsActive: true, IsPlaying: true, BackdropPreference: preference, GlassOpacity: 0.40);

        ThemeResult result = _sut.Resolve(inputs);

        Assert.Equal(expected, result.BackdropMode);
        // Also validate numeric DWM values where applicable
        if (expected == WindowBackdropMode.Tabbed) Assert.Equal(4, (int)result.BackdropMode);
        if (expected == WindowBackdropMode.Transient) Assert.Equal(3, (int)result.BackdropMode);
        if (expected == WindowBackdropMode.None) Assert.Equal(1, (int)result.BackdropMode);
        // When visible, material must be Glass and visible true
        Assert.True(result.IsWallpaperVisible);
        Assert.Equal(UIMaterialMode.Glass, result.MaterialMode);
    }

    // -----------------------------------------------------------------
    // Case insensitivity (mica/MICA) and null -> Mica default
    // -----------------------------------------------------------------
    [Theory]
    [InlineData("mica", WindowBackdropMode.Tabbed)]
    [InlineData("MICA", WindowBackdropMode.Tabbed)]
    [InlineData("MiCa", WindowBackdropMode.Tabbed)]
    [InlineData("mIcA", WindowBackdropMode.Tabbed)]
    [InlineData("glass", WindowBackdropMode.Transient)]
    [InlineData("GLASS", WindowBackdropMode.Transient)]
    [InlineData("GlAsS", WindowBackdropMode.Transient)]
    [InlineData("none", WindowBackdropMode.None)]
    [InlineData("NONE", WindowBackdropMode.None)]
    [InlineData("NoNe", WindowBackdropMode.None)]
    public void Resolve_Is_Case_Insensitive_For_Known_Preferences(string preference, WindowBackdropMode expected)
    {
        var inputs = new ThemeInputs(true, true, preference, 0.40);

        var result = _sut.Resolve(inputs);

        Assert.Equal(expected, result.BackdropMode);
        Assert.True(result.IsWallpaperVisible);
    }

    [Fact]
    public void Resolve_Null_BackdropPreference_Defaults_To_Mica_Tabbed()
    {
        var inputs = new ThemeInputs(IsActive: true, IsPlaying: true, BackdropPreference: null!, GlassOpacity: 0.40);

        var result = _sut.Resolve(inputs);

        Assert.Equal(WindowBackdropMode.Tabbed, result.BackdropMode);
        Assert.Equal(4, (int)result.BackdropMode);
        Assert.True(result.IsWallpaperVisible);
        Assert.Equal(UIMaterialMode.Glass, result.MaterialMode);
        Assert.Equal(0.40, result.OverlayOpacity);
    }

    [Fact]
    public void Resolve_Null_BackdropPreference_Via_Interface_Defaults_To_Tabbed()
    {
        var inputs = new ThemeInputs(true, true, null!, 0.40);

        var result = _sutViaInterface.Resolve(inputs);

        Assert.Equal(WindowBackdropMode.Tabbed, result.BackdropMode);
    }

    // -----------------------------------------------------------------
    // visible=false always -> Transient + 1.0 + Solid regardless of preference
    // -----------------------------------------------------------------
    [Theory]
    [InlineData("Mica")]
    [InlineData("Glass")]
    [InlineData("None")]
    [InlineData("Tabbed")]
    [InlineData("unknown")]
    [InlineData("MICA")]
    [InlineData(null)]
    public void Resolve_NotVisible_Always_Returns_Transient_Solid_And_Opacity_One(string? preference)
    {
        // Test all three non-visible combinations: (false,false), (false,true), (true,false)
        var combos = new[]
        {
            new ThemeInputs(false, false, preference!, 0.25),
            new ThemeInputs(false, true, preference!, 0.40),
            new ThemeInputs(true, false, preference!, 0.75),
        };

        foreach (var inputs in combos)
        {
            var result = _sut.Resolve(inputs);

            Assert.False(result.IsWallpaperVisible);
            Assert.Equal(WindowBackdropMode.Transient, result.BackdropMode);
            Assert.Equal(3, (int)result.BackdropMode);
            Assert.Equal(1.0, result.OverlayOpacity);
            Assert.Equal(UIMaterialMode.Solid, result.MaterialMode);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Resolve_NotVisible_Ignores_GlassOpacity_Always_One(bool isActive, bool isPlaying)
    {
        foreach (var opacity in new[] { 0.0, 0.25, 0.40, 0.75, 1.0 })
        {
            var inputs = new ThemeInputs(isActive, isPlaying, "Mica", opacity);
            var result = _sut.Resolve(inputs);
            Assert.Equal(1.0, result.OverlayOpacity);
        }
    }

    // -----------------------------------------------------------------
    // OverlayOpacity == GlassOpacity when visible else 1.0
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.40)]
    [InlineData(0.75)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.55)]
    public void Resolve_Visible_OverlayOpacity_Equals_GlassOpacity(double glassOpacity)
    {
        var inputs = new ThemeInputs(true, true, "Mica", glassOpacity);

        var result = _sut.Resolve(inputs);

        Assert.Equal(glassOpacity, result.OverlayOpacity);
        Assert.True(result.IsWallpaperVisible);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.40)]
    [InlineData(0.75)]
    public void Resolve_NotVisible_OverlayOpacity_Is_Always_One_Regardless_Of_GlassOpacity(double glassOpacity)
    {
        var inputs = new ThemeInputs(false, false, "Mica", glassOpacity);

        var result = _sut.Resolve(inputs);

        Assert.Equal(1.0, result.OverlayOpacity);
        Assert.NotEqual(glassOpacity, result.OverlayOpacity);
    }

    [Theory]
    [InlineData(0.25, "Mica")]
    [InlineData(0.40, "Glass")]
    [InlineData(0.75, "None")]
    [InlineData(0.33, "Tabbed")]
    public void Resolve_Visible_OverlayOpacity_Matches_GlassOpacity_For_Any_Preference(double opacity, string preference)
    {
        var inputs = new ThemeInputs(true, true, preference, opacity);

        var result = _sut.Resolve(inputs);

        Assert.Equal(opacity, result.OverlayOpacity);
    }

    // -----------------------------------------------------------------
    // IsWallpaperVisible truth table: IsActive && IsPlaying (4 combos)
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Resolve_IsWallpaperVisible_Truth_Table(bool isActive, bool isPlaying, bool expectedVisible)
    {
        var inputs = new ThemeInputs(isActive, isPlaying, "Mica", 0.40);

        var result = _sut.Resolve(inputs);

        Assert.Equal(expectedVisible, result.IsWallpaperVisible);
        // MaterialMode must correlate with visibility
        Assert.Equal(expectedVisible ? UIMaterialMode.Glass : UIMaterialMode.Solid, result.MaterialMode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Resolve_MaterialMode_Correlates_With_Visibility(bool isActive, bool isPlaying)
    {
        var inputs = new ThemeInputs(isActive, isPlaying, "Mica", 0.40);
        var result = _sut.Resolve(inputs);

        bool visible = isActive && isPlaying;
        Assert.Equal(visible, result.IsWallpaperVisible);
        Assert.Equal(visible ? UIMaterialMode.Glass : UIMaterialMode.Solid, result.MaterialMode);
    }

    // -----------------------------------------------------------------
    // Equatable: same inputs produce equal result, different backdrop produce not equal
    // -----------------------------------------------------------------
    [Fact]
    public void Resolve_SameInputs_Produce_Equal_Result()
    {
        var inputs = new ThemeInputs(true, true, "Mica", 0.40);

        var a = _sut.Resolve(inputs);
        var b = _sut.Resolve(inputs);

        Assert.Equal(a, b);
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Resolve_SameInputs_Via_Different_Instances_Produce_Equal_Result()
    {
        var inputs1 = new ThemeInputs(true, true, "Glass", 0.25);
        var inputs2 = new ThemeInputs(true, true, "Glass", 0.25);

        var a = _sut.Resolve(inputs1);
        var b = _sut.Resolve(inputs2);

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("Mica", "Glass")]
    [InlineData("Mica", "None")]
    [InlineData("Glass", "None")]
    [InlineData("Mica", "Tabbed")]
    [InlineData("Glass", "unknown")]
    public void Resolve_DifferentBackdropPreference_Produce_NotEqual_Result(string prefA, string prefB)
    {
        // Only visible case yields different backdrops; invisible always Transient
        var inputsA = new ThemeInputs(true, true, prefA, 0.40);
        var inputsB = new ThemeInputs(true, true, prefB, 0.40);

        var a = _sut.Resolve(inputsA);
        var b = _sut.Resolve(inputsB);

        // Guard: ensure test data actually maps to different modes
        // (e.g., Mica vs Tabbed both map to Tabbed, so they would be equal)
        if (a.BackdropMode == b.BackdropMode)
        {
            // If mapping collides, results should be equal — not applicable for not-equal assertion
            Assert.Equal(a, b);
            return;
        }

        Assert.NotEqual(a, b);
        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void Resolve_DifferentBackdrop_When_Visible_Produce_NotEqual_And_Same_When_NotVisible_Produce_Equal()
    {
        var visibleA = _sut.Resolve(new ThemeInputs(true, true, "Mica", 0.40));
        var visibleB = _sut.Resolve(new ThemeInputs(true, true, "Glass", 0.40));
        Assert.NotEqual(visibleA, visibleB);

        var hiddenA = _sut.Resolve(new ThemeInputs(false, true, "Mica", 0.40));
        var hiddenB = _sut.Resolve(new ThemeInputs(false, true, "Glass", 0.40));
        // Both hidden map to Transient/Solid/1.0 regardless of preference, so equal
        Assert.Equal(hiddenA, hiddenB);
    }

    [Fact]
    public void Resolve_Different_Opacity_When_Visible_Produce_NotEqual()
    {
        var a = _sut.Resolve(new ThemeInputs(true, true, "Mica", 0.25));
        var b = _sut.Resolve(new ThemeInputs(true, true, "Mica", 0.75));

        Assert.NotEqual(a, b);
        Assert.Equal(0.25, a.OverlayOpacity);
        Assert.Equal(0.75, b.OverlayOpacity);
    }

    // -----------------------------------------------------------------
    // GlassOpacity preservation (e.g., 0.25, 0.40, 0.75)
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.40)]
    [InlineData(0.75)]
    public void Resolve_Preserves_GlassOpacity_When_Visible(double glassOpacity)
    {
        var inputs = new ThemeInputs(true, true, "Glass", glassOpacity);

        var result = _sut.Resolve(inputs);

        Assert.Equal(glassOpacity, result.OverlayOpacity);
        // Backdrop for Glass should be Transient regardless of opacity
        Assert.Equal(WindowBackdropMode.Transient, result.BackdropMode);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.40)]
    [InlineData(0.75)]
    public void Resolve_Preserves_GlassOpacity_For_Mica_When_Visible(double glassOpacity)
    {
        var inputs = new ThemeInputs(true, true, "Mica", glassOpacity);
        var result = _sut.Resolve(inputs);
        Assert.Equal(glassOpacity, result.OverlayOpacity);
        Assert.Equal(WindowBackdropMode.Tabbed, result.BackdropMode);
    }

    [Theory]
    [InlineData(0.10)]
    [InlineData(0.50)]
    [InlineData(0.90)]
    public void Resolve_Preserves_Arbitrary_GlassOpacity_When_Visible(double glassOpacity)
    {
        var inputs = new ThemeInputs(true, true, "Mica", glassOpacity);
        var result = _sut.Resolve(inputs);
        Assert.Equal(glassOpacity, result.OverlayOpacity);
    }

    // -----------------------------------------------------------------
    // Additional: ensure IThemeResolver contract is pure (no side effects)
    // -----------------------------------------------------------------
    [Fact]
    public void Resolve_Is_Pure_Same_Call_Twice_Returns_Identical()
    {
        var inputs = new ThemeInputs(true, true, "None", 0.40);
        var first = _sut.Resolve(inputs);
        var second = _sut.Resolve(inputs);
        Assert.Equal(first, second);

        var hidden = new ThemeInputs(false, false, "Mica", 0.40);
        var hiddenFirst = _sut.Resolve(hidden);
        var hiddenSecond = _sut.Resolve(hidden);
        Assert.Equal(hiddenFirst, hiddenSecond);
    }
}
