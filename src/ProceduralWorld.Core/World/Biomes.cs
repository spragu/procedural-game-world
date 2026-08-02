namespace ProceduralWorld.Core.World;

/// <summary>
/// Biome identifiers, ordered roughly from deep water inland/upward so that
/// index order is meaningful for legends and debugging.
/// </summary>
public enum BiomeId : byte
{
    DeepOcean,
    Ocean,
    ShallowWater,
    CoralReef,
    Beach,
    RockyShore,
    SaltMarsh,
    River,
    Lake,
    Marsh,
    Grassland,
    Savanna,
    Shrubland,
    TemperateForest,
    Rainforest,
    BorealForest,
    Desert,
    Badlands,
    Tundra,
    AlpineMeadow,
    RockyMountain,
    SnowPeak,
    Glacier,

    // ---- Deep interior / high danger ------------------------------------
    AshWaste,
    Volcanic,
    Blightlands,
}

/// <summary>
/// Coarse difficulty banding, in the spirit of RuneScape's wilderness levels or
/// Realm of the Mad God's lands. Tier rises as you push toward the map centre.
/// </summary>
public enum DangerTier : byte
{
    /// <summary>Starting shores and open water. Nothing much wants to kill you.</summary>
    Safe = 0,
    Low = 1,
    Moderate = 2,
    High = 3,
    Severe = 4,

    /// <summary>The heart of the world. Endgame territory.</summary>
    Lethal = 5,
}

public readonly record struct BiomeInfo(
    BiomeId Id,
    string Name,
    string Description,
    bool IsWater,
    bool IsPassable,
    DangerTier Danger)
{
    public string DangerLabel => Danger switch
    {
        DangerTier.Safe => "Safe",
        DangerTier.Low => "Low threat",
        DangerTier.Moderate => "Moderate threat",
        DangerTier.High => "High threat",
        DangerTier.Severe => "Severe threat",
        _ => "Lethal",
    };
}

public static class Biomes
{
    private static readonly BiomeInfo[] Table = BuildTable();

    public static BiomeInfo Get(BiomeId id) => Table[(int)id];

    public static IReadOnlyList<BiomeInfo> All => Table;

    public static int Count => Table.Length;

    private static BiomeInfo[] BuildTable()
    {
        var values = Enum.GetValues<BiomeId>();
        var table = new BiomeInfo[values.Length];

        foreach (var id in values)
        {
            table[(int)id] = id switch
            {
                BiomeId.DeepOcean => new(id, "Deep Ocean", "Abyssal open water far from any landmass.", true, false, DangerTier.Low),
                BiomeId.Ocean => new(id, "Ocean", "Open saltwater over the continental shelf.", true, false, DangerTier.Safe),
                BiomeId.ShallowWater => new(id, "Shallow Water", "Sunlit coastal shallows lapping the shore.", true, false, DangerTier.Safe),
                BiomeId.CoralReef => new(id, "Coral Reef", "Warm shallow reef teeming with colour and life.", true, false, DangerTier.Safe),
                BiomeId.Beach => new(id, "Beach", "Soft sand and shell wrack along the tideline.", false, true, DangerTier.Safe),
                BiomeId.RockyShore => new(id, "Rocky Shore", "Wave-cut boulders and tidal pools.", false, true, DangerTier.Safe),
                BiomeId.SaltMarsh => new(id, "Salt Marsh", "Brackish grasses flooded by the tide.", false, true, DangerTier.Low),
                BiomeId.River => new(id, "River", "Freshwater carving its way to the sea.", true, false, DangerTier.Low),
                BiomeId.Lake => new(id, "Lake", "Still inland freshwater.", true, false, DangerTier.Low),
                BiomeId.Marsh => new(id, "Marsh", "Waterlogged reeds and slow black water.", false, true, DangerTier.Moderate),
                BiomeId.Grassland => new(id, "Grassland", "Rolling prairie of wind-combed grass.", false, true, DangerTier.Low),
                BiomeId.Savanna => new(id, "Savanna", "Dry golden grass punctuated by lone trees.", false, true, DangerTier.Low),
                BiomeId.Shrubland => new(id, "Shrubland", "Hardy scrub and aromatic brush.", false, true, DangerTier.Low),
                BiomeId.TemperateForest => new(id, "Temperate Forest", "Deep broadleaf canopy and mossy floor.", false, true, DangerTier.Moderate),
                BiomeId.Rainforest => new(id, "Rainforest", "Dense, dripping, impossibly green jungle.", false, true, DangerTier.High),
                BiomeId.BorealForest => new(id, "Boreal Forest", "Cold ranks of spruce and pine.", false, true, DangerTier.Moderate),
                BiomeId.Desert => new(id, "Desert", "Sun-hammered dunes and bare stone.", false, true, DangerTier.High),
                BiomeId.Badlands => new(id, "Badlands", "Banded clay mesas cut by dry washes.", false, true, DangerTier.High),
                BiomeId.Tundra => new(id, "Tundra", "Frozen ground, lichen and low heath.", false, true, DangerTier.Moderate),
                BiomeId.AlpineMeadow => new(id, "Alpine Meadow", "High wildflower slopes above the treeline.", false, true, DangerTier.High),
                BiomeId.RockyMountain => new(id, "Rocky Mountain", "Bare scree and wind-scoured granite.", false, true, DangerTier.Severe),
                BiomeId.SnowPeak => new(id, "Snow Peak", "Permanent snowfield near the summit.", false, true, DangerTier.Severe),
                BiomeId.Glacier => new(id, "Glacier", "Ancient compressed ice, blue at depth.", false, false, DangerTier.Severe),
                BiomeId.AshWaste => new(id, "Ash Waste", "Grey drifts of cinder where nothing takes root.", false, true, DangerTier.Severe),
                BiomeId.Volcanic => new(id, "Volcanic Wastes", "Cracked basalt bleeding molten light.", false, true, DangerTier.Lethal),
                BiomeId.Blightlands => new(id, "Blightlands", "The corrupted heart of the world. The air itself is hostile.", false, true, DangerTier.Lethal),
                _ => new(id, id.ToString(), string.Empty, false, true, DangerTier.Safe),
            };
        }

        return table;
    }
}
