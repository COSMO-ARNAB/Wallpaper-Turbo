using Xunit;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("v1.2.1-beta.1", 1, 2, 1, "beta.1")]
    [InlineData("v1.2.1-beta.2", 1, 2, 1, "beta.2")]
    [InlineData("v1.2.1-rc.1", 1, 2, 1, "rc.1")]
    [InlineData("v1.2.1", 1, 2, 1, "")]
    [InlineData("v1.2.2", 1, 2, 2, "")]
    [InlineData("1.2.1-beta.1", 1, 2, 1, "beta.1")]
    [InlineData("1.2.0", 1, 2, 0, "")]
    public void Parse_ParsesCorrectly(string input, int expectedMajor, int expectedMinor, int expectedPatch, string expectedPrerelease)
    {
        bool success = SemanticVersion.TryParse(input, out var version);

        Assert.True(success);
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Patch);
        Assert.Equal(expectedPrerelease, version.PreReleaseLabel);
    }

    [Fact]
    public void Compare_Beta1_IsOlderThan_Beta2()
    {
        SemanticVersion.TryParse("v1.2.1-beta.1", out var beta1);
        SemanticVersion.TryParse("v1.2.1-beta.2", out var beta2);

        Assert.True(beta1 < beta2);
        Assert.True(beta2 > beta1);
    }

    [Fact]
    public void Compare_Beta2_IsOlderThan_Rc1()
    {
        SemanticVersion.TryParse("v1.2.1-beta.2", out var beta2);
        SemanticVersion.TryParse("v1.2.1-rc.1", out var rc1);

        Assert.True(beta2 < rc1);
        Assert.True(rc1 > beta2);
    }

    [Fact]
    public void Compare_Rc1_IsOlderThan_Stable()
    {
        SemanticVersion.TryParse("v1.2.1-rc.1", out var rc1);
        SemanticVersion.TryParse("v1.2.1", out var stable);

        Assert.True(rc1 < stable);
        Assert.True(stable > rc1);
    }

    [Fact]
    public void Compare_Stable_IsOlderThan_NextPatch()
    {
        SemanticVersion.TryParse("v1.2.1", out var stable1);
        SemanticVersion.TryParse("v1.2.2", out var stable2);

        Assert.True(stable1 < stable2);
        Assert.True(stable2 > stable1);
    }

    [Fact]
    public void Equality_WorksCorrectly()
    {
        SemanticVersion.TryParse("v1.2.1-beta.1", out var v1);
        SemanticVersion.TryParse("1.2.1-beta.1", out var v2);
        SemanticVersion.TryParse("v1.2.1-beta.2", out var v3);

        Assert.True(v1 == v2);
        Assert.True(v1.Equals(v2));
        Assert.True(v1 != v3);
        Assert.False(v1.Equals(v3));
    }
}
