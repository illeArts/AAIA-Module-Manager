using AAIA.Shared.Contracts.Marketplace;
using System.IO;
using System.Text.Json;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Orchestriert den vollständigen Publish-Flow:
/// Build → Pack (.aaix) → Sign → NuGet push → GitHub Release → Marketplace API.
/// </summary>
public sealed class PublishService(
    PublisherCertService certSvc,
    MarketplaceApiClient marketplace,
    AppConfig            config)
{
    public record PublishOptions(
        string  ProjectPath,
        string  Version,
        string  Changelog,
        string? PrivateKeyPath,
        string? KeyId,
        bool    PublishNuGet   = false,
        bool    CreateGitHub   = false,
        bool    PublishToMarketplace = true);

    public record PublishResult(
        bool    Success,
        string? ModuleId,
        string? MarketplaceUrl,
        string? NuGetPackageUrl,
        string? GitHubReleaseUrl,
        string? Error);

    public async Task<PublishResult> PublishAsync(
        PublishOptions opts,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("📦 Lese Manifest...");
            var manifestPath = Path.Combine(opts.ProjectPath, "aaia-extension.json");
            if (!File.Exists(manifestPath))
                return Fail($"aaia-extension.json nicht gefunden in {opts.ProjectPath}");

            var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
            using var doc    = JsonDocument.Parse(manifestJson);
            var moduleId = doc.RootElement.GetProperty("id").GetString();
            if (moduleId is null)
                return Fail("Manifest hat kein 'id' Feld.");

            // ── Build ──────────────────────────────────────────────────────────
            progress?.Report($"🔨 Build {moduleId} v{opts.Version}...");
            var buildResult = await ProcessRunner.RunCapturedAsync(
                "dotnet",
                $"publish \"{opts.ProjectPath}\" -c Release -o \"{opts.ProjectPath}/publish\"",
                ct: ct);

            if (!buildResult.Success)
                return Fail($"Build fehlgeschlagen:\n{buildResult.Output}");

            // ── Pack (.aaix) ───────────────────────────────────────────────────
            progress?.Report("🗜 Erstelle .aaix-Paket...");
            var aaixPath = Path.Combine(opts.ProjectPath, "publish", $"{moduleId}-{opts.Version}.aaix");
            // TODO: aaix-Packer implementieren (zip publish/* + manifest)
            // Placeholder: publish-Verzeichnis zippen
            System.IO.Compression.ZipFile.CreateFromDirectory(
                Path.Combine(opts.ProjectPath, "publish"),
                aaixPath);

            // ── Sign ───────────────────────────────────────────────────────────
            string? sha256 = null;
            if (opts.PrivateKeyPath is not null && opts.KeyId is not null)
            {
                progress?.Report("🔐 Signiere Paket...");
                sha256 = await certSvc.SignPackageAsync(aaixPath, opts.PrivateKeyPath, opts.KeyId, ct);
            }

            // ── NuGet ──────────────────────────────────────────────────────────
            string? nugetUrl = null;
            if (opts.PublishNuGet)
            {
                progress?.Report("📤 NuGet push...");
                // dotnet pack + dotnet nuget push
                await ProcessRunner.RunCapturedAsync(
                    "dotnet",
                    $"pack \"{opts.ProjectPath}\" -c Release -p:PackageVersion={opts.Version} -o \"{opts.ProjectPath}/nupkg\"",
                    ct: ct);
                await ProcessRunner.RunCapturedAsync(
                    "dotnet",
                    $"nuget push \"{opts.ProjectPath}/nupkg/*.nupkg\" --source https://api.nuget.org/v3/index.json --skip-duplicate",
                    ct: ct);
                nugetUrl = $"https://www.nuget.org/packages/{moduleId}/{opts.Version}";
            }

            // ── GitHub Release ─────────────────────────────────────────────────
            string? githubUrl = null;
            if (opts.CreateGitHub)
            {
                progress?.Report("🐙 GitHub Release...");
                // TODO: Octokit.NET integration (Phase 4)
                githubUrl = null;
            }

            // ── Marketplace API ────────────────────────────────────────────────
            string? marketplaceUrl = null;
            if (opts.PublishToMarketplace)
            {
                if (!string.IsNullOrEmpty(config.MarketplaceToken))
                    marketplace.SetBearer(config.MarketplaceToken);

                progress?.Report("🌐 Veröffentliche im Marketplace...");
                var pubResult = await marketplace.PublishModuleAsync(
                    moduleId,
                    new ModulePublishRequest(
                        ModuleId:      moduleId,
                        Version:       opts.Version,
                        Changelog:     opts.Changelog,
                        NuGetVersion:  opts.PublishNuGet  ? opts.Version : null,
                        GitHubRelease: githubUrl),
                    ct,
                    filePath: aaixPath);

                if (!pubResult.Success)
                    return Fail($"Marketplace-Publish fehlgeschlagen: {pubResult.Error}");

                marketplaceUrl = pubResult.MarketplaceUrl;
            }

            progress?.Report("✅ Fertig!");
            return new PublishResult(
                Success:         true,
                ModuleId:        moduleId,
                MarketplaceUrl:  marketplaceUrl,
                NuGetPackageUrl: nugetUrl,
                GitHubReleaseUrl: githubUrl,
                Error:           null);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static PublishResult Fail(string error) =>
        new(f