using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace NativeHub.Services;

public enum ShellCommand { Open, Search, Clipboard, NewNote, ToggleTheme, Exit }

public sealed class NativeShellService : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmAppTray = 0x8001;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const int HotkeyId = 0x4E48;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private NotifyIconData _icon;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;
    public event EventHandler<ShellCommand>? CommandInvoked;

    public NativeShellService(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _callback = WindowMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _ = SetWindowSubclass(_hwnd, _callback, 1, 0);
        IsHotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, 0x0001 | 0x0002, 0x20);
        AddTrayIcon();
    }

    public bool IsHotkeyRegistered { get; }

    public void Show()
    {
        _window.AppWindow.Show();
        _window.Activate();
    }

    public void Hide() => _window.AppWindow.Hide();

    private nint WindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated) { AddTrayIcon(); return 0; }
        if (message == WmHotkey && (int)wParam == HotkeyId)
        {
            Show();
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        if (message == WmAppTray && (uint)lParam == WmLButtonDoubleClick)
        {
            Show();
            CommandInvoked?.Invoke(this, ShellCommand.Open);
            return 0;
        }
        if (message == WmAppTray && (uint)lParam == WmRButtonUp)
        {
            ShowContextMenu();
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            _ = AppendMenu(menu, MfString, 1, "Open NativeHub");
            _ = AppendMenu(menu, MfString, 2, "Search files");
            _ = AppendMenu(menu, MfString, 3, "Clipboard history");
            _ = AppendMenu(menu, MfString, 4, "New quick note");
            _ = AppendMenu(menu, MfString, 5, "Toggle light/dark theme");
            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, 6, "Exit");
            _ = GetCursorPos(out var point);
            _ = SetForegroundWindow(_hwnd);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _hwnd, 0);
            var shellCommand = command switch
            {
                1 => ShellCommand.Open,
                2 => ShellCommand.Search,
                3 => ShellCommand.Clipboard,
                4 => ShellCommand.NewNote,
                5 => ShellCommand.ToggleTheme,
                6 => ShellCommand.Exit,
                _ => (ShellCommand?)null,
            };
            if (shellCommand is { } value) CommandInvoked?.Invoke(this, value);
        }
        finally { _ = DestroyMenu(menu); }
    }

    private void AddTrayIcon()
    {
        if (_icon.Icon != 0) _ = DestroyIcon(_icon.Icon);
        _icon = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _hwnd,
            Id = 1,
            Flags = 0x1 | 0x2 | 0x4,
            CallbackMessage = WmAppTray,
            Icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 0, 0, 0x10),
            Tip = "NativeHub — Ctrl+Alt+Space to search",
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
        _ = ShellNotifyIcon(0, ref _icon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = ShellNotifyIcon(2, ref _icon);
        if (_icon.Icon != 0) _ = DestroyIcon(_icon.Icon);
        _ = UnregisterHotKey(_hwnd, HotkeyId);
        _ = RemoveWindowSubclass(_hwnd, _callback, 1);
        GC.SuppressFinalize(this);
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id; public uint Flags; public uint CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")] private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
