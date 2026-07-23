namespace procedural_game_world;

public class BiomeProbabilityMatrix
{
    public static readonly Dictionary<Biome, Dictionary<Biome, float>> _probabilities = CreateProbabilities();

    private static Dictionary<Biome, Dictionary<Biome, float>> CreateProbabilities()
    {
        var biomes = Enum.GetValues<Biome>();
        var probabilities = new Dictionary<Biome, Dictionary<Biome, float>>(biomes.Length);

        foreach (var biome in biomes)
        {
            var row = new Dictionary<Biome, float>(biomes.Length);

            foreach (var adjacentBiome in biomes)
            {
                row.Add(adjacentBiome, 0f);
            }

            probabilities.Add(biome, row);
        }

        foreach (var biome in biomes)
        {
            SetProbability(probabilities, biome, biome, 0.4f);
        }

        SetProbabilities(probabilities, 0.3f,
            (Biome.Grassland, Biome.Meadow),
            (Biome.Desert, Biome.Mesa),
            (Biome.Swamp, Biome.Riverlands),
            (Biome.Marsh, Biome.Mangrove),
            (Biome.Marsh, Biome.Riverlands),
            (Biome.Tundra, Biome.Glacier),
            (Biome.SnowyPeaks, Biome.AlpineMeadow),
            (Biome.CoastalCliffs, Biome.Beach),
            (Biome.CoastalCliffs, Biome.OpenOcean),
            (Biome.Beach, Biome.OpenOcean),
            (Biome.Jungle, Biome.Swamp),
            (Biome.Badlands, Biome.Canyon),
            (Biome.Mesa, Biome.Canyon),
            (Biome.VolcanicWastes, Biome.AshenWastes),
            (Biome.SnowyPeaks, Biome.FrozenWastes),
            (Biome.Riverlands, Biome.Lake),
            (Biome.WaterfallBasin, Biome.FloodedCavern),
            (Biome.OpenOcean, Biome.DeepSea),
            (Biome.CursedWoods, Biome.EnchantedForest),
            (Biome.EnchantedForest, Biome.FeywildGrove),
            (Biome.FeywildGrove, Biome.CelestialGarden),
            (Biome.AshenWastes, Biome.ObsidianFields),
            (Biome.ObsidianFields, Biome.InfernalWastes),
            (Biome.ElementalRift, Biome.InfernalWastes),
            (Biome.FloatingIslands, Biome.Stormlands),
            (Biome.SkyIslands, Biome.Stormlands),
            (Biome.Stormlands, Biome.ElementalRift),
            (Biome.Shadowfen, Biome.Bloodmarsh));

        SetProbabilities(probabilities, 0.35f,
            (Biome.Rainforest, Biome.Jungle),
            (Biome.Swamp, Biome.Marsh),
            (Biome.Desert, Biome.Badlands),
            (Biome.Badlands, Biome.Mesa),
            (Biome.Glacier, Biome.SnowyPeaks),
            (Biome.Glacier, Biome.FrozenWastes),
            (Biome.CoralReef, Biome.OpenOcean),
            (Biome.KelpForest, Biome.OpenOcean),
            (Biome.FloatingIslands, Biome.SkyIslands),
            (Biome.SkyIslands, Biome.Stormlands),
            (Biome.ArcaneWastes, Biome.ElementalRift));

        SetProbabilities(probabilities, 0.25f,
            (Biome.TemperateForest, Biome.BorealForest),
            (Biome.Rainforest, Biome.Swamp),
            (Biome.Jungle, Biome.Mangrove),
            (Biome.BorealForest, Biome.Tundra),
            (Biome.Swamp, Biome.Mangrove),
            (Biome.Mangrove, Biome.Beach),
            (Biome.Tundra, Biome.SnowyPeaks),
            (Biome.AlpineMeadow, Biome.RockyHighlands),
            (Biome.RockyHighlands, Biome.Canyon),
            (Biome.Beach, Biome.CoralReef),
            (Biome.CoralReef, Biome.KelpForest),
            (Biome.Riverlands, Biome.WaterfallBasin),
            (Biome.Lake, Biome.WaterfallBasin),
            (Biome.FloodedCavern, Biome.CrystalCavern),
            (Biome.FloodedCavern, Biome.MushroomGrotto),
            (Biome.CrystalCavern, Biome.MushroomGrotto),
            (Biome.VolcanicWastes, Biome.ElementalRift),
            (Biome.VolcanicWastes, Biome.InfernalWastes),
            (Biome.ObsidianFields, Biome.ElementalRift),
            (Biome.FloatingIslands, Biome.ArcaneWastes),
            (Biome.SkyIslands, Biome.CelestialGarden),
            (Biome.Stormlands, Biome.ArcaneWastes),
            (Biome.ArcaneWastes, Biome.CelestialGarden),
            (Biome.CursedWoods, Biome.Shadowfen),
            (Biome.DragonboneValley, Biome.ElementalRift));

        SetProbabilities(probabilities, 0.2f,
            (Biome.Grassland, Biome.Savanna),
            (Biome.Meadow, Biome.Savanna),
            (Biome.Savanna, Biome.Desert),
            (Biome.TemperateForest, Biome.Riverlands),
            (Biome.Rainforest, Biome.Mangrove),
            (Biome.Jungle, Biome.Riverlands),
            (Biome.Marsh, Biome.Lake),
            (Biome.Marsh, Biome.Shadowfen),
            (Biome.Mangrove, Biome.CoralReef),
            (Biome.Mangrove, Biome.Riverlands),
            (Biome.Mangrove, Biome.OpenOcean),
            (Biome.Desert, Biome.Canyon),
            (Biome.Mesa, Biome.RockyHighlands),
            (Biome.Canyon, Biome.DragonboneValley),
            (Biome.Beach, Biome.KelpForest),
            (Biome.Lake, Biome.FloodedCavern),
            (Biome.CrystalCavern, Biome.AncientRuins),
            (Biome.MushroomGrotto, Biome.AncientRuins),
            (Biome.EnchantedForest, Biome.CelestialGarden),
            (Biome.AshenWastes, Biome.ArcaneWastes),
            (Biome.AshenWastes, Biome.InfernalWastes),
            (Biome.FloatingIslands, Biome.CelestialGarden),
            (Biome.SkyIslands, Biome.ArcaneWastes),
            (Biome.Stormlands, Biome.CelestialGarden),
            (Biome.Bloodmarsh, Biome.DragonboneValley),
            (Biome.Bloodmarsh, Biome.InfernalWastes),
            (Biome.DragonboneValley, Biome.InfernalWastes));

        SetProbabilities(probabilities, 0.15f,
            (Biome.TemperateForest, Biome.Swamp),
            (Biome.TemperateForest, Biome.CursedWoods),
            (Biome.TemperateForest, Biome.EnchantedForest),
            (Biome.Rainforest, Biome.Riverlands),
            (Biome.BorealForest, Biome.SnowyPeaks),
            (Biome.BorealForest, Biome.CursedWoods),
            (Biome.Swamp, Biome.Bloodmarsh),
            (Biome.Marsh, Biome.Bloodmarsh),
            (Biome.Desert, Biome.VolcanicWastes),
            (Biome.Desert, Biome.AshenWastes),
            (Biome.Badlands, Biome.RockyHighlands),
            (Biome.Badlands, Biome.AshenWastes),
            (Biome.Mesa, Biome.DragonboneValley),
            (Biome.Tundra, Biome.AlpineMeadow),
            (Biome.RockyHighlands, Biome.CoastalCliffs),
            (Biome.RockyHighlands, Biome.DragonboneValley),
            (Biome.Canyon, Biome.AncientRuins),
            (Biome.CoastalCliffs, Biome.KelpForest),
            (Biome.AncientRuins, Biome.CursedWoods),
            (Biome.AncientRuins, Biome.DragonboneValley),
            (Biome.AncientRuins, Biome.ArcaneWastes),
            (Biome.EnchantedForest, Biome.ArcaneWastes),
            (Biome.FeywildGrove, Biome.FloatingIslands),
            (Biome.ObsidianFields, Biome.DragonboneValley),
            (Biome.ArcaneWastes, Biome.Shadowfen),
            (Biome.Shadowfen, Biome.InfernalWastes),
            (Biome.ElementalRift, Biome.FrozenWastes));

        SetProbabilities(probabilities, 0.1f,
            (Biome.Grassland, Biome.TemperateForest),
            (Biome.Meadow, Biome.TemperateForest),
            (Biome.KelpForest, Biome.DeepSea),
            (Biome.Riverlands, Biome.FloodedCavern));

        return probabilities;
    }

    private static void SetProbabilities(
        Dictionary<Biome, Dictionary<Biome, float>> probabilities,
        float probability,
        params (Biome First, Biome Second)[] pairs)
    {
        foreach (var pair in pairs)
        {
            SetProbability(probabilities, pair.First, pair.Second, probability);
        }
    }

    private static void SetProbability(
        Dictionary<Biome, Dictionary<Biome, float>> probabilities,
        Biome first,
        Biome second,
        float probability)
    {
        if (probability < 0f || probability > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        probabilities[first][second] = probability;
        probabilities[second][first] = probability;
    }
}
