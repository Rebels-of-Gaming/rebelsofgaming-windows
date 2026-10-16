using System.Runtime.InteropServices;

namespace RoG.App.Services;

/// <summary>Win32-Aufrufe, die das Windows App SDK nicht abdeckt.</summary>
internal static partial class NativeMethods
{
    /// <summary>Fenster in den Vordergrund (zweiter App-Start holt die laufende Instanz nach vorn).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);
}
