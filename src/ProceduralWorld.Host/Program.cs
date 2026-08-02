// Minimal static host for the published Blazor client.
//
// Two things the app needs that a plain file server will not do:
//  * COOP/COEP headers, without which the browser refuses to expose
//    SharedArrayBuffer and the WASM threads the generator relies on.
//  * Correct MIME types for the .wasm and .dat files in the boot sequence.
//
// The client is published separately rather than referenced, because its own
// build is what substitutes the fingerprinted asset names into index.html.
//
//   dotnet publish src/ProceduralWorld.Web -c Release -o publish-web
//   dotnet run --project src/ProceduralWorld.Host -- publish-web/wwwroot

using Microsoft.AspNetCore.StaticFiles;

string requestedWebRoot = args.FirstOrDefault(a => !a.StartsWith('-'))
    ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
string webRoot = ResolveWebRoot(requestedWebRoot);

if (!Directory.Exists(webRoot))
{
    Console.Error.WriteLine($"Web root not found: {webRoot}");
    Console.Error.WriteLine("Publish the client first:");
    Console.Error.WriteLine("  dotnet publish src/ProceduralWorld.Web -c Release -o publish-web");
    return 1;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRoot,
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    // Required for SharedArrayBuffer, which .NET WASM threads are built on.
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
    await next();
});

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".wasm"] = "application/wasm";
contentTypes.Mappings[".dat"] = "application/octet-stream";
contentTypes.Mappings[".blat"] = "application/octet-stream";
contentTypes.Mappings[".pdb"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    ServeUnknownFileTypes = true,
});

app.MapFallbackToFile("index.html");

Console.WriteLine($"Serving {webRoot}");
app.Run();

return 0;

static string ResolveWebRoot(string requestedPath)
{
    if (Path.IsPathRooted(requestedPath)) return Path.GetFullPath(requestedPath);

    for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
         directory is not null;
         directory = directory.Parent)
    {
        string candidate = Path.GetFullPath(Path.Combine(directory.FullName, requestedPath));
        if (Directory.Exists(candidate)) return candidate;
    }

    return Path.GetFullPath(requestedPath);
}
