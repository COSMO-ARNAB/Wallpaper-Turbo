using System;
using System.Text.RegularExpressions;

namespace WallpaperTurbo.Core.Updates.Models;

public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string PreReleaseLabel { get; }

    public SemanticVersion(int major, int minor, int patch, string preReleaseLabel = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreReleaseLabel = preReleaseLabel ?? string.Empty;
    }

    private static readonly Regex VersionRegex = new Regex(
        @"^v?(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string input, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = VersionRegex.Match(input.Trim());
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["major"].Value, out int major) ||
            !int.TryParse(match.Groups["minor"].Value, out int minor))
        {
            return false;
        }

        int patch = 0;
        if (match.Groups["patch"].Success)
        {
            if (!int.TryParse(match.Groups["patch"].Value, out patch))
                return false;
        }

        string preRelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : string.Empty;

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);

        bool thisIsPre = !string.IsNullOrEmpty(PreReleaseLabel);
        bool otherIsPre = !string.IsNullOrEmpty(other.PreReleaseLabel);

        // Pre-release is always LOWER than stable
        if (thisIsPre && !otherIsPre) return -1;
        if (!thisIsPre && otherIsPre) return 1;
        if (!thisIsPre && !otherIsPre) return 0;

        // Both are pre-release, compare identifiers
        var thisParts = PreReleaseLabel.Split('.');
        var otherParts = other.PreReleaseLabel.Split('.');
        int length = Math.Min(thisParts.Length, otherParts.Length);

        for (int i = 0; i < length; i++)
        {
            bool thisIsNum = int.TryParse(thisParts[i], out int thisNum);
            bool otherIsNum = int.TryParse(otherParts[i], out int otherNum);

            if (thisIsNum && otherIsNum)
            {
                int cmp = thisNum.CompareTo(otherNum);
                if (cmp != 0) return cmp;
            }
            else if (!thisIsNum && !otherIsNum)
            {
                int cmp = string.Compare(thisParts[i], otherParts[i], StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            else
            {
                // Numeric identifiers always have lower precedence than non-numeric
                return thisIsNum ? -1 : 1;
            }
        }

        return thisParts.Length.CompareTo(otherParts.Length);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreReleaseLabel);
    
    public override string ToString()
    {
        var baseVersion = $"{Major}.{Minor}.{Patch}";
        return string.IsNullOrEmpty(PreReleaseLabel) ? baseVersion : $"{baseVersion}-{PreReleaseLabel}";
    }

    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}
