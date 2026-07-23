using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using procedural_game_world;

namespace WorldGenerator.App;

internal static class WorldMapRenderer
{
    private static readonly Dictionary<Biome, Color> BiomeColors = new()
    {
        [Biome.Grassland] = Color.FromRgb(124, 179, 82),
        [Biome.Meadow] = Color.FromRgb(170, 214, 100),
        [Biome.Savanna] = Color.FromRgb(197, 172, 75),
        [Biome.TemperateForest] = Color.FromRgb(58, 117, 70),
        [Biome.Rainforest] = Color.FromRgb(20, 105, 67),
        [Biome.BorealForest] = Color.FromRgb(49, 94, 84),
        [Biome.Jungle] = Color.FromRgb(28, 122, 58),
        [Biome.Swamp] = Color.FromRgb(61, 106, 78),
        [Biome.Marsh] = Color.FromRgb(103, 140, 82),
        [Biome.Mangrove] = Color.FromRgb(48, 124, 101),
        [Biome.Desert] = Color.FromRgb(225, 190, 106),
        [Biome.Badlands] = Color.FromRgb(191, 100, 69),
        [Biome.Mesa] = Color.FromRgb(164, 77, 53),
        [Biome.VolcanicWastes] = Color.FromRgb(88, 67, 62),
        [Biome.Tundra] = Color.FromRgb(168, 188, 180),
        [Biome.Glacier] = Color.FromRgb(168, 221, 228),
        [Biome.SnowyPeaks] = Color.FromRgb(239, 246, 245),
        [Biome.AlpineMeadow] = Color.FromRgb(111, 161, 90),
        [Biome.RockyHighlands] = Color.FromRgb(120, 122, 108),
        [Biome.Canyon] = Color.FromRgb(179, 87, 60),
        [Biome.CoastalCliffs] = Color.FromRgb(91, 109, 111),
        [Biome.Beach] = Color.FromRgb(244, 220, 143),
        [Biome.CoralReef] = Color.FromRgb(237, 127, 117),
        [Biome.KelpForest] = Color.FromRgb(43, 117, 113),
        [Biome.OpenOcean] = Color.FromRgb(49, 125, 166),
        [Biome.DeepSea] = Color.FromRgb(26, 66, 112),
        [Biome.Riverlands] = Color.FromRgb(83, 162, 180),
        [Biome.Lake] = Color.FromRgb(66, 143, 183),
        [Biome.WaterfallBasin] = Color.FromRgb(111, 193, 210),
        [Biome.FloodedCavern] = Color.FromRgb(55, 98, 135),
        [Biome.CrystalCavern] = Color.FromRgb(101, 169, 191),
        [Biome.MushroomGrotto] = Color.FromRgb(135, 91, 130),
        [Biome.AncientRuins] = Color.FromRgb(141, 126, 96),
        [Biome.CursedWoods] = Color.FromRgb(67, 68, 82),
        [Biome.EnchantedForest] = Color.FromRgb(73, 135, 119),
        [Biome.FeywildGrove] = Color.FromRgb(112, 192, 147),
        [Biome.AshenWastes] = Color.FromRgb(112, 96, 91),
        [Biome.ObsidianFields] = Color.FromRgb(48, 48, 55),
        [Biome.FloatingIslands] = Color.FromRgb(120, 170, 190),
        [Biome.SkyIslands] = Color.FromRgb(118, 174, 213),
        [Biome.Stormlands] = Color.FromRgb(92, 102, 139),
        [Biome.ArcaneWastes] = Color.FromRgb(133, 96, 161),
        [Biome.CelestialGarden] = Color.FromRgb(201, 190, 111),
        [Biome.Shadowfen] = Color.FromRgb(71, 80, 83),
        [Biome.Bloodmarsh] = Color.FromRgb(138, 55, 64),
        [Biome.DragonboneValley] = Color.FromRgb(156, 125, 105),
        [Biome.ElementalRift] = Color.FromRgb(206, 93, 55),
        [Biome.InfernalWastes] = Color.FromRgb(130, 48, 42),
        [Biome.FrozenWastes] = Color.FromRgb(186, 214, 225)
    };

    public static WriteableBitmap Render(ProceduralGameWorld world)
    {
        var width = world.WorldWidth;
        var height = world.WorldHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var tileY = 0; tileY < height; tileY++)
        {
            for (var tileX = 0; tileX < width; tileX++)
            {
                var color = GetColor(world.Tiles[tileX, tileY].Biome);
                var pixelIndex = (tileY * stride) + (tileX * 4);

                pixels[pixelIndex] = color.B;
                pixels[pixelIndex + 1] = color.G;
                pixels[pixelIndex + 2] = color.R;
                pixels[pixelIndex + 3] = color.A;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return bitmap;
    }

    private static Color GetColor(Biome biome)
    {
        return BiomeColors.TryGetValue(biome, out var color)
            ? color
            : Color.FromRgb(255, 0, 255);
    }
}
