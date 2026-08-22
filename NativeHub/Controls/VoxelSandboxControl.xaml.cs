using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NativeHub.Pages;
using NativeHub.Services;
using NativeHub.Services.NativOs;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.System;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace NativeHub.Controls;

public sealed partial class VoxelSandboxControl : UserControl, INativOsAppLifecycle
{
    private const uint InvisibleCursorResourceId = 217;
    private readonly VoxelRenderer _renderer = new();
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _mouseTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private readonly HashSet<VirtualKey> _keys = [];
    private VoxelWorld _world;
    private CanvasBitmap? _frameBitmap;
    private InputDesktopResourceCursor? _invisibleCursor;
    private InputSystemCursor? _defaultCursor;
    private nint _cursorResourceModule;
    private byte[] _pixels;
    private Vector3 _position;
    private float _yaw = 0.65f;
    private float _pitch = -0.18f;
    private float _verticalVelocity;
    private VoxelHit? _target;
    private Window? _hostWindow;
    private NativePoint? _cursorBeforeCapture;
    private VoxelBlock _selectedBlock = VoxelBlock.Grass;
    private bool _active = true;
    private bool _flight;
    private bool _mouseLook;
    private bool _suppressNextRightTap;
    private bool _closed;

    public VoxelSandboxControl()
    {
        var data = AppServices.NativOs.SaveData;
        _world = new VoxelWorld(data.VoxelSeed, data.VoxelOptions, data.VoxelBlocks);
        _position = SpawnPosition(_world);
        _pixels = _renderer.Render(_world, _position, _yaw, _pitch);
        InitializeComponent();

        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
        _renderTimer.Tick += RenderTimer_Tick;
        _mouseTimer = DispatcherQueue.CreateTimer();
        _mouseTimer.Interval = TimeSpan.FromMilliseconds(8);
        _mouseTimer.Tick += (_, _) => UpdateMouseLock();
        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(900);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += async (_, _) => await SaveWorldAsync();
        _renderTimer.Start();
        if (App.MainWindow is Window hostWindow)
        {
            _hostWindow = hostWindow;
            _hostWindow.Activated += HostWindow_Activated;
        }
        UpdateHud();
    }

    public void SetActive(bool active)
    {
        _active = active;
        _keys.Clear();
        if (active && !_closed)
        {
            _frameClock.Restart();
            _renderTimer.Start();
        }
        else
        {
            SetMouseLook(false);
            _renderTimer.Stop();
        }
    }

    public void Close()
    {
        SetMouseLook(false);
        _closed = true;
        _renderTimer.Stop();
        _mouseTimer.Stop();
        _saveTimer.Stop();
        QueueSnapshot();
        _ = AppServices.NativOs.SaveAsync();
        _frameBitmap?.Dispose();
        _frameBitmap = null;
        ProtectedCursor = null!;
        _invisibleCursor?.Dispose();
        _invisibleCursor = null;
        _defaultCursor?.Dispose();
        _defaultCursor = null;
        if (_cursorResourceModule != 0)
        {
            NativeLibrary.Free(_cursorResourceModule);
            _cursorResourceModule = 0;
        }
        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow = null;
        }
    }

    private void GameCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _frameBitmap?.Dispose();
        _frameBitmap = CanvasBitmap.CreateFromBytes(
            sender,
            _pixels,
            _renderer.Width,
            _renderer.Height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized);
    }

    private void GameCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_frameBitmap is null || sender.ActualWidth <= 0 || sender.ActualHeight <= 0) return;
        _frameBitmap.SetPixelBytes(_pixels);
        var sourceAspect = _renderer.Width / (double)_renderer.Height;
        var targetAspect = sender.ActualWidth / sender.ActualHeight;
        double width;
        double height;
        if (targetAspect > sourceAspect)
        {
            height = sender.ActualHeight;
            width = height * sourceAspect;
        }
        else
        {
            width = sender.ActualWidth;
            height = width / sourceAspect;
        }
        var destination = new Rect((sender.ActualWidth - width) / 2, (sender.ActualHeight - height) / 2, width, height);
        args.DrawingSession.Clear(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        args.DrawingSession.DrawImage(_frameBitmap, destination, _frameBitmap.Bounds, 1, CanvasImageInterpolation.NearestNeighbor);
    }

    private void RenderTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_active || _closed) return;
        var delta = Math.Clamp((float)_frameClock.Elapsed.TotalSeconds, 0.001f, 0.08f);
        _frameClock.Restart();
        UpdatePlayer(delta);
        _target = _world.Raycast(_position, ViewDirection(), 7f);
        _pixels = _renderer.Render(_world, _position, _yaw, _pitch, _target);
        GameCanvas.Invalidate();
        UpdateHud();
    }

    private void UpdatePlayer(float delta)
    {
        var forward = Vector3.Normalize(new Vector3(MathF.Cos(_yaw), 0, MathF.Sin(_yaw)));
        var right = Vector3.Normalize(new Vector3(-MathF.Sin(_yaw), 0, MathF.Cos(_yaw)));
        var movement = Vector3.Zero;
        if (_keys.Contains(VirtualKey.W)) movement += forward;
        if (_keys.Contains(VirtualKey.S)) movement -= forward;
        if (_keys.Contains(VirtualKey.D)) movement += right;
        if (_keys.Contains(VirtualKey.A)) movement -= right;
        if (movement.LengthSquared() > 0) movement = Vector3.Normalize(movement) * (_flight ? 7f : 4.5f) * delta;
        TryMove(new Vector3(movement.X, 0, 0));
        TryMove(new Vector3(0, 0, movement.Z));

        if (_flight)
        {
            var vertical = 0f;
            if (_keys.Contains(VirtualKey.Space)) vertical += 1;
            if (_keys.Contains(VirtualKey.Shift)) vertical -= 1;
            TryMove(new Vector3(0, vertical * 6f * delta, 0));
            _verticalVelocity = 0;
            return;
        }

        var grounded = !CanOccupy(_position + new Vector3(0, -0.06f, 0));
        if (grounded && _keys.Contains(VirtualKey.Space)) _verticalVelocity = 5.7f;
        _verticalVelocity -= 13.5f * delta;
        var verticalMove = new Vector3(0, _verticalVelocity * delta, 0);
        if (!TryMove(verticalMove)) _verticalVelocity = 0;
    }

    private bool TryMove(Vector3 delta)
    {
        var candidate = _position + delta;
        if (!CanOccupy(candidate)) return false;
        _position = candidate;
        return true;
    }

    private bool CanOccupy(Vector3 eye)
    {
        const float radius = 0.28f;
        const float eyeHeight = 1.62f;
        var feet = eye.Y - eyeHeight;
        if (eye.X < radius || eye.Z < radius || eye.X >= _world.Width - radius || eye.Z >= _world.Depth - radius || feet < 0 || eye.Y >= _world.Height - 0.1f)
            return false;
        var xs = new[] { eye.X - radius, eye.X + radius };
        var zs = new[] { eye.Z - radius, eye.Z + radius };
        var ys = new[] { feet + 0.05f, feet + 0.85f, eye.Y - 0.05f };
        foreach (var x in xs)
        foreach (var z in zs)
        foreach (var y in ys)
            if (_world.Get((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)) != VoxelBlock.Air) return false;
        return true;
    }

    private void RootControl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                SetMouseLook(false);
                _keys.Clear();
                e.Handled = true;
                return;
            case VirtualKey.F:
                FlightSwitch.IsOn = !FlightSwitch.IsOn;
                e.Handled = true;
                return;
            case VirtualKey.Number1: SelectBlock(VoxelBlock.Grass); e.Handled = true; return;
            case VirtualKey.Number2: SelectBlock(VoxelBlock.Dirt); e.Handled = true; return;
            case VirtualKey.Number3: SelectBlock(VoxelBlock.Stone); e.Handled = true; return;
            case VirtualKey.Number4: SelectBlock(VoxelBlock.Sand); e.Handled = true; return;
            case VirtualKey.Number5: SelectBlock(VoxelBlock.Wood); e.Handled = true; return;
            case VirtualKey.Left: _yaw -= 0.09f; e.Handled = true; return;
            case VirtualKey.Right: _yaw += 0.09f; e.Handled = true; return;
            case VirtualKey.Up: _pitch = Math.Clamp(_pitch + 0.07f, -1.45f, 1.45f); e.Handled = true; return;
            case VirtualKey.Down: _pitch = Math.Clamp(_pitch - 0.07f, -1.45f, 1.45f); e.Handled = true; return;
        }
        if (e.Key is VirtualKey.W or VirtualKey.A or VirtualKey.S or VirtualKey.D or VirtualKey.Space or VirtualKey.Shift)
        {
            _keys.Add(e.Key);
            e.Handled = true;
        }
    }

    private void RootControl_KeyUp(object sender, KeyRoutedEventArgs e) => _keys.Remove(e.Key);

    private void GameCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        GameCanvas.Focus(FocusState.Programmatic);
        var point = e.GetCurrentPoint(GameCanvas);
        if (!_mouseLook)
        {
            _suppressNextRightTap = point.Properties.IsRightButtonPressed;
            SetMouseLook(true);
            e.Handled = true;
            return;
        }
        if (point.Properties.IsLeftButtonPressed) BreakTarget();
        e.Handled = true;
    }

    private void GameCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        GameCanvas.Focus(FocusState.Programmatic);
        if (_suppressNextRightTap)
        {
            _suppressNextRightTap = false;
        }
        else if (!_mouseLook)
        {
            SetMouseLook(true);
        }
        else
        {
            PlaceTarget();
        }
        e.Handled = true;
    }

    private void GameCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(GameCanvas).Properties.MouseWheelDelta;
        var value = (int)_selectedBlock + (delta < 0 ? 1 : -1);
        if (value < (int)VoxelBlock.Grass) value = (int)VoxelBlock.Wood;
        if (value > (int)VoxelBlock.Wood) value = (int)VoxelBlock.Grass;
        SelectBlock((VoxelBlock)value);
        e.Handled = true;
    }

    private void BreakTarget()
    {
        if (_target is not { } hit) { SaveStatusText.Text = "No block in reach"; return; }
        if (!_world.Set(hit.X, hit.Y, hit.Z, VoxelBlock.Air)) return;
        SaveStatusText.Text = $"Removed {BlockName(hit.Block)}";
        ScheduleSave();
    }

    private void PlaceTarget()
    {
        if (_target is not { } hit || !_world.IsInside(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ))
        {
            SaveStatusText.Text = "No surface in reach";
            return;
        }
        if (_world.Get(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ) != VoxelBlock.Air || WouldIntersectPlayer(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ))
        {
            SaveStatusText.Text = "That space is occupied";
            return;
        }
        _world.Set(hit.AdjacentX, hit.AdjacentY, hit.AdjacentZ, _selectedBlock);
        SaveStatusText.Text = $"Placed {BlockName(_selectedBlock)}";
        ScheduleSave();
    }

    private bool WouldIntersectPlayer(int x, int y, int z)
    {
        const float radius = 0.28f;
        var playerMinimum = new Vector3(_position.X - radius, _position.Y - 1.62f, _position.Z - radius);
        var playerMaximum = new Vector3(_position.X + radius, _position.Y + 0.1f, _position.Z + radius);
        return playerMinimum.X < x + 1 && playerMaximum.X > x &&
               playerMinimum.Y < y + 1 && playerMaximum.Y > y &&
               playerMinimum.Z < z + 1 && playerMaximum.Z > z;
    }

    private void BlockBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Enum.TryParse<VoxelBlock>((BlockBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var block)) _selectedBlock = block;
    }

    private void SelectBlock(VoxelBlock block)
    {
        _selectedBlock = block;
        BlockBox.SelectedIndex = (int)block - 1;
    }

    private void FlightSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        _flight = FlightSwitch.IsOn;
        _verticalVelocity = 0;
        UpdateHud();
    }

    private void MouseLookButton_Click(object sender, RoutedEventArgs e)
    {
        SetMouseLook(!_mouseLook);
        if (_mouseLook) GameCanvas.Focus(FocusState.Programmatic);
    }

    private void SetMouseLook(bool enabled)
    {
        if (enabled == _mouseLook) return;
        if (enabled)
        {
            if (!_active || _closed || !TryGetCaptureBounds(out var bounds, out var center))
            {
                SaveStatusText.Text = "Mouse capture unavailable: game surface is inactive";
                return;
            }
            if (!ClipCursor(ref bounds))
            {
                SaveStatusText.Text = "Mouse capture unavailable: Windows rejected confinement";
                return;
            }
            _cursorBeforeCapture = GetCursorPos(out var cursor) ? cursor : null;
            try
            {
                _cursorResourceModule = _cursorResourceModule == 0
                    ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "NativeHub.CursorResources.dll"))
                    : _cursorResourceModule;
                _invisibleCursor ??= InputDesktopResourceCursor.CreateFromModule("NativeHub.CursorResources.dll", InvisibleCursorResourceId);
                ProtectedCursor = _invisibleCursor;
            }
            catch (Exception error) when (error is COMException or DllNotFoundException or BadImageFormatException)
            {
                _ = ReleaseCursorClip(0);
                SaveStatusText.Text = $"Mouse capture unavailable: cursor resource error 0x{error.HResult:X8}";
                return;
            }
            _mouseLook = true;
            _mouseTimer.Start();
            _ = SetCursorPos(center.X, center.Y);
        }
        else
        {
            _mouseLook = false;
            _mouseTimer.Stop();
            _ = ReleaseCursorClip(0);
            _defaultCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            ProtectedCursor = _defaultCursor;
            if (_cursorBeforeCapture is { } cursor) _ = SetCursorPos(cursor.X, cursor.Y);
            _cursorBeforeCapture = null;
            _suppressNextRightTap = false;
        }
        MouseHint.Visibility = _mouseLook ? Visibility.Collapsed : Visibility.Visible;
        MouseLookButton.Content = _mouseLook ? "Release mouse" : "Capture mouse";
    }

    private void UpdateMouseLock()
    {
        if (!_mouseLook) return;
        if (!TryGetCaptureBounds(out var bounds, out var center) || !ClipCursor(ref bounds))
        {
            SaveStatusText.Text = "Mouse capture released: game surface changed";
            SetMouseLook(false);
            return;
        }

        if (GetCursorPos(out var cursor))
        {
            var deltaX = cursor.X - center.X;
            var deltaY = cursor.Y - center.Y;
            if (deltaX != 0 || deltaY != 0)
            {
                _yaw += deltaX * 0.0032f;
                _pitch = Math.Clamp(_pitch - deltaY * 0.0028f, -1.45f, 1.45f);
                _ = SetCursorPos(center.X, center.Y);
            }
        }

    }

    private bool TryGetCaptureBounds(out NativeRect bounds, out NativePoint center)
    {
        bounds = default;
        center = default;
        if (_hostWindow is null || GameCanvas.XamlRoot is null || GameCanvas.ActualWidth < 2 || GameCanvas.ActualHeight < 2) return false;

        try
        {
            var windowHandle = WindowNative.GetWindowHandle(_hostWindow);
            var offset = GameCanvas.TransformToVisual(null).TransformPoint(default);
            var scale = GameCanvas.XamlRoot.RasterizationScale;
            var clientOrigin = new NativePoint
            {
                X = (int)Math.Floor(offset.X * scale),
                Y = (int)Math.Floor(offset.Y * scale),
            };
            if (!ClientToScreen(windowHandle, ref clientOrigin)) return false;
            bounds = new NativeRect
            {
                Left = clientOrigin.X,
                Top = clientOrigin.Y,
                Right = clientOrigin.X + Math.Max(2, (int)Math.Ceiling(GameCanvas.ActualWidth * scale)),
                Bottom = clientOrigin.Y + Math.Max(2, (int)Math.Ceiling(GameCanvas.ActualHeight * scale)),
            };
            center = new NativePoint
            {
                X = bounds.Left + (bounds.Right - bounds.Left) / 2,
                Y = bounds.Top + (bounds.Bottom - bounds.Top) / 2,
            };
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void HostWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) SetMouseLook(false);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveWorldAsync();

    private async void WorldOptions_Click(object sender, RoutedEventArgs e)
    {
        SetMouseLook(false);
        var shapeBox = CreateOptionsBox("Shape", Enum.GetValues<VoxelWorldShape>(), _world.Options.Shape);
        var sizeBox = CreateOptionsBox("Size", Enum.GetValues<VoxelWorldSize>(), _world.Options.Size);
        var typeBox = CreateOptionsBox("Type", Enum.GetValues<VoxelWorldType>(), _world.Options.Type);
        var themeBox = CreateOptionsBox("Theme", Enum.GetValues<VoxelWorldTheme>(), _world.Options.Theme);
        var summary = new TextBlock
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 184, 200, 222)),
        };
        var fields = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddOption(fields, shapeBox, 0, 0);
        AddOption(fields, sizeBox, 0, 1);
        AddOption(fields, typeBox, 1, 0);
        AddOption(fields, themeBox, 1, 1);

        VoxelWorldOptions ReadOptions() => new()
        {
            Shape = ReadOption(shapeBox, VoxelWorldShape.Square),
            Size = ReadOption(sizeBox, VoxelWorldSize.Normal),
            Type = ReadOption(typeBox, VoxelWorldType.Inland),
            Theme = ReadOption(themeBox, VoxelWorldTheme.Normal),
        };
        void UpdateSummary()
        {
            var dimensions = VoxelWorld.GetDimensions(ReadOptions());
            summary.Text = $"{dimensions.Width} × {dimensions.Height} × {dimensions.Depth} blocks · {dimensions.ChunkCount} generated chunks";
        }
        shapeBox.SelectionChanged += (_, _) => UpdateSummary();
        sizeBox.SelectionChanged += (_, _) => UpdateSummary();
        typeBox.SelectionChanged += (_, _) => UpdateSummary();
        themeBox.SelectionChanged += (_, _) => UpdateSummary();
        UpdateSummary();

        var content = new StackPanel { Spacing = 12, MinWidth = 430 };
        content.Children.Add(new TextBlock
        {
            Text = "Indev-inspired finite-world controls. Generating replaces the saved BlockWorld.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(fields);
        content.Children.Add(summary);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "BlockWorld generation",
            Content = content,
            PrimaryButtonText = "Generate world",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        GenerateWorld(ReadOptions());
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reset BlockWorld?",
            Content = "This permanently replaces your saved world with a newly generated five-block landscape.",
            PrimaryButtonText = "Reset world",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        GenerateWorld(_world.Options);
    }

    private void ScheduleSave()
    {
        QueueSnapshot();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void QueueSnapshot() => AppServices.NativOs.UpdateVoxelWorld(_world.CreateSnapshot(), _world.Seed, _world.Options);

    private async Task SaveWorldAsync()
    {
        _saveTimer.Stop();
        QueueSnapshot();
        await AppServices.NativOs.SaveAsync();
        SaveStatusText.Text = $"Saved {DateTime.Now.ToString("t", CultureInfo.CurrentCulture)}";
    }

    private void UpdateHud()
    {
        var target = _target is { } value ? BlockName(value.Block) : "—";
        HudText.Text = $"XYZ {_position.X:00.0}  {_position.Y:00.0}  {_position.Z:00.0}\n{(_flight ? "FLIGHT" : "WALK")} · {BlockName(_selectedBlock)} · TARGET {target}\n{_world.Options.Type} · {ShapeName(_world.Options.Shape)} {_world.Options.Size} · {_world.Options.Theme} · {_world.ChunkCount} CHUNKS";
    }

    private Vector3 ViewDirection()
    {
        var cosPitch = MathF.Cos(_pitch);
        return Vector3.Normalize(new Vector3(MathF.Cos(_yaw) * cosPitch, MathF.Sin(_pitch), MathF.Sin(_yaw) * cosPitch));
    }

    private static Vector3 SpawnPosition(VoxelWorld world)
    {
        var x = world.Width / 2;
        var z = world.Depth / 2;
        return new Vector3(x + 0.5f, world.GetSurfaceHeight(x, z) + 2.62f, z + 0.5f);
    }

    private void GenerateWorld(VoxelWorldOptions options)
    {
        var seed = Random.Shared.Next(1, int.MaxValue);
        _world = new VoxelWorld(seed, options);
        _position = SpawnPosition(_world);
        _verticalVelocity = 0;
        _target = null;
        ScheduleSave();
        SaveStatusText.Text = $"Generated {_world.ChunkCount} chunks";
        UpdateHud();
    }

    private static ComboBox CreateOptionsBox<T>(string header, IReadOnlyList<T> values, T selected) where T : struct, Enum
    {
        var box = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var value in values)
        {
            var item = new ComboBoxItem
            {
                Content = value is VoxelWorldShape.Lengthened ? "Long" : value.ToString(),
                Tag = value,
                IsSelected = EqualityComparer<T>.Default.Equals(value, selected),
            };
            box.Items.Add(item);
        }
        return box;
    }

    private static T ReadOption<T>(ComboBox box, T fallback) where T : struct, Enum =>
        (box.SelectedItem as ComboBoxItem)?.Tag is T value ? value : fallback;

    private static void AddOption(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static string BlockName(VoxelBlock block) => block switch
    {
        VoxelBlock.Grass => "GRASS",
        VoxelBlock.Dirt => "DIRT",
        VoxelBlock.Stone => "STONE",
        VoxelBlock.Sand => "SAND",
        VoxelBlock.Wood => "WOOD",
        _ => "AIR",
    };

    private static string ShapeName(VoxelWorldShape shape) => shape == VoxelWorldShape.Lengthened ? "Long" : shape.ToString();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClipCursor(ref NativeRect rectangle);

    [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCursorClip(nint rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

}
