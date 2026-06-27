using System.IO.Compression;
using AAIA.ModuleManager.Services.LanguagePacks;
using Xunit;

namespace AAIA.ModuleManager.Tests;

public sealed class LanguagePackWizardServiceTests
{
    [Fact]
    public void Wizard_input_generates_valid_manifest()
    {
        var manifest = LanguagePackWizardService.CreateManifest(Input());
        var issues = LanguagePackWizardService.ValidateInput(Input());

        Assert.Empty(issues);
        Assert.Equal("language-pack", manifest.Type);
        Assert.Equal("core-ui", manifest.TargetKind);
        Assert.Equal("de-DE", manifest.Locale);
        Assert.Equal("ETW-000004", manifest.PublisherEtwId);
    }

    [Fact]
    public void Invalid_locale_is_blocked()
    {
        var issues = LanguagePackWizardService.ValidateInput(Input() with { Locale = "not a locale" });

        Assert.Contains(issues, issue => issue.Code == "manifest.locale.invalid");
    }

    [Fact]
    public void Unknown_target_kind_is_blocked()
    {
        var issues = LanguagePackWizardService.ValidateInput(Input() with { TargetKind = "securelink-runtime" });

        Assert.Contains(issues, issue => issue.Code == "manifest.targetKind.unknown");
    }

    [Fact]
    public void Missing_registry_key_is_detected()
    {
        var draft = Draft([
            Entry("core-ui", "unknown.key", "Unbekannt")
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), Registry(), draft);

        Assert.Contains(result.Issues, issue => issue.Code == "registry.key.unknown");
    }

    [Fact]
    public void Locked_key_is_blocked()
    {
        var registry = Registry([
            RegistryEntry("core-ui", "role.admin", "Admin", translatable: true, locked: true, placeholders: [])
        ]);
        var draft = Draft([
            Entry("core-ui", "role.admin", "Administrator")
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), registry, draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.key.locked");
    }

    [Fact]
    public void Non_translatable_key_is_blocked()
    {
        var registry = Registry([
            RegistryEntry("core-ui", "error.code", "PACKAGE_SIGNATURE_INVALID", translatable: false, locked: false, placeholders: [])
        ]);
        var draft = Draft([
            Entry("core-ui", "error.code", "Signaturfehler")
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), registry, draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.key.notTranslatable");
    }

    [Fact]
    public void Placeholder_mismatch_is_detected()
    {
        var draft = Draft([
            Entry("core-ui", "package.signature.invalid", "Die Signatur ist ungueltig. {extra}")
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), Registry(), draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.placeholder.missing");
        Assert.Contains(result.Issues, issue => issue.Code == "translation.placeholder.extra");
    }

    [Fact]
    public void Glossary_hint_is_detected()
    {
        var draft = Draft([
            Entry("core-ui", "package.signature.invalid", "Paket {packageId} Signatur ist ungueltig.", glossaryTerms: [])
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), Registry(), draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.glossary.protectedTerm");
    }

    [Fact]
    public void Review_flags_are_marked()
    {
        var registry = Registry([
            RegistryEntry(
                "marketplace",
                "approval.notice",
                "AAIA Marketplace approval for {packageId}",
                placeholders: ["{packageId}"],
                securityReviewRequired: true,
                legalReviewRequired: true,
                marketplaceReviewRequired: true,
                glossaryTerms: ["AAIA", "Marketplace"])
        ]);
        var draft = Draft([
            Entry("marketplace", "approval.notice", "AAIA Marketplace Freigabe fuer {packageId}", glossaryTerms: ["AAIA", "Marketplace"])
        ]);

        var result = LanguagePackWizardService.DryRunValidate(
            Input() with { TargetKind = "marketplace", ContainsMarketplaceText = true },
            registry,
            draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.securityReview.required");
        Assert.Contains(result.Issues, issue => issue.Code == "translation.legalReview.required");
        Assert.Contains(result.Issues, issue => issue.Code == "translation.marketplaceReview.required");
    }

    [Fact]
    public void Max_length_warning_is_generated()
    {
        var registry = Registry([
            RegistryEntry("core-ui", "short.label", "Continue", placeholders: [], maxLength: 5)
        ]);
        var draft = Draft([
            Entry("core-ui", "short.label", "Weitergehen")
        ]);

        var result = LanguagePackWizardService.DryRunValidate(Input(), registry, draft);

        Assert.Contains(result.Issues, issue => issue.Code == "translation.maxLength.exceeded");
    }

    [Fact]
    public async Task Draft_package_contains_manifest_translations_and_validation_report()
    {
        var root = CreateTempDir();
        try
        {
            await LanguagePackWizardService.CreateProjectAsync(root, Input(), Registry(), ValidDraft());

            var result = await LanguagePackWizardService.BuildDraftPackageAsync(root);

            Assert.True(result.Success);
            Assert.Equal("local_validated", result.Status);
            Assert.NotNull(result.PackagePath);
            Assert.True(File.Exists(result.PackagePath));
            Assert.Contains("manifest.json", result.IncludedFiles);
            Assert.Contains("translations/de-DE.json", result.IncludedFiles);
            Assert.Contains("evidence/validation-report.json", result.IncludedFiles);

            using var zip = ZipFile.OpenRead(result.PackagePath!);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("translations/de-DE.json"));
            Assert.NotNull(zip.GetEntry("evidence/validation-report.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Phase_16_3_does_not_enable_upload_publish_or_runtime_activation()
    {
        Assert.False(LanguagePackWizardBoundary.AllowsMarketplaceUpload);
        Assert.False(LanguagePackWizardBoundary.AllowsPublish);
        Assert.False(LanguagePackWizardBoundary.AllowsRuntimeActivation);
        Assert.False(LanguagePackWizardBoundary.AllowsClientInstall);
        Assert.False(LanguagePackWizardBoundary.AllowsFirstRunApply);
    }

    private static LanguagePackWizardInput Input() =>
        new(
            PackageId: "aaia.lang.de-DE.core-ui",
            Name: "AAIA Deutsch",
            Version: "1.0.0",
            Locale: "de-DE",
            FallbackLocale: "en-US",
            TargetKind: "core-ui",
            MinAaiaVersion: "16.0.0",
            PublisherEtwId: "ETW-000004",
            ContainsSecurityText: true,
            ContainsLegalText: false);

    private static TranslationKeyRegistry Registry(IReadOnlyList<TranslationKeyRegistryEntry>? keys = null) =>
        new(
            SchemaVersion: "1.0",
            SourceLocale: "en-US",
            Keys: keys ?? [
                RegistryEntry(
                    "core-ui",
                    "package.signature.invalid",
                    "AAIA package {packageId} signature is invalid.",
                    placeholders: ["{packageId}"],
                    securityReviewRequired: true,
                    glossaryTerms: ["AAIA"])
            ]);

    private static TranslationKeyRegistryEntry RegistryEntry(
        string ns,
        string key,
        string sourceText,
        bool translatable = true,
        bool locked = false,
        IReadOnlyList<string>? placeholders = null,
        bool securityReviewRequired = false,
        bool legalReviewRequired = false,
        bool marketplaceReviewRequired = false,
        int? maxLength = null,
        IReadOnlyList<string>? glossaryTerms = null) =>
        new(
            Namespace: ns,
            Key: key,
            SourceText: sourceText,
            SourceLocale: "en-US",
            Translatable: translatable,
            Locked: locked,
            SecurityReviewRequired: securityReviewRequired,
            LegalReviewRequired: legalReviewRequired,
            MarketplaceReviewRequired: marketplaceReviewRequired,
            Placeholders: placeholders ?? [],
            ProtectionCategory: securityReviewRequired ? "security" : "none",
            MaxLength: maxLength,
            Context: "test",
            GlossaryTerms: glossaryTerms ?? []);

    private static LanguagePackTranslationDraft ValidDraft() =>
        Draft([
            Entry(
                "core-ui",
                "package.signature.invalid",
                "AAIA Paket {packageId} Signatur ist ungueltig.",
                glossaryTerms: ["AAIA"],
                hasSecurityReview: true)
        ]);

    private static LanguagePackTranslationDraft Draft(IReadOnlyList<LanguagePackTranslationDraftEntry> entries) =>
        new("de-DE", entries);

    private static LanguagePackTranslationDraftEntry Entry(
        string ns,
        string key,
        string text,
        IReadOnlyList<string>? glossaryTerms = null,
        bool hasSecurityReview = false,
        bool hasOwnerAdminReview = false,
        bool hasMarketplaceReview = false) =>
        new(
            Namespace: ns,
            Key: key,
            Text: text,
            GlossaryTerms: glossaryTerms,
            HasSecurityReview: hasSecurityReview,
            HasOwnerAdminReview: hasOwnerAdminReview,
            HasMarketplaceReview: hasMarketplaceReview);

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aaia-lang-wizard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
