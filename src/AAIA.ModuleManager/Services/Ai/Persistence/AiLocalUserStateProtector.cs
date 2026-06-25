using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AAIA.Air;
using AAIA.Air.Contracts;

namespace AAIA.ModuleManager.Services.Ai.Persistence;

public interface IAiNativeSecretStore
{
    string PlatformId { get; }
    ValueTask<byte[]?> TryGetSecretAsync(string keyId, CancellationToken ct = default);
    ValueTask StoreSecretAsync(string keyId, ReadOnlyMemory<byte> secret, CancellationToken ct = default);
}

/// <summary>
/// Produktiver benutzergebundener Protector. Windows liest bestehende DPAPI-v1-Payloads
/// weiter. Neue macOS-/Linux-Payloads verwenden einen nativen, benutzergebundenen Secret
/// Store für das Schlüsselmaterial und AES-GCM mit Store/Record/Schema als AAD.
/// </summary>
public sealed class AiLocalUserStateProtector : IAiStateProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const byte EnvelopeVersion = 2;
    private const string LegacyWindowsProtectorId = "windows-dpapi-current-user-v1";
    private const string NativeProtectorPrefix = "aaia-native-user-secret-v2";
    private const string KeyIdPrefix = "aaia-air-state";

    private readonly IAiNativeSecretStore? _secretStore;
    private readonly bool _legacyWindowsOnly;

    public string ProtectorId
        => _legacyWindowsOnly ? LegacyWindowsProtectorId : $"{NativeProtectorPrefix}:{_secretStore!.PlatformId}";

    public AiLocalUserStateProtector()
        : this(CreatePlatformSecretStore(), legacyWindowsOnly: OperatingSystem.IsWindows())
    {
    }

    public AiLocalUserStateProtector(IAiNativeSecretStore secretStore)
        : this(secretStore, legacyWindowsOnly: false)
    {
    }

    private AiLocalUserStateProtector(IAiNativeSecretStore? secretStore, bool legacyWindowsOnly)
    {
        _secretStore = secretStore;
        _legacyWindowsOnly = legacyWindowsOnly;
        if (!_legacyWindowsOnly && _secretStore is null)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                "Produktiver State Protector ist auf dieser Plattform nicht verfügbar.");
    }

    public async ValueTask<AiProtectedStatePayload> ProtectAsync(
        ReadOnlyMemory<byte> plaintext,
        AiStateProtectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        return _legacyWindowsOnly
            ? new AiProtectedStatePayload
            {
                ProtectorId = LegacyWindowsProtectorId,
                Ciphertext = ProtectDpapi(plaintext.Span, Entropy(context))
            }
            : await ProtectNativeAsync(plaintext, context, ct).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> UnprotectAsync(
        AiProtectedStatePayload protectedPayload,
        AiStateProtectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        if (string.Equals(protectedPayload.ProtectorId, LegacyWindowsProtectorId, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorUnavailable,
                    "Windows-DPAPI-Payload kann auf dieser Plattform nicht entschlüsselt werden.");
            try
            {
                return UnprotectDpapi(protectedPayload.Ciphertext, Entropy(context));
            }
            catch (Win32Exception ex)
            {
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.SnapshotCorrupt,
                    "State-Payload konnte nicht entschlüsselt werden.", ex);
            }
        }

        if (!protectedPayload.ProtectorId.StartsWith(NativeProtectorPrefix + ":", StringComparison.Ordinal))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                "State-Payload verwendet einen unbekannten Protector.");
        if (_secretStore is null)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                "Nativer State Protector ist auf dieser Plattform nicht verfügbar.");
        return await UnprotectNativeAsync(protectedPayload, context, ct).ConfigureAwait(false);
    }

    private async ValueTask<AiProtectedStatePayload> ProtectNativeAsync(
        ReadOnlyMemory<byte> plaintext,
        AiStateProtectionContext context,
        CancellationToken ct)
    {
        var keyId = KeyId(context);
        var key = await GetOrCreateKeyAsync(keyId, ct).ConfigureAwait(false);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagBytes];
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, AssociatedData(context, keyId));

            var envelope = new byte[1 + NonceBytes + TagBytes + ciphertext.Length];
            envelope[0] = EnvelopeVersion;
            nonce.CopyTo(envelope.AsSpan(1, NonceBytes));
            tag.CopyTo(envelope.AsSpan(1 + NonceBytes, TagBytes));
            ciphertext.CopyTo(envelope.AsSpan(1 + NonceBytes + TagBytes));
            return new AiProtectedStatePayload
            {
                ProtectorId = $"{ProtectorId}:{keyId}",
                Ciphertext = envelope
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async ValueTask<byte[]> UnprotectNativeAsync(
        AiProtectedStatePayload protectedPayload,
        AiStateProtectionContext context,
        CancellationToken ct)
    {
        var keyId = ParseKeyId(protectedPayload.ProtectorId);
        var key = await _secretStore!.TryGetSecretAsync(keyId, ct).ConfigureAwait(false)
                  ?? throw new AiStateStoreException(
                      AiRuntimeStateReasonCodes.ProtectorKeyMissing,
                      "Nativer State-Protector-Schlüssel fehlt.");
        try
        {
            if (key.Length != KeyBytes)
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorKeyMissing,
                    "Nativer State-Protector-Schlüssel ist ungültig.");
            var envelope = protectedPayload.Ciphertext;
            if (envelope.Length < 1 + NonceBytes + TagBytes || envelope[0] != EnvelopeVersion)
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.SnapshotCorrupt,
                    "Native State-Payload besitzt ein ungültiges Format.");
            var plaintext = new byte[envelope.Length - 1 - NonceBytes - TagBytes];
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(
                envelope.AsSpan(1, NonceBytes),
                envelope.AsSpan(1 + NonceBytes + TagBytes),
                envelope.AsSpan(1 + NonceBytes, TagBytes),
                plaintext,
                AssociatedData(context, keyId));
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "State-Payload konnte nicht entschlüsselt werden.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async ValueTask<byte[]> GetOrCreateKeyAsync(string keyId, CancellationToken ct)
    {
        var existing = await _secretStore!.TryGetSecretAsync(keyId, ct).ConfigureAwait(false);
        if (existing is { Length: KeyBytes }) return existing;
        if (existing is not null)
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorKeyMissing,
                "Nativer State-Protector-Schlüssel ist ungültig.");
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        try
        {
            await _secretStore.StoreSecretAsync(keyId, key, ct).ConfigureAwait(false);
            return key.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static IAiNativeSecretStore? CreatePlatformSecretStore()
    {
        if (OperatingSystem.IsWindows()) return null;
        if (OperatingSystem.IsMacOS()) return new MacOsKeychainSecretStore();
        if (OperatingSystem.IsLinux()) return new LinuxSecretServiceSecretStore();
        return null;
    }

    private static string KeyId(AiStateProtectionContext context)
    {
        ValidateContext(context);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{context.StoreId}|{context.SchemaVersion}"));
        return $"{KeyIdPrefix}-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static string ParseKeyId(string protectorId)
    {
        var parts = protectorId.Split(':');
        if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[^1]) ||
            !parts[^1].StartsWith(KeyIdPrefix + "-", StringComparison.Ordinal))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorKeyMissing,
                "State-Payload enthält keine gültige Protector-Key-ID.");
        return parts[^1];
    }

    private static byte[] AssociatedData(AiStateProtectionContext context, string keyId)
    {
        ValidateContext(context);
        return Encoding.UTF8.GetBytes(
            $"AAIA-AIR-v2|{context.StoreId}|{context.RecordType}|{context.RecordId}|{context.SchemaVersion}|{keyId}");
    }

    private static byte[] Entropy(AiStateProtectionContext context)
    {
        ValidateContext(context);
        return Encoding.UTF8.GetBytes(
            $"AAIA-AIR|{context.StoreId}|{context.RecordType}|{context.RecordId}|{context.SchemaVersion}");
    }

    private static void ValidateContext(AiStateProtectionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.StoreId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RecordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RecordId);
    }

    private static byte[] ProtectDpapi(ReadOnlySpan<byte> plaintext, byte[] entropy)
    {
        using var input = DataBlob.From(plaintext);
        using var optionalEntropy = DataBlob.From(entropy);
        if (!CryptProtectData(ref input.Value, null, ref optionalEntropy.Value,
                IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return CopyAndFree(output);
    }

    private static byte[] UnprotectDpapi(byte[] ciphertext, byte[] entropy)
    {
        using var input = DataBlob.From(ciphertext);
        using var optionalEntropy = DataBlob.From(entropy);
        if (!CryptUnprotectData(ref input.Value, IntPtr.Zero, ref optionalEntropy.Value,
                IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return CopyAndFree(output);
    }

    private static byte[] CopyAndFree(NativeBlob blob)
    {
        try
        {
            if (blob.Length == 0) return Array.Empty<byte>();
            var result = new byte[blob.Length];
            Marshal.Copy(blob.Data, result, 0, blob.Length);
            return result;
        }
        finally
        {
            if (blob.Data != IntPtr.Zero) LocalFree(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBlob
    {
        public int Length;
        public IntPtr Data;
    }

    private sealed class DataBlob : IDisposable
    {
        public NativeBlob Value;

        private DataBlob(ReadOnlySpan<byte> data)
        {
            Value.Length = data.Length;
            Value.Data = data.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(data.Length);
            if (data.Length > 0)
            {
                var copy = data.ToArray();
                Marshal.Copy(copy, 0, Value.Data, copy.Length);
                CryptographicOperations.ZeroMemory(copy);
            }
        }

        public static DataBlob From(ReadOnlySpan<byte> data) => new(data);

        public void Dispose()
        {
            if (Value.Data == IntPtr.Zero) return;
            if (Value.Length > 0)
            {
                var zeros = new byte[Value.Length];
                Marshal.Copy(zeros, 0, Value.Data, zeros.Length);
            }
            Marshal.FreeHGlobal(Value.Data);
            Value.Data = IntPtr.Zero;
            Value.Length = 0;
        }
    }

    private sealed class MacOsKeychainSecretStore : CommandSecretStore
    {
        private const string Service = "AAIA.ModuleManager.AIR";
        public override string PlatformId => "macos-keychain";

        public override async ValueTask<byte[]?> TryGetSecretAsync(string keyId, CancellationToken ct = default)
        {
            EnsureMacOs();
            var result = await RunAsync("security", new[]
            {
                "find-generic-password", "-w", "-s", Service, "-a", keyId
            }, allowNotFound: true, ct).ConfigureAwait(false);
            return result is null ? null : DecodeSecret(result);
        }

        public override async ValueTask StoreSecretAsync(
            string keyId,
            ReadOnlyMemory<byte> secret,
            CancellationToken ct = default)
        {
            EnsureMacOs();
            await RunAsync("security", new[]
            {
                "add-generic-password", "-U", "-s", Service, "-a", keyId,
                "-D", "application password", "-w", Convert.ToBase64String(secret.Span)
            }, allowNotFound: false, ct).ConfigureAwait(false);
        }

        private static void EnsureMacOs()
        {
            if (!OperatingSystem.IsMacOS())
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorUnavailable,
                    "macOS-Keychain ist auf dieser Plattform nicht verfügbar.");
        }
    }

    private sealed class LinuxSecretServiceSecretStore : CommandSecretStore
    {
        public override string PlatformId => "linux-secret-service";

        public override async ValueTask<byte[]?> TryGetSecretAsync(string keyId, CancellationToken ct = default)
        {
            EnsureLinuxSecretService();
            var result = await RunAsync("secret-tool", new[]
            {
                "lookup", "application", "AAIA.ModuleManager", "purpose", "air-state", "key", keyId
            }, allowNotFound: true, ct).ConfigureAwait(false);
            return result is null ? null : DecodeSecret(result);
        }

        public override async ValueTask StoreSecretAsync(
            string keyId,
            ReadOnlyMemory<byte> secret,
            CancellationToken ct = default)
        {
            EnsureLinuxSecretService();
            await RunAsync("secret-tool", new[]
            {
                "store", "--label", $"AAIA AIR State {keyId}",
                "application", "AAIA.ModuleManager", "purpose", "air-state", "key", keyId
            }, allowNotFound: false, ct, stdin: Convert.ToBase64String(secret.Span)).ConfigureAwait(false);
        }

        private static void EnsureLinuxSecretService()
        {
            if (!OperatingSystem.IsLinux() ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorUnavailable,
                    "Linux Secret Service ist in dieser Benutzersitzung nicht verfügbar.");
        }
    }

    private abstract class CommandSecretStore : IAiNativeSecretStore
    {
        public abstract string PlatformId { get; }
        public abstract ValueTask<byte[]?> TryGetSecretAsync(string keyId, CancellationToken ct = default);
        public abstract ValueTask StoreSecretAsync(
            string keyId,
            ReadOnlyMemory<byte> secret,
            CancellationToken ct = default);

        protected static byte[] DecodeSecret(string value)
        {
            try { return Convert.FromBase64String(value.Trim()); }
            catch (FormatException ex)
            {
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorKeyMissing,
                    "Nativer State-Protector-Schlüssel ist beschädigt.", ex);
            }
        }

        protected static async ValueTask<string?> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            bool allowNotFound,
            CancellationToken ct,
            string? stdin = null)
        {
            var start = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                throw new AiStateStoreException(
                    AiRuntimeStateReasonCodes.ProtectorUnavailable,
                    "Nativer Secret-Service-Client ist nicht verfügbar.", ex);
            }
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.AsMemory(), ct).ConfigureAwait(false);
                await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
                await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode == 0) return stdout.Trim();
            if (allowNotFound) return null;
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                AiAuditService.Redact(stderr) ?? "Nativer Secret-Service-Aufruf ist fehlgeschlagen.");
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref NativeBlob dataIn,
        string? description,
        ref NativeBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out NativeBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref NativeBlob dataIn,
        IntPtr description,
        ref NativeBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out NativeBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
