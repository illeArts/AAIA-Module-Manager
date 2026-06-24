using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AAIA.Air.Contracts;

namespace AAIA.ModuleManager.Services.Ai.Persistence;

/// <summary>
/// Produktiver benutzergebundener Protector. Windows verwendet DPAPI CurrentUser und
/// bindet jeden Ciphertext zusätzlich an Store, Record-Typ, Record-ID und Schema.
/// Andere Plattformen bleiben fail-closed, bis ein nativer Keychain-Adapter existiert.
/// </summary>
public sealed class AiLocalUserStateProtector : IAiStateProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    public string ProtectorId => "windows-dpapi-current-user-v1";

    public AiLocalUserStateProtector()
    {
        if (!OperatingSystem.IsWindows())
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                "Produktiver State Protector ist auf dieser Plattform noch nicht verfügbar.");
    }

    public ValueTask<AiProtectedStatePayload> ProtectAsync(
        ReadOnlyMemory<byte> plaintext,
        AiStateProtectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AiProtectedStatePayload
        {
            ProtectorId = ProtectorId,
            Ciphertext = Protect(plaintext.Span, Entropy(context))
        });
    }

    public ValueTask<byte[]> UnprotectAsync(
        AiProtectedStatePayload protectedPayload,
        AiStateProtectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(protectedPayload.ProtectorId, ProtectorId, StringComparison.Ordinal))
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.ProtectorUnavailable,
                "State-Payload verwendet einen anderen Protector.");
        try
        {
            return ValueTask.FromResult(Unprotect(protectedPayload.Ciphertext, Entropy(context)));
        }
        catch (Win32Exception ex)
        {
            throw new AiStateStoreException(
                AiRuntimeStateReasonCodes.SnapshotCorrupt,
                "State-Payload konnte nicht entschlüsselt werden.", ex);
        }
    }

    private static byte[] Entropy(AiStateProtectionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.StoreId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RecordType);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RecordId);
        return Encoding.UTF8.GetBytes(
            $"AAIA-AIR|{context.StoreId}|{context.RecordType}|{context.RecordId}|{context.SchemaVersion}");
    }

    private static byte[] Protect(ReadOnlySpan<byte> plaintext, byte[] entropy)
    {
        using var input = DataBlob.From(plaintext);
        using var optionalEntropy = DataBlob.From(entropy);
        if (!CryptProtectData(ref input.Value, null, ref optionalEntropy.Value,
                IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return CopyAndFree(output);
    }

    private static byte[] Unprotect(byte[] ciphertext, byte[] entropy)
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
