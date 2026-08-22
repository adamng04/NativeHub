using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NativeHub.Pages;
using NativeHub.Services;
using NativeHub.Services.NativOs;

namespace NativeHub.Controls;

public sealed partial class MinefieldControl : UserControl, INativOsAppLifecycle
{
    private static readonly SolidColorBrush HiddenBrush = Brush(53, 72, 96);
    private static readonly SolidColorBrush RevealedBrush = Brush(31, 42, 57);
    private static readonly SolidColorBrush MineBrush = Brush(151, 52, 64);
    private static readonly SolidColorBrush FlagBrush = Brush(189, 116, 31);
    private static readonly SolidColorBrush[] NumberBrushes =
    [
        Brush(225, 233, 243), Brush(101, 168, 255), Brush(105, 210, 139), Brush(255, 113, 113),
        Brush(180, 135, 255), Brush(255, 153, 92), Brush(86, 214, 221), Brush(230, 230, 230), Brush(170, 180, 194),
    ];

    private readonly DispatcherQueueTimer _timer;
    private MinefieldEngine _game = new();
    private Button[,] _buttons = new Button[1, 1];
    private int _elapsedSeconds;
    private bool _running;
    private bool _active = true;

    public MinefieldControl()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            if (!_running || !_active) return;
            _elapsedSeconds++;
            UpdateHeader();
        };
        BuildBoard();
        UpdateHeader();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active && _running) _timer.Start(); else _timer.Stop();
    }

    public void Close() => _timer.Stop();

    private void DifficultyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        NewGame(GetSelectedDifficulty());
    }

    private void NewGame_Click(object sender, RoutedEventArgs e) => NewGame(GetSelectedDifficulty());

    private void NewGameShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NewGame(GetSelectedDifficulty());
        args.Handled = true;
    }

    private void NewGame(MinefieldDifficulty difficulty)
    {
        _timer.Stop();
        _running = false;
        _elapsedSeconds = 0;
        _game = new MinefieldEngine(difficulty);
        BuildBoard();
        StatusText.Text = "Choose a cell to begin";
        UpdateHeader();
    }

    private void BuildBoard()
    {
        BoardGrid.Children.Clear();
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();
        for (var row = 0; row < _game.Rows; row++) BoardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < _game.Columns; column++) BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var size = _game.Difficulty switch { MinefieldDifficulty.Beginner => 36d, MinefieldDifficulty.Intermediate => 30d, _ => 26d };
        _buttons = new Button[_game.Rows, _game.Columns];
        for (var row = 0; row < _game.Rows; row++)
        for (var column = 0; column < _game.Columns; column++)
        {
            var button = new Button
            {
                Width = size,
                Height = size,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Tag = row * _game.Columns + column,
                Background = HiddenBrush,
            };
            AutomationProperties.SetName(button, $"Hidden cell, row {row + 1}, column {column + 1}");
            button.Click += Cell_Click;
            button.RightTapped += Cell_RightTapped;
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            BoardGrid.Children.Add(button);
            _buttons[row, column] = button;
        }
        UpdateBoard();
    }

    private async void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        if (!_game.IsStarted)
        {
            _running = true;
            if (_active) _timer.Start();
        }
        var result = _game.Reveal(index / _game.Columns, index % _game.Columns);
        await FinishMoveAsync(result);
    }

    private void Cell_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        if (_game.ToggleFlag(index / _game.Columns, index % _game.Columns))
        {
            StatusText.Text = "Flag updated";
            UpdateBoard();
            UpdateHeader();
        }
        e.Handled = true;
    }

    private async Task FinishMoveAsync(MinefieldMoveResult result)
    {
        switch (result)
        {
            case MinefieldMoveResult.Won:
                _running = false;
                _timer.Stop();
                StatusText.Text = $"Field cleared in {_elapsedSeconds} seconds";
                await AppServices.NativOs.RecordMinefieldBestAsync(_game.Difficulty, Math.Max(1, _elapsedSeconds));
                break;
            case MinefieldMoveResult.Lost:
                _running = false;
                _timer.Stop();
                StatusText.Text = "Mine disturbed — press F2 to try again";
                break;
            case MinefieldMoveResult.Revealed:
                StatusText.Text = "Field scan in progress";
                break;
        }
        UpdateBoard();
        UpdateHeader();
    }

    private void UpdateBoard()
    {
        for (var row = 0; row < _game.Rows; row++)
        for (var column = 0; column < _game.Columns; column++)
        {
            var state = _game.GetCell(row, column);
            var button = _buttons[row, column];
            if (state.IsRevealed)
            {
                button.Background = state.IsMine ? MineBrush : RevealedBrush;
                button.Content = state.IsMine ? "✹" : state.AdjacentMines == 0 ? string.Empty : state.AdjacentMines.ToString(System.Globalization.CultureInfo.InvariantCulture);
                button.Foreground = state.IsMine ? NumberBrushes[0] : NumberBrushes[state.AdjacentMines];
                AutomationProperties.SetName(button, state.IsMine
                    ? $"Mine, row {row + 1}, column {column + 1}"
                    : $"Revealed cell, {state.AdjacentMines} adjacent mines, row {row + 1}, column {column + 1}");
            }
            else
            {
                button.Background = state.IsFlagged ? FlagBrush : HiddenBrush;
                button.Content = state.IsFlagged ? "⚑" : string.Empty;
                button.Foreground = NumberBrushes[0];
                AutomationProperties.SetName(button, $"{(state.IsFlagged ? "Flagged" : "Hidden")} cell, row {row + 1}, column {column + 1}");
            }
            button.IsEnabled = !_game.IsWon && !_game.IsLost;
        }
    }

    private void UpdateHeader()
    {
        MineText.Text = $"MINES {_game.MineCount - _game.FlagsPlaced:000}";
        TimerText.Text = $"TIME {_elapsedSeconds:000}";
        var best = AppServices.NativOs.GetMinefieldBest(_game.Difficulty);
        BestText.Text = best > 0 ? $"Best {best}s" : "Best —";
    }

    private MinefieldDifficulty GetSelectedDifficulty() =>
        Enum.TryParse<MinefieldDifficulty>((DifficultyBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var difficulty)
            ? difficulty
            : MinefieldDifficulty.Beginner;

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Windows.UI.Color.FromArgb(255, red, green, blue));
}
