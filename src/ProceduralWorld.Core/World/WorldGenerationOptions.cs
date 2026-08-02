namespace ProceduralWorld.Core.World;

/// <summary>
/// Shape of the landmass carved out of the ocean.
/// </summary>
public enum ContinentShape
{
    /// <summary>A single round-ish continent with ocean on every edge.</summary>
    Island,

    /// <summary>A broad landmass with a ragged, fjord-heavy coast.</summary>
    Continent,

    /// <summary>Several medium islands scattered inside an ocean rim.</summary>
    Archipelago,
}

/// <summary>
/// Every knob the world generator exposes. Defaults are tuned to produce a
/// believable continent surrounded by open ocean, with difficulty rising toward
/// the interior.
/// </summary>
public sealed record WorldGenerationOptions
{
    public int Seed { get; init; } = 1337;

    public int Width { get; init; } = 512;

    public int Height { get; init; } = 320;

    public ContinentShape Shape { get; init; } = ContinentShape.Continent;

    /// <summary>
    /// Normalised distance from each map edge reserved as guaranteed open ocean.
    /// 0.18 means the outer 18% fades toward deep water.
    /// </summary>
    public float OceanRim { get; init; } = 0.18f;

    /// <summary>
    /// Elevation value assigned to the calibrated shoreline. Shape and ocean rim
    /// determine water coverage; this controls relative land height and water depth.
    /// </summary>
    public float SeaLevel { get; init; } = 0.42f;

    /// <summary>Base terrain-noise frequency. Larger values create smaller, busier features.</summary>
    public float TerrainScale { get; init; } = 2.6f;

    /// <summary>How hard the coastline is distorted. This is what kills circular islands.</summary>
    public float CoastWarp { get; init; } = 0.34f;

    /// <summary>Weight of the ridged mountain fractal blended into elevation.</summary>
    public float MountainStrength { get; init; } = 0.42f;

    /// <summary>0 = uniform climate, 1 = strong pole-to-equator temperature banding.</summary>
    public float LatitudeStrength { get; init; } = 0.85f;

    /// <summary>Global temperature offset in [-1, 1]. Negative = ice age.</summary>
    public float TemperatureBias { get; init; }

    /// <summary>Global moisture offset in [-1, 1]. Negative = arid world.</summary>
    public float MoistureBias { get; init; }

    /// <summary>
    /// Target river sources for the world. Map dimensions change raster resolution,
    /// not this count. Set to 0 to disable rivers and hydrology-created lakes.
    /// </summary>
    public int RiverCount { get; init; } = 42;

    /// <summary>
    /// Width of the fuzzy band between biomes, in threshold units. Higher values
    /// interleave neighbouring biomes over a wider strip for gradual transitions.
    /// </summary>
    public float TransitionSoftness { get; init; } = 0.055f;

    /// <summary>
    /// How strongly the radial danger field reshapes terrain, climate and biome
    /// choice. 0 disables the difficulty gradient; 1 makes the interior brutal.
    /// </summary>
    public float DifficultyStrength { get; init; } = 0.85f;

    /// <summary>
    /// Exponent applied to normalised centre-proximity when building the danger
    /// field. Values above 1 keep the safe outer ring generous and compress the
    /// worst danger into the core.
    /// </summary>
    public float DifficultyCurve { get; init; } = 1.6f;

    /// <summary>
    /// How irregular the danger rings are. 0 gives perfect concentric bands,
    /// higher values give organic lobed zones that follow the terrain.
    /// </summary>
    public float DifficultyWarp { get; init; } = 0.28f;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Width, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(Height, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Width, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Height, 4096);
        ArgumentOutOfRangeException.ThrowIfNegative(RiverCount);

        if (SeaLevel is <= 0.05f or >= 0.95f)
            throw new ArgumentOutOfRangeException(nameof(SeaLevel), SeaLevel, "Sea level must be within (0.05, 0.95).");

        if (OceanRim is < 0f or > 0.45f)
            throw new ArgumentOutOfRangeException(nameof(OceanRim), OceanRim, "Ocean rim must be within [0, 0.45].");

        if (DifficultyStrength is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(DifficultyStrength), DifficultyStrength, "Difficulty strength must be within [0, 1].");

        if (DifficultyCurve is < 0.25f or > 6f)
            throw new ArgumentOutOfRangeException(nameof(DifficultyCurve), DifficultyCurve, "Difficulty curve must be within [0.25, 6].");

        if (DifficultyWarp is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(DifficultyWarp), DifficultyWarp, "Difficulty warp must be within [0, 1].");
    }
}
