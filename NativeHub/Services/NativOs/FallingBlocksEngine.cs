namespace NativeHub.Services.NativOs;

public enum TetrominoKind
{
    I,
    O,
    T,
    S,
    Z,
    J,
    L,
}

public readonly record struct FallingCell(int X, int Y);

public sealed class TetrominoBag
{
    private readonly Random _random;
    private readonly Queue<TetrominoKind> _queue = new();

    public TetrominoBag(int? seed = null) => _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;

    public TetrominoKind Next()
    {
        if (_queue.Count == 0) Refill();
        return _queue.Dequeue();
    }

    private void Refill()
    {
        var values = Enum.GetValues<TetrominoKind>();
        for (var index = values.Length - 1; index > 0; index--)
        {
            var swap = _random.Next(index + 1);
            (values[index], values[swap]) = (values[swap], values[index]);
        }
        foreach (var value in values) _queue.Enqueue(value);
    }
}

public sealed class FallingBlocksEngine
{
    public const int Rows = 20;
    public const int Columns = 10;
    private readonly int[,] _board = new int[Rows, Columns];
    private readonly TetrominoBag _bag;
    private TetrominoKind _queued;

    public FallingBlocksEngine(int? seed = null)
    {
        _bag = new TetrominoBag(seed);
        NewGame();
    }

    public TetrominoKind CurrentKind { get; private set; }
    public TetrominoKind NextKind => _queued;
    public TetrominoKind? HoldKind { get; private set; }
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }
    public int Rotation { get; private set; }
    public int Score { get; private set; }
    public int Lines { get; private set; }
    public int Level => 1 + Lines / 10;
    public bool IsGameOver { get; private set; }
    public bool IsPaused { get; set; }
    public bool HoldUsed { get; private set; }
    public int GhostY
    {
        get
        {
            var y = CurrentY;
            while (CanOccupy(CurrentKind, Rotation, CurrentX, y + 1)) y++;
            return y;
        }
    }

    public void NewGame()
    {
        Array.Clear(_board);
        Score = 0;
        Lines = 0;
        HoldKind = null;
        IsGameOver = false;
        IsPaused = false;
        _queued = _bag.Next();
        SpawnNext();
    }

    public int GetLockedCell(int row, int column)
    {
        ValidateCell(row, column);
        return _board[row, column];
    }

    public int GetDisplayCell(int row, int column)
    {
        ValidateCell(row, column);
        var cell = _board[row, column];
        if (cell != 0) return cell;
        return Occupies(CurrentKind, Rotation, CurrentX, CurrentY, column, row) ? (int)CurrentKind + 1 : 0;
    }

    public bool IsGhostCell(int row, int column) =>
        !IsGameOver && _board[row, column] == 0 && GhostY != CurrentY && Occupies(CurrentKind, Rotation, CurrentX, GhostY, column, row);

    internal void SetLockedCellForTesting(int row, int column, int value)
    {
        ValidateCell(row, column);
        _board[row, column] = value;
    }

    internal void SetCurrentForTesting(TetrominoKind kind, int rotation, int x, int y)
    {
        CurrentKind = kind;
        Rotation = rotation;
        CurrentX = x;
        CurrentY = y;
    }

    public bool Move(int horizontal, int vertical)
    {
        if (IsGameOver || IsPaused || !CanOccupy(CurrentKind, Rotation, CurrentX + horizontal, CurrentY + vertical)) return false;
        CurrentX += horizontal;
        CurrentY += vertical;
        return true;
    }

    public bool Rotate(int direction)
    {
        if (IsGameOver || IsPaused) return false;
        var targetRotation = (Rotation + direction + 4) % 4;
        foreach (var kick in new (int X, int Y)[] { (0, 0), (-1, 0), (1, 0), (-2, 0), (2, 0), (0, -1) })
        {
            if (!CanOccupy(CurrentKind, targetRotation, CurrentX + kick.X, CurrentY + kick.Y)) continue;
            CurrentX += kick.X;
            CurrentY += kick.Y;
            Rotation = targetRotation;
            return true;
        }
        return false;
    }

    public bool SoftDrop()
    {
        if (!Move(0, 1)) return false;
        Score++;
        return true;
    }

    public int HardDrop()
    {
        if (IsGameOver || IsPaused) return 0;
        var distance = 0;
        while (Move(0, 1)) distance++;
        Score += distance * 2;
        LockPiece();
        return distance;
    }

    public void Tick()
    {
        if (IsGameOver || IsPaused) return;
        if (!Move(0, 1)) LockPiece();
    }

    public bool Hold()
    {
        if (IsGameOver || IsPaused || HoldUsed) return false;
        var outgoing = CurrentKind;
        if (HoldKind is { } held)
        {
            CurrentKind = held;
            ResetCurrentPiece();
        }
        else
        {
            SpawnNext();
        }
        HoldKind = outgoing;
        HoldUsed = true;
        if (!CanOccupy(CurrentKind, Rotation, CurrentX, CurrentY)) IsGameOver = true;
        return true;
    }

    public static IReadOnlyList<FallingCell> GetCells(TetrominoKind kind, int rotation) => Shapes[(int)kind][(rotation % 4 + 4) % 4];

    private void LockPiece()
    {
        foreach (var cell in GetCells(CurrentKind, Rotation))
        {
            var x = CurrentX + cell.X;
            var y = CurrentY + cell.Y;
            if (y < 0) { IsGameOver = true; return; }
            _board[y, x] = (int)CurrentKind + 1;
        }

        var cleared = ClearFullRows();
        if (cleared > 0)
        {
            Lines += cleared;
            Score += (cleared switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 }) * Level;
        }
        HoldUsed = false;
        SpawnNext();
    }

    private int ClearFullRows()
    {
        var cleared = 0;
        for (var row = Rows - 1; row >= 0; row--)
        {
            var full = true;
            for (var column = 0; column < Columns; column++)
                if (_board[row, column] == 0) { full = false; break; }
            if (!full) continue;
            cleared++;
            for (var moveRow = row; moveRow > 0; moveRow--)
            for (var column = 0; column < Columns; column++)
                _board[moveRow, column] = _board[moveRow - 1, column];
            for (var column = 0; column < Columns; column++) _board[0, column] = 0;
            row++;
        }
        return cleared;
    }

    private void SpawnNext()
    {
        CurrentKind = _queued;
        _queued = _bag.Next();
        ResetCurrentPiece();
        HoldUsed = false;
        if (!CanOccupy(CurrentKind, Rotation, CurrentX, CurrentY)) IsGameOver = true;
    }

    private void ResetCurrentPiece()
    {
        CurrentX = 3;
        CurrentY = 0;
        Rotation = 0;
    }

    private bool CanOccupy(TetrominoKind kind, int rotation, int originX, int originY)
    {
        foreach (var cell in GetCells(kind, rotation))
        {
            var x = originX + cell.X;
            var y = originY + cell.Y;
            if (x < 0 || x >= Columns || y >= Rows) return false;
            if (y >= 0 && _board[y, x] != 0) return false;
        }
        return true;
    }

    private static bool Occupies(TetrominoKind kind, int rotation, int originX, int originY, int x, int y) =>
        GetCells(kind, rotation).Any(cell => originX + cell.X == x && originY + cell.Y == y);

    private static void ValidateCell(int row, int column)
    {
        if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
    }

    private static readonly FallingCell[][][] Shapes =
    [
        // I
        [
            [new(0,1), new(1,1), new(2,1), new(3,1)], [new(2,0), new(2,1), new(2,2), new(2,3)],
            [new(0,2), new(1,2), new(2,2), new(3,2)], [new(1,0), new(1,1), new(1,2), new(1,3)],
        ],
        // O
        [
            [new(1,0), new(2,0), new(1,1), new(2,1)], [new(1,0), new(2,0), new(1,1), new(2,1)],
            [new(1,0), new(2,0), new(1,1), new(2,1)], [new(1,0), new(2,0), new(1,1), new(2,1)],
        ],
        // T
        [
            [new(1,0), new(0,1), new(1,1), new(2,1)], [new(1,0), new(1,1), new(2,1), new(1,2)],
            [new(0,1), new(1,1), new(2,1), new(1,2)], [new(1,0), new(0,1), new(1,1), new(1,2)],
        ],
        // S
        [
            [new(1,0), new(2,0), new(0,1), new(1,1)], [new(1,0), new(1,1), new(2,1), new(2,2)],
            [new(1,1), new(2,1), new(0,2), new(1,2)], [new(0,0), new(0,1), new(1,1), new(1,2)],
        ],
        // Z
        [
            [new(0,0), new(1,0), new(1,1), new(2,1)], [new(2,0), new(1,1), new(2,1), new(1,2)],
            [new(0,1), new(1,1), new(1,2), new(2,2)], [new(1,0), new(0,1), new(1,1), new(0,2)],
        ],
        // J
        [
            [new(0,0), new(0,1), new(1,1), new(2,1)], [new(1,0), new(2,0), new(1,1), new(1,2)],
            [new(0,1), new(1,1), new(2,1), new(2,2)], [new(1,0), new(1,1), new(0,2), new(1,2)],
        ],
        // L
        [
            [new(2,0), new(0,1), new(1,1), new(2,1)], [new(1,0), new(1,1), new(1,2), new(2,2)],
            [new(0,1), new(1,1), new(2,1), new(0,2)], [new(0,0), new(1,0), new(1,1), new(1,2)],
        ],
    ];
}
