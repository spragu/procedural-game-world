namespace ProceduralWorld.Core.World;

/// <summary>
/// Per-tile result of world generation.
///
/// Fields are stored quantised rather than as floats. A 4096x4096 world is 16.7
/// million tiles, so every byte per tile costs 16 MB. Packing to 12 bytes instead
/// of eight floats (32 bytes) is the difference between a 200 MB and a 536 MB
/// allocation, which is what makes extra-large worlds fit in a browser at all.
///
/// The quantisation sits well below the noise floor of the generator - elevation
/// keeps 16 bits, and the climate fields are only ever compared against jittered
/// thresholds - so nothing downstream can tell the difference.
/// </summary>
public readonly struct WorldTile
{
    private const float ByteToUnit = 1f / 255f;
    private const float SlopeScale = 8192f;

    private readonly byte _biome;
    private readonly byte _temperature;
    private readonly byte _moisture;
    private readonly byte _riverFlow;
    private readonly byte _danger;
    private readonly ushort _elevation;
    private readonly ushort _shoreDistance;
    private readonly ushort _slope;

    public WorldTile(
        BiomeId biome,
        float elevation,
        float temperature,
        float moisture,
        float shoreDistance,
        float riverFlow,
        float slope,
        float danger)
    {
        _biome = (byte)biome;
        _elevation = PackUnit16(elevation);
        _temperature = PackUnit8(temperature);
        _moisture = PackUnit8(moisture);
        _riverFlow = PackUnit8(riverFlow);
        _danger = PackUnit8(danger);
        _shoreDistance = (ushort)Math.Clamp((int)MathF.Round(shoreDistance), 0, ushort.MaxValue);
        _slope = (ushort)Math.Clamp((int)MathF.Round(slope * SlopeScale), 0, ushort.MaxValue);
    }

    public BiomeId Biome => (BiomeId)_biome;

    public float Elevation => _elevation * (1f / 65535f);

    public float Temperature => _temperature * ByteToUnit;

    public float Moisture => _moisture * ByteToUnit;

    public float RiverFlow => _riverFlow * ByteToUnit;

    public float Danger => _danger * ByteToUnit;

    public float ShoreDistance => _shoreDistance;

    public float Slope => _slope * (1f / SlopeScale);

    public bool IsWater => Biomes.Get(Biome).IsWater;

    /// <summary>Height above sea level normalised to [0, 1]; 0 for anything submerged.</summary>
    public float LandHeight(float seaLevel)
    {
        float e = Elevation;
        return e <= seaLevel ? 0f : (e - seaLevel) / MathF.Max(1e-4f, 1f - seaLevel);
    }

    /// <summary>Depth below sea level normalised to [0, 1]; 0 for anything above water.</summary>
    public float WaterDepth(float seaLevel)
    {
        float e = Elevation;
        return e >= seaLevel ? 0f : (seaLevel - e) / MathF.Max(1e-4f, seaLevel);
    }

    /// <summary>Continuous danger banded into the six discrete difficulty tiers.</summary>
    public DangerTier Tier => DangerScale.ToTier(Danger);

    /// <summary>Suggested character level for this tile, 1 through 100.</summary>
    public int SuggestedLevel => DangerScale.ToLevel(Danger);

    private static byte PackUnit8(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    private static ushort PackUnit16(float v) => (ushort)(Math.Clamp(v, 0f, 1f) * 65535f + 0.5f);
}

/// <summary>Shared mapping between the continuous danger field and player-facing bands.</summary>
public static class DangerScale
{
    public static DangerTier ToTier(float danger) => danger switch
    {
        < 0.16f => DangerTier.Safe,
        < 0.34f => DangerTier.Low,
        < 0.52f => DangerTier.Moderate,
        < 0.70f => DangerTier.High,
        < 0.86f => DangerTier.Severe,
        _ => DangerTier.Lethal,
    };

    public static int ToLevel(float danger) =>
        Math.Clamp(1 + (int)MathF.Round(Math.Clamp(danger, 0f, 1f) * 99f), 1, 100);

    public static string Label(DangerTier tier) => tier switch
    {
        DangerTier.Safe => "Safe",
        DangerTier.Low => "Low threat",
        DangerTier.Moderate => "Moderate threat",
        DangerTier.High => "High threat",
        DangerTier.Severe => "Severe threat",
        _ => "Lethal",
    };
}

/// <summary>
/// An immutable generated world: a flat grid of <see cref="WorldTile"/> plus the
/// options it was produced from, so a map can always be regenerated exactly.
/// </summary>
public sealed class WorldMap
{
    private readonly WorldTile[] _tiles;

    internal WorldMap(WorldGenerationOptions options, WorldTile[] tiles)
    {
        Options = options;
        Width = options.Width;
        Height = options.Height;
        _tiles = tiles;
    }

    public WorldGenerationOptions Options { get; }

    public int Width { get; }

    public int Height { get; }

    public int TileCount => _tiles.Length;

    public ReadOnlySpan<WorldTile> Tiles => _tiles;

    public WorldTile this[int x, int y] => _tiles[Index(x, y)];

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>Reads a tile, clamping the coordinates into range. Handy for neighbour taps.</summary>
    public WorldTile Clamped(int x, int y) =>
        _tiles[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    public bool TryGet(int x, int y, out WorldTile tile)
    {
        if (!InBounds(x, y))
        {
            tile = default;
            return false;
        }

        tile = _tiles[y * Width + x];
        return true;
    }

    public int Index(int x, int y)
    {
        if (!InBounds(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside {Width}x{Height}.");

        return y * Width + x;
    }

    /// <summary>Share of tiles occupied by each biome, useful for tuning and legends.</summary>
    public IReadOnlyDictionary<BiomeId, float> BiomeDistribution()
    {
        var counts = new int[Biomes.Count];
        foreach (var tile in _tiles) counts[(int)tile.Biome]++;

        var result = new Dictionary<BiomeId, float>(Biomes.Count);
        float inv = _tiles.Length == 0 ? 0f : 1f / _tiles.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0) result[(BiomeId)i] = counts[i] * inv;
        }

        return result;
    }

    /// <summary>Share of tiles in each difficulty tier.</summary>
    public IReadOnlyDictionary<DangerTier, float> DangerDistribution()
    {
        var counts = new int[6];
        foreach (var tile in _tiles) counts[(int)tile.Tier]++;

        var result = new Dictionary<DangerTier, float>(6);
        float inv = _tiles.Length == 0 ? 0f : 1f / _tiles.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0) result[(DangerTier)i] = counts[i] * inv;
        }

        return result;
    }
}
