using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NativeHub.Pages;
using NativeHub.Services;
using NativeHub.Services.NativOs;
using Windows.System;

namespace NativeHub.Controls;

public sealed partial class FallingBlocksControl : UserControl, INativOsAppLifecycle
{
    private static readonly SolidColorBrush EmptyBrush = Brush(11, 22, 37);
    private static readonly SolidColorBrush GhostBrush = Brush(55, 72, 92);
    private static readonly SolidColorBrush[] PieceBrushes =
    [
        Brush(65, 216, 230), Brush(244, 211, 68), Brush(166, 99, 235), Brush(83, 204, 104),
        Brush(236, 78, 83), Brush(70, 118, 235), Brush(238, 148, 57),
    ];

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Border[,] _cells = new Border[FallingBlocksEngine.Rows, FallingBlocksEngine.Columns];
    private FallingBlocksEngine _game = new();
    private bool _active = true;
    private bool _scoreRecorded;

    public FallingBlocksControl()
    {
        InitializeComponent();
        BuildGrid();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(800);
        _timer.Tick += async (_, _) =>
        {
            _game.Tick();
            await RenderAndRecordAsync();
        };
        _timer.Start();
        Render();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active && !_game.IsPaused && !_game.IsGameOver) _timer.Start(); else _timer.Stop();
    }

    public void Close() => _timer.Stop();

    private void BuildGrid()
    {
        for (var row = 0; row < FallingBlocksEngine.Rows; row++) BoardGrid.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < FallingBlocksEngine.Columns; column++) BoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < FallingBlocksEngine.Rows; row++)
        for (var column = 0; column < FallingBlocksEngine.Columns; column++)
        {
            var border = new Border { Margin = new Thickness(1), Background = EmptyBrush, CornerRadius = new CornerRadius(2) };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            BoardGrid.Children.Add(border);
            _cells[row, column] = border;
        }
        BuildPreviewGrid(HoldGrid);
        BuildPreviewGrid(NextGrid);
    }

    private static void BuildPreviewGrid(Grid grid)
    {
        for (var row = 0; row < 4; row++) grid.RowDefinitions.Add(new RowDefinition());
        for (var column = 0; column < 4; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
        {
            var border = new Border { Margin = new Thickness(1), Background = EmptyBrush, CornerRadius = new CornerRadius(2), Tag = row * 4 + column };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            grid.Children.Add(border);
        }
    }

    private async void RootControl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_game.IsGameOver && e.Key != VirtualKey.N) return;
        switch (e.Key)
        {
            case VirtualKey.Left: _game.Move(-1, 0); break;
            case VirtualKey.Right: _game.Move(1, 0); break;
            case VirtualKey.Down: _game.SoftDrop(); break;
            case VirtualKey.Up: _game.Rotate(1); break;
            case VirtualKey.Z: _game.Rotate(-1); break;
            case VirtualKey.Space: _game.HardDrop(); break;
            case VirtualKey.C: _game.Hold(); break;
            case VirtualKey.P: TogglePause(); break;
            case VirtualKey.N: StartNewGame(); break;
            default: return;
        }
        e.Handled = true;
        await RenderAndRecordAsync();
    }

    private void RootControl_PointerPressed(object sender, PointerRoutedEventArgs e) => Focus(FocusState.Programmatic);
    private async void Left_Click(object sender, RoutedEventArgs e) { _game.Move(-1, 0); await RenderAndRecordAsync(); Focus(FocusState.Programmatic); }
    private async void Right_Click(object sender, RoutedEventArgs e) { _game.Move(1, 0); await RenderAndRecordAsync(); Focus(FocusState.Programmatic); }
    private async void Rotate_Click(object sender, RoutedEventArgs e) { _game.Rotate(1); await RenderAndRecordAsync(); Focus(FocusState.Programmatic); }
    private async void Drop_Click(object sender, RoutedEventArgs e) { _game.HardDrop(); await RenderAndRecordAsync(); Focus(FocusState.Programmatic); }
    private void Pause_Click(object sender, RoutedEventArgs e) => TogglePause();
    private void NewGame_Click(object sender, RoutedEventArgs e) => StartNewGame();

    private void TogglePause()
    {
        if (_game.IsGameOver) return;
        _game.IsPaused = !_game.IsPaused;
        if (_game.IsPaused) _timer.Stop(); else if (_active) _timer.Start();
        Render();
    }

    private void StartNewGame()
    {
        _game = new FallingBlocksEngine();
        _scoreRecorded = false;
        if (_active) _timer.Start();
        Render();
        Focus(FocusState.Programmatic);
    }

    private async Task RenderAndRecordAsync()
    {
        if (_game.IsGameOver && !_scoreRecorded)
        {
            _scoreRecorded = true;
            _timer.Stop();
            await AppServices.NativOs.RecordFallingBlocksBestAsync(_game.Score);
        }
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(90, 800 - (_game.Level - 1) * 65));
        Render();
    }

    private void Render()
    {
        for (var row = 0; row < FallingBlocksEngine.Rows; row++)
        for (var column = 0; column < FallingBlocksEngine.Columns; column++)
        {
            var value = _game.GetDisplayCell(row, column);
            _cells[row, column].Background = value > 0 ? PieceBrushes[value - 1] : _game.IsGhostCell(row, column) ? GhostBrush : EmptyBrush;
            _cells[row, column].BorderBrush = value > 0 ? new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255)) : null;
            _cells[row, column].BorderThickness = value > 0 ? new Thickness(1) : new Thickness(0);
        }
        RenderPreview(NextGrid, _game.NextKind);
        RenderPreview(HoldGrid, _game.HoldKind);
        ScoreText.Text = $"SCORE {_game.Score:0000000}";
        LinesText.Text = $"LINES {_game.Lines:000}";
        LevelText.Text = $"LEVEL {_game.Level:00}";
        BestScoreText.Text = $"BEST  {AppServices.NativOs.SaveData.FallingBlocksBestScore:0000000}";
        GameStatusText.Text = _game.IsGameOver ? "Game over · N starts a new game" : _game.IsPaused ? "Paused" : "Playing";
        PauseButton.Content = _game.IsPaused ? "Resume" : "Pause";
    }

    private static void RenderPreview(Grid grid, TetrominoKind? kind)
    {
        foreach (var child in grid.Children.OfType<Border>()) child.Background = EmptyBrush;
        if (kind is null) return;
        foreach (var cell in FallingBlocksEngine.GetCells(kind.Value, 0))
        {
            var target = grid.Children.OfType<Border>().First(item => (int)item.Tag == cell.Y * 4 + cell.X);
            target.Background = PieceBrushes[(int)kind.Value];
        }
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Windows.UI.Color.FromArgb(255, red, green, blue));
}
