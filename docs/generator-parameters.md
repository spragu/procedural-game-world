# World generator parameters

`WorldGenerationOptions` is the complete input to `WorldGenerator.Generate`.
Generation is deterministic: the same options produce the same tile data. Rendering
options such as hillshading, danger tint, coastline outlines, view mode, and zoom do
not change the generated world.

```csharp
using ProceduralWorld.Core.World;

var options = new WorldGenerationOptions
{
    Seed = 8128,
    Width = 1024,
    Height = 640,
    Shape = ContinentShape.Continent,
    TerrainScale = 2.2f,
    MountainStrength = 0.52f,
    MoistureBias = 0.08f,
    RiverCount = 28,
};

WorldMap map = WorldGenerator.Generate(options);
```

## Generation pipeline

Options are consumed in this order. Earlier passes can therefore affect several
later systems.

1. **Continent mask** builds the broad landmass from `Shape`, `OceanRim`, and
   `CoastWarp`.
2. **Elevation** combines domain-warped fractal terrain with inland ridges using
   `TerrainScale`, `CoastWarp`, and `MountainStrength`.
3. **Sea-level calibration** remaps elevation so the shape's target water coverage
   meets the numeric `SeaLevel` shoreline.
4. **Slope and shore distance** derive gradients and distance from salt water.
5. **Temperature** combines latitude, elevation cooling, ocean moderation, noise,
   `LatitudeStrength`, and `TemperatureBias`.
6. **Moisture** combines noise, coastal humidity, temperature, and a west-to-east
   rain shadow, then applies `MoistureBias`.
7. **Hydrology** selects spaced highland sources and traces validated downhill
   paths into ocean, lakes, or existing rivers.
8. **Danger** builds a warped coast-to-interior difficulty field.
9. **Biome classification** combines elevation, coast distance, slope, climate,
   freshwater, danger, and `TransitionSoftness`.

## Range conventions

- **Core range** is enforced by `WorldGenerationOptions.Validate()`.
- **Atlas range** is the narrower range exposed by the browser UI.
- A recommended range is not currently enforced by the core API. Values outside it
  may be valid C# inputs but can produce saturated or hard-to-read maps.

## Identity and dimensions

| Parameter | Default | Range | Influence |
| --- | ---: | --- | --- |
| `Seed` | `1337` | Any `int` | Selects every noise field and the deterministic river-source order. Change it for a different world without changing its overall tuning. |
| `Width` | `512` | Core and Atlas: `16..4096` | Horizontal tile resolution. It affects memory, generation time, river spacing in tiles, and the amount of detail available when zooming. |
| `Height` | `320` | Core and Atlas: `16..4096` | Vertical tile resolution. It also controls how many rows represent each latitude band. |
| `Shape` | `Continent` | `Island`, `Continent`, `Archipelago` | Chooses the broad mask profile and baseline water coverage. |

`Width` and `Height` are tile dimensions, so cost grows with `Width * Height`.
Noise is sampled in normalized map coordinates; increasing resolution gives the same
kind of macro-geography more tiles rather than simply multiplying every feature.

### Shape behavior

- `Island` uses a compact radial mask and targets about 60% water before the
  `OceanRim` adjustment.
- `Continent` uses a broad mask and targets about 50% water before the adjustment.
- `Archipelago` multiplies the radial mask by an island noise field and targets about
  68% water before the adjustment.

## Land and elevation

| Parameter | Default | Range | Influence |
| --- | ---: | --- | --- |
| `OceanRim` | `0.18` | Core and Atlas: `0..0.45` | Forces the outer part of every edge toward open ocean. Larger values shrink land away from the border and also raise target water coverage. |
| `SeaLevel` | `0.42` | Core: `(0.05, 0.95)`; Atlas: `0.20..0.70` | Sets the numeric elevation assigned to the calibrated shoreline. It changes stored elevation scaling, not the shape's main water-coverage target. |
| `TerrainScale` | `2.6` | Effective minimum: `0.25`; Atlas: `0.5..8` | Controls terrain-noise frequency. Larger values make smaller, busier hills and ridges; smaller values make broad terrain. |
| `CoastWarp` | `0.34` | Recommended: `0..0.8`; Atlas: `0..0.8` | Distorts the continent mask and domain-warps elevation. `0` is smooth and regular; larger values make ragged coasts and bent terrain bands. |
| `MountainStrength` | `0.42` | Recommended: `0..1`; Atlas: `0..1` | Weights ridged elevation, especially near the interior. Higher values create more alpine terrain, stronger rain shadows, and steeper river routes. |

Water coverage is chosen from `Shape`, then adjusted by `OceanRim * 0.35` and
clamped to 25%-85%. `SeaLevel` is where that sampled percentile is placed on the
stored 0-1 elevation scale. Raising `SeaLevel` therefore does not behave like a
simple flood control. It can still reduce river-source eligibility at extreme high
values because sources must begin at least `0.16` raw elevation above sea level.

## Climate and biome boundaries

| Parameter | Default | Range | Influence |
| --- | ---: | --- | --- |
| `LatitudeStrength` | `0.85` | Recommended: `0..1`; not in Atlas | Blends between a mostly uniform baseline and strong cold-pole/warm-equator bands. Elevation cooling still applies at `0`. |
| `TemperatureBias` | `0` | Recommended: `-1..1`; not in Atlas | Adds a global temperature offset. Negative values expand tundra, snow, and glaciers; positive values expand warm biomes and reefs. The final field is clamped. |
| `MoistureBias` | `0` | Recommended: `-1..1`; not in Atlas | Adds a global moisture offset. Negative values favor desert, badlands, shrubland, and savanna; positive values favor forests, marshes, and rainforest. |
| `TransitionSoftness` | `0.055` | Recommended: `0..0.16`; Atlas: `0..0.16` | Widens the jittered band around biome and danger thresholds. `0` gives crisp borders; higher values interleave neighboring biomes more broadly. |

Moisture is calculated before hydrology. Rivers require a moist source, but a river
channel does not currently increase moisture on neighboring tiles. Prevailing wind
runs west to east, so mountains can create a wet windward side and a dry rain shadow.

## Rivers and lakes

| Parameter | Default | Range | Influence |
| --- | ---: | --- | --- |
| `RiverCount` | `42` | Core: `0` or greater; Atlas: `0..160` | Target source count for the normalized world geography. It stays fixed across map dimensions. `0` disables rivers and hydrology-created lakes. |

The generator clamps the effective target to 20,000. With the default value it
attempts 42 accepted sources at every map size. Map dimensions increase raster
resolution rather than geographic extent, so scaling the count would overcrowd the
same normalized terrain with duplicate parallel streams. Source spacing and minimum
path length still scale in tiles, allowing rivers to use the extra resolution.

The target is not a guarantee. A source must:

- be away from the map border and at least six tiles inland;
- be at least `SeaLevel + 0.16` in elevation;
- have moisture of roughly `0.30` or higher;
- be separated from accepted sources by a resolution-aware minimum distance; and
- produce a sufficiently long downhill path that reaches ocean, a lake basin, or an
  existing river.

Rejected, short, or truncated paths do not carve terrain or leave flow fragments.
Accepted paths accumulate flow downstream, carve a shallow channel, and may fill a
local minimum as a lake. Nearby paths can merge into tributaries. Lower
`RiverCount` first when a map still feels busy; raising `MountainStrength` or
`MoistureBias` can make more candidate headwaters eligible.

## Difficulty

| Parameter | Default | Range | Influence |
| --- | ---: | --- | --- |
| `DifficultyStrength` | `0.85` | Core and Atlas: `0..1` | Scales the full danger field. `0` disables danger-biome overrides; `1` allows the strongest hostile interior. |
| `DifficultyCurve` | `1.6` | Core: `0.25..6`; Atlas: `0.5..4` | Exponent applied to center proximity. Values above `1` preserve a wider safe outer region and compress danger toward the core; values below `1` spread danger outward. |
| `DifficultyWarp` | `0.28` | Core and Atlas: `0..1` | Distorts danger rings with broad and fine noise. `0` gives regular center-out bands; larger values make lobed, irregular zones. |

High altitude contributes danger independently of center proximity. Open ocean is
capped to a lower danger range, while the coastal shelf remains relatively safe.
Freshwater classification happens before danger-biome overrides, so rivers and lakes
remain readable through hostile interior zones.

## Common interactions

- **Cleaner drainage:** lower `RiverCount`. Source spacing and path validation prevent
  clustering, but count remains the direct density control.
- **Broad, readable landforms:** lower `TerrainScale` and keep `CoastWarp` moderate.
- **Rugged interior:** raise `MountainStrength`; expect colder peaks, stronger rain
  shadows, and more pronounced downhill drainage.
- **More forests and potential headwaters:** raise `MoistureBias`. This changes both
  biome selection and river-source eligibility.
- **Ice-age world:** lower `TemperatureBias`; high, cold river tiles can classify as
  glacier instead of river.
- **Wider safe coast:** raise `DifficultyCurve` above `1`. Use
  `DifficultyStrength` to control severity and `DifficultyWarp` to control shape.
- **Stable comparison:** keep every option fixed and change only `Seed`. This is the
  quickest way to judge whether tuning works across different worlds.

The source of truth for defaults and hard validation is
[`WorldGenerationOptions.cs`](../src/ProceduralWorld.Core/World/WorldGenerationOptions.cs).
