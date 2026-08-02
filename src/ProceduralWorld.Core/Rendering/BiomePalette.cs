using ProceduralWorld.Core.World;

namespace ProceduralWorld.Core.Rendering;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb Lerp(Rgb a, Rgb b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgb(
            (byte)MathF.Round(a.R + (b.R - a.R) * t),
            (byte)MathF.Round(a.G + (b.G - a.G) * t),
            (byte)MathF.Round(a.B + (b.B - a.B) * t));
    }

    public Rgb Scale(float factor)
    {
        return new Rgb(
            (byte)Math.Clamp(MathF.Round(R * factor), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(G * factor), 0f, 255f),
            (byte)Math.Clamp(MathF.Round(B * factor), 0f, 255f));
    }
}

/// <summary>
/// Flat colour approximations for every biome, plus the near/far shades used to
/// give each biome internal variation instead of a single dead fill.
/// </summary>
public static class BiomePalette
{
    private static readonly Rgb[] Base = new Rgb[Biomes.Count];
    private static readonly Rgb[] Low = new Rgb[Biomes.Count];
    private static readonly Rgb[] High = new Rgb[Biomes.Count];

    static BiomePalette()
    {
        foreach (var id in Enum.GetValues<BiomeId>())
        {
            (Rgb lo, Rgb mid, Rgb hi) = id switch
            {
                BiomeId.DeepOcean => (new Rgb(8, 22, 52), new Rgb(14, 36, 78), new Rgb(21, 52, 104)),
                BiomeId.Ocean => (new Rgb(19, 55, 108), new Rgb(28, 76, 140), new Rgb(40, 100, 168)),
                BiomeId.ShallowWater => (new Rgb(46, 118, 172), new Rgb(72, 156, 200), new Rgb(112, 194, 224)),
                BiomeId.CoralReef => (new Rgb(78, 168, 178), new Rgb(112, 198, 190), new Rgb(168, 226, 206)),
                BiomeId.Beach => (new Rgb(206, 186, 138), new Rgb(226, 208, 160), new Rgb(240, 227, 188)),
                BiomeId.RockyShore => (new Rgb(112, 112, 108), new Rgb(138, 138, 132), new Rgb(166, 166, 158)),
                BiomeId.SaltMarsh => (new Rgb(104, 124, 92), new Rgb(126, 148, 108), new Rgb(150, 172, 128)),
                BiomeId.River => (new Rgb(48, 108, 164), new Rgb(66, 140, 196), new Rgb(96, 172, 218)),
                BiomeId.Lake => (new Rgb(38, 96, 152), new Rgb(52, 122, 182), new Rgb(80, 152, 206)),
                BiomeId.Marsh => (new Rgb(62, 82, 62), new Rgb(80, 102, 74), new Rgb(102, 124, 92)),
                BiomeId.Grassland => (new Rgb(112, 150, 78), new Rgb(134, 174, 92), new Rgb(160, 196, 114)),
                BiomeId.Savanna => (new Rgb(168, 158, 88), new Rgb(192, 180, 104), new Rgb(212, 202, 128)),
                BiomeId.Shrubland => (new Rgb(126, 134, 82), new Rgb(148, 156, 96), new Rgb(172, 178, 118)),
                BiomeId.TemperateForest => (new Rgb(42, 88, 52), new Rgb(56, 110, 64), new Rgb(76, 134, 82)),
                BiomeId.Rainforest => (new Rgb(20, 72, 44), new Rgb(28, 94, 56), new Rgb(42, 118, 70)),
                BiomeId.BorealForest => (new Rgb(38, 74, 68), new Rgb(50, 92, 82), new Rgb(68, 112, 98)),
                BiomeId.Desert => (new Rgb(206, 176, 116), new Rgb(226, 198, 138), new Rgb(240, 218, 166)),
                BiomeId.Badlands => (new Rgb(148, 92, 62), new Rgb(174, 112, 74), new Rgb(198, 138, 96)),
                BiomeId.Tundra => (new Rgb(142, 148, 138), new Rgb(166, 172, 160), new Rgb(190, 196, 184)),
                BiomeId.AlpineMeadow => (new Rgb(110, 146, 104), new Rgb(132, 168, 120), new Rgb(158, 190, 142)),
                BiomeId.RockyMountain => (new Rgb(104, 100, 98), new Rgb(132, 128, 124), new Rgb(162, 158, 152)),
                BiomeId.SnowPeak => (new Rgb(216, 220, 228), new Rgb(234, 238, 244), new Rgb(250, 252, 255)),
                BiomeId.Glacier => (new Rgb(158, 196, 214), new Rgb(186, 216, 230), new Rgb(214, 236, 246)),
                BiomeId.AshWaste => (new Rgb(58, 54, 56), new Rgb(78, 72, 74), new Rgb(102, 96, 96)),
                BiomeId.Volcanic => (new Rgb(48, 26, 24), new Rgb(84, 34, 26), new Rgb(150, 58, 28)),
                BiomeId.Blightlands => (new Rgb(48, 26, 58), new Rgb(72, 36, 82), new Rgb(104, 54, 112)),
                _ => (new Rgb(120, 120, 120), new Rgb(150, 150, 150), new Rgb(180, 180, 180)),
            };

            Low[(int)id] = lo;
            Base[(int)id] = mid;
            High[(int)id] = hi;
        }
    }

    public static Rgb Of(BiomeId id) => Base[(int)id];

    public static string HexOf(BiomeId id) => Base[(int)id].ToHex();

    /// <summary>Blends between the dark and light variant of a biome using <paramref name="t"/> in [0, 1].</summary>
    public static Rgb Shade(BiomeId id, float t)
    {
        int i = (int)id;
        return t < 0.5f
            ? Rgb.Lerp(Low[i], Base[i], t * 2f)
            : Rgb.Lerp(Base[i], High[i], (t - 0.5f) * 2f);
    }

    public static Rgb DangerColor(DangerTier tier) => tier switch
    {
        DangerTier.Safe => new Rgb(86, 190, 128),
        DangerTier.Low => new Rgb(152, 200, 96),
        DangerTier.Moderate => new Rgb(226, 198, 82),
        DangerTier.High => new Rgb(230, 150, 60),
        DangerTier.Severe => new Rgb(220, 88, 62),
        _ => new Rgb(176, 56, 152),
    };
}
