namespace NativeHub.Services.NativOs;

public enum MinefieldDifficulty
{
    Beginner,
    Intermediate,
    Expert,
}

public enum MinefieldMoveResult
{
    None,
    Revealed,
    Won,
    Lost,
}

public readonly record struct MinefieldCellState(bool IsMine, bool IsRevealed, bool IsFlagged, int AdjacentMines);

public sealed class MinefieldEngine
{
    private readonly Random _random;
    private bool[,] _mines = new bool[1, 1];
    private bool[,] _revealed = new bool[1, 1];
    private bool[,] _flagged = new bool[1, 1];
    private byte[,] _adjacent = new byte[1, 1];

    public MinefieldEngine(MinefieldDifficulty difficulty = MinefieldDifficulty.Beginner, int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        Reset(difficulty);
    }

    public int Rows { get; private set; }
    public int Columns { get; private set; }
    public int MineCount { get; private set; }
    public int FlagsPlaced { get; private set; }
    public bool IsStarted { get; private set; }
    public bool IsWon { get; private set; }
    public bool IsLost { get; private set; }
    public MinefieldDifficulty Difficulty { get; private set; }

    public static (int Rows, int Columns, int Mines) GetSettings(MinefieldDifficulty difficulty) => difficulty switch
    {
        MinefieldDifficulty.Intermediate => (16, 16, 40),
        MinefieldDifficulty.Expert => (16, 30, 99),
        _ => (9, 9, 10),
    };

    public void Reset(MinefieldDifficulty difficulty)
    {
        Difficulty = difficulty;
        (Rows, Columns, MineCount) = GetSettings(difficulty);
        _mines = new bool[Rows, Columns];
        _revealed = new bool[Rows, Columns];
        _flagged = new bool[Rows, Columns];
        _adjacent = new byte[Rows, Columns];
        FlagsPlaced = 0;
        IsStarted = false;
        IsWon = false;
        IsLost = false;
    }

    public MinefieldCellState GetCell(int row, int column)
    {
        Validate(row, column);
        return new MinefieldCellState(_mines[row, column], _revealed[row, column], _flagged[row, column], _adjacent[row, column]);
    }

    public MinefieldMoveResult Reveal(int row, int column)
    {
        Validate(row, column);
        if (IsWon || IsLost || _flagged[row, column]) return MinefieldMoveResult.None;
        if (!IsStarted) PlaceMines(row, column);
        if (_revealed[row, column]) return Chord(row, column);

        if (_mines[row, column])
        {
            _revealed[row, column] = true;
            RevealAllMines();
            IsLost = true;
            return MinefieldMoveResult.Lost;
        }

        FloodReveal(row, column);
        return CompleteMove();
    }

    public bool ToggleFlag(int row, int column)
    {
        Validate(row, column);
        if (IsWon || IsLost || _revealed[row, column]) return false;
        if (!_flagged[row, column] && FlagsPlaced >= MineCount) return false;
        _flagged[row, column] = !_flagged[row, column];
        FlagsPlaced += _flagged[row, column] ? 1 : -1;
        return true;
    }

    public MinefieldMoveResult Chord(int row, int column)
    {
        Validate(row, column);
        if (IsWon || IsLost || !_revealed[row, column] || _adjacent[row, column] == 0) return MinefieldMoveResult.None;
        var neighbors = Neighbors(row, column).ToList();
        if (neighbors.Count(cell => _flagged[cell.Row, cell.Column]) != _adjacent[row, column]) return MinefieldMoveResult.None;

        foreach (var cell in neighbors.Where(cell => !_flagged[cell.Row, cell.Column] && !_revealed[cell.Row, cell.Column]))
        {
            if (_mines[cell.Row, cell.Column])
            {
                _revealed[cell.Row, cell.Column] = true;
                RevealAllMines();
                IsLost = true;
                return MinefieldMoveResult.Lost;
            }
            FloodReveal(cell.Row, cell.Column);
        }
        return CompleteMove();
    }

    private void PlaceMines(int safeRow, int safeColumn)
    {
        var candidates = new List<(int Row, int Column)>(Rows * Columns);
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
            if (Math.Abs(row - safeRow) > 1 || Math.Abs(column - safeColumn) > 1)
                candidates.Add((row, column));

        for (var index = candidates.Count - 1; index > 0; index--)
        {
            var swap = _random.Next(index + 1);
            (candidates[index], candidates[swap]) = (candidates[swap], candidates[index]);
        }
        foreach (var cell in candidates.Take(MineCount)) _mines[cell.Row, cell.Column] = true;

        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
            _adjacent[row, column] = (byte)Neighbors(row, column).Count(cell => _mines[cell.Row, cell.Column]);
        IsStarted = true;
    }

    private void FloodReveal(int startRow, int startColumn)
    {
        var queue = new Queue<(int Row, int Column)>();
        queue.Enqueue((startRow, startColumn));
        while (queue.TryDequeue(out var cell))
        {
            if (_revealed[cell.Row, cell.Column] || _flagged[cell.Row, cell.Column] || _mines[cell.Row, cell.Column]) continue;
            _revealed[cell.Row, cell.Column] = true;
            if (_adjacent[cell.Row, cell.Column] != 0) continue;
            foreach (var neighbor in Neighbors(cell.Row, cell.Column))
                if (!_revealed[neighbor.Row, neighbor.Column]) queue.Enqueue(neighbor);
        }
    }

    private MinefieldMoveResult CompleteMove()
    {
        var hiddenSafeCell = false;
        for (var row = 0; row < Rows && !hiddenSafeCell; row++)
        for (var column = 0; column < Columns; column++)
            if (!_mines[row, column] && !_revealed[row, column]) { hiddenSafeCell = true; break; }
        if (!hiddenSafeCell)
        {
            IsWon = true;
            return MinefieldMoveResult.Won;
        }
        return MinefieldMoveResult.Revealed;
    }

    private void RevealAllMines()
    {
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
            if (_mines[row, column]) _revealed[row, column] = true;
    }

    private IEnumerable<(int Row, int Column)> Neighbors(int row, int column)
    {
        for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
        for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
        {
            if (rowOffset == 0 && columnOffset == 0) continue;
            var neighborRow = row + rowOffset;
            var neighborColumn = column + columnOffset;
            if (neighborRow >= 0 && neighborRow < Rows && neighborColumn >= 0 && neighborColumn < Columns)
                yield return (neighborRow, neighborColumn);
        }
    }

    private void Validate(int row, int column)
    {
        if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
    }
}
