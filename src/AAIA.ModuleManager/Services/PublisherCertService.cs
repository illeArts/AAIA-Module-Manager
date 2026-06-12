using AAIA.Shared.Contracts.Publisher;
using System.IO;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Wrapper um das aaia-sign CLI-Tool.
/// Verantwortlich für: Schlüsselgenerierung, Paket-Signierung, Signatur-Verifizierung,
/// und Registrierung des Public Keys bei der Marketplace API.
/// </summary>
public sealed class PublisherCertService(MarketplaceApiClient marketplace)
{
    /// <summary>
    /// Generiert ein neues RSA-PSS-SHA256 Schlüsselpaar via aaia-sign.
    /// Schlüssel werden in %APPDATA%\AAIAModuleManager\keys\ abgelegt.
    /// </summary>
    public async Task<(string publicKeyPath, string privateKeyPath, string keyId)> GenerateKeyPairAsync(
        string displayName,
        CancellationToken ct = default)
    {
        var keyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AAIAModuleManager", "keys");

        Directory.CreateDirectory(keyDir);

        var keyId = $"key-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8]}";

        var result = await ProcessRunner.RunCapturedAsync(
            "aaia-sign",
            $"keygen --output \"{keyDir}\" --id \"{keyId}\" --algorithm RSA-PSS-SHA256",
            ct: ct);

        if (!result.Success)
            throw new InvalidOperationException($"aaia-sign keygen fehlgeschlagen:\n{result.Output}");

        var pubPath  = Path.Combine(keyDir, $"{keyId}.pub.pem");
        var privPath = Path.Combine(keyDir, $"{keyId}.key.pem");

        return (pubPath, privPath, keyId);
    }

    /// <summary>
    /// Signiert ein .aaix-Paket und gibt den Signatur-Hash zurück.
    /// </summary>
    public async Task<string> SignPackageAsync(
        string packagePath,
        string privateKeyPath,
        string keyId,
        CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunCapturedAsync(
            "aaia-sign",
            $"sign --package \"{packagePath}\" --key \"{privateKeyPath}\" --id \"{keyId}\"",
            ct: ct);

        if (!result.Success)
            throw new InvalidOperationException($"Signierung fehlgeschlagen:\n{result.Output}");

        // aaia-sign gibt SHA256-Hash der signierten Datei aus
        return result.Output.Trim();
    }

    /// <summary>
    /// Verifiziert die Signatur eines .aaix-Pakets.
    /// </summary>
    public async Task<bool> VerifyPackageAsync(
        string packagePath,
        CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunCapturedAsync(
            "aaia-sign",
            $"verify --package \"{packagePath}\"",
            ct: ct);

        return result.Success && result.Output.Contains("VALID", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Liest den Public Key und registriert ihn bei der Marketplace API.
    /// </summary>
    public async Task<RegisterPublisherKeyResponse> UploadPublicKeyAsync(
        string publicKeyPath,
        string keyId,
        CancellationToken ct = default)
    {
        var pem = await File.ReadAllTextAsync(publicKeyPath, ct);

        return await marketplace.RegisterKeyAsync(
            new RegisterPublisherKeyRequest(
                PublicKeyPem: pem,
                KeyId:        keyId,
                Algorithm:    "RSA-PSS-SHA256"),
            ct);
    }
}
