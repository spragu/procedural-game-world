using ProceduralWorld.Core.Threading;
using ProceduralWorld.Core.World;

namespace ProceduralWorld.Core.Rendering;

public enum MapView
{
    Biome,
    Danger,
    Elevation,
    Temperature,
    Moisture,
}

/// <summary>
/// The slice of a world to rasterise, and how densely to sample it.
///
/// A 4096x4096 world is 16.7 million tiles; rendering all of them produces a
/// 268 MB RGBA buffer that no browser wants to receive. Sampling with a stride
/// lets the caller ask for a cheap overview (stride 4 = 1/16th the pixels) or a
/// full-detail crop of a small region, using exactly the same code path.
/// </summary>
public readonly record struct RenderRegion(int X, int Y, int Width, int Height, int Stride)
{
    /// <summary>Renders the whole map, choosing a stride that keeps the output under <paramref name="maxDimension"/> pixels.</summary>
    public static RenderRegion Overview(WorldMap map, int maxDimension = 1024)
    {
        int longest = Math.Max(map.Width, map.Height);
        int stride = Math.Max(1, (int)MathF.Ceiling(longest / (float)maxDimension));
        return new RenderRegion(0, 0, map.Width, map.Height, stride);
    }

    /// <summary>Output width in pixels.</summary>
    public int PixelWidth => (Width + Stride - 1) / Stride;

    /// <summary>Output height in pixels.</summary>
    public int PixelHeight => (Height + Stride - 1) / Stride;

    internal RenderRegion Normalise(WorldMap map)
    {
        int stride = Math.Max(1, Stride);
        int x = Math.Clamp(X, 0, Math.Max(0, map.Width - 1));
        int y = Math.Clamp(Y, 0, Math.Max(0, map.Height - 1));
        int w = Width <= 0 ? map.Width - x : Math.Min(Width, map.Width - x);
        int h = Height <= 0 ? map.Height - y : Math.Min(Height, map.Height - y);
        return new RenderRegion(x, y, w, h, stride);
    }
}

public sealed record RenderOptions
{
    public MapView View { get; init; } = MapView.Biome;

    /// <summary>Directional hillshading strength. 0 disables relief entirely.</summary>
    public float Relief { get; init; } = 1f;

    /// <summary>Tints land by its danger tier so difficulty rings read at a glance.</summary>
    public bool ShowDangerTint { get; init; } = true;

    /// <summary>Draws a thin dark line along the land/water boundary.</summary>
    public bool ShowCoastline { get; init; } = true;

    /// <summary>Which slice of the map to draw. Defaults to the whole map at full detail.</summary>
    public RenderRegion? Region { get; init; }
}

/// <summary>
/// Rasterises a <see cref="WorldMap"/> to a straight RGBA8 buffer. The caller is
/// responsible for scaling it up (the Blazor client hands it to a canvas and lets
/// the GPU do nearest-neighbour zoom).
/// </summary>
public static class WorldRenderer
{
    /// <summary>Number of bytes <see cref="RenderRgba"/> will return for the given options.</summary>
    public static int BufferSize(WorldMap map, RenderOptions? options = null)
    {
        var region = (options?.Region ?? new RenderRegion(0, 0, map.Width, map.Height, 1)).Normalise(map);
        return region.PixelWidth * region.PixelHeight * 4;
    }

    /// <summary>Renders to a tightly packed RGBA buffer of PixelWidth * PixelHeight * 4 bytes.</summary>
    public static byte[] RenderRgba(WorldMap map, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        options ??= new RenderOptions();

        var region = (options.Region ?? new RenderRegion(0, 0, map.Width, map.Height, 1)).Normalise(map);

        int pw = region.PixelWidth;
        int ph = region.PixelHeight;
        var buffer = GC.AllocateUninitializedArray<byte>(pw * ph * 4);
        float sea = map.Options.SeaLevel;
        int stride = region.Stride;

        // Rows are independent, so large renders scale across cores. Dedicated
        // threads rather than the pool, for the reasons documented on RowPartitioner.
        int degree = pw * ph >= 65_536 ? Environment.ProcessorCount : 1;

        RowPartitioner.For(ph, degree, py =>
        {
            int ty = region.Y + py * stride;
            int o = py * pw * 4;

            for (int px = 0; px < pw; px++)
            {
                int tx = region.X + px * stride;
                var tile = map.Clamped(tx, ty);
                var color = ColorOf(map, tile, tx, ty, stride, sea, options);

                buffer[o] = color.R;
                buffer[o + 1] = color.G;
                buffer[o + 2] = color.B;
                buffer[o + 3] = 255;
                o += 4;
            }
        });

        return buffer;
    }

    private static Rgb ColorOf(WorldMap map, in WorldTile tile, int x, int y, int stride, float sea, RenderOptions options)
    {
        Rgb color = options.View switch
        {
            MapView.Danger => DangerRamp(tile),
            MapView.Elevation => ElevationRamp(tile, sea),
            MapView.Temperature => TemperatureRamp(tile),
            MapView.Moisture => MoistureRamp(tile),
            _ => BiomeColor(tile, sea),
        };

        if (options.View != MapView.Biome) return color;

        // Directional hillshade from the north-west, the cartographic convention.
        if (options.Relief > 0f && !tile.IsWater)
        {
            float shade = Hillshade(map, x, y, stride);
            color = color.Scale(1f + (shade - 1f) * options.Relief);
        }

        // Depth darkening so open water reads as volume rather than a flat plane.
        if (tile.IsWater)
        {
            float depth = tile.WaterDepth(sea);
            color = color.Scale(1f - depth * 0.35f);
        }

        if (options.ShowDangerTint && !tile.IsWater)
        {
            var tint = BiomePalette.DangerColor(tile.Tier);
            float amount = Math.Clamp(tile.Danger, 0f, 1f) * 0.16f;
            color = Rgb.Lerp(color, tint, amount);
        }

        if (options.ShowCoastline && IsCoastEdge(map, x, y, stride))
            color = color.Scale(0.62f);

        return color;
    }

    private static Rgb BiomeColor(in WorldTile tile, float sea)
    {
        // Use a per-biome-relevant scalar to pick where inside the biome's own
        // gradient this tile sits, so a forest is not one flat green rectangle.
        float t = tile.IsWater
            ? 1f - tile.WaterDepth(sea)
            : Math.Clamp(tile.LandHeight(sea) * 1.8f + tile.Moisture * 0.35f, 0f, 1f);

        return BiomePalette.Shade(tile.Biome, t);
    }

    /// <summary>
    /// Samples neighbours a full stride away so relief stays visible when the map is
    /// downsampled - taking adjacent tiles at stride 8 would compare two points that
    /// are 1/8th of a pixel apart and wash the shading out completely.
    /// </summary>
    private static float Hillshade(WorldMap map, int x, int y, int stride)
    {
        float l = map.Clamped(x - stride, y).Elevation;
        float r = map.Clamped(x + stride, y).Elevation;
        float u = map.Clamped(x, y - stride).Elevation;
        float d = map.Clamped(x, y + stride).Elevation;

        // Light from the north-west: positive when the slope faces the light.
        float lit = ((l - r) + (u - d)) * 6.5f / stride;
        return Math.Clamp(1f + lit, 0.62f, 1.42f);
    }

    private static bool IsCoastEdge(WorldMap map, int x, int y, int stride)
    {
        bool water = map.Clamped(x, y).IsWater;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (map.Clamped(x + dx * stride, y + dy * stride).IsWater != water) return true;
            }
        }

        return false;
    }

    private static Rgb DangerRamp(in WorldTile tile)
    {
        var c = BiomePalette.DangerColor(tile.Tier);
        float within = Math.Clamp(tile.Danger, 0f, 1f);
        return c.Scale(0.72f + within * 0.42f);
    }

    private static Rgb ElevationRamp(in WorldTile tile, float sea)
    {
        if (tile.Elevation < sea)
        {
            float d = tile.WaterDepth(sea);
            return Rgb.Lerp(new Rgb(120, 190, 225), new Rgb(6, 18, 46), d);
        }

        float t = tile.LandHeight(sea);
        if (t < 0.5f) return Rgb.Lerp(new Rgb(64, 128, 72), new Rgb(206, 190, 118), t * 2f);
        return Rgb.Lerp(new Rgb(206, 190, 118), new Rgb(252, 252, 255), (t - 0.5f) * 2f);
    }

    private static Rgb TemperatureRamp(in WorldTile tile)
    {
        float t = Math.Clamp(tile.Temperature, 0f, 1f);
        if (t < 0.5f) return Rgb.Lerp(new Rgb(48, 88, 190), new Rgb(238, 232, 180), t * 2f);
        return Rgb.Lerp(new Rgb(238, 232, 180), new Rgb(190, 46, 40), (t - 0.5f) * 2f);
    }

    private static Rgb MoistureRamp(in WorldTile tile)
    {
        float t = Math.Clamp(tile.Moisture, 0f, 1f);
        return Rgb.Lerp(new Rgb(206, 176, 116), new Rgb(24, 78, 128), t);
    }
}
