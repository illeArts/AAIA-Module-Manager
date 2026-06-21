namespace AAIA.ModuleManager.Services.Signing;

/// <summary>
/// Offizielles AAIA Trust-Level-Modell — Phase 4.2.
///
/// Progression:
///   Unsigned
///   → LocalHashPrepared   (Phase 4.0 — SHA256-Hashes berechnet)
///   → EtwLocalSigned      (Phase 4.1 — RSA-Signatur erstellt)
///   → EtwLocalVerified    (Phase 4.2 — Signatur lokal erfolgreich geprüft)
///   → MarketplaceVerified (Phase 5.1 — Marketplace hat ETW-ID + Public Key + Paket geprüft)
///   → MarketplacePublished(Phase 5.2 — Paket ist öffentlich installierbar)
///
/// Sonderstatus:
///   Blocked               — Paket enthält Blocker oder Signatur gesperrt
///
/// WICHTIG:
///   EtwLocalSigned ≠ MarketplaceVerified.
///   Lokale Signatur beweist Integrität + Entwickleridentität auf diesem Rechner.
///   Marketplace-Verifikation prüft zusätzlich, ob die ETW-ID auf dem Marketplace
///   registriert ist und ob der Public Key dort hinterlegt ist.
/// </summary>
public static class TrustLevels
{
    // ── Stufen ────────────────────────────────────────────────────────────────

    public const string Unsigned              = "Unsigned";
    public const string LocalHashPrepared     = "LocalHashPrepared";
    public const string EtwLocalSigned        = "EtwLocalSigned";
    public const string EtwLocalVerified      = "EtwLocalVerified";
    public const string MarketplaceVerified   = "MarketplaceVerified";
    public const string MarketplacePublished  = "MarketplacePublished";
    public const string Blocked               = "Blocked";

    // ── Beschreibungen (kurz, UI-tauglich) ────────────────────────────────────

    public static string ShortLabel(string level) => level switch
    {
        Unsigned              => "⚪ Nicht signiert",
        LocalHashPrepared     => "🟡 Hash-Vorbereitung",
        EtwLocalSigned        => "🟡 ETW-Signiert (ungeprüft)",
        EtwLocalVerified      => "🟢 ETW-Signatur verifiziert",
        MarketplaceVerified   => "🟢 Marketplace-verifiziert",
        MarketplacePublished  => "🟢 Veröffentlicht",
        Blocked               => "🔴 Gesperrt",
        _                     => $"❓ {level}"
    };

    // ── Beschreibungen (lang, Help-Center-tauglich) ───────────────────────────

    public static string LongDescription(string level) => level switch
    {
        Unsigned =>
            "Keine Signatur vorhanden. Release wurde noch nicht vorbereitet.",

        LocalHashPrepared =>
            "SHA256-Hashes von .aaiaext, release-info.json und inspection-report.json " +
            "wurden berechnet und in signature-info.json gespeichert. " +
            "Manipulationen zwischen Paketbau und Upload können erkannt werden. " +
            "Noch keine kryptografische Signatur.",

        EtwLocalSigned =>
            "Das Release wurde mit dem lokalen RSA-2048-Schlüssel des ETW-Entwicklers " +
            "signiert. Der kanonische Payload (Extension ID, Version, Hashes, Zeitstempel) " +
            "ist mit dem privaten Schlüssel verschlüsselt. " +
            "Die Signatur ist mathematisch prüfbar, aber noch nicht lokal verifiziert.",

        EtwLocalVerified =>
            "Die ETW-Signatur wurde auf diesem Rechner erfolgreich geprüft. " +
            "Der Public Key aus signature-info.json wurde geladen, der kanonische Payload " +
            "rekonstruiert und die RSA-Signatur mathematisch bestätigt. " +
            "Das Paket wurde seit der Signierung nicht verändert.",

        MarketplaceVerified =>
            "Der AAIA-Marketplace hat die ETW-ID, den Public Key und das Paket serverseitig " +
            "geprüft. ETW-ID ist auf dem Marketplace registriert, Public Key stimmt überein, " +
            "Signatur ist gültig, Paket-Hashes stimmen. Paket ist vertrauenswürdig.",

        MarketplacePublished =>
            "Das Paket ist veröffentlicht und über den Marketplace installierbar. " +
            "MarketplaceVerified ist Voraussetzung.",

        Blocked =>
            "Das Paket ist gesperrt oder enthält Blocker. " +
            "Überprüfe die Paketprüfung (Step 5) auf Fehler.",

        _ => $"Unbekannter Trust-Level: {level}"
    };

    // ── Reihenfolge (für Sortierung / Fortschrittsanzeige) ───────────────────

    public static int Rank(string level) => level switch
    {
        Unsigned             => 0,
        LocalHashPrepared    => 1,
        EtwLocalSigned       => 2,
        EtwLocalVerified     => 3,
        MarketplaceVerified  => 4,
        MarketplacePublished => 5,
        Blocked              => -1,
        _                    => 0
    };

    public static bool IsAtLeast(string current, string required)
        => Rank(current) >= Rank(required);
}

// ── Key-Storage-Modus ─────────────────────────────────────────────────────────

/// <summary>
/// Bestimmt, wo und wie der private ETW-Schlüssel gespeichert wird.
///
/// Phase 4.1/4.2: LocalFile — PEM-Datei in %APPDATA%/AAIA/Keys/
/// Phase 4.3+:    PlatformSecureStore — DPAPI (Windows), Keychain (macOS),
///                                       Secret Service (Linux)
/// </summary>
public enum KeyStorageMode
{
    /// <summary>PEM-Datei in %APPDATA%/AAIA/Keys/ (Phase 4.1/4.2).</summary>
    LocalFile,

    /// <summary>Plattform-nativer Key Store (Phase 4.3+, noch nicht implementiert).</summary>
    PlatformSecureStore
}
