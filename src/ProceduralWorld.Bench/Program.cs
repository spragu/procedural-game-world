using System.Diagnostics;
using ProceduralWorld.Core.Rendering;
using ProceduralWorld.Core.World;

// Times generation and rendering across map sizes and reports peak managed memory,
// which is the limiting factor for extra-large worlds in a browser.

int[][] sizes =
[
    [512, 320],
    [1024, 1024],
    [2048, 2048],
    [4096, 4096],
];

Console.WriteLine($"Cores: {Environment.ProcessorCount}");
Console.WriteLine($"{"Size",-12}{"Tiles",12}{"Generate",12}{"Overview",12}{"Managed MB",12}");
Console.WriteLine(new string('-', 60));

foreach (var size in sizes)
{
    int w = size[0], h = size[1];

    var options = new WorldGenerationOptions
    {
        Seed = 1337,
        Width = w,
        Height = h,
    };

    GC.Collect();
    GC.WaitForPendingFinalizers();
    long before = GC.GetTotalMemory(true);

    var sw = Stopwatch.StartNew();
    var map = WorldGenerator.Generate(options);
    sw.Stop();
    long generateMs = sw.ElapsedMilliseconds;

    long peak = GC.GetTotalMemory(false) - before;

    sw.Restart();
    var pixels = WorldRenderer.RenderRgba(map, new RenderOptions
    {
        Region = RenderRegion.Overview(map),
    });
    sw.Stop();

    Console.WriteLine(
        $"{w + "x" + h,-12}{(long)w * h,12:N0}{generateMs + " ms",12}{sw.ElapsedMilliseconds + " ms",12}{peak / (1024.0 * 1024.0),12:N0}");

    Console.WriteLine("  " + string.Join(", ",
        WorldGenerator.LastTimings.Select(timing => $"{timing.Pass}: {timing.Milliseconds} ms")));

    // Sanity: the outer rim must be open water on every edge, and the interior
    // must reach a higher danger tier than the coast.
    var corner = map[2, 2];
    var centre = map[w / 2, h / 2];
    if (!corner.IsWater) Console.WriteLine("  WARNING: map corner is not water");
    if (centre.Danger <= corner.Danger) Console.WriteLine("  WARNING: centre is not more dangerous than the rim");

    GC.KeepAlive(pixels);
}
