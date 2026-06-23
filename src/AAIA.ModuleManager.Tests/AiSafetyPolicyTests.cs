using AAIA.ModuleManager.Services.AiAdapter;
using Xunit;

namespace AAIA.ModuleManager.Tests;

/// <summary>Tests für AiSafetyPolicy — Prompt-Validierung und User-Note-Sanitizer.</summary>
public sealed class AiSafetyPolicyTests
{
    // ── Validate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyPrompt_NoWarnings()
    {
        var warnings = AiSafetyPolicy.Validate("", out bool critical);
        Assert.Empty(warnings);
        Assert.False(critical);
    }

    [Fact]
    public void Validate_SafePrompt_NoWarnings()
    {
        var prompt = "Bitte analysiere die Validierungsfehler und schlage Korrekturen vor.";
        var warnings = AiSafetyPolicy.Validate(prompt, out bool critical);
        Assert.Empty(warnings);
        Assert.False(critical);
    }

    [Fact]
    public void Validate_PrivateKeyFilePath_IsCritical()
    {
        var prompt = "Verwende die Datei developer-private.pem zum Signieren.";
        var warnings = AiSafetyPolicy.Validate(prompt, out bool critical);
        Assert.True(critical);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Validate_VeryLongPrompt_AddsLengthWarning()
    {
        var prompt = new string('x', 60_000);
        var warnings = AiSafetyPolicy.Validate(prompt, out _);
        Assert.Contains(warnings, w => w.Contains("lang"));
    }

    // ── SanitizeUserNote ──────────────────────────────────────────────────────

    [Fact]
    public void SanitizeUserNote_Null_ReturnsEmpty()
        => Assert.Equal("", AiSafetyPolicy.SanitizeUserNote(null));

    [Fact]
    public void SanitizeUserNote_WhitespaceOnly_ReturnsEmpty()
        => Assert.Equal("", AiSafetyPolicy.SanitizeUserNote("   "));

    [Fact]
    public void SanitizeUserNote_SafeText_Unchanged()
    {
        const string note = "Bitte auch die Manifest-Felder prüfen.";
        Assert.Equal(note, AiSafetyPolicy.SanitizeUserNote(note));
    }

    [Fact]
    public void SanitizeUserNote_ContainsSensitivePattern_IsRedacted()
    {
        // Ein Hex-String der langen Key-Mustern ähnelt (40+ Zeichen)
        const string hexKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        var result = AiSafetyPolicy.SanitizeUserNote($"Key: {hexKey}");
        Assert.DoesNotContain(hexKey, result);
    }
}
