using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Liest und schreibt das version-Feld in aaia-manifest.json.
/// Unterstützt SemVer: Major.Minor.Patch (Pre-Release-Suffixe werden beibehalten).
/// </summary>
public static class ManifestVersionService
{
    private const string ManifestFileName = "aaia-manifest.json";

    // ── Lesen ─────────────────────────────────────────────────────────────────

    /// <summary>Liest die aktuelle Version aus dem Manifest. Gibt "0.1.0" zurück wenn nicht gesetzt.</summary>
    public static string GetVersion(string projectDir)
    {
        var path = Path.Combine(projectDir, ManifestFileName);
        if (!File.Exists(path)) return "0.1.0";

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            return root?["version"]?.GetValue<string>() ?? "0.1.0";
        }
        catch { return "0.1.0"; }
    }

    // ── Schreiben ─────────────────────────────────────────────────────────────

    /// <summary>Setzt das version-Feld im Manifest auf den angegebenen Wert.</summary>
    public static async Task SetVersionAsync(string projectDir, string newVersion)
    {
        if (!SemVer.TryParse(newVersion, out _))
            throw new ArgumentException($"'{newVersion}' ist keine gültige SemVer-Version (z. B. 1.2.3).");

        await PatchManifestAsync(projectDir, root =>
        {
            if (root is JsonObject obj)
                obj["version"] = newVersion;
        });
    }

    // ── Bump ──────────────────────────────────────────────────────────────────

    public static Task<string> BumpPatchAsync(string projectDir)  => BumpAsync(projectDir, BumpKind.Patch);
    public static Task<string> BumpMinorAsync(string projectDir)  => BumpAsync(projectDir, BumpKind.Minor);
    public static Task<string> BumpMajorAsync(string projectDir)  => BumpAsync(projectDir, BumpKind.Major);

    private static async Task<string> BumpAsync(string projectDir, BumpKind kind)
    {
        var current = GetVersion(projectDir);
        SemVer.TryParse(current, out var v);

        var next = kind switch
        {
            BumpKind.Patch => v with { Patch = v.Patch + 1 },
            BumpKind.Minor => v with { Minor = v.Minor + 1, Patch = 0 },
            BumpKind.Major => v with { Major = v.Major + 1, Minor = 0, Patch = 0 },
            _              => v
        };

        // Pre-Release-Suffix beim Bump entfernen (0.1.0-beta → 0.1.1)
        var nextStr = next.ToString(stripPreRelease: true);
        await SetVersionAsync(projectDir, nextStr);
        return nextStr;
    }

    // ── JSON-Patch ────────────────────────────────────────────────────────────

    private static async Task PatchManifestAsync(string projectDir, Action<JsonNode?> patch)
    {
        var path = Path.Combine(projectDir, ManifestFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"aaia-manifest.json nicht gefunden in: {projectDir}");

        var text = await File.ReadAllTextAsync(path);
        var root = JsonNode.Parse(text);
        patch(root);

        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, root!.ToJsonString(opts), Encoding.UTF8);
    }

    // ── SemVer-Mini-Parser ────────────────────────────────────────────────────

    private enum BumpKind { Patch, Minor, Major }

    public readonly record struct SemVer(int Major, int Minor, int Patch, string PreRelease = "")
    {
        public static readonly SemVer Default = new(0, 1, 0);

        public static bool TryParse(string? input, out SemVer result)
        {
            result = Default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Trennt Pre-Release-Suffix ab: "1.2.3-beta" → "1.2.3" + "beta"
            var dashIdx     = input.IndexOf('-');
            var core        = dashIdx >= 0 ? input[..dashIdx] : input;
            var preRelease  = dashIdx >= 0 ? input[(dashIdx + 1)..] : "";

            var parts = core.Split('.');
            if (parts.Length < 2) return false;

            if (!int.TryParse(parts[0], out var major)) return false;
            if (!int.TryParse(parts[1], out var minor)) return false;
            var patch = parts.Length >= 3 && int.TryParse(parts[2], out var p) ? p : 0;

            result = new SemVer(major, minor, patch, preRelease);
            return true;
        }

        public string ToString(bool stripPreRelease)
        {
            var core = $"{Major}.{Minor}.{Patch}";
            return stripPreRelease || string.IsNullOrEmpty(PreRelease) ? core : $"{core}-{PreRelease}";
        }

        public override string ToString() => ToString(stripPreRelease: false);
    }
}
