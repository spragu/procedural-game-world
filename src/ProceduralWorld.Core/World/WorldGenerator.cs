using System.Runtime.CompilerServices;
using ProceduralWorld.Core.Noise;
using ProceduralWorld.Core.Threading;

namespace ProceduralWorld.Core.World;

/// <summary>
/// The full world generation pipeline.
///
/// Passes, in order:
///  1. Continent mask      - warped radial falloff that guarantees open ocean on every edge.
///  2. Elevation           - domain-warped fBm plus a ridged mountain fractal biased inland.
///  3. Sea-level calibration - elevation is remapped so the target water fraction lands
///                            exactly on <see cref="WorldGenerationOptions.SeaLevel"/>.
///  4. Slope + shore distance - chamfer distance transform from every water tile.
///  5. Temperature         - latitude banding, elevation lapse rate, maritime moderation.
///  6. Moisture            - billowed fBm, coastal humidity and a west-to-east rain shadow.
///  7. Hydrology           - steepest-descent rivers with flow accumulation and lake basins.
///  8. Danger              - warped centre-proximity field driving the difficulty gradient.
///  9. Classification      - per-tile biome via <see cref="BiomeClassifier"/>.
///
/// Scaling notes for extra-large worlds (up to 4096x4096 = 16.7M tiles):
///  * Every per-pixel pass is row-parallel via <see cref="RowPartitioner"/>, which
///    uses dedicated threads rather than the thread pool. Noise sampling is pure and
///    each pass only writes its own row, so this is safe and scales with core count.
///    Note that generation must therefore be started off the WebAssembly main thread.
///  * Scratch fields are quantised to the smallest type that carries enough precision,
///    and one float buffer is recycled across passes, which keeps peak working set at
///    roughly 21 bytes per tile instead of 40.
///  * Smoothing runs in place off a three-row ring buffer, so it costs kilobytes
///    rather than another full-map allocation.
/// </summary>
public static class WorldGenerator
{
    /// <summary>
    /// Per-pass wall-clock timings from the most recent <see cref="Generate"/> call.
    /// Generation is not re-entrant in practice (one map at a time), so a simple
    /// static is enough and keeps the profiling hook out of the public signature.
    /// </summary>
    public static IReadOnlyList<(string Pass, long Milliseconds)> LastTimings { get; private set; } = [];

    public static WorldMap Generate(WorldGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        int w = options.Width;
        int h = options.Height;

        var fields = new Fields(w, h);
        var seedRng = new SplitMix64((ulong)options.Seed * 0x2545F4914F6CDD1DUL + 11UL);

        var timings = new List<(string, long)>(9);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        void Time(string name, Action pass)
        {
            long from = clock.ElapsedMilliseconds;
            pass();
            timings.Add((name, clock.ElapsedMilliseconds - from));
        }

        Time("mask", () => BuildContinentMask(options, fields));
        Time("elevation", () => BuildElevation(options, fields));
        Time("sea level", () => CalibrateSeaLevel(options, fields));
        Time("slope+shore", () => BuildSlopeAndShore(options, fields));
        Time("temperature", () => BuildTemperature(options, fields));
        Time("moisture", () => BuildMoisture(options, fields));

        var rng = seedRng;
        Time("hydrology", () => BuildHydrology(options, fields, ref rng));

        Time("danger", () => BuildDanger(options, fields));

        var tiles = GC.AllocateUninitializedArray<WorldTile>(w * h);
        Time("classify", () => Classify(options, fields, tiles));

        LastTimings = timings.Select(t => (t.Item1, t.Item2)).ToList();

        return new WorldMap(options, tiles);
    }

    // ------------------------------------------------------------------ masks

    private static void BuildContinentMask(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;

        var warpA = new FractalNoise(o.Seed + 101, octaves: 3, frequency: 1.6f);
        var warpB = new FractalNoise(o.Seed + 202, octaves: 3, frequency: 1.6f);
        var lobes = new FractalNoise(o.Seed + 303, octaves: 4, frequency: 2.1f);
        var islands = new FractalNoise(o.Seed + 404, octaves: 3, frequency: 4.4f);

        (float inner, float outer) = o.Shape switch
        {
            ContinentShape.Island => (0.10f, 0.80f),
            ContinentShape.Archipelago => (0.05f, 1.00f),
            _ => (0.34f, 0.96f),
        };

        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        // Scratch doubles as the continent mask; it is consumed by BuildElevation
        // and then recycled as a working buffer by later passes.
        var mask = f.Scratch;
        var proximity = f.Proximity;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float v = y * invH;
            float ny = v * 2f - 1f;
            int row = y * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float u = x * invW;
                float nx = u * 2f - 1f;

                // Warp the sampling position so the falloff is not a clean ellipse.
                float wx = u + warpA.Sample(u, v) * o.CoastWarp * 0.5f;
                float wy = v + warpB.Sample(u, v) * o.CoastWarp * 0.5f;

                // Blend a euclidean and a chebyshev metric: pure euclidean gives a
                // circle, pure chebyshev gives a square, the mix reads as a landmass.
                float euclid = MathF.Sqrt(nx * nx + ny * ny) * 0.7071f;
                float cheby = MathF.Max(MathF.Abs(nx), MathF.Abs(ny));
                float d = euclid * 0.55f + cheby * 0.45f;

                // Push the coastline in and out with low-frequency lobes.
                d += lobes.Sample(wx * 1.3f, wy * 1.3f) * o.CoastWarp;

                float m = 1f - SmoothStep(inner, outer, d);

                if (o.Shape == ContinentShape.Archipelago)
                {
                    float blob = islands.Sample01(wx * 1.7f, wy * 1.7f);
                    m *= SmoothStep(0.34f, 0.62f, blob);
                }

                // Hard guarantee: the outer rim is always open ocean, no matter what
                // the noise says. Distance to the nearest border, normalised.
                float border = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
                float rim = o.OceanRim <= 0f ? 1f : SmoothStep(0f, o.OceanRim, border);
                m *= rim;

                mask[i] = Math.Clamp(m, 0f, 1f);
                // Centre proximity before warping; reused by the danger field.
                proximity[i] = Math.Clamp(1f - d, 0f, 1f);
            }
        });
    }

    // ------------------------------------------------------------- elevation

    private static void BuildElevation(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;
        float scale = MathF.Max(0.25f, o.TerrainScale);

        var baseNoise = new FractalNoise(o.Seed + 11, octaves: 6, frequency: scale, lacunarity: 2.07f, gain: 0.51f);
        var warpX = new FractalNoise(o.Seed + 12, octaves: 2, frequency: scale * 0.55f);
        var warpY = new FractalNoise(o.Seed + 13, octaves: 2, frequency: scale * 0.55f);
        var ridges = new FractalNoise(o.Seed + 14, octaves: 5, frequency: scale * 0.85f, lacunarity: 2.11f, gain: 0.55f);
        var detail = new FractalNoise(o.Seed + 15, octaves: 2, frequency: scale * 4.5f, gain: 0.45f);

        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);

        var mask = f.Scratch;
        var proximity = f.Proximity;
        var elevation = f.Elevation;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float v = y * invH;
            int row = y * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float u = x * invW;

                float m = mask[i];

                float baseH = baseNoise.Warped(u, v, warpX, warpY, o.CoastWarp * 0.55f) * 0.5f + 0.5f;

                // Mountains concentrate inland: the ridged fractal is weighted by
                // centre proximity, which is what puts the endgame peaks in the core.
                float inland = proximity[i];
                float mountainWeight = 0.30f + 0.70f * inland * inland;
                float ridge = ridges.Ridged(u * 1.05f, v * 1.05f);
                ridge = ridge * ridge * (3f - 2f * ridge); // sharpen the spines

                float land = baseH * 0.72f + 0.34f;
                land += ridge * o.MountainStrength * mountainWeight;
                land += detail.Sample(u, v) * 0.035f;

                // Ocean floor still has structure so deep water is not a flat slab.
                float floor = 0.06f + baseH * 0.20f;

                elevation[i] = Lerp(floor, land, m * m * (3f - 2f * m));
            }
        });

        // Safe to clobber the mask now that every tile has consumed it.
        SmoothInPlace(elevation, w, h, passes: 1);
    }

    /// <summary>
    /// Remaps elevation with a piecewise-linear curve so that exactly the target
    /// fraction of the map sits below <see cref="WorldGenerationOptions.SeaLevel"/>.
    /// This makes land/water balance stable across seeds instead of depending on
    /// whatever the noise happened to produce.
    /// </summary>
    private static void CalibrateSeaLevel(WorldGenerationOptions o, Fields f)
    {
        float targetWater = o.Shape switch
        {
            ContinentShape.Island => 0.60f,
            ContinentShape.Archipelago => 0.68f,
            _ => 0.50f,
        };
        targetWater = Math.Clamp(targetWater + o.OceanRim * 0.35f, 0.25f, 0.85f);

        var elevation = f.Elevation;

        // Sorting 16.7M floats to find one percentile is wasteful, and the clone
        // alone would be another 67 MB. A strided sample of at most ~400k values
        // locates the same percentile to well within quantisation error.
        int stride = Math.Max(1, elevation.Length / 400_000);
        int sampleCount = (elevation.Length + stride - 1) / stride;
        var sample = GC.AllocateUninitializedArray<float>(sampleCount);

        int s = 0;
        for (int i = 0; i < elevation.Length && s < sampleCount; i += stride)
            sample[s++] = elevation[i];

        Array.Sort(sample, 0, s);

        float min = sample[0];
        float max = sample[s - 1];
        float pivot = sample[Math.Clamp((int)(s * targetWater), 0, s - 1)];

        if (max - min < 1e-5f) return;

        float sea = o.SeaLevel;
        float lowSpan = MathF.Max(1e-5f, pivot - min);
        float highSpan = MathF.Max(1e-5f, max - pivot);
        float lowScale = sea / lowSpan;
        float highScale = (1f - sea) / highSpan;

        int h = f.Height, w = f.Width;
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float e = elevation[i];
                elevation[i] = e <= pivot
                    ? Math.Clamp((e - min) * lowScale, 0f, sea)
                    : Math.Clamp(sea + (e - pivot) * highScale, sea, 1f);
            }
        });
    }

    // --------------------------------------------------- slope + shore distance

    private static void BuildSlopeAndShore(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;
        var e = f.Elevation;
        var slope = f.Slope;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            int up = Math.Max(0, y - 1) * w;
            int down = Math.Min(h - 1, y + 1) * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float l = e[row + Math.Max(0, x - 1)];
                float r = e[row + Math.Min(w - 1, x + 1)];
                float u = e[up + x];
                float d = e[down + x];
                float dx = (r - l) * 0.5f;
                float dy = (d - u) * 0.5f;
                slope[i] = QuantiseSlope(MathF.Sqrt(dx * dx + dy * dy));
            }
        });

        // Two-pass 3-4 chamfer distance transform seeded from every water tile.
        // Inherently sequential in both sweeps, so it runs on the recycled float
        // buffer and is quantised down to ushort afterwards.
        var dist = f.Scratch;
        const float Far = 1e9f;
        float sea = o.SeaLevel;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
                dist[row + x] = e[row + x] < sea ? 0f : Far;
        });

        const float Ortho = 1f;
        const float Diag = 1.41421356f;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float best = dist[i];
                if (best == 0f) continue;

                if (y > 0)
                {
                    best = MathF.Min(best, dist[i - w] + Ortho);
                    if (x > 0) best = MathF.Min(best, dist[i - w - 1] + Diag);
                    if (x < w - 1) best = MathF.Min(best, dist[i - w + 1] + Diag);
                }
                if (x > 0) best = MathF.Min(best, dist[i - 1] + Ortho);
                dist[i] = best;
            }
        }

        for (int y = h - 1; y >= 0; y--)
        {
            int row = y * w;
            for (int x = w - 1; x >= 0; x--)
            {
                int i = row + x;
                float best = dist[i];
                if (best == 0f) continue;

                if (y < h - 1)
                {
                    best = MathF.Min(best, dist[i + w] + Ortho);
                    if (x > 0) best = MathF.Min(best, dist[i + w - 1] + Diag);
                    if (x < w - 1) best = MathF.Min(best, dist[i + w + 1] + Diag);
                }
                if (x < w - 1) best = MathF.Min(best, dist[i + 1] + Ortho);
                dist[i] = best;
            }
        }

        var shore = f.ShoreDistance;
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                shore[i] = (ushort)Math.Clamp((int)dist[i], 0, ushort.MaxValue);
            }
        });
    }

    // ------------------------------------------------------------- temperature

    private static void BuildTemperature(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;
        var noise = new FractalNoise(o.Seed + 21, octaves: 3, frequency: 1.9f);

        float invH = 1f / (h - 1);
        float invW = 1f / (w - 1);
        float sea = o.SeaLevel;
        var elevation = f.Elevation;
        var temperature = f.Temperature;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float v = y * invH;
            int row = y * w;

            // Latitude: 0 at the poles (top/bottom edges), 1 at the equator band.
            float lat = 1f - MathF.Abs(v * 2f - 1f);
            float band = MathF.Pow(lat, 0.85f);
            float latitudeTerm = Lerp(0.55f, band, o.LatitudeStrength);

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float u = x * invW;

                float t = latitudeTerm;
                t += noise.Sample(u, v) * 0.09f;

                // Lapse rate: it gets colder the higher you climb.
                float above = MathF.Max(0f, elevation[i] - sea) / MathF.Max(1e-4f, 1f - sea);
                t -= above * above * 0.62f;

                // Oceans moderate temperature toward the global mean.
                if (elevation[i] < sea) t = Lerp(t, 0.5f, 0.28f);

                t += o.TemperatureBias * 0.5f;
                temperature[i] = QuantiseUnit(t);
            }
        });
    }

    // ---------------------------------------------------------------- moisture

    private static void BuildMoisture(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;
        var noise = new FractalNoise(o.Seed + 31, octaves: 4, frequency: 3.1f);

        float invH = 1f / (h - 1);
        float invW = 1f / (w - 1);
        float sea = o.SeaLevel;

        var elevation = f.Elevation;
        var shore = f.ShoreDistance;
        var temperature = f.Temperature;
        var work = f.Scratch;

        // The prevailing-wind term carries state left-to-right, but each row is
        // fully independent of every other, so rows still parallelise cleanly.
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float v = y * invH;
            int row = y * w;

            // Prevailing wind blows west to east. Carry humidity across the row,
            // gaining it over water and dumping it on windward mountain faces.
            float carry = 0.85f;
            float prevElev = elevation[row];

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float u = x * invW;
                float elev = elevation[i];

                if (elev < sea)
                {
                    carry = Lerp(carry, 1f, 0.10f);
                }
                else
                {
                    float climb = MathF.Max(0f, elev - prevElev);
                    carry -= climb * 6.5f;               // orographic rainfall
                    carry -= 0.0009f;                    // gradual drying inland
                    carry = Lerp(carry, 0.55f, 0.006f);  // relax toward continental mean
                }

                prevElev = elev;
                carry = Math.Clamp(carry, 0f, 1f);

                // Baseline humidity keeps the whole world from reading as scrubland;
                // the terms above then carve deserts out of it rather than the
                // other way round.
                float m = 0.22f + carry * 0.46f;
                m += noise.Billow(u, v) * 0.36f;

                // Coastal humidity: everything within a few tiles of the sea is damp.
                m += MathF.Exp(-shore[i] * 0.055f) * 0.16f;

                // Cold air holds less water.
                m *= 0.76f + temperature[i] * (1f / 255f) * 0.24f;

                work[i] = Math.Clamp(m + o.MoistureBias * 0.5f, 0f, 1f);
            }
        });

        SmoothInPlace(work, w, h, passes: 1);

        var moisture = f.Moisture;
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                moisture[i] = QuantiseUnit(work[i]);
            }
        });
    }

    // --------------------------------------------------------------- hydrology

    private static void BuildHydrology(WorldGenerationOptions o, Fields f, ref SplitMix64 rng)
    {
        if (o.RiverCount <= 0) return;

        int w = f.Width, h = f.Height;
        float sea = o.SeaLevel;
        var elev = f.Elevation;
        var flow = f.RiverFlow;
        var shore = f.ShoreDistance;
        var moisture = f.Moisture;

        // Terrain is sampled in normalised map space, so larger dimensions increase
        // raster resolution rather than geographic extent. Keep the requested river
        // count fixed or high-resolution maps become crowded with duplicate streams.
        long area = (long)w * h;
        int riverCount = Math.Min(o.RiverCount, 20_000);

        // Collecting every eligible tile would be tens of millions of ints on a large
        // world. Stride-sampling keeps the candidate pool bounded while still spreading
        // sources evenly across the map.
        int candidateStride = Math.Max(1, (int)(area / 250_000));
        var candidates = new List<int>(Math.Min(262_144, (int)(area / candidateStride) + 1));

        float minShore = 6f;
        for (long p = 0; p < area; p += candidateStride)
        {
            int i = (int)p;
            int y = i / w;
            int x = i - y * w;
            if (x < 2 || y < 2 || x >= w - 2 || y >= h - 2) continue;
            if (elev[i] < sea + 0.16f) continue;
            if (shore[i] < minShore) continue;
            if (moisture[i] < 76) continue; // ~0.30 in byte space
            candidates.Add(i);
        }

        if (candidates.Count == 0) return;

        int maxSteps = w + h;
        int minRiverLength = Math.Max(12, Math.Min(w, h) / 40);
        int sourceSpacing = Math.Max(8, (int)MathF.Sqrt((float)area / riverCount) / 2);
        int sourceCellSize = Math.Max(1, sourceSpacing / 2);
        int sourceCellRadius = (sourceSpacing + sourceCellSize - 1) / sourceCellSize;
        int sourceGridWidth = (w + sourceCellSize - 1) / sourceCellSize;
        int sourceGridHeight = (h + sourceCellSize - 1) / sourceCellSize;
        var sourceGrid = new int[sourceGridWidth * sourceGridHeight];
        Array.Fill(sourceGrid, -1);

        var path = new List<int>(Math.Min(4096, maxSteps));
        var visited = new HashSet<int>();
        int acceptedRivers = 0;

        // Shuffle lazily while scanning candidates. Rejected sources do not consume
        // the river budget, but the candidate pool stays bounded on very large maps.
        for (int candidateIndex = 0;
             candidateIndex < candidates.Count && acceptedRivers < riverCount;
             candidateIndex++)
        {
            int shuffledIndex = candidateIndex + (int)(rng.NextUInt() % (uint)(candidates.Count - candidateIndex));
            (candidates[candidateIndex], candidates[shuffledIndex]) = (candidates[shuffledIndex], candidates[candidateIndex]);

            int current = candidates[candidateIndex];
            int sourceY = current / w;
            int sourceX = current - sourceY * w;
                        int sourceCellX = sourceX / sourceCellSize;
                        int sourceCellY = sourceY / sourceCellSize;
            bool sourceTooClose = false;

                        for (int cellY = Math.Max(0, sourceCellY - sourceCellRadius);
                                 cellY <= Math.Min(sourceGridHeight - 1, sourceCellY + sourceCellRadius) && !sourceTooClose;
                                 cellY++)
            {
                int sourceGridRow = cellY * sourceGridWidth;
                                for (int cellX = Math.Max(0, sourceCellX - sourceCellRadius);
                     cellX <= Math.Min(sourceGridWidth - 1, sourceCellX + sourceCellRadius);
                     cellX++)
                {
                    int other = sourceGrid[sourceGridRow + cellX];
                    if (other < 0) continue;

                    int otherY = other / w;
                    int otherX = other - otherY * w;
                    int deltaX = sourceX - otherX;
                    int deltaY = sourceY - otherY;
                    if (deltaX * deltaX + deltaY * deltaY < sourceSpacing * sourceSpacing)
                    {
                        sourceTooClose = true;
                        break;
                    }
                }
            }

            if (sourceTooClose || flow[current] >= 128) continue;

            path.Clear();
            visited.Clear();
            bool reachesDrainage = false;
            int lakeSeed = -1;

            for (int step = 0; step < maxSteps; step++)
            {
                if (!visited.Add(current)) break;
                path.Add(current);

                if (elev[current] < sea || f.Lake[current] || (path.Count > 1 && flow[current] >= 128))
                {
                    reachesDrainage = true;
                    break;
                }

                int cy = current / w;
                int cx = current - cy * w;

                int best = -1;
                float bestElev = elev[current];

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if ((uint)ny >= (uint)h) continue;
                    int nrow = ny * w;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        if ((uint)nx >= (uint)w) continue;

                        int ni = nrow + nx;
                        if (elev[ni] < bestElev)
                        {
                            bestElev = elev[ni];
                            best = ni;
                        }
                    }
                }

                if (best < 0)
                {
                    lakeSeed = current;
                    reachesDrainage = true;
                    break;
                }

                current = best;
            }

            if (!reachesDrainage || path.Count < minRiverLength) continue;

            if (lakeSeed >= 0) FloodLake(f, lakeSeed, sea);

            sourceGrid[sourceCellY * sourceGridWidth + sourceCellX] = path[0];
            acceptedRivers++;

            // Accumulate flow and carve a shallow channel.
            float volume = 0f;
            foreach (int i in path)
            {
                volume += 0.06f;
                float v = MathF.Min(1f, flow[i] * (1f / 255f) + volume);
                flow[i] = QuantiseUnit(v);
                if (elev[i] > sea) elev[i] = MathF.Max(sea + 0.002f, elev[i] - 0.008f * MathF.Min(1f, volume));
            }
        }

        // Thin, isolated flow specks read as noise rather than rivers; drop them.
        const byte MinFlow = 36; // ~0.14
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (flow[i] < MinFlow) flow[i] = 0;
            }
        });
    }

    private static void FloodLake(Fields f, int seed, float sea)
    {
        int w = f.Width, h = f.Height;
        float level = f.Elevation[seed] + 0.004f;
        if (level <= sea) return;

        // Lowest-first expansion. A plain FIFO flood leaves a square frontier when
        // the budget runs out on flat ground, which reads as an obvious artefact;
        // filling the deepest cells first means truncation follows a contour.
        var frontier = new PriorityQueue<int, float>();
        frontier.Enqueue(seed, f.Elevation[seed]);
        f.Lake[seed] = true;

        int budget = 260;
        while (frontier.TryDequeue(out int i, out _) && budget-- > 0)
        {
            int cy = i / w;
            int cx = i - cy * w;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = cy + dy;
                if ((uint)ny >= (uint)h) continue;
                int nrow = ny * w;

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx;
                    if ((uint)nx >= (uint)w) continue;

                    int ni = nrow + nx;
                    if (f.Lake[ni]) continue;
                    if (f.Elevation[ni] > level) continue;
                    if (f.Elevation[ni] < sea) continue;

                    f.Lake[ni] = true;
                    frontier.Enqueue(ni, f.Elevation[ni]);
                }
            }
        }
    }

    // ------------------------------------------------------------------ danger

    private static void BuildDanger(WorldGenerationOptions o, Fields f)
    {
        int w = f.Width, h = f.Height;

        var warp = new FractalNoise(o.Seed + 41, octaves: 4, frequency: 2.4f);
        var grain = new FractalNoise(o.Seed + 42, octaves: 2, frequency: 7.5f);

        float invW = 1f / (w - 1);
        float invH = 1f / (h - 1);
        float sea = o.SeaLevel;

        var elevation = f.Elevation;
        var proximity = f.Proximity;
        var work = f.Scratch;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float v = y * invH;
            int row = y * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float u = x * invW;

                // Centre proximity, made lumpy so danger rings follow the terrain
                // instead of drawing perfect circles.
                float p = proximity[i];
                p += warp.Sample(u, v) * o.DifficultyWarp * 0.35f;
                p += grain.Sample(u, v) * o.DifficultyWarp * 0.06f;
                p = Math.Clamp(p, 0f, 1f);

                float d = MathF.Pow(p, o.DifficultyCurve);

                // Altitude is its own hazard: peaks are dangerous wherever they are.
                float above = MathF.Max(0f, elevation[i] - sea) / MathF.Max(1e-4f, 1f - sea);
                d = MathF.Max(d, above * above * 0.82f);

                // Open ocean far from land is its own late-game frontier, but the
                // safe coastal shelf must stay gentle.
                if (elevation[i] < sea)
                    d = MathF.Min(d, 0.30f + proximity[i] * 0.20f);

                work[i] = Math.Clamp(d * o.DifficultyStrength, 0f, 1f);
            }
        });

        SmoothInPlace(work, w, h, passes: 2);

        var danger = f.Danger;
        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                danger[i] = QuantiseUnit(work[i]);
            }
        });
    }

    // -------------------------------------------------------------- classify

    private static void Classify(WorldGenerationOptions o, Fields f, WorldTile[] tiles)
    {
        int w = f.Width, h = f.Height;

        var jitter = new FractalNoise(o.Seed + 9931, octaves: 2, frequency: 11f, lacunarity: 2.13f, gain: 0.55f);
        var reef = new FractalNoise(o.Seed + 5501, octaves: 2, frequency: 6.5f);
        var volcanism = new FractalNoise(o.Seed + 7717, octaves: 3, frequency: 4.2f);

        float invW = 1f / w;
        float invH = 1f / h;
        const float Inv255 = 1f / 255f;

        var elevation = f.Elevation;
        var temperature = f.Temperature;
        var moisture = f.Moisture;
        var shore = f.ShoreDistance;
        var slope = f.Slope;
        var river = f.RiverFlow;
        var danger = f.Danger;
        var lake = f.Lake;

        RowPartitioner.For(h, f.MaxDegreeOfParallelism, y =>
        {
            float ny = y * invH;
            int row = y * w;

            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                float nx = x * invW;

                float elev = elevation[i];
                float temp = temperature[i] * Inv255;
                float moist = moisture[i] * Inv255;
                float shoreDist = shore[i];
                float slopeValue = DequantiseSlope(slope[i]);
                float riverFlow = river[i] * Inv255;
                float dangerValue = danger[i] * Inv255;

                var sample = new ClimateSample(
                    Elevation: elev,
                    Temperature: temp,
                    Moisture: moist,
                    ShoreDistance: shoreDist,
                    Slope: slopeValue,
                    RiverFlow: riverFlow,
                    IsLake: lake[i],
                    Reef: reef.Sample01(nx, ny),
                    Jitter: jitter.Sample(nx, ny),
                    Danger: dangerValue,
                    Volcanism: volcanism.Sample01(nx, ny));

                tiles[i] = new WorldTile(
                    BiomeClassifier.Classify(sample, o),
                    elev,
                    temp,
                    moist,
                    shoreDist,
                    riverFlow,
                    slopeValue,
                    dangerValue);
            }
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Box blur applied in place using a two-row history buffer. The obvious
    /// implementation allocates a second full-map array, which on a 4096x4096 world
    /// is another 67 MB per call; this variant needs only two rows.
    ///
    /// The trick is that when row y is being written, rows below it in
    /// <paramref name="data"/> have not been touched yet and still hold their
    /// pre-blur values, so only rows y-1 and y have to be kept aside.
    /// </summary>
    private static void SmoothInPlace(float[] data, int w, int h, int passes)
    {
        if (passes <= 0 || h < 3 || w < 3) return;

        var previousRow = new float[w];
        var currentRow = new float[w];

        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                Array.Copy(data, row, currentRow, 0, w);

                var top = y > 0 ? previousRow : currentRow;

                // Below is still unmodified in `data`, except on the last row where
                // the clamped neighbour is the current row itself.
                float[] bottom;
                int bottomOffset;
                if (y < h - 1) { bottom = data; bottomOffset = row + w; }
                else { bottom = currentRow; bottomOffset = 0; }

                for (int x = 0; x < w; x++)
                {
                    int xl = x > 0 ? x - 1 : 0;
                    int xr = x < w - 1 ? x + 1 : w - 1;

                    float sum =
                        top[xl] + top[x] + top[xr] +
                        currentRow[xl] + currentRow[x] + currentRow[xr] +
                        bottom[bottomOffset + xl] + bottom[bottomOffset + x] + bottom[bottomOffset + xr];

                    // Keep most of the original so smoothing softens rather than blurs.
                    data[row + x] = Lerp(currentRow[x], sum * (1f / 9f), 0.55f);
                }

                (previousRow, currentRow) = (currentRow, previousRow);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte QuantiseUnit(float v) => (byte)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort QuantiseSlope(float v) =>
        (ushort)Math.Clamp((int)(v * 8192f + 0.5f), 0, ushort.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DequantiseSlope(ushort v) => v * (1f / 8192f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 - edge0 < 1e-6f) return x < edge0 ? 0f : 1f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Scratch buffers shared by every generation pass.
    ///
    /// Types are chosen for footprint, not convenience: at 4096x4096 the difference
    /// between float and byte for a single field is 50 MB. <see cref="Scratch"/> is
    /// deliberately recycled - it starts life as the continent mask, then serves as
    /// the distance-transform buffer, then as the moisture and danger accumulators.
    /// </summary>
    private sealed class Fields
    {
        public Fields(int width, int height)
        {
            Width = width;
            Height = height;
            int n = width * height;

            // Fully overwritten before first read - skip the zeroing pass.
            Elevation = GC.AllocateUninitializedArray<float>(n);
            Proximity = GC.AllocateUninitializedArray<float>(n);
            Scratch = GC.AllocateUninitializedArray<float>(n);
            Temperature = GC.AllocateUninitializedArray<byte>(n);
            Moisture = GC.AllocateUninitializedArray<byte>(n);
            ShoreDistance = GC.AllocateUninitializedArray<ushort>(n);
            Slope = GC.AllocateUninitializedArray<ushort>(n);

            // Accumulated into, so these must start zeroed.
            RiverFlow = new byte[n];
            Lake = new bool[n];
            Danger = new byte[n];

            // On a small map the fork/join overhead outweighs the work, so stay serial.
            MaxDegreeOfParallelism = n >= 65_536 ? Environment.ProcessorCount : 1;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Thread count used by every row-parallel pass.</summary>
        public int MaxDegreeOfParallelism { get; }

        public float[] Elevation { get; }
        public float[] Proximity { get; }
        public float[] Scratch { get; }
        public byte[] Temperature { get; }
        public byte[] Moisture { get; }
        public ushort[] ShoreDistance { get; }
        public ushort[] Slope { get; }
        public byte[] RiverFlow { get; }
        public byte[] Danger { get; }
        public bool[] Lake { get; }
    }
}
