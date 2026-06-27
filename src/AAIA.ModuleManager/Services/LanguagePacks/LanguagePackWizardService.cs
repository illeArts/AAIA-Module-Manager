using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AAIA.ModuleManager.Services.LanguagePacks;

public sealed record LanguagePackWizardInput(
    string PackageId,
    string Name,
    string Version,
    string Locale,
    string FallbackLocale,
    string TargetKind,
    string MinAaiaVersion,
    string PublisherEtwId,
    bool ContainsSecurityText,
    bool ContainsLegalText,
    bool ContainsMarketplaceText = false,
    string? TargetPackageId = null);

public sealed record LanguagePackWizardManifest(
    string SchemaVersion,
    string Type,
    string PackageId,
    string Name,
    string Version,
    string Locale,
    string FallbackLocale,
    string TargetKind,
    string MinAaiaVersion,
    string PublisherEtwId,
    bool ContainsSecurityText,
    bool ContainsLegalText,
    IReadOnlyList<LanguagePackWizardFileHash> Files,
    string? TargetPackageId = null,
    bool ContainsMarketplaceText = false,
    DateTimeOffset? CreatedAt = null);

public sealed record LanguagePackWizardFileHash(string Path, string Sha256);

public sealed record TranslationKeyRegistry(
    string SchemaVersion,
    string SourceLocale,
    IReadOnlyList<TranslationKeyRegistryEntry> Keys);

public sealed record TranslationKeyRegistryEntry(
    string Namespace,
    string Key,
    string SourceText,
    string SourceLocale,
    bool Translatable,
    bool Locked,
    bool SecurityReviewRequired,
    bool LegalReviewRequired,
    bool MarketplaceReviewRequired,
    IReadOnlyList<string> Placeholders,
    string ProtectionCategory = "none",
    int? MaxLength = null,
    string? Context = null,
    IReadOnlyList<string>? GlossaryTerms = null,
    string? Owner = null);

public sealed record LanguagePackTranslationDraft(
    string Locale,
    IReadOnlyList<LanguagePackTranslationDraftEntry> Entries);

public sealed record LanguagePackTranslationDraftEntry(
    string Namespace,
    string Key,
    string Text,
    string ReviewState = "draft",
    IReadOnlyList<string>? GlossaryTerms = null,
    bool HasSecurityReview = false,
    bool HasOwnerAdminReview = false,
    bool HasMarketplaceReview = false);

public sealed record LanguagePackDryRunIssue(
    string Severity,
    string Code,
    string Message,
    string? Namespace = null,
    string? Key = null);

public sealed record LanguagePackDryRunResult(
    IReadOnlyList<LanguagePackDryRunIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => string.Equals(i.Severity, "Error", StringComparison.Ordinal));
    public bool IsValid => !HasErrors;
}

public sealed record LanguagePackDraftPackageResult(
    bool Success,
    string? PackagePath,
    string? PackageSha256,
    string Status,
    IReadOnlyList<string> IncludedFiles,
    LanguagePackDryRunResult DryRun);

public static class LanguagePackWizardBoundary
{
    public const bool AllowsMarketplaceUpload = false;
    public const bool AllowsPublish = false;
    public const bool AllowsRuntimeActivation = false;
    public const bool AllowsClientInstall = false;
    public const bool AllowsFirstRunApply = false;
}

public static class LanguagePackWizardService
{
    public const string LanguagePackType = "language-pack";
    public const string DraftStatus = "draft";
    public const string LocalValidatedStatus = "local_validated";

    public static readonly IReadOnlySet<string> TargetKinds = new HashSet<string>(
        ["core-ui", "setup-first-run", "marketplace", "module", "documentation", "legal-compliance"],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ProtectedGlossaryTerms = new HashSet<string>(
        ["AAIA", "AAIAS", "AAIAC", "AAIAM", "ETW", "Module Manager", "SecureLink", "FirstRun", "Marketplace", "Publisher", "License", "Audit", "Revoke", "Rollback"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex LocalePattern = new(
        "^[a-z]{2,3}(-[A-Z]{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SemVerPattern = new(
        @"^\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderPattern = new(
        @"\{[A-Za-z0-9_.-]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<LanguagePackDryRunIssue> ValidateInput(LanguagePackWizardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var issues = new List<LanguagePackDryRunIssue>();
        Require(input.PackageId, "manifest.packageId.required", "packageId is required.", issues);
        Require(input.Name, "manifest.name.required", "name is required.", issues);
        Require(input.MinAaiaVersion, "manifest.minAaiaVersion.required", "minAaiaVersion is required.", issues);
        Require(input.PublisherEtwId, "manifest.publisherEtwId.required", "publisherEtwId is written for traceability but is not trusted as authoritative publisher truth.", issues);

        if (string.IsNullOrWhiteSpace(input.Version) || !SemVerPattern.IsMatch(input.Version))
        {
            issues.Add(Error("manifest.version.invalid", "version must be semver-compatible."));
        }

        if (!IsValidLocale(input.Locale))
        {
            issues.Add(Error("manifest.locale.invalid", "locale must be a BCP-47-like value such as de-DE or en-US."));
        }

        if (!IsValidLocale(input.FallbackLocale))
        {
            issues.Add(Error("manifest.fallbackLocale.invalid", "fallbackLocale must be a BCP-47-like value."));
        }

        if (!TargetKinds.Contains(input.TargetKind))
        {
            issues.Add(Error("manifest.targetKind.unknown", "targetKind must be a canonical Phase 16 language-pack target kind."));
        }

        if (string.Equals(input.TargetKind, "legal-compliance", StringComparison.Ordinal) && !input.ContainsLegalText)
        {
            issues.Add(Error("manifest.legalCompliance.requiresLegalText", "legal-compliance language packs must declare containsLegalText."));
        }

        return issues;
    }

    public static LanguagePackWizardManifest CreateManifest(
        LanguagePackWizardInput input,
        IReadOnlyList<LanguagePackWizardFileHash>? files = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new(
            SchemaVersion: "1.0",
            Type: LanguagePackType,
            PackageId: input.PackageId,
            Name: input.Name,
            Version: input.Version,
            Locale: input.Locale,
            FallbackLocale: input.FallbackLocale,
            TargetKind: input.TargetKind,
            MinAaiaVersion: input.MinAaiaVersion,
            PublisherEtwId: input.PublisherEtwId,
            ContainsSecurityText: input.ContainsSecurityText,
            ContainsLegalText: input.ContainsLegalText,
            Files: files ?? [],
            TargetPackageId: input.TargetPackageId,
            ContainsMarketplaceText: input.ContainsMarketplaceText,
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow);
    }

    public static LanguagePackDryRunResult DryRunValidate(
        LanguagePackWizardInput input,
        TranslationKeyRegistry registry,
        LanguagePackTranslationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(draft);

        var issues = new List<LanguagePackDryRunIssue>();
        issues.AddRange(ValidateInput(input));

        if (!string.Equals(input.Locale, draft.Locale, StringComparison.Ordinal))
        {
            issues.Add(Error("draft.locale.mismatch", "draft locale must match manifest locale."));
        }

        var registryByKey = registry.Keys.ToDictionary(
            k => $"{k.Namespace}:{k.Key}",
            StringComparer.Ordinal);

        var draftByKey = draft.Entries
            .GroupBy(e => $"{e.Namespace}:{e.Key}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var duplicate in draftByKey.Where(kvp => kvp.Value.Count > 1))
        {
            var first = duplicate.Value[0];
            issues.Add(Error("draft.key.duplicate", "translation draft contains a duplicate key.", first.Namespace, first.Key));
        }

        foreach (var entry in draft.Entries)
        {
            var canonical = $"{entry.Namespace}:{entry.Key}";
            if (!registryByKey.TryGetValue(canonical, out var key))
            {
                issues.Add(Error("registry.key.unknown", "translation draft contains a key that is not present in the registry.", entry.Namespace, entry.Key));
                continue;
            }

            ValidateEntryAgainstRegistry(key, entry, issues);
        }

        foreach (var required in registry.Keys.Where(k => k.Translatable && !k.Locked))
        {
            var canonical = $"{required.Namespace}:{required.Key}";
            if (!draftByKey.ContainsKey(canonical))
            {
                issues.Add(Error("translation.required.missing", "required translatable registry key is missing from the draft.", required.Namespace, required.Key));
            }
        }

        return new(issues);
    }

    public static async Task CreateProjectAsync(
        string projectDir,
        LanguagePackWizardInput input,
        TranslationKeyRegistry registry,
        LanguagePackTranslationDraft draft,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "translations"));
        Directory.CreateDirectory(Path.Combine(projectDir, "evidence"));

        var translationRelativePath = $"translations/{input.Locale}.json";
        var translationPath = Path.Combine(projectDir, "translations", $"{input.Locale}.json");
        await File.WriteAllTextAsync(translationPath, JsonSerializer.Serialize(draft, JsonOptions), ct);

        var translationHash = await ComputeSha256Async(translationPath, ct);
        var manifest = CreateManifest(
            input,
            [new LanguagePackWizardFileHash(translationRelativePath, translationHash)]);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            ct);

        var dryRun = DryRunValidate(input, registry, draft);
        await File.WriteAllTextAsync(
            Path.Combine(projectDir, "evidence", "validation-report.json"),
            JsonSerializer.Serialize(BuildEvidenceReport(dryRun), JsonOptions),
            ct);

        var readme = "# AAIA Language Pack Draft\n\nLocal Phase 16.3 draft. No Marketplace upload, publish, runtime activation, or client install is performed.\n";
        await File.WriteAllTextAsync(Path.Combine(projectDir, "README.md"), readme, ct);
    }

    public static async Task<LanguagePackDraftPackageResult> BuildDraftPackageAsync(
        string projectDir,
        string? outputDir = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);
        outputDir ??= Path.Combine(projectDir, "packages");
        Directory.CreateDirectory(outputDir);

        var manifestPath = Path.Combine(projectDir, "manifest.json");
        var translationDir = Path.Combine(projectDir, "translations");
        var validationReportPath = Path.Combine(projectDir, "evidence", "validation-report.json");

        if (!File.Exists(manifestPath) || !Directory.Exists(translationDir) || !File.Exists(validationReportPath))
        {
            var dryRun = new LanguagePackDryRunResult([
                Error("package.structure.invalid", "language-pack draft requires manifest.json, translations/, and evidence/validation-report.json.")
            ]);
            return new(false, null, null, DraftStatus, [], dryRun);
        }

        var manifest = JsonSerializer.Deserialize<LanguagePackWizardManifest>(
            await File.ReadAllTextAsync(manifestPath, ct),
            JsonOptions);
        if (manifest is null)
        {
            var dryRun = new LanguagePackDryRunResult([
                Error("manifest.json.invalid", "manifest.json could not be parsed.")
            ]);
            return new(false, null, null, DraftStatus, [], dryRun);
        }

        var safePackageId = SanitizeFileName(manifest.PackageId);
        var safeVersion = SanitizeFileName(manifest.Version);
        var packagePath = Path.Combine(outputDir, $"{safePackageId}.{safeVersion}.aaialangdraft");
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        var included = new List<string>();
        using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            AddFile(zip, manifestPath, "manifest.json", included);

            foreach (var file in Directory.EnumerateFiles(translationDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                AddFile(zip, file, $"translations/{Path.GetFileName(file)}", included);
            }

            AddFile(zip, validationReportPath, "evidence/validation-report.json", included);

            var readmePath = Path.Combine(projectDir, "README.md");
            if (File.Exists(readmePath))
            {
                AddFile(zip, readmePath, "README.md", included);
            }
        }

        var packageHash = await ComputeSha256Async(packagePath, ct);
        var dryRunResult = new LanguagePackDryRunResult([]);
        return new(true, packagePath, packageHash, LocalValidatedStatus, included, dryRunResult);
    }

    private static void ValidateEntryAgainstRegistry(
        TranslationKeyRegistryEntry key,
        LanguagePackTranslationDraftEntry entry,
        List<LanguagePackDryRunIssue> issues)
    {
        if (key.Locked)
        {
            issues.Add(Error("translation.key.locked", "locked registry keys cannot be overwritten.", entry.Namespace, entry.Key));
        }

        if (!key.Translatable)
        {
            issues.Add(Error("translation.key.notTranslatable", "translatable=false registry keys cannot be translated.", entry.Namespace, entry.Key));
        }

        var expectedPlaceholders = new HashSet<string>(key.Placeholders, StringComparer.Ordinal);
        var actualPlaceholders = ExtractPlaceholders(entry.Text).ToHashSet(StringComparer.Ordinal);

        foreach (var placeholder in expectedPlaceholders.Except(actualPlaceholders))
        {
            issues.Add(Error("translation.placeholder.missing", $"placeholder {placeholder} is missing.", entry.Namespace, entry.Key));
        }

        foreach (var placeholder in actualPlaceholders.Except(expectedPlaceholders))
        {
            issues.Add(Error("translation.placeholder.extra", $"placeholder {placeholder} is not declared by the registry key.", entry.Namespace, entry.Key));
        }

        foreach (var term in key.GlossaryTerms ?? [])
        {
            if (ProtectedGlossaryTerms.Contains(term))
            {
                if (!entry.Text.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    entry.GlossaryTerms is null ||
                    !entry.GlossaryTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
                {
                    issues.Add(Warning("translation.glossary.protectedTerm", $"protected glossary term {term} must remain visible and marked.", entry.Namespace, entry.Key));
                }
            }
        }

        if (key.SecurityReviewRequired && !entry.HasSecurityReview)
        {
            issues.Add(Warning("translation.securityReview.required", "security text requires review before release.", entry.Namespace, entry.Key));
        }

        if (key.LegalReviewRequired && !entry.HasOwnerAdminReview)
        {
            issues.Add(Warning("translation.legalReview.required", "legal text requires owner/admin review before release.", entry.Namespace, entry.Key));
        }

        if (key.MarketplaceReviewRequired && !entry.HasMarketplaceReview)
        {
            issues.Add(Warning("translation.marketplaceReview.required", "marketplace text requires marketplace review before release.", entry.Namespace, entry.Key));
        }

        if (key.MaxLength is { } maxLength && entry.Text.Length > maxLength)
        {
            issues.Add(Warning("translation.maxLength.exceeded", "translated text exceeds the registry maxLength budget.", entry.Namespace, entry.Key));
        }
    }

    private static object BuildEvidenceReport(LanguagePackDryRunResult dryRun) => new
    {
        phase = "16.3",
        status = dryRun.IsValid ? LocalValidatedStatus : DraftStatus,
        generatedAt = DateTimeOffset.UtcNow,
        boundary = new
        {
            marketplaceUpload = false,
            publish = false,
            runtimeActivation = false,
            clientInstall = false,
            firstRunApply = false
        },
        issues = dryRun.Issues.Select(i => new
        {
            i.Severity,
            i.Code,
            i.Message,
            i.Namespace,
            i.Key
        })
    };

    private static void AddFile(ZipArchive zip, string path, string entryName, List<string> included)
    {
        zip.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        included.Add(entryName);
    }

    private static IReadOnlyList<string> ExtractPlaceholders(string text)
        => PlaceholderPattern.Matches(text).Select(match => match.Value).ToList();

    private static bool IsValidLocale(string value)
        => !string.IsNullOrWhiteSpace(value) && LocalePattern.IsMatch(value);

    private static void Require(
        string value,
        string code,
        string message,
        List<LanguagePackDryRunIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(code, message));
        }
    }

    private static LanguagePackDryRunIssue Error(string code, string message, string? ns = null, string? key = null)
        => new("Error", code, message, ns, key);

    private static LanguagePackDryRunIssue Warning(string code, string message, string? ns = null, string? key = null)
        => new("Warning", code, message, ns, key);

    private static string SanitizeFileName(string name)
        => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
