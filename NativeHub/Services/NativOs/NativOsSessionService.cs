namespace NativeHub.Services.NativOs;

public enum NativOsPowerState
{
    Off,
    Booting,
    Running,
}

public sealed class NativOsSaveData
{
    public Dictionary<string, int> MinefieldBestSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int FallingBlocksBestScore { get; set; }
    public byte[]? VoxelBlocks { get; set; }
    public int VoxelSeed { get; set; } = 1999;
    public VoxelWorldOptions VoxelOptions { get; set; } = new();
}

public sealed class NativOsSessionService(JsonStore store)
{
    private readonly List<string> _bootLines = [];
    private CancellationTokenSource? _bootCancellation;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private bool _initialized;

    public NativOsPowerState PowerState { get; private set; } = NativOsPowerState.Off;
    public IReadOnlyList<string> BootLines => _bootLines;
    public int BootProgress { get; private set; }
    public NativOsSaveData SaveData { get; private set; } = new();

    public event EventHandler? StateChanged;

    public async Task PowerOnAsync()
    {
        if (PowerState != NativOsPowerState.Off) return;
        await InitializeAsync();

        _bootCancellation?.Dispose();
        _bootCancellation = new CancellationTokenSource();
        var token = _bootCancellation.Token;
        _bootLines.Clear();
        BootProgress = 0;
        PowerState = NativOsPowerState.Booting;
        RaiseChanged();

        var sequence = new (string Text, int Progress, int Delay)[]
        {
            ("NATIVOS ROM BIOS v0.98 (C) 1999 Native Systems", 4, 260),
            ("CPU: VIRTUAL RISC PROCESSOR ............ OK", 10, 210),
            ("MEMORY TEST: 65536K .................... OK", 17, 270),
            ("VIDEO ADAPTER: NATIVGA 32 .............. OK", 24, 180),
            ("KEYBOARD CONTROLLER .................... OK", 30, 160),
            ("IDE PRIMARY MASTER: NATIVDISK 512 MB", 38, 240),
            ("BOOT DEVICE FOUND AT 00:1F.2", 44, 180),
            (string.Empty, 47, 100),
            ("C:\\>CHKDSK /F", 51, 220),
            ("Checking file allocation table...", 60, 310),
            ("Checking directory structure...", 68, 290),
            ("Checking free space...", 76, 270),
            ("524,288 KB total disk space", 80, 120),
            ("0 KB in bad sectors", 84, 170),
            ("Disk check complete.", 88, 200),
            (string.Empty, 90, 80),
            ("Loading NATIVOS.KRN", 93, 230),
            ("Starting desktop services", 96, 220),
            ("Mounting user workspace", 98, 200),
            ("Welcome to NativOS", 100, 360),
        };

        try
        {
            foreach (var item in sequence)
            {
                token.ThrowIfCancellationRequested();
                _bootLines.Add(item.Text);
                BootProgress = item.Progress;
                RaiseChanged();
                await Task.Delay(item.Delay, token);
            }

            token.ThrowIfCancellationRequested();
            PowerState = NativOsPowerState.Running;
            RaiseChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutdown deliberately interrupts POST and owns the final state.
        }
    }

    public void ShutDown()
    {
        _bootCancellation?.Cancel();
        _bootLines.Clear();
        BootProgress = 0;
        PowerState = NativOsPowerState.Off;
        RaiseChanged();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _persistenceGate.WaitAsync();
        try
        {
            if (_initialized) return;
            SaveData = await store.LoadAsync("nativos.json", new NativOsSaveData());
            _initialized = true;
        }
        finally { _persistenceGate.Release(); }
    }

    public async Task SaveAsync()
    {
        await InitializeAsync();
        await store.SaveAsync("nativos.json", SaveData);
    }

    public int GetMinefieldBest(MinefieldDifficulty difficulty) =>
        SaveData.MinefieldBestSeconds.TryGetValue(difficulty.ToString(), out var seconds) ? seconds : 0;

    public async Task RecordMinefieldBestAsync(MinefieldDifficulty difficulty, int seconds)
    {
        if (seconds <= 0) return;
        var key = difficulty.ToString();
        if (SaveData.MinefieldBestSeconds.TryGetValue(key, out var current) && current <= seconds) return;
        SaveData.MinefieldBestSeconds[key] = seconds;
        await SaveAsync();
    }

    public async Task RecordFallingBlocksBestAsync(int score)
    {
        if (score <= SaveData.FallingBlocksBestScore) return;
        SaveData.FallingBlocksBestScore = score;
        await SaveAsync();
    }

    public void UpdateVoxelWorld(byte[] snapshot, int seed, VoxelWorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        SaveData.VoxelBlocks = (byte[])snapshot.Clone();
        SaveData.VoxelSeed = seed;
        SaveData.VoxelOptions = options.Normalize();
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
