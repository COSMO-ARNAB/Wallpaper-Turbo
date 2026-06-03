//UpdateErrorEventArgs.cs
using System;
using WallpaperTurbo.Core.Updates.Models;

namespace WallpaperTurbo.Updater.Events;

public sealed class UpdateErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public UpdateState StateWhenFailed { get; }

    public UpdateErrorEventArgs(string message, Exception? exception, UpdateState stateWhenFailed)
    {
        Message = message;
        Exception = exception;
        StateWhenFailed = stateWhenFailed;
    }
}
