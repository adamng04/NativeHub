using NativeHub.Models;
using NativeHub.Services;

namespace NativeHub.Tests;

[TestClass]
public sealed class FormattingTests
{
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(1024L, "1 KB")]
    [DataRow(1_572_864L, "1.5 MB")]
    public void FileSize_UsesReadableBinaryUnits(long bytes, string expected) => Assert.AreEqual(expected, UtilityFormatting.FormatBytes(bytes));

    [TestMethod]
    public void TemperatureConversion_IsCorrect()
    {
        Assert.AreEqual(32d, UtilityFormatting.ConvertTemperature(0, true), 0.001);
        Assert.AreEqual(100d, UtilityFormatting.ConvertTemperature(100, false), 0.001);
    }

    [TestMethod]
    public void WeatherCodes_MapToReadableConditions()
    {
        Assert.AreEqual("Clear sky", UtilityFormatting.DescribeWeather(0));
        Assert.AreEqual("Thunderstorm", UtilityFormatting.DescribeWeather(95));
    }

    [TestMethod]
    public void HardwareBytes_UseSameReadableUnits() => Assert.AreEqual("2 GB", UtilityFormatting.FormatBytes(2_147_483_648));

    [TestMethod]
    public void WeatherPlace_BuildsReadableSuggestionLabels()
    {
        var place = new WeatherPlace("Amsterdam", "North Holland", "Netherlands", 52.37, 4.89, "Europe/Amsterdam");
        Assert.AreEqual("Amsterdam, North Holland, Netherlands", place.DisplayName);
        Assert.AreEqual("North Holland · Netherlands", place.RegionLabel);
    }
}
