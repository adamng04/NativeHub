using Windows.UI.StartScreen;

namespace NativeHub.Services;

public static class ShellIntegrationService
{
    public static async Task ConfigureJumpListAsync()
    {
        try
        {
            if (!JumpList.IsSupported()) return;
            var list = await JumpList.LoadCurrentAsync();
            list.Items.Clear();
            list.SystemGroupKind = JumpListSystemGroupKind.Recent;
            list.Items.Add(Create("--search", "Search files", "Open NativeHub file search"));
            list.Items.Add(Create("--note", "New quick note", "Create a note"));
            list.Items.Add(Create("--clipboard", "Clipboard history", "Open clipboard history"));
            await list.SaveAsync();
        }
        catch (Exception) { }
    }

    private static JumpListItem Create(string argument, string name, string description)
    {
        var item = JumpListItem.CreateWithArguments(argument, name);
        item.Description = description;
        item.GroupName = "NativeHub";
        item.Logo = new Uri("ms-appx:///Assets/Square44x44Logo.png");
        return item;
    }
}
