namespace ProceduralWorld.Core.World;

/// <summary>
/// The climate sample handed to the classifier for a single tile.
/// </summary>
public readonly record struct ClimateSample(
    float Elevation,
    float Temperature,
    float Moisture,
    float ShoreDistance,
    float Slope,
    float RiverFlow,
    bool IsLake,
    float Reef,
    float Jitter,
    float Danger,
    float Volcanism);

/// <summary>
/// Turns a climate sample into a biome using a Whittaker-style temperature/moisture
/// diagram, with elevation and shore proximity overriding it near coasts and peaks,
/// and a radial danger field overriding it in the deep interior.
///
/// Transitions are made gradual by perturbing every decision threshold with a
/// per-tile jitter value. Instead of a hard line between, say, savanna and desert,
/// the two interleave across a band whose width is
/// <see cref="WorldGenerationOptions.TransitionSoftness"/>. The same trick is used
/// for the danger overrides, so the corrupted core dissolves into the surrounding
/// wilderness rather than appearing as a stamped-on circle.
/// </summary>
public static class BiomeClassifier
{
    public static BiomeId Classify(in ClimateSample s, WorldGenerationOptions options)
    {
        float sea = options.SeaLevel;
        float soft = options.TransitionSoftness;
        float j = s.Jitter * soft;

        if (s.Elevation < sea)
            return ClassifyWater(s, options, j);

        // Fresh water sitting on top of land.
        if (s.IsLake) return BiomeId.Lake;
        if (s.RiverFlow > 0.5f) return s.Temperature < 0.16f + j ? BiomeId.Glacier : BiomeId.River;

        var biome = ClassifyLand(s, options, j);
        return ApplyDangerOverride(biome, s, options, j);
    }

    private static BiomeId ClassifyWater(in ClimateSample s, WorldGenerationOptions options, float j)
    {
        float depth = (options.SeaLevel - s.Elevation) / MathF.Max(1e-4f, options.SeaLevel);

        // Ice shelf over very cold water.
        if (s.Temperature < 0.08f + j && depth < 0.55f) return BiomeId.Glacier;

        if (depth < 0.22f + j * 1.5f)
        {
            // Reefs only form in warm, calm, sunlit shallows.
            bool warm = s.Temperature > 0.62f + j;
            if (warm && s.Reef > 0.58f - j) return BiomeId.CoralReef;
            return BiomeId.ShallowWater;
        }

        if (depth < 0.64f + j * 2f) return BiomeId.Ocean;
        return BiomeId.DeepOcean;
    }

    private static BiomeId ClassifyLand(in ClimateSample s, WorldGenerationOptions options, float j)
    {
        float sea = options.SeaLevel;
        float height = (s.Elevation - sea) / MathF.Max(1e-4f, 1f - sea);
        float temp = Math.Clamp(s.Temperature + j, 0f, 1f);
        float moist = Math.Clamp(s.Moisture + j * 0.8f, 0f, 1f);

        // ---- Coastal band -------------------------------------------------
        // ShoreDistance is measured in tiles from the nearest water tile, so the
        // beach strip naturally widens on flat land and vanishes against cliffs.
        float coastReach = 1.6f + (1f - Math.Clamp(s.Slope * 6f, 0f, 1f)) * 3.4f + s.Jitter * 1.8f;

        if (s.ShoreDistance <= coastReach && height < 0.16f)
        {
            if (temp < 0.18f) return BiomeId.RockyShore;
            if (s.Slope > 0.055f + j) return BiomeId.RockyShore;
            if (moist > 0.68f - j && s.Slope < 0.02f) return BiomeId.SaltMarsh;
            return BiomeId.Beach;
        }

        // Flooded flats just inland of the marshes.
        if (height < 0.06f && moist > 0.74f - j && s.Slope < 0.025f)
            return temp < 0.22f ? BiomeId.Tundra : BiomeId.Marsh;

        // ---- Alpine band --------------------------------------------------
        // The treeline drops as the climate gets colder, which is why polar
        // regions show bare rock and snow at much lower elevations.
        float snowLine = 0.74f + temp * 0.22f + j;
        float rockLine = 0.58f + temp * 0.20f + j;
        float alpineLine = 0.46f + temp * 0.16f + j;

        if (height > snowLine)
            return moist > 0.55f - j && temp < 0.30f ? BiomeId.Glacier : BiomeId.SnowPeak;

        if (height > rockLine) return BiomeId.RockyMountain;

        if (height > alpineLine)
        {
            if (temp < 0.22f) return BiomeId.Tundra;
            if (moist < 0.28f + j) return BiomeId.Badlands;
            return BiomeId.AlpineMeadow;
        }

        // ---- Whittaker diagram -------------------------------------------
        if (temp < 0.10f && moist > 0.60f - j) return BiomeId.Glacier;

        if (temp < 0.20f) return BiomeId.Tundra;

        if (temp < 0.36f)
            return moist > 0.34f - j ? BiomeId.BorealForest : BiomeId.Tundra;

        if (temp < 0.58f)
        {
            if (moist < 0.22f + j) return BiomeId.Shrubland;
            if (moist < 0.42f + j) return BiomeId.Grassland;
            if (moist < 0.74f + j) return BiomeId.TemperateForest;
            return BiomeId.Marsh;
        }

        if (temp < 0.78f)
        {
            if (moist < 0.18f + j) return BiomeId.Desert;
            if (moist < 0.34f + j) return BiomeId.Shrubland;
            if (moist < 0.56f + j) return BiomeId.Grassland;
            if (moist < 0.78f + j) return BiomeId.TemperateForest;
            return BiomeId.Rainforest;
        }

        if (moist < 0.16f + j) return BiomeId.Desert;
        if (moist < 0.30f + j) return BiomeId.Badlands;
        if (moist < 0.52f + j) return BiomeId.Savanna;
        if (moist < 0.72f + j) return BiomeId.Shrubland;
        return BiomeId.Rainforest;
    }

    /// <summary>
    /// Replaces ordinary biomes with hostile interior variants once the danger
    /// field gets high enough. Thresholds are jittered and combined with a
    /// separate volcanism field so the corrupted core has a ragged, marbled edge
    /// instead of a clean ring.
    /// </summary>
    private static BiomeId ApplyDangerOverride(BiomeId biome, in ClimateSample s, WorldGenerationOptions options, float j)
    {
        if (options.DifficultyStrength <= 0f) return biome;

        // Scale the override thresholds by strength so a low-difficulty world
        // never grows a blighted core at all.
        float reach = options.DifficultyStrength;
        float danger = s.Danger + j * 1.4f;

        // Snow, ice and standing water resist corruption - it reads better to keep
        // the peaks white and let the blight creep around them.
        bool immune = biome is BiomeId.SnowPeak or BiomeId.Glacier;

        float blightLine = 1f - 0.16f * reach;
        float volcanicLine = 1f - 0.24f * reach;
        float ashLine = 1f - 0.34f * reach;

        if (!immune && danger >= blightLine && s.Volcanism < 0.55f + j)
            return BiomeId.Blightlands;

        if (danger >= volcanicLine && s.Volcanism >= 0.52f - j)
            return BiomeId.Volcanic;

        if (!immune && danger >= ashLine && s.Volcanism > 0.42f - j)
            return BiomeId.AshWaste;

        return biome;
    }
}
