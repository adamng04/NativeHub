using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NativeHub.Models;
using NativeHub.Services;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;

namespace NativeHub;

public partial class App : Application
{
    private Window? _window;
    private AppInstance? _mainInstance;
    private bool _notificationsRegistered;
    private const int ErrorInsufficientBuffer = 122;

    public static Window? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppActivationArguments? activation = null;
        if (HasPackageIdentity())
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var instance = AppInstance.FindOrRegisterForKey("NativeHub.Main");
            if (!instance.IsCurrent)
            {
                await instance.RedirectActivationToAsync(activation);
                Environment.Exit(0);
                return;
            }

            _mainInstance = instance;
            _mainInstance.Activated += MainInstance_Activated;
            RegisterNotifications();
        }

        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
        if (activation is not null)
        {
            await ShellIntegrationService.ConfigureJumpListAsync();
            HandleActivation(activation);
        }
        else
        {
            Dispatch("search");
        }
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, 0);
        return result is 0 or ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);

    private void MainInstance_Activated(object? sender, AppActivationArguments args) => DispatchActivation(args);

    private void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += NotificationManager_NotificationInvoked;
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception) { _notificationsRegistered = false; }
    }

    private void NotificationManager_NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var page = args.Arguments.TryGetValue("page", out var value) ? value : "notes";
        var note = args.Arguments.TryGetValue("note", out var noteId) ? noteId : null;
        Dispatch(page, note);
    }

    private void DispatchActivation(AppActivationArguments args)
    {
        if (_window is not MainWindow window) return;
        window.DispatcherQueue.TryEnqueue(() => HandleActivation(args));
    }

    private void HandleActivation(AppActivationArguments args)
    {
        var argument = args.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch
            ? launch.Arguments
            : string.Empty;
        Dispatch(string.IsNullOrWhiteSpace(argument) ? "search" : argument.TrimStart('-'));
    }

    private void Dispatch(string argument, string? context = null)
    {
        if (_window is not MainWindow window) return;
        var normalized = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "search";
        var page = normalized switch
        {
            "note" or "notes" => "notes",
            "clipboard" => "clipboard",
            "hardware" => "hardware",
            "weather" => "weather",
            "clock" => "clock",
            "nativos" => "nativos",
            "settings" => "settings",
            _ => "search",
        };
        if (normalized == "note" && context is null) Pages.NotesPage.CreateNewRequested = true;
        if (page == "notes" && Guid.TryParse(context, out var noteId)) Pages.NotesPage.PendingNoteId = noteId;
        window.ShowAndActivate();
        window.NavigateTo(page);
    }

    public static bool TryShowNotification(string title, string message, Note? note = null)
    {
        if (Current is not App app || !app._notificationsRegistered) return false;
        try
        {
            var builder = new AppNotificationBuilder().AddArgument("page", note is null ? "settings" : "notes");
            if (note is not null) builder.AddArgument("note", note.Id.ToString());
            AppNotificationManager.Default.Show(builder.AddText(title).AddText(message).BuildNotification());
            return true;
        }
        catch (Exception) { return false; }
    }

    public static void ShutdownIntegrations()
    {
        if (Current is not App app) return;
        app._mainInstance?.UnregisterKey();
        if (!app._notificationsRegistered) return;
        AppNotificationManager.Default.NotificationInvoked -= app.NotificationManager_NotificationInvoked;
        AppNotificationManager.Default.Unregister();
        app._notificationsRegistered = false;
    }
}
