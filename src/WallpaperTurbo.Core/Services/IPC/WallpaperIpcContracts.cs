using System;

namespace WallpaperTurbo.Core.Services.IPC;

/// <summary>
/// Immutable IPC command payload for controlling Wallpaper Turbo.
/// </summary>
public sealed record IpcCommand(
    string Action,
    int? WallpaperIndex = null,
    string? VideoPath = null
);

/// <summary>
/// Immutable IPC response payload from the background Wallpaper Turbo host.
/// </summary>
public sealed record IpcResponse(
    bool Success,
    string Message
);
