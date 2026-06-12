using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AAIA.ModuleManager.Services;

public class NuGetService
{
    private static readonly HttpClient _http = new();
    private const string RegistrationUrl =
        "https://api.nuget.org/v3/registration5-semver1/aaia.shared.contracts/index.json";

    /// <summary>Gibt die neueste veröffentlichte Version zurück oder null bei Fehler.</summary>
    public static async Task<string?> GetLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(RegistrationUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .SelectMany(page => page.GetProperty("items").EnumerateArray())
                .Select(item => item.GetProperty("catalogEntry")
                                    .GetProperty("version").GetString())
                .Where(v => v != null)
                .ToList();

            return versions.LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Pollt bis die angegebene Version auf NuGet erscheint (max timeoutSec Sekunden).</summary>
    public static async Task<bool> WaitForVersionAsync(
        string version,
        Action<string> onProgress,
        int timeoutSec = 300,
        int pollIntervalSec = 15,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSec), ct);

            var latest = await GetLatestVersionAsync(ct);
            if (latest == version)
            {
                onProgress($"✓ NuGet hat v{version} indexiert.");
                return true;
            }

            var remaining = (int)(deadline - DateTime.UtcNow).TotalSeconds;
            onProgress($"⏳ Warte auf NuGet v{version} … (noch ~{remaining}s)");
        }

        return false;
    }
}
