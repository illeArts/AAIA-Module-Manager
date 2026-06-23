using System;
using System.Linq;

namespace AAIA.ModuleManager.Services.Ai.Runtime;

/// <summary>
/// Versionierung für Runtime-Tools. Statt eines zweiten Tools "aaia.project.build2"
/// wird die neue Version unter gleichem Namen geführt, die alte als Deprecated markiert.
/// </summary>
public static class AiToolVersioning
{
    /// <summary>Vergleicht zwei SemVer-Strings ("1.2.0" vs "1.10.0"). Fehlt ein Teil, gilt 0.</summary>
    public static int Compare(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (int i = 0; i < 3; i++)
        {
            int c = pa[i].CompareTo(pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

    private static int[] Parse(string version)
    {
        var core = (version ?? "0").Split('-', '+')[0]; // pre-release/build abschneiden
        var parts = core.Split('.');
        var result = new int[3];
        for (int i = 0; i < 3; i++)
            result[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return result;
    }

    private static readonly System.Collections.Generic.IComparer<string> VersionComparer =
        System.Collections.Generic.Comparer<string>.Create((x, y) => Compare(x ?? "0", y ?? "0"));

    /// <summary>Wählt aus mehreren gleichnamigen Definitionen die höchste, nicht-deprecatete Version.</summary>
    public static AiToolDefinition? ResolveActive(System.Collections.Generic.IEnumerable<AiToolDefinition> sameName)
    {
        var list = sameName.ToList();
        var active = list.Where(t => !t.Deprecated)
                         .OrderByDescending(t => t.Version, VersionComparer)
                         .FirstOrDefault();
        return active ?? list.OrderByDescending(t => t.Version, VersionComparer).FirstOrDefault();
    }
}
