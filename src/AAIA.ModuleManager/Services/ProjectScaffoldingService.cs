using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public enum NewProjectType
{
    ServerModule,
    ClientPlugin,
    HybridModule,
    LanguagePack
}

public sealed record ScaffoldOptions(
    NewProjectType Type,
    string         Name,
    string         Id,
    string         Description,
    string         OutputPath,
    string         PublisherId,
    string         SdkPath
);

/// <summary>
/// Erstellt ein neues AAIA-Erweiterungsprojekt (Modul / Plugin / Sprachpaket)
/// mit .csproj, Implementierungsklasse, aaia-manifest.json und README.
/// </summary>
public static class ProjectScaffoldingService
{
    public static async Task<string> ScaffoldAsync(ScaffoldOptions opts)
    {
        var safeName   = SanitizeName(opts.Name);
        var projectDir = Path.Combine(opts.OutputPath, safeName);

        if (Directory.Exists(projectDir))
            throw new InvalidOperationException($"Ordner existiert bereits: {projectDir}");

        Directory.CreateDirectory(projectDir);

        switch (opts.Type)
        {
            case NewProjectType.ServerModule:
                await ScaffoldServerModuleAsync(projectDir, safeName, opts);
                break;
            case NewProjectType.ClientPlugin:
                await ScaffoldClientPluginAsync(projectDir, safeName, opts);
                break;
            case NewProjectType.HybridModule:
                await ScaffoldHybridModuleAsync(projectDir, safeName, opts);
                break;
            case NewProjectType.LanguagePack:
                await ScaffoldLanguagePackAsync(projectDir, safeName, opts);
                break;
        }

        await WriteManifestAsync(projectDir, opts);
        await WriteReadMeAsync(projectDir, safeName, opts);

        return projectDir;
    }

    // ── Server-Modul (AAIAS) ──────────────────────────────────────────────────

    private static async Task ScaffoldServerModuleAsync(string dir, string safeName, ScaffoldOptions opts)
    {
        var ns = $"AAIAS.Modules.{safeName}";

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"{safeName}.csproj"),
            BuildCsproj("Microsoft.NET.Sdk.Web", $"AAIAS.Module.{safeName}", ns));

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"{safeName}Module.cs"),
            BuildServerModuleCs(safeName, ns, opts));
    }

    private static string BuildServerModuleCs(string safeName, string ns, ScaffoldOptions opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Aaia.Shared.Contracts.Modules;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// AAIA Server-Modul: {opts.Name}");
        sb.AppendLine("/// Laeuft als Plugin im AAIAS-Host. Plattformunabhaengig.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public class {safeName}Module : IAaiaModule");
        sb.AppendLine("{");
        sb.AppendLine($"    public string Id          => \"{opts.Id}\";");
        sb.AppendLine($"    public string DisplayName => \"{EscapeCs(opts.Name)}\";");
        sb.AppendLine("    public string Version     => \"1.0.0\";");
        sb.AppendLine($"    public string Description => \"{EscapeCs(opts.Description)}\";");
        sb.AppendLine();
        sb.AppendLine("    public void AddServices(IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine($"        // TODO: Services registrieren, z. B.:");
        sb.AppendLine($"        // services.AddSingleton<{safeName}Service>();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void MapRoutes(WebApplication app)");
        sb.AppendLine("    {");
        sb.AppendLine($"        // Konvention: /api/modules/{opts.Id}/...");
        sb.AppendLine($"        app.MapGet(\"/api/modules/{opts.Id}/status\",");
        sb.AppendLine("            () => Results.Ok(new { status = \"ok\", version = Version }))");
        sb.AppendLine("           .RequireAuthorization();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Client-Plugin (AAIAC) ─────────────────────────────────────────────────

    private static async Task ScaffoldClientPluginAsync(string dir, string safeName, ScaffoldOptions opts)
    {
        var ns = $"AAIAC.Plugins.{safeName}";

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"{safeName}.csproj"),
            BuildCsproj("Microsoft.NET.Sdk", $"AAIAC.Plugin.{safeName}", ns));

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"{safeName}Plugin.cs"),
            BuildClientPluginCs(safeName, ns, opts));
    }

    private static string BuildClientPluginCs(string safeName, string ns, ScaffoldOptions opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// AAIAC Client-Plugin: {opts.Name}");
        sb.AppendLine("//");
        sb.AppendLine("// Implementiere IAaiacPlugin aus AAIA.Client.Core (separates NuGet-Paket).");
        sb.AppendLine("// Der AAIAC-Host laedt das Plugin ueber den PluginHost-Mechanismus.");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// AAIAC-Plugin \"{EscapeCs(opts.Name)}\".");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public class {safeName}Plugin");
        sb.AppendLine("{");
        sb.AppendLine($"    public string Id          => \"{opts.Id}\";");
        sb.AppendLine($"    public string DisplayName => \"{EscapeCs(opts.Name)}\";");
        sb.AppendLine("    public string Version     => \"1.0.0\";");
        sb.AppendLine();
        sb.AppendLine("    // TODO: AAIA.Client.Core als PackageReference einbinden, dann:");
        sb.AppendLine("    // public async Task InitialiseAsync(IAaiacPluginHost host) { }");
        sb.AppendLine("    // public async Task ActivateAsync()                        { }");
        sb.AppendLine("    // public async Task StopAsync()                            { }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Hybrid-Modul (AAIAS + AAIAC) ─────────────────────────────────────────

    private static async Task ScaffoldHybridModuleAsync(string dir, string safeName, ScaffoldOptions opts)
    {
        // Server-Teil
        var serverDir = Path.Combine(dir, "Server");
        Directory.CreateDirectory(serverDir);
        var serverNs = $"AAIAS.Modules.{safeName}";
        await File.WriteAllTextAsync(
            Path.Combine(serverDir, $"{safeName}.Server.csproj"),
            BuildCsproj("Microsoft.NET.Sdk.Web", $"AAIAS.Module.{safeName}", serverNs));
        await File.WriteAllTextAsync(
            Path.Combine(serverDir, $"{safeName}Module.cs"),
            BuildServerModuleCs(safeName, serverNs, opts));

        // Client-Teil
        var clientDir = Path.Combine(dir, "Client");
        Directory.CreateDirectory(clientDir);
        var clientNs = $"AAIAC.Plugins.{safeName}";
        await File.WriteAllTextAsync(
            Path.Combine(clientDir, $"{safeName}.Client.csproj"),
            BuildCsproj("Microsoft.NET.Sdk", $"AAIAC.Plugin.{safeName}", clientNs));
        await File.WriteAllTextAsync(
            Path.Combine(clientDir, $"{safeName}Plugin.cs"),
            BuildClientPluginCs(safeName, clientNs, opts));
    }

    // ── Sprachpaket ───────────────────────────────────────────────────────────

    private static async Task ScaffoldLanguagePackAsync(string dir, string safeName, ScaffoldOptions opts)
    {
        var localesDir = Path.Combine(dir, "locales");
        Directory.CreateDirectory(localesDir);

        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

        await File.WriteAllTextAsync(
            Path.Combine(localesDir, "de.json"),
            JsonSerializer.Serialize(new
            {
                meta = new { language = "de", name = opts.Name, version = "1.0.0" },
                keys = new { example_key = "Beispieltext", another_key = "Weiterer Text" }
            }, jsonOpts));

        await File.WriteAllTextAsync(
            Path.Combine(localesDir, "en.json"),
            JsonSerializer.Serialize(new
            {
                meta = new { language = "en", name = opts.Name, version = "1.0.0" },
                keys = new { example_key = "Example text", another_key = "Another text" }
            }, jsonOpts));
    }

    // ── .csproj-Template ─────────────────────────────────────────────────────

    private static string BuildCsproj(string sdk, string assemblyName, string rootNs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<Project Sdk=\"{sdk}\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
        sb.AppendLine($"    <AssemblyName>{assemblyName}</AssemblyName>");
        sb.AppendLine($"    <RootNamespace>{rootNs}</RootNamespace>");
        sb.AppendLine("    <Version>1.0.0</Version>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <PackageReference Include=\"AAIA.Shared.Contracts\" Version=\"*\"/>");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    // ── Manifest ──────────────────────────────────────────────────────────────

    private static async Task WriteManifestAsync(string dir, ScaffoldOptions opts)
    {
        var (host, kind, pluginClass) = opts.Type switch
        {
            NewProjectType.ServerModule => ("AAIAS",  "Module",    "ServerModule"),
            NewProjectType.ClientPlugin => ("AAIAC",  "Plugin",    "ClientPlugin"),
            NewProjectType.HybridModule => ("Hybrid", "Module",    "HybridModule"),
            NewProjectType.LanguagePack => ("AAIAS",  "Connector", "ServerModule"),
            _                           => ("AAIAS",  "Module",    "ServerModule")
        };

        var manifest = new
        {
            id                 = opts.Id,
            displayName        = opts.Name,
            version            = "1.0.0",
            host,
            kind,
            pluginClass,
            publisherId        = opts.PublisherId,
            publisherEtwId     = (string?)null,
            description        = opts.Description,
            supportedPlatforms = new[] { "all" },
            permissions        = Array.Empty<object>(),
            routes             = Array.Empty<object>(),
            requiredSecrets    = Array.Empty<string>(),
            networkTargets     = Array.Empty<string>(),
            licenseModel       = "Free",
            nuGetPackageId     = (string?)null,
            repository         = (string?)null
        };

        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        await File.WriteAllTextAsync(
            Path.Combine(dir, "aaia-manifest.json"),
            JsonSerializer.Serialize(manifest, jsonOpts));
    }

    // ── README ────────────────────────────────────────────────────────────────

    private static async Task WriteReadMeAsync(string dir, string safeName, ScaffoldOptions opts)
    {
        var typeLabel = opts.Type switch
        {
            NewProjectType.ServerModule => "AAIA Server-Modul",
            NewProjectType.ClientPlugin => "AAIA Client-Plugin",
            NewProjectType.HybridModule => "AAIA Hybrid-Modul (Server + Client)",
            NewProjectType.LanguagePack => "AAIA Sprachpaket",
            _                           => "AAIA Extension"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"# {opts.Name}");
        sb.AppendLine();
        sb.AppendLine($"**Typ:** {typeLabel}  ");
        sb.AppendLine($"**ID:** `{opts.Id}`  ");
        sb.AppendLine($"**Publisher:** {opts.PublisherId}");
        sb.AppendLine();
        sb.AppendLine("## Beschreibung");
        sb.AppendLine();
        sb.AppendLine(opts.Description);
        sb.AppendLine();
        sb.AppendLine("## Entwicklung");
        sb.AppendLine();
        sb.AppendLine("1. SDK einbinden: `dotnet restore`");
        if (opts.Type == NewProjectType.HybridModule)
        {
            sb.AppendLine($"2. Server-Implementierung in `Server/{safeName}Module.cs` anpassen");
            sb.AppendLine($"3. Client-Implementierung in `Client/{safeName}Plugin.cs` anpassen");
        }
        else
        {
            sb.AppendLine($"2. Implementierung in `{safeName}Module.cs` anpassen");
        }
        sb.AppendLine("3. `aaia-manifest.json` vervollstaendigen");
        sb.AppendLine("4. Im AAIA Module Manager testen → Tab \"Tester\"");
        sb.AppendLine("5. Im AAIA Module Manager veroeffentlichen → Tab \"Publish\"");
        sb.AppendLine();
        sb.AppendLine("## Links");
        sb.AppendLine();
        sb.AppendLine("- [AAIA SDK](https://github.com/illeArts/AAIA-Module-Manager)");
        sb.AppendLine("- [AAIA Marketplace](https://aaiagent.de)");

        await File.WriteAllTextAsync(Path.Combine(dir, "README.md"), sb.ToString());
    }

    // ── Hilfsfunktionen ───────────────────────────────────────────────────────

    private static string SanitizeName(string name)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "");
        return string.IsNullOrWhiteSpace(clean) ? "MyModule" : clean;
    }

    private static string EscapeCs(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
