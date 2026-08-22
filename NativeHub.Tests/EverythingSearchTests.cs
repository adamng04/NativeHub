using NativeHub.Services;

namespace NativeHub.Tests;

[TestClass]
public sealed class EverythingSearchTests
{
    [TestMethod]
    public async Task Search_ReturnsRealMetadataWhenEverythingIsRunning()
    {
        var service = new FileSearchService();
        if (!service.IsEverythingAvailable) Assert.Inconclusive("Everything IPC is not available on this machine.");

        var results = await service.SearchAsync("NativeHub.csproj", false, false, false, false, EverythingSort.NameAscending, CancellationToken.None);
        Assert.IsNotEmpty(results);
        Assert.IsTrue(results.Any(result => result.Name.Equals("NativeHub.csproj", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(results.Where(result => !result.IsFolder).All(result => result.Size >= 0));
    }
}
