using Windows.ApplicationModel;

namespace NativeHub.Services;

public static class StartupService
{
    public const string TaskId = "NativeHubStartup";

    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try { return (await StartupTask.GetAsync(TaskId)).State; }
        catch (Exception) { return null; }
    }

    public static async Task<StartupTaskState?> SetEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled && task.State == StartupTaskState.Disabled) return await task.RequestEnableAsync();
            if (!enabled && task.State == StartupTaskState.Enabled) task.Disable();
            return task.State;
        }
        catch (Exception) { return null; }
    }
}
