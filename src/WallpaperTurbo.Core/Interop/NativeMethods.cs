// NativeMethods.cs - Contains P/Invoke signatures for Win32 API calls used by the engine for window manipulation and desktop integration.
using System;
using System.Runtime.InteropServices;

// FIX: Target the entire assembly scope instead of the static class
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace WallpaperTurbo.Core.Interop
{
    /// <summary>
    /// Common Win32 interop signatures used by the engine for desktop/window manipulation.
    /// </summary>
    public static class NativeMethods
    {
        // Window style constants used by the engine when adjusting window decorations.
        public const int GWL_STYLE = -16;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_SYSMENU = 0x00080000;

        // Window positioning constants added for full-screen wallpaper sizing.
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        /// <summary>
        /// Find a top-level window by class name and/or window name (Unicode).
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindWindowW")]
        public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

        /// <summary>
        /// Send a message with timeout; returns result in <paramref name="lpdwResult"/>.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            IntPtr lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult
        );

        /// <summary>
        /// Delegate used by EnumWindows.
        /// Return true to continue enumeration, false to stop.
        /// </summary>
        /// <param name="hWnd">Window handle.</param>
        /// <param name="lParam">User-supplied parameter.</param>
        /// <returns>True to continue enumeration.</returns>
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// Enumerates all top-level windows on the screen by calling the provided callback for each.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        /// <summary>
        /// Find a child window that matches given class/name under a parent window (Unicode wrapper).
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindWindowExW")]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        /// <summary>
        /// Retrieves the name of the class to which the specified window belongs.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        /// <summary>
        /// Retrieves information about the specified window. This is the pointer-sized version.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        /// <summary>
        /// Changes an attribute of the specified window. This is the pointer-sized version.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// Change the parent window of a child window.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        /// <summary>
        /// Changes the size, position, and Z order of a child or top-level window.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// Retrieves the specified system metric or system configuration setting.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

    }
}