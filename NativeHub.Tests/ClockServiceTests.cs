using NativeHub.Services;

namespace NativeHub.Tests;

[TestClass]
public sealed class ClockServiceTests
{
    [TestMethod]
    public void ToBraille_UsesNumberSignForEachNumericRun()
    {
        const string expected = "\u283C\u2801\u2803:\u283C\u2809\u2819:\u283C\u281A\u2811";
        Assert.AreEqual(expected, ClockService.ToBraille("12:34:05"));
    }

    [TestMethod]
    public void ToBraille_PreservesNonDigitsAndHandlesEmptyText()
    {
        Assert.AreEqual(string.Empty, ClockService.ToBraille(string.Empty));
        Assert.AreEqual("UTC \u283C\u2801", ClockService.ToBraille("UTC 1"));
    }

    [TestMethod]
    public void WorldClocks_ApplyWindowsDstRules()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(TimeSpan.FromHours(7), ClockService.GetTime("SE Asia Standard Time", instant).Offset);
        Assert.AreEqual(TimeSpan.FromHours(-4), ClockService.GetTime("Eastern Standard Time", instant).Offset);
        Assert.AreEqual(TimeSpan.FromHours(9), ClockService.GetTime("Tokyo Standard Time", instant).Offset);
        Assert.AreEqual(TimeSpan.FromHours(2), ClockService.GetTime("W. Europe Standard Time", instant).Offset);
        Assert.AreEqual(TimeSpan.FromHours(2), ClockService.GetTime("Romance Standard Time", instant).Offset);
    }

    [TestMethod]
    public void WorldClocks_ContainTwelveUniqueCitiesAndValidTimeZones()
    {
        Assert.AreEqual(12, ClockService.Clocks.Count);
        Assert.AreEqual(12, ClockService.Clocks.Select(clock => clock.City).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(ClockService.Clocks.Any(clock => clock.City == "Madrid, Spain"));

        var instant = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        foreach (var clock in ClockService.Clocks)
        {
            _ = ClockService.GetTime(clock.TimeZoneId, instant);
        }
    }
}
