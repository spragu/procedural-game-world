# Procedural World Atlas

A procedural world-map generator written in C#, running entirely in the browser
via Blazor WebAssembly. It generates a continent surrounded by open ocean, with
climate bands, rivers, biomes, and a radial difficulty gradient that makes the
interior of the map more dangerous than the coast.

The generator is pure .NET — no shaders, no JS — and is AOT-compiled to
WebAssembly with multithreading enabled so large worlds stay interactive.

## Features

- Three landmass shapes: island, continent, archipelago
- Fractal + ridged noise terrain with coastline warping
- Latitude-driven temperature, moisture, and biome classification with soft
  transition bands between biomes
- River carving via hydrology passes
- Radial danger field (strength, curve, zone irregularity) layered over terrain,
  climate, and biome choice
- Multiple view modes (biome, elevation, climate, danger), zoom levels, and
  per-tile inspection on hover
- Map sizes from 512×320 up to 4096×4096

## Layout

| Project | Purpose |
| --- | --- |
| `src/ProceduralWorld.Core` | Generation, noise, biomes, hydrology, rendering. No UI dependencies. |
| `src/ProceduralWorld.Web` | Blazor WebAssembly front end (the atlas UI). |
| `src/ProceduralWorld.Host` | Minimal ASP.NET Core static host that serves the published client with the COOP/COEP headers WASM threads require. |
| `src/ProceduralWorld.Bench` | Console benchmark: generation/render timings and peak managed memory across map sizes. |

## Requirements

- .NET 10 SDK
- For Release publishing, the WebAssembly AOT workload:

  ```powershell
  dotnet workload install wasm-tools
  ```

## Running

### Development (fast iteration, slow generation)

```powershell
dotnet run --project src/ProceduralWorld.Web
```

The development build uses the IL interpreter and a serial generator so it can
start reliably without WebAssembly worker threads. Use the small size preset
while iterating; use the Release path below for fast, parallel world generation.

### Release (AOT-compiled, what you actually want)

```powershell
dotnet publish src/ProceduralWorld.Web -c Release -o publish-web
dotnet run --project src/ProceduralWorld.Host -- publish-web/wwwroot
```

Then open the URL printed by the host. The publish step is what fingerprints the
asset names into `index.html`, which is why the client is published separately
rather than referenced by the host.

AOT makes a large difference: the interpreted development build can take about
10 s for even the default 512×320 world. On a 16-core development machine, the
threaded AOT build generated a 4096×4096 world in about 3.4 s.

### Benchmark

```powershell
dotnet run --project src/ProceduralWorld.Bench -c Release
```

Prints tiles, generation time, overview render time, and peak managed memory for
512×320 through 4096×4096.

## Notes

- Release builds enable `WasmEnableThreads`; the browser needs `SharedArrayBuffer`,
  which requires cross-origin isolation headers. Serving the published output from
  a plain static file server without those headers will fail to boot.
- The maximum heap is capped at 2 GB for compatibility with WASM helper modules;
  a 4096×4096 world peaks near 535 MB of managed memory.
- Generator passes, parameter ranges, and tuning interactions are documented in
  [docs/generator-parameters.md](docs/generator-parameters.md).
