using System.Numerics;

namespace NativeHub.Services.NativOs;

public sealed class VoxelRenderer(int width = 256, int height = 144)
{
    public int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));

    public byte[] Render(VoxelWorld world, Vector3 camera, float yaw, float pitch, VoxelHit? target = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var pixels = new byte[Width * Height * 4];
        var cosPitch = MathF.Cos(pitch);
        var forward = Vector3.Normalize(new Vector3(MathF.Cos(yaw) * cosPitch, MathF.Sin(pitch), MathF.Sin(yaw) * cosPitch));
        var right = Vector3.Normalize(new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw)));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var horizontalFov = MathF.Tan(78f * MathF.PI / 360f);
        var verticalFov = horizontalFov * Height / Width;

        Parallel.For(0, Height, row =>
        {
            var viewY = (1f - (row + 0.5f) * 2f / Height) * verticalFov;
            for (var column = 0; column < Width; column++)
            {
                var viewX = ((column + 0.5f) * 2f / Width - 1f) * horizontalFov;
                var direction = Vector3.Normalize(forward + right * viewX + up * viewY);
                var hit = world.Raycast(camera, direction, 30f);
                var (red, green, blue) = hit is { } voxel
                    ? Shade(voxel, direction, target, world.Options.Theme)
                    : Sky(direction, world.Options.Theme);
                var offset = (row * Width + column) * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = 255;
            }
        });

        DrawCrosshair(pixels);
        return pixels;
    }

    private static (byte Red, byte Green, byte Blue) Shade(VoxelHit hit, Vector3 direction, VoxelHit? target, VoxelWorldTheme theme)
    {
        var color = (theme, hit.Block) switch
        {
            (VoxelWorldTheme.Hell, VoxelBlock.Grass) => (Red: 111f, Green: 53f, Blue: 45f),
            (VoxelWorldTheme.Hell, VoxelBlock.Dirt) => (Red: 102f, Green: 48f, Blue: 43f),
            (VoxelWorldTheme.Hell, VoxelBlock.Stone) => (Red: 91f, Green: 69f, Blue: 72f),
            (VoxelWorldTheme.Hell, VoxelBlock.Sand) => (Red: 150f, Green: 99f, Blue: 68f),
            (VoxelWorldTheme.Hell, VoxelBlock.Wood) => (Red: 70f, Green: 48f, Blue: 48f),
            (VoxelWorldTheme.Paradise, VoxelBlock.Grass) => (Red: 86f, Green: 190f, Blue: 101f),
            (VoxelWorldTheme.Paradise, VoxelBlock.Sand) => (Red: 239f, Green: 218f, Blue: 153f),
            (VoxelWorldTheme.Woods, VoxelBlock.Grass) => (Red: 50f, Green: 130f, Blue: 65f),
            (VoxelWorldTheme.Woods, VoxelBlock.Wood) => (Red: 105f, Green: 72f, Blue: 42f),
            (_, VoxelBlock.Grass) => (Red: 73f, Green: 166f, Blue: 79f),
            (_, VoxelBlock.Dirt) => (Red: 145f, Green: 98f, Blue: 62f),
            (_, VoxelBlock.Stone) => (Red: 132f, Green: 139f, Blue: 148f),
            (_, VoxelBlock.Sand) => (Red: 218f, Green: 199f, Blue: 132f),
            (_, VoxelBlock.Wood) => (Red: 134f, Green: 88f, Blue: 49f),
            _ => (Red: 255f, Green: 0f, Blue: 255f),
        };
        var faceLight = hit.NormalY > 0 ? 1f : hit.NormalY < 0 ? 0.52f : hit.NormalX != 0 ? 0.78f : 0.66f;
        var selected = target is { } value && value.X == hit.X && value.Y == hit.Y && value.Z == hit.Z;
        if (selected) faceLight = Math.Min(1.25f, faceLight + 0.27f);
        var checker = ((hit.X * 17 + hit.Y * 31 + hit.Z * 13) & 3) * 0.025f + 0.94f;
        faceLight *= checker;
        var fog = Math.Clamp((hit.Distance - 12f) / 20f, 0, 0.72f);
        var sky = Sky(direction, theme);
        return (
            Blend(color.Red * faceLight, sky.Red, fog),
            Blend(color.Green * faceLight, sky.Green, fog),
            Blend(color.Blue * faceLight, sky.Blue, fog));
    }

    private static (byte Red, byte Green, byte Blue) Sky(Vector3 direction, VoxelWorldTheme theme)
    {
        var horizon = Math.Clamp(0.45f + direction.Y * 0.65f, 0, 1);
        return theme switch
        {
            VoxelWorldTheme.Hell => ((byte)(68 + 76 * horizon), (byte)(20 + 28 * horizon), (byte)(28 + 31 * horizon)),
            VoxelWorldTheme.Paradise => ((byte)(69 + 80 * horizon), (byte)(126 + 90 * horizon), (byte)(181 + 68 * horizon)),
            VoxelWorldTheme.Woods => ((byte)(35 + 38 * horizon), (byte)(69 + 65 * horizon), (byte)(83 + 66 * horizon)),
            _ => ((byte)(40 + 36 * horizon), (byte)(72 + 72 * horizon), (byte)(112 + 92 * horizon)),
        };
    }

    private void DrawCrosshair(byte[] pixels)
    {
        var centerX = Width / 2;
        var centerY = Height / 2;
        for (var offset = -5; offset <= 5; offset++)
        {
            if (Math.Abs(offset) <= 1) continue;
            SetPixel(pixels, centerX + offset, centerY, 245, 245, 245);
            SetPixel(pixels, centerX, centerY + offset, 245, 245, 245);
        }
    }

    private void SetPixel(byte[] pixels, int x, int y, byte red, byte green, byte blue)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        var offset = (y * Width + x) * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = 255;
    }

    private static byte Blend(float foreground, byte background, float amount) =>
        (byte)Math.Clamp(foreground * (1 - amount) + background * amount, 0, 255);
}
