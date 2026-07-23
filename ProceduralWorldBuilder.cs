namespace procedural_game_world;

public class ProceduralWorldBuilder
{
    private const int SmoothingRadius = 2;
    private static readonly Biome[] AllBiomes = Enum.GetValues<Biome>();
    private static readonly (int X, int Y)[] SmoothingNeighborOffsets = CreateCircularNeighborOffsets();

    private readonly record struct BiomeSeed(int X, int Y, Biome Biome);

    public int MinBiomeCount { get; set; } = 5;
    public int MaxBiomeCount { get; set; } = 10;

    public ProceduralGameWorld BuildWorld()
    {
        return BuildWorld(new WorldGenerationSettings
        {
            MinBiomeCount = MinBiomeCount,
            MaxBiomeCount = MaxBiomeCount
        });
    }

    public static ProceduralGameWorld BuildWorld(WorldGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var random = settings.Seed is int seed ? new Random(seed) : Random.Shared;
        var world = new ProceduralGameWorld
        {
            WorldWidth = settings.WorldWidth,
            WorldHeight = settings.WorldHeight
        };
        int biomeCount = random.Next(settings.MinBiomeCount, settings.MaxBiomeCount + 1);
        var biomeSeeds = CreateBiomeSeeds(biomeCount, world.WorldWidth, world.WorldHeight, random);
        var biomes = new Biome[world.WorldWidth, world.WorldHeight];

        AssignBiomesFromSeeds(biomes, biomeSeeds, settings.BiomeVariationChance, random);
        biomes = ApplySmoothingPasses(biomes, settings.SmoothingPasses);

        world.Tiles = CreateTiles(
            world.WorldWidth,
            world.WorldHeight,
            biomes,
            out var generatedBiomeCount);
        world.GeneratedBiomeCount = generatedBiomeCount;
        return world;
    }

    private static WorldTile[,] CreateTiles(
        int worldWidth,
        int worldHeight,
        Biome[,] biomes,
        out int generatedBiomeCount)
    {
        var tiles = new WorldTile[worldWidth, worldHeight];
        var encounteredBiomes = new bool[AllBiomes.Length];
        generatedBiomeCount = 0;

        for (var tileX = 0; tileX < worldWidth; tileX++)
        {
            for (var tileY = 0; tileY < worldHeight; tileY++)
            {
                var biome = biomes[tileX, tileY];

                if (!encounteredBiomes[(int)biome])
                {
                    encounteredBiomes[(int)biome] = true;
                    generatedBiomeCount++;
                }

                tiles[tileX, tileY] = new WorldTile
                {
                    Position = new(tileX, tileY),
                    Biome = biome
                };
            }
        }

        return tiles;
    }

    private static BiomeSeed[] CreateBiomeSeeds(
        int biomeCount,
        int worldWidth,
        int worldHeight,
        Random random)
    {
        var biomeSeeds = new BiomeSeed[biomeCount];
        var usedPositions = new HashSet<int>();
        var usedBiomes = new HashSet<Biome>();

        for (var index = 0; index < biomeSeeds.Length; index++)
        {
            int tileX;
            int tileY;
            int positionIndex;

            do
            {
                tileX = random.Next(worldWidth);
                tileY = random.Next(worldHeight);
                positionIndex = (tileY * worldWidth) + tileX;
            }
            while (!usedPositions.Add(positionIndex));

            var biome = index == 0
                ? AllBiomes[random.Next(AllBiomes.Length)]
                : SelectUniqueAdjacentBiome(
                    FindNearestSeed(biomeSeeds, index, tileX, tileY).Biome,
                    usedBiomes,
                    random);

            biomeSeeds[index] = new BiomeSeed(tileX, tileY, biome);
            usedBiomes.Add(biome);
        }

        return biomeSeeds;
    }

    private static BiomeSeed FindNearestSeed(
        BiomeSeed[] biomeSeeds,
        int seedCount,
        float tileX,
        float tileY)
    {
        var nearestSeed = biomeSeeds[0];
        var nearestDistance = GetSquaredDistance(nearestSeed, tileX, tileY);

        for (var index = 1; index < seedCount; index++)
        {
            var candidateSeed = biomeSeeds[index];
            var candidateDistance = GetSquaredDistance(candidateSeed, tileX, tileY);

            if (candidateDistance < nearestDistance)
            {
                nearestSeed = candidateSeed;
                nearestDistance = candidateDistance;
            }
        }

        return nearestSeed;
    }

    private static void AssignBiomesFromSeeds(
        Biome[,] biomes,
        BiomeSeed[] biomeSeeds,
        float boundaryVariation,
        Random random)
    {
        var worldWidth = biomes.GetLength(0);
        var worldHeight = biomes.GetLength(1);
        var noiseCellSize = Math.Max(4, Math.Min(worldWidth, worldHeight) / 8);
        var horizontalWarp = CreateNoiseGrid(worldWidth, worldHeight, noiseCellSize, random);
        var verticalWarp = CreateNoiseGrid(worldWidth, worldHeight, noiseCellSize, random);
        var warpStrength = noiseCellSize * boundaryVariation * 2f;

        for (var tileX = 0; tileX < worldWidth; tileX++)
        {
            for (var tileY = 0; tileY < worldHeight; tileY++)
            {
                var warpedX = tileX + (SampleSmoothNoise(horizontalWarp, tileX, tileY, noiseCellSize) * warpStrength);
                var warpedY = tileY + (SampleSmoothNoise(verticalWarp, tileX, tileY, noiseCellSize) * warpStrength);
                biomes[tileX, tileY] = FindNearestSeed(biomeSeeds, biomeSeeds.Length, warpedX, warpedY).Biome;
            }
        }
    }

    private static float[,] CreateNoiseGrid(
        int worldWidth,
        int worldHeight,
        int noiseCellSize,
        Random random)
    {
        var noiseWidth = ((worldWidth - 1) / noiseCellSize) + 2;
        var noiseHeight = ((worldHeight - 1) / noiseCellSize) + 2;
        var noise = new float[noiseWidth, noiseHeight];

        for (var noiseX = 0; noiseX < noiseWidth; noiseX++)
        {
            for (var noiseY = 0; noiseY < noiseHeight; noiseY++)
            {
                noise[noiseX, noiseY] = (random.NextSingle() * 2f) - 1f;
            }
        }

        return noise;
    }

    private static float SampleSmoothNoise(
        float[,] noise,
        int tileX,
        int tileY,
        int noiseCellSize)
    {
        var sampleX = (float)tileX / noiseCellSize;
        var sampleY = (float)tileY / noiseCellSize;
        var noiseX = (int)sampleX;
        var noiseY = (int)sampleY;
        var blendX = SmoothStep(sampleX - noiseX);
        var blendY = SmoothStep(sampleY - noiseY);
        var top = Lerp(noise[noiseX, noiseY], noise[noiseX + 1, noiseY], blendX);
        var bottom = Lerp(noise[noiseX, noiseY + 1], noise[noiseX + 1, noiseY + 1], blendX);

        return Lerp(top, bottom, blendY);
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - (2f * value));
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private static float GetSquaredDistance(BiomeSeed biomeSeed, float tileX, float tileY)
    {
        var xDistance = tileX - biomeSeed.X;
        var yDistance = tileY - biomeSeed.Y;
        return (xDistance * xDistance) + (yDistance * yDistance);
    }

    private static bool IsWithinBounds(Biome[,] biomes, int tileX, int tileY)
    {
        return tileX >= 0 && tileX < biomes.GetLength(0)
            && tileY >= 0 && tileY < biomes.GetLength(1);
    }

    private static Biome[,] ApplySmoothingPasses(Biome[,] biomes, int smoothingPasses)
    {
        var worldWidth = biomes.GetLength(0);
        var worldHeight = biomes.GetLength(1);
        var smoothedBiomes = new Biome[worldWidth, worldHeight];
        var biomeCounts = new int[AllBiomes.Length];
        var countedBiomes = new Biome[SmoothingNeighborOffsets.Length + 1];

        for (var pass = 0; pass < smoothingPasses; pass++)
        {
            for (var tileX = 0; tileX < worldWidth; tileX++)
            {
                for (var tileY = 0; tileY < worldHeight; tileY++)
                {
                    smoothedBiomes[tileX, tileY] = SelectDominantBiome(
                        biomes,
                        tileX,
                        tileY,
                        biomeCounts,
                        countedBiomes);
                }
            }

            (biomes, smoothedBiomes) = (smoothedBiomes, biomes);
        }

        return biomes;
    }

    private static Biome SelectDominantBiome(
        Biome[,] biomes,
        int tileX,
        int tileY,
        int[] biomeCounts,
        Biome[] countedBiomes)
    {
        var currentBiome = biomes[tileX, tileY];
        var countedBiomeCount = 0;

        AddBiomeCount(biomeCounts, countedBiomes, ref countedBiomeCount, currentBiome);

        foreach (var (xOffset, yOffset) in SmoothingNeighborOffsets)
        {
            var neighborX = tileX + xOffset;
            var neighborY = tileY + yOffset;

            if (IsWithinBounds(biomes, neighborX, neighborY))
            {
                AddBiomeCount(
                    biomeCounts,
                    countedBiomes,
                    ref countedBiomeCount,
                    biomes[neighborX, neighborY]);
            }
        }

        var dominantBiome = currentBiome;
        var dominantCount = biomeCounts[(int)currentBiome];

        for (var index = 0; index < countedBiomeCount; index++)
        {
            var biome = countedBiomes[index];
            var count = biomeCounts[(int)biome];

            if (count > dominantCount)
            {
                dominantBiome = biome;
                dominantCount = count;
            }
        }

        for (var index = 0; index < countedBiomeCount; index++)
        {
            biomeCounts[(int)countedBiomes[index]] = 0;
        }

        return dominantBiome;
    }

    private static void AddBiomeCount(
        int[] biomeCounts,
        Biome[] countedBiomes,
        ref int countedBiomeCount,
        Biome biome)
    {
        var biomeIndex = (int)biome;

        if (biomeCounts[biomeIndex] == 0)
        {
            countedBiomes[countedBiomeCount] = biome;
            countedBiomeCount++;
        }

        biomeCounts[biomeIndex]++;
    }

    private static Biome SelectUniqueAdjacentBiome(
        Biome sourceBiome,
        HashSet<Biome> usedBiomes,
        Random random)
    {
        var distribution = BiomeProbabilityMatrix._probabilities[sourceBiome];
        float totalWeight = 0f;

        foreach (var (biome, weight) in distribution)
        {
            if (!usedBiomes.Contains(biome))
            {
                totalWeight += weight;
            }
        }

        if (totalWeight > 0f)
        {
            float selection = random.NextSingle() * totalWeight;

            foreach (var (biome, weight) in distribution)
            {
                if (usedBiomes.Contains(biome))
                {
                    continue;
                }

                if (selection < weight)
                {
                    return biome;
                }

                selection -= weight;
            }
        }

        var remainingBiomeIndex = random.Next(AllBiomes.Length - usedBiomes.Count);

        foreach (var biome in AllBiomes)
        {
            if (usedBiomes.Contains(biome))
            {
                continue;
            }

            if (remainingBiomeIndex == 0)
            {
                return biome;
            }

            remainingBiomeIndex--;
        }

        throw new InvalidOperationException("No unused biome is available.");
    }

    private static (int X, int Y)[] CreateCircularNeighborOffsets()
    {
        var offsets = new List<(int X, int Y)>();

        for (var yOffset = -SmoothingRadius; yOffset <= SmoothingRadius; yOffset++)
        {
            for (var xOffset = -SmoothingRadius; xOffset <= SmoothingRadius; xOffset++)
            {
                var squaredDistance = (xOffset * xOffset) + (yOffset * yOffset);

                if (squaredDistance > 0 && squaredDistance <= SmoothingRadius * SmoothingRadius)
                {
                    offsets.Add((xOffset, yOffset));
                }
            }
        }

        return offsets.ToArray();
    }
}
