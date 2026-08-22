using System.Numerics;

namespace NativeHub.Services.NativOs;

public enum VoxelBlock : byte
{
    Air,
    Grass,
    Dirt,
    Stone,
    Sand,
    Wood,
}

public enum VoxelWorldShape
{
    Square,
    Lengthened,
    Deep,
}

public enum VoxelWorldSize
{
    Small,
    Normal,
    Huge,
}

public enum VoxelWorldType
{
    Inland,
    Island,
    Floating,
    Flat,
}

public enum VoxelWorldTheme
{
    Normal,
    Hell,
    Paradise,
    Woods,
}

public sealed record VoxelWorldOptions
{
    public VoxelWorldShape Shape { get; init; } = VoxelWorldShape.Square;
    public VoxelWorldSize Size { get; init; } = VoxelWorldSize.Normal;
    public VoxelWorldType Type { get; init; } = VoxelWorldType.Inland;
    public VoxelWorldTheme Theme { get; init; } = VoxelWorldTheme.Normal;

    public VoxelWorldOptions Normalize() => new()
    {
        Shape = Enum.IsDefined(Shape) ? Shape : VoxelWorldShape.Square,
        Size = Enum.IsDefined(Size) ? Size : VoxelWorldSize.Normal,
        Type = Enum.IsDefined(Type) ? Type : VoxelWorldType.Inland,
        Theme = Enum.IsDefined(Theme) ? Theme : VoxelWorldTheme.Normal,
    };
}

public readonly record struct VoxelWorldDimensions(int Width, int Height, int Depth)
{
    public int ChunkCount => DivideRoundUp(Width, VoxelWorld.ChunkEdge) * DivideRoundUp(Depth, VoxelWorld.ChunkEdge);
    public int SnapshotLength => checked(Width * Height * Depth);

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
}

public readonly record struct VoxelHit(
    int X,
    int Y,
    int Z,
    int AdjacentX,
    int AdjacentY,
    int AdjacentZ,
    int NormalX,
    int NormalY,
    int NormalZ,
    float Distance,
    VoxelBlock Block);

public sealed class VoxelWorld
{
    public const int ChunkEdge = 16;
    public const int OriginalChunkCount = 9;
    private readonly byte[] _blocks;

    public VoxelWorld(int seed = 1999, VoxelWorldOptions? options = null, byte[]? snapshot = null)
    {
        Seed = seed;
        Options = (options ?? new VoxelWorldOptions()).Normalize();
        Dimensions = GetDimensions(Options);
        var validSnapshot = IsValidSnapshot(snapshot);
        _blocks = validSnapshot ? (byte[])snapshot!.Clone() : new byte[SnapshotLength];
        if (!validSnapshot) Generate();
    }

    public VoxelWorld(int seed, byte[]? snapshot) : this(seed, null, snapshot) { }

    public int Seed { get; }
    public VoxelWorldOptions Options { get; }
    public VoxelWorldDimensions Dimensions { get; }
    public int Width => Dimensions.Width;
    public int Height => Dimensions.Height;
    public int Depth => Dimensions.Depth;
    public int ChunkCount => Dimensions.ChunkCount;
    public int SnapshotLength => Dimensions.SnapshotLength;

    public static VoxelWorldDimensions GetDimensions(VoxelWorldOptions? options)
    {
        var normalized = (options ?? new VoxelWorldOptions()).Normalize();
        var dimensions = normalized.Size switch
        {
            VoxelWorldSize.Small => new VoxelWorldDimensions(96, 32, 80),
            VoxelWorldSize.Huge => new VoxelWorldDimensions(240, 48, 224),
            _ => new VoxelWorldDimensions(160, 40, 144),
        };

        return normalized.Shape switch
        {
            VoxelWorldShape.Lengthened => dimensions with
            {
                Width = dimensions.Width * 2,
                Depth = Math.Max(32, dimensions.Depth / 2),
            },
            VoxelWorldShape.Deep => dimensions with
            {
                Width = Math.Max(32, dimensions.Width / 2),
                Height = dimensions.Height * 4,
                Depth = Math.Max(32, dimensions.Depth / 2),
            },
            _ => dimensions,
        };
    }

    public VoxelBlock Get(int x, int y, int z) => IsInside(x, y, z) ? (VoxelBlock)_blocks[Index(x, y, z)] : VoxelBlock.Air;

    public bool Set(int x, int y, int z, VoxelBlock block)
    {
        if (!IsInside(x, y, z) || !Enum.IsDefined(block)) return false;
        _blocks[Index(x, y, z)] = (byte)block;
        return true;
    }

    public byte[] CreateSnapshot() => (byte[])_blocks.Clone();

    public int GetSurfaceHeight(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Depth) return 0;
        for (var y = Height - 1; y >= 0; y--)
            if (Get(x, y, z) != VoxelBlock.Air) return y;
        return 0;
    }

    public VoxelHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.LengthSquared() < 0.000001f || maxDistance <= 0) return null;
        direction = Vector3.Normalize(direction);
        var x = (int)MathF.Floor(origin.X);
        var y = (int)MathF.Floor(origin.Y);
        var z = (int)MathF.Floor(origin.Z);
        var previousX = x;
        var previousY = y;
        var previousZ = z;
        var stepX = Math.Sign(direction.X);
        var stepY = Math.Sign(direction.Y);
        var stepZ = Math.Sign(direction.Z);
        var deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.X);
        var deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Y);
        var deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Z);
        var maxX = InitialAxisDistance(origin.X, x, stepX, direction.X);
        var maxY = InitialAxisDistance(origin.Y, y, stepY, direction.Y);
        var maxZ = InitialAxisDistance(origin.Z, z, stepZ, direction.Z);
        var distance = 0f;
        var normalX = 0;
        var normalY = 0;
        var normalZ = 0;

        while (distance <= maxDistance)
        {
            if (IsInside(x, y, z) && Get(x, y, z) is { } block and not VoxelBlock.Air)
                return new VoxelHit(x, y, z, previousX, previousY, previousZ, normalX, normalY, normalZ, distance, block);

            previousX = x;
            previousY = y;
            previousZ = z;
            if (maxX <= maxY && maxX <= maxZ)
            {
                x += stepX;
                distance = maxX;
                maxX += deltaX;
                normalX = -stepX;
                normalY = 0;
                normalZ = 0;
            }
            else if (maxY <= maxZ)
            {
                y += stepY;
                distance = maxY;
                maxY += deltaY;
                normalX = 0;
                normalY = -stepY;
                normalZ = 0;
            }
            else
            {
                z += stepZ;
                distance = maxZ;
                maxZ += deltaZ;
                normalX = 0;
                normalY = 0;
                normalZ = -stepZ;
            }
        }
        return null;
    }

    public bool IsInside(int x, int y, int z) =>
        x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;

    private bool IsValidSnapshot(byte[]? snapshot) =>
        snapshot is { } value && value.Length == SnapshotLength && value.All(block => block <= (byte)VoxelBlock.Wood);

    private void Generate()
    {
        var chunksX = (Width + ChunkEdge - 1) / ChunkEdge;
        var chunksZ = (Depth + ChunkEdge - 1) / ChunkEdge;
        for (var chunkZ = 0; chunkZ < chunksZ; chunkZ++)
        for (var chunkX = 0; chunkX < chunksX; chunkX++)
            GenerateChunk(chunkX, chunkZ);

        GrowTrees();
    }

    private void GenerateChunk(int chunkX, int chunkZ)
    {
        var startX = chunkX * ChunkEdge;
        var startZ = chunkZ * ChunkEdge;
        var endX = Math.Min(Width, startX + ChunkEdge);
        var endZ = Math.Min(Depth, startZ + ChunkEdge);
        for (var z = startZ; z < endZ; z++)
        for (var x = startX; x < endX; x++)
        {
            if (Options.Type == VoxelWorldType.Floating) GenerateFloatingColumn(x, z);
            else GenerateGroundColumn(x, z);
        }
    }

    private void GenerateGroundColumn(int x, int z)
    {
        var baseLevel = Math.Clamp(Height / 5, 6, 22);
        var plains = Math.Sin((x + Seed * 0.013) * 0.033) * 0.75 +
                     Math.Cos((z - Seed * 0.009) * 0.029) * 0.65 +
                     Noise(x / 12, z / 12, 17) * 0.55;
        var surface = baseLevel + (int)Math.Round(plains);
        var beach = surface <= baseLevel - 1;

        switch (Options.Type)
        {
            case VoxelWorldType.Flat:
                surface = baseLevel;
                beach = Options.Theme == VoxelWorldTheme.Paradise && ((x + z) % 17 < 3);
                break;
            case VoxelWorldType.Island:
                var normalizedX = (x + 0.5 - Width / 2d) / (Width / 2d);
                var normalizedZ = (z + 0.5 - Depth / 2d) / (Depth / 2d);
                var distance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
                if (distance >= 0.97) return;
                surface += (int)Math.Round((1 - distance) * 8 - 5);
                beach = distance > 0.72 || surface <= baseLevel - 1;
                break;
        }

        surface = Math.Clamp(surface, 2, Height - 7);
        FillGround(x, z, surface, beach);
    }

    private void GenerateFloatingColumn(int x, int z)
    {
        var centerX = Width / 2d;
        var centerZ = Depth / 2d;
        var dx = x - centerX;
        var dz = z - centerZ;
        var normalizedX = dx / Math.Max(1, Width / 2d);
        var normalizedZ = dz / Math.Max(1, Depth / 2d);
        var edgeDistance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
        var field = Math.Sin((x + Seed * 0.021) * 0.083) + Math.Cos((z - Seed * 0.017) * 0.091) + Noise(x / 3, z / 3, 71) * 0.7;
        var spawnIsland = dx * dx + dz * dz < 12 * 12;
        if (!spawnIsland && (edgeDistance > 0.94 || field < 0.68)) return;

        var surface = Math.Clamp(Height / 2 + (int)Math.Round(field * 2.2), 9, Height - 8);
        var thickness = Math.Clamp(5 + (int)Math.Round((field + 1.5) * 1.6), 4, 10);
        FillFloatingLayer(x, z, surface, thickness);

        if (Options.Shape != VoxelWorldShape.Deep || spawnIsland) return;
        var secondary = Math.Cos((x - Seed * 0.014) * 0.071) + Math.Sin((z + Seed * 0.019) * 0.076) + Noise(x, z, 103) * 0.55;
        if (secondary < 1.05) return;
        var lowerSurface = Math.Clamp(Height / 3 + (int)Math.Round(secondary * 2), 7, surface - 12);
        FillFloatingLayer(x, z, lowerSurface, 4 + (int)Math.Abs(Noise(x, z, 121) * 3));
    }

    private void FillGround(int x, int z, int surface, bool beach)
    {
        for (var y = 0; y <= surface; y++)
            _blocks[Index(x, y, z)] = (byte)LayerBlock(y, surface, beach);
    }

    private void FillFloatingLayer(int x, int z, int surface, int thickness)
    {
        var bottom = Math.Max(1, surface - thickness);
        for (var y = bottom; y <= surface; y++)
            _blocks[Index(x, y, z)] = (byte)LayerBlock(y, surface, false);
    }

    private VoxelBlock LayerBlock(int y, int surface, bool beach)
    {
        if (Options.Theme == VoxelWorldTheme.Hell)
            return y == surface || y < surface - 2 ? VoxelBlock.Stone : VoxelBlock.Dirt;
        if (beach) return y >= surface - 2 ? VoxelBlock.Sand : VoxelBlock.Stone;
        if (y == surface) return VoxelBlock.Grass;
        return y >= surface - 2 ? VoxelBlock.Dirt : VoxelBlock.Stone;
    }

    private void GrowTrees()
    {
        var divisor = Options.Theme switch
        {
            VoxelWorldTheme.Woods => 70,
            VoxelWorldTheme.Paradise => 140,
            VoxelWorldTheme.Hell => 500,
            _ => 180,
        };
        var attempts = Math.Max(20, Width * Depth / divisor);
        var random = new Random(Seed ^ 0x5EED5EED);
        var centerX = Width / 2;
        var centerZ = Depth / 2;
        var planted = new List<(int X, int Z)>();
        for (var tree = 0; tree < attempts; tree++)
        {
            var x = random.Next(3, Width - 3);
            var z = random.Next(3, Depth - 3);
            if (Math.Abs(x - centerX) < 4 && Math.Abs(z - centerZ) < 4) continue;
            if (planted.Any(value => (value.X - x) * (value.X - x) + (value.Z - z) * (value.Z - z) < 25)) continue;
            var surface = GetSurfaceHeight(x, z);
            var ground = Get(x, surface, z);
            var canGrow = ground == VoxelBlock.Grass || (Options.Theme == VoxelWorldTheme.Hell && ground == VoxelBlock.Stone);
            var trunkHeight = random.Next(3, Options.Theme == VoxelWorldTheme.Woods ? 6 : 5);
            if (!canGrow || surface + trunkHeight + 2 >= Height) continue;

            GrowTree(x, surface, z, trunkHeight, random);
            planted.Add((x, z));
        }
    }

    private void GrowTree(int x, int surface, int z, int trunkHeight, Random random)
    {
        var crownY = surface + trunkHeight;
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            var radius = offsetY == 1 ? 1 : 2;
            for (var offsetZ = -radius; offsetZ <= radius; offsetZ++)
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var corner = Math.Abs(offsetX) == radius && Math.Abs(offsetZ) == radius;
                if (corner && random.Next(2) == 0) continue;
                var blockX = x + offsetX;
                var blockY = crownY + offsetY;
                var blockZ = z + offsetZ;
                if (Get(blockX, blockY, blockZ) == VoxelBlock.Air) Set(blockX, blockY, blockZ, VoxelBlock.Wood);
            }
        }

        for (var y = 1; y <= trunkHeight; y++) Set(x, surface + y, z, VoxelBlock.Wood);
    }

    private double Noise(int x, int z, int salt)
    {
        unchecked
        {
            var value = (uint)Seed;
            value ^= (uint)(x * 374761393);
            value = (value << 13) | (value >> 19);
            value ^= (uint)(z * 668265263);
            value ^= (uint)(salt * 2246822519L);
            value *= 3266489917U;
            value ^= value >> 16;
            return value / (double)uint.MaxValue * 2 - 1;
        }
    }

    private static float InitialAxisDistance(float origin, int cell, int step, float direction)
    {
        if (step == 0) return float.PositiveInfinity;
        var boundary = step > 0 ? cell + 1f : cell;
        return (boundary - origin) / direction;
    }

    private int Index(int x, int y, int z) => x + z * Width + y * Width * Depth;
}
