using System.Management;
using System.Runtime.InteropServices;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using NativeHub.Models;

namespace NativeHub.Services;

public sealed class HardwareService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<HardwareItem>? _inventory;
    private DateTimeOffset _inventoryTimestamp;
    private Computer? _computer;

    public async Task<IReadOnlyList<HardwareItem>> GetAsync(bool refreshInventory = false, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run<IReadOnlyList<HardwareItem>>(() =>
            {
                if (refreshInventory || _inventory is null || DateTimeOffset.Now - _inventoryTimestamp > TimeSpan.FromMinutes(5))
                {
                    _inventory = ReadInventory();
                    _inventoryTimestamp = DateTimeOffset.Now;
                }

                var result = new List<HardwareItem>(_inventory);
                result.AddRange(ReadSensors(token));
                return result;
            }, token);
        }
        finally { _gate.Release(); }
    }

    private static List<HardwareItem> ReadInventory()
    {
        var items = new List<HardwareItem>
        {
            new("System", "Computer name", Environment.MachineName),
            new("System", "Architecture", RuntimeInformation.OSArchitecture.ToString()),
            new("System", "Uptime", FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64))),
            new("Runtime", ".NET", RuntimeInformation.FrameworkDescription),
        };

        AddWmi(items, "System", "Win32_OperatingSystem", [("Caption", "Operating system"), ("Version", "Version"), ("BuildNumber", "Build")]);
        AddWmi(items, "System", "Win32_ComputerSystem", [("Manufacturer", "Manufacturer", null), ("Model", "Model", null), ("TotalPhysicalMemory", "Installed memory", FormatWmiBytes)]);
        AddWmi(items, "Processor", "Win32_Processor", [("Name", "CPU", null), ("NumberOfCores", "Physical cores", null), ("NumberOfLogicalProcessors", "Logical processors", null), ("MaxClockSpeed", "Maximum clock", value => $"{value} MHz")]);
        AddWmi(items, "Graphics", "Win32_VideoController", [("Name", "GPU", null), ("DriverVersion", "Driver", null), ("AdapterRAM", "Adapter memory", FormatWmiBytes)]);
        AddWmi(items, "Memory", "Win32_PhysicalMemory", [("Manufacturer", "Module manufacturer", null), ("PartNumber", "Part number", null), ("Capacity", "Module capacity", FormatWmiBytes), ("ConfiguredClockSpeed", "Configured speed", value => $"{value} MT/s")]);
        AddWmi(items, "Board", "Win32_BaseBoard", [("Manufacturer", "Manufacturer"), ("Product", "Motherboard"), ("Version", "Revision")]);
        AddWmi(items, "Firmware", "Win32_BIOS", [("Manufacturer", "Vendor", null), ("SMBIOSBIOSVersion", "BIOS", null), ("ReleaseDate", "Release date", FormatWmiDate)]);
        AddWmi(items, "Storage", "Win32_DiskDrive", [("Model", "Disk", null), ("InterfaceType", "Interface", null), ("Size", "Capacity", FormatWmiBytes)]);
        AddWmi(items, "Volumes", "Win32_LogicalDisk WHERE DriveType = 3", [("DeviceID", "Volume", null), ("FileSystem", "File system", null), ("Size", "Capacity", FormatWmiBytes), ("FreeSpace", "Free space", FormatWmiBytes)]);
        AddWmi(items, "Network", "Win32_NetworkAdapter WHERE NetEnabled = TRUE", [("Name", "Adapter", null), ("MACAddress", "MAC address", null), ("Speed", "Link speed", FormatBits)]);
        AddWmi(items, "Battery", "Win32_Battery", [("Name", "Battery", null), ("EstimatedChargeRemaining", "Charge", value => $"{value}%"), ("BatteryStatus", "Status code", null)]);

        if (GlobalMemoryStatusEx(out var memory))
        {
            items.Add(new("Memory", "Physical memory used", FormatBytes(memory.TotalPhysical - memory.AvailablePhysical)));
            items.Add(new("Memory", "Physical memory available", FormatBytes(memory.AvailablePhysical)));
        }
        return items;
    }

    private List<HardwareItem> ReadSensors(CancellationToken token)
    {
        var items = new List<HardwareItem>();
        try
        {
            _computer ??= CreateComputer();
            _computer.Accept(new UpdateVisitor());
            foreach (var hardware in _computer.Hardware)
            {
                token.ThrowIfCancellationRequested();
                AddSensors(items, hardware);
                foreach (var subHardware in hardware.SubHardware) AddSensors(items, subHardware);
            }
            if (items.Count == 0) items.Add(new("Sensors", "Live sensors", "Unavailable", "No readable sensors were reported without elevation."));
        }
        catch (Exception ex)
        {
            _computer?.Close();
            _computer = null;
            items.Add(new("Sensors", "Live sensors", "Unavailable", ex.Message));
        }
        return items;
    }

    private static Computer CreateComputer()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
            IsBatteryEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true,
            IsPowerMonitorEnabled = true,
        };
        computer.Open();
        return computer;
    }

    private static void AddSensors(List<HardwareItem> items, IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors.Where(sensor => sensor.Value is { } value && !float.IsNaN(value)))
            items.Add(new("Sensors", $"{hardware.Name} · {sensor.Name}", $"{sensor.Value:0.##} {Unit(sensor.SensorType)}".TrimEnd(), sensor.SensorType.ToString()));
    }

    private static void AddWmi(List<HardwareItem> items, string group, string source, IEnumerable<(string Property, string Name, Func<object, string>? Format)> fields)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM " + source);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                foreach (var field in fields)
                {
                    var raw = item[field.Property];
                    if (raw is null) continue;
                    var value = field.Format?.Invoke(raw) ?? raw.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) items.Add(new(group, field.Name, value));
                }
                item.Dispose();
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            items.Add(new(group, source.Split(' ')[0], "Unavailable", ex.Message));
        }
    }

    private static void AddWmi(List<HardwareItem> items, string group, string source, IEnumerable<(string Property, string Name)> fields) =>
        AddWmi(items, group, source, fields.Select(field => (field.Property, field.Name, (Func<object, string>?)null)));

    private static string FormatWmiBytes(object value) => ulong.TryParse(value.ToString(), out var bytes) ? FormatBytes(bytes) : value.ToString() ?? "Unknown";
    private static string FormatBits(object value) => ulong.TryParse(value.ToString(), out var bits) ? $"{bits / 1_000_000d:0.#} Mbps" : value.ToString() ?? "Unknown";
    private static string FormatWmiDate(object value)
    {
        try { return ManagementDateTimeConverter.ToDateTime(value.ToString() ?? "").ToString("d", CultureInfo.CurrentCulture); }
        catch (ArgumentOutOfRangeException) { return value.ToString() ?? "Unknown"; }
    }
    private static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Power => "W",
        SensorType.Fan => "RPM", SensorType.Data => "GB", SensorType.SmallData => "MB", SensorType.Voltage => "V",
        SensorType.Current => "A", SensorType.Energy => "mWh", SensorType.Flow => "L/h", SensorType.Humidity => "%", _ => "",
    };
    internal static string FormatBytes(ulong bytes) => UtilityFormatting.FormatBytes(bytes > long.MaxValue ? long.MaxValue : (long)bytes);
    private static string FormatDuration(TimeSpan value) => $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private static bool GlobalMemoryStatusEx(out MemoryStatus status)
    {
        status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusExNative(ref status);
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusExNative(ref MemoryStatus status);
}
