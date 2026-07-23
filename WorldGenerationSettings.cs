namespace procedural_game_world;

public class WorldGenerationSettings
{
    public int WorldWidth { get; set; } = 100;
    public int WorldHeight { get; set; } = 100;
    public int MinBiomeCount { get; set; } = 5;
    public int MaxBiomeCount { get; set; } = 10;
    public int SmoothingPasses { get; set; }
    public float BiomeVariationChance { get; set; } = 0.15f;
    public int? Seed { get; set; }

    internal void Validate()
    {
        if (WorldWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(WorldWidth), "World width must be at least one tile.");
        }

        if (WorldHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(WorldHeight), "World height must be at least one tile.");
        }

        if (MinBiomeCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinBiomeCount), "At least one seed biome is required.");
        }

        if (MaxBiomeCount < MinBiomeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBiomeCount), "Maximum seed biomes must be at least the minimum.");
        }

        if (MaxBiomeCount > Enum.GetValues<Biome>().Length)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBiomeCount), "Seed biomes cannot exceed the number of available biome types.");
        }

        if (SmoothingPasses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SmoothingPasses), "Smoothing passes cannot be negative.");
        }

        if (BiomeVariationChance is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(BiomeVariationChance), "Biome variation chance must be between zero and one.");
        }

        var tileCount = (long)WorldWidth * WorldHeight;

        if (MaxBiomeCount > tileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBiomeCount), "Seed biomes cannot exceed the number of tiles.");
        }
    }
}
