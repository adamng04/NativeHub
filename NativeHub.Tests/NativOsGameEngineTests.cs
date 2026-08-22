using NativeHub.Services.NativOs;
using System.Numerics;

namespace NativeHub.Tests;

[TestClass]
public sealed class NativOsGameEngineTests
{
    [TestMethod]
    public void Minefield_FirstRevealProtectsThreeByThreeArea()
    {
        var game = new MinefieldEngine(MinefieldDifficulty.Expert, 42);

        var result = game.Reveal(8, 15);

        Assert.AreNotEqual(MinefieldMoveResult.Lost, result);
        Assert.IsTrue(game.GetCell(8, 15).IsRevealed);
        for (var row = 7; row <= 9; row++)
        for (var column = 14; column <= 16; column++)
            Assert.IsFalse(game.GetCell(row, column).IsMine, $"Mine was placed in protected cell {row},{column}.");

        var mines = 0;
        for (var row = 0; row < game.Rows; row++)
        for (var column = 0; column < game.Columns; column++)
            if (game.GetCell(row, column).IsMine) mines++;
        Assert.AreEqual(game.MineCount, mines);
    }

    [TestMethod]
    public void Minefield_RevealingEverySafeCellWins()
    {
        var game = new MinefieldEngine(MinefieldDifficulty.Beginner, 7);
        _ = game.Reveal(4, 4);

        for (var row = 0; row < game.Rows; row++)
        for (var column = 0; column < game.Columns; column++)
            if (!game.GetCell(row, column).IsMine) _ = game.Reveal(row, column);

        Assert.IsTrue(game.IsWon);
        Assert.IsFalse(game.IsLost);
    }

    [TestMethod]
    public void Minefield_FlagsNeverExceedMineCount()
    {
        var game = new MinefieldEngine(MinefieldDifficulty.Beginner, 1);
        var placed = 0;
        for (var row = 0; row < game.Rows; row++)
        for (var column = 0; column < game.Columns; column++)
            if (game.ToggleFlag(row, column)) placed++;

        Assert.AreEqual(game.MineCount, placed);
        Assert.AreEqual(game.MineCount, game.FlagsPlaced);
    }

    [TestMethod]
    public void TetrominoBag_EachBagContainsAllSevenPieces()
    {
        var bag = new TetrominoBag(9);
        var first = Enumerable.Range(0, 7).Select(_ => bag.Next()).ToHashSet();
        var second = Enumerable.Range(0, 7).Select(_ => bag.Next()).ToHashSet();

        Assert.AreEqual(7, first.Count);
        Assert.AreEqual(7, second.Count);
    }

    [TestMethod]
    public void FallingBlocks_HoldCanOnlyBeUsedOncePerPiece()
    {
        var game = new FallingBlocksEngine(4);
        var initial = game.CurrentKind;

        Assert.IsTrue(game.Hold());
        Assert.AreEqual(initial, game.HoldKind);
        Assert.IsFalse(game.Hold());
    }

    [TestMethod]
    public void FallingBlocks_HardDropClearsCompletedRowAndScores()
    {
        var game = new FallingBlocksEngine(3);
        for (var column = 0; column < FallingBlocksEngine.Columns; column++)
            if (column != 5) game.SetLockedCellForTesting(FallingBlocksEngine.Rows - 1, column, 1);
        game.SetCurrentForTesting(TetrominoKind.I, 1, 3, 0);

        _ = game.HardDrop();

        Assert.AreEqual(1, game.Lines);
        Assert.IsGreaterThanOrEqualTo(100, game.Score);
    }

    [TestMethod]
    public void VoxelWorld_SnapshotRoundTripsFiveBlockWorld()
    {
        var original = new VoxelWorld(1234);
        var snapshot = original.CreateSnapshot();
        var restored = new VoxelWorld(1234, original.Options, snapshot);

        Assert.HasCount(original.SnapshotLength, snapshot);
        CollectionAssert.AreEqual(snapshot, restored.CreateSnapshot());
        Assert.IsTrue(snapshot.All(value => value <= (byte)VoxelBlock.Wood));
        foreach (var block in Enum.GetValues<VoxelBlock>().Where(value => value != VoxelBlock.Air))
            Assert.Contains(block, snapshot.Select(value => (VoxelBlock)value));
    }

    [TestMethod]
    public void VoxelWorld_RaycastReturnsHitAndPlacementNeighbor()
    {
        var options = new VoxelWorldOptions { Size = VoxelWorldSize.Small };
        var dimensions = VoxelWorld.GetDimensions(options);
        var empty = new byte[dimensions.SnapshotLength];
        var world = new VoxelWorld(1, options, empty);
        Assert.IsTrue(world.Set(5, 5, 5, VoxelBlock.Stone));

        var hit = world.Raycast(new Vector3(1.5f, 5.5f, 5.5f), Vector3.UnitX, 10);

        Assert.IsNotNull(hit);
        Assert.AreEqual((5, 5, 5), (hit.Value.X, hit.Value.Y, hit.Value.Z));
        Assert.AreEqual((4, 5, 5), (hit.Value.AdjacentX, hit.Value.AdjacentY, hit.Value.AdjacentZ));
        Assert.AreEqual(-1, hit.Value.NormalX);
    }

    [TestMethod]
    public void VoxelRenderer_ProducesOpaqueBgraFramebuffer()
    {
        var world = new VoxelWorld(22);
        var centerX = world.Width / 2;
        var centerZ = world.Depth / 2;
        var camera = new Vector3(centerX + 0.5f, world.GetSurfaceHeight(centerX, centerZ) + 2.6f, centerZ + 0.5f);
        var renderer = new VoxelRenderer(48, 27);

        var pixels = renderer.Render(world, camera, 0, -0.15f);

        Assert.HasCount(48 * 27 * 4, pixels);
        for (var offset = 3; offset < pixels.Length; offset += 4) Assert.AreEqual(255, pixels[offset]);
    }

    [TestMethod]
    public void VoxelWorld_DefaultGenerationIsTenTimesThePreviousChunkCount()
    {
        var world = new VoxelWorld(19);

        Assert.AreEqual(VoxelWorld.OriginalChunkCount * 10, world.ChunkCount);
        Assert.AreEqual(160, world.Width);
        Assert.AreEqual(144, world.Depth);
    }

    [TestMethod]
    public void VoxelWorld_DefaultGenerationCreatesMostlyFlatWoodedPlains()
    {
        var world = new VoxelWorld(73, new VoxelWorldOptions { Size = VoxelWorldSize.Small });
        var groundHeights = new List<int>(world.Width * world.Depth);
        var woodBlocks = 0;

        for (var z = 0; z < world.Depth; z++)
        for (var x = 0; x < world.Width; x++)
        {
            for (var y = 0; y < world.Height; y++)
                if (world.Get(x, y, z) == VoxelBlock.Wood) woodBlocks++;

            for (var y = world.Height - 1; y >= 0; y--)
            {
                var block = world.Get(x, y, z);
                if (block is VoxelBlock.Air or VoxelBlock.Wood) continue;
                groundHeights.Add(y);
                break;
            }
        }

        Assert.IsLessThanOrEqualTo(4, groundHeights.Max() - groundHeights.Min());
        Assert.IsGreaterThan(100, woodBlocks);
    }

    [TestMethod]
    public void VoxelWorld_IndevShapesChangeFiniteWorldDimensions()
    {
        var square = VoxelWorld.GetDimensions(new VoxelWorldOptions { Shape = VoxelWorldShape.Square });
        var longWorld = VoxelWorld.GetDimensions(new VoxelWorldOptions { Shape = VoxelWorldShape.Lengthened });
        var deep = VoxelWorld.GetDimensions(new VoxelWorldOptions { Shape = VoxelWorldShape.Deep });

        Assert.IsGreaterThan(square.Width, longWorld.Width);
        Assert.IsLessThan(square.Depth, longWorld.Depth);
        Assert.AreEqual(square.Height * 4, deep.Height);
        Assert.IsLessThan(square.Width, deep.Width);
        Assert.IsLessThan(square.Depth, deep.Depth);
    }

    [TestMethod]
    public void VoxelWorld_IndevTypesGenerateDistinctTerrain()
    {
        var island = new VoxelWorld(31, new VoxelWorldOptions { Size = VoxelWorldSize.Small, Type = VoxelWorldType.Island });
        var floating = new VoxelWorld(31, new VoxelWorldOptions { Size = VoxelWorldSize.Small, Type = VoxelWorldType.Floating });
        var flat = new VoxelWorld(31, new VoxelWorldOptions { Size = VoxelWorldSize.Small, Type = VoxelWorldType.Flat });

        Assert.AreEqual(VoxelBlock.Air, island.Get(0, 0, 0));
        Assert.AreEqual(VoxelBlock.Air, floating.Get(floating.Width / 2, 0, floating.Depth / 2));
        Assert.IsGreaterThan(0, floating.GetSurfaceHeight(floating.Width / 2, floating.Depth / 2));
        Assert.AreEqual(flat.GetSurfaceHeight(flat.Width / 2, flat.Depth / 2), flat.GetSurfaceHeight(flat.Width / 2 + 1, flat.Depth / 2));
    }

    [TestMethod]
    public async Task NativOsSession_PersistsScoresAndVoxelSnapshot()
    {
        var folder = Path.Combine(Path.GetTempPath(), "NativeHub.NativOs.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new VoxelWorldOptions
            {
                Shape = VoxelWorldShape.Lengthened,
                Size = VoxelWorldSize.Small,
                Type = VoxelWorldType.Island,
                Theme = VoxelWorldTheme.Woods,
            };
            var snapshot = new VoxelWorld(88, options).CreateSnapshot();
            var first = new NativOsSessionService(new NativeHub.Services.JsonStore(folder));
            await first.InitializeAsync();
            await first.RecordMinefieldBestAsync(MinefieldDifficulty.Beginner, 42);
            await first.RecordFallingBlocksBestAsync(1200);
            first.UpdateVoxelWorld(snapshot, 88, options);
            await first.SaveAsync();

            var restored = new NativOsSessionService(new NativeHub.Services.JsonStore(folder));
            await restored.InitializeAsync();

            Assert.AreEqual(42, restored.GetMinefieldBest(MinefieldDifficulty.Beginner));
            Assert.AreEqual(1200, restored.SaveData.FallingBlocksBestScore);
            Assert.AreEqual(88, restored.SaveData.VoxelSeed);
            Assert.AreEqual(options, restored.SaveData.VoxelOptions);
            CollectionAssert.AreEqual(snapshot, restored.SaveData.VoxelBlocks);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
