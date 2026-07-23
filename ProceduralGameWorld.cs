using System;

namespace procedural_game_world;

public class ProceduralGameWorld
{
    public int WorldWidth { get; set; } = 100;
    public int WorldHeight { get; set; } = 100;
    public int GeneratedBiomeCount { get; set; }
    public int TileSizeSquared { get; set; } = 1;
    public WorldTile[,] Tiles { get; set; } = new WorldTile[0, 0];
}
