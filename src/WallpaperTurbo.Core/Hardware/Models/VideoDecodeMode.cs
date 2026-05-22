namespace WallpaperTurbo.Core.Models;

/// <summary>
/// Specifies the hardware/software video decoding strategy for the VLC engine.
/// </summary>
public enum VideoDecodeMode
{
    /// <summary>
    /// VLC auto-negotiates the best available path natively (recommended for general usage).
    /// </summary>
    Auto,

    /// <summary>
    /// Prefers and forces hardware decoding (D3D11VA) aggressively for debugging/testing.
    /// </summary>
    Hardware,

    /// <summary>
    /// Disables hardware acceleration entirely, falling back to CPU software decoding.
    /// </summary>
    Software
}
