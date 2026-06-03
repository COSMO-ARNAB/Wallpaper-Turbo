using System;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Events;

public sealed class UpdateStateChangedEventArgs : EventArgs
{
    public UpdateState OldState { get; }
    public UpdateState NewState { get; }
    public DateTime Timestamp { get; }

    public UpdateStateChangedEventArgs(UpdateState oldState, UpdateState newState, DateTime timestamp)
    {
        OldState = oldState;
        NewState = newState;
        Timestamp = timestamp;
    }
}
