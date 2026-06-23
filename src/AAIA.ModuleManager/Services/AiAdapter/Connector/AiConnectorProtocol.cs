namespace AAIA.ModuleManager.Services.AiAdapter.Connector;

/// <summary>
/// AAIA AI Connector Protocol — Konstanten und Endpunkt-Definitionen.
///
/// Lokaler HTTP-Server auf localhost:39157
/// Präfix: /aaia/v1/
///
/// LESE-Endpunkte (immer erlaubt):
///   GET  /aaia/v1/capabilities          → Server-Capabilities + erlaubte Aktionen
///   GET  /aaia/v1/context/current       → Kompakter Projektzustand
///   GET  /aaia/v1/context/project       → Vollständige Projekt-Zusammenfassung
///   GET  /aaia/v1/handoff/latest        → Letztes Handoff-Paket (Manifest)
///
/// SCHREIB-Endpunkte (erfordern User-Approval im UI):
///   POST /aaia/v1/patch/propose         → Patch-Vorschlag einreichen
///   GET  /aaia/v1/patch/{id}/status     → Status eines eingereichten Vorschlags
///
/// VERBOTEN (gibt immer 403):
///   Alles was Signaturen, Keys, Tokens oder Marketplace-Uploads betrifft.
/// </summary>
public static class AiConnectorProtocol
{
    // ── Server ────────────────────────────────────────────────────────────────

    public const int    Port       = 39157;
    public const string BaseUrl    = "http://localhost:39157";
    public const string ApiPrefix  = "/aaia/v1";
    public const string FullPrefix = "http://localhost:39157/aaia/v1/";

    // ── Endpunkte ─────────────────────────────────────────────────────────────

    public static class Endpoints
    {
        public const string Capabilities    = "/aaia/v1/capabilities";
        public const string ContextCurrent  = "/aaia/v1/context/current";
        public const string ContextProject  = "/aaia/v1/context/project";
        public const string HandoffLatest   = "/aaia/v1/handoff/latest";
        public const string PatchPropose    = "/aaia/v1/patch/propose";
        public const string PatchStatus     = "/aaia/v1/patch/";   // + {id}/status
    }

    // ── Header ────────────────────────────────────────────────────────────────

    public const string HeaderConnectorId   = "X-AAIA-Connector-Id";
    public const string HeaderConnectorName = "X-AAIA-Connector-Name";
    public const string ContentTypeJson     = "application/json; charset=utf-8";

    // ── Protokoll-Version ─────────────────────────────────────────────────────

    public const string ProtocolVersion = "aaia-connector-v1";

    // ── Bekannte Connector-IDs ────────────────────────────────────────────────

    public static class KnownConnectors
    {
        public const string ChatGpt    = "chatgpt";
        public const string Claude     = "claude";
        public const string Gemini     = "gemini";
        public const string Codex      = "codex";
        public const string Unknown    = "unknown";
    }

    // ── HTTP-Status-Codes (semantische Konstanten) ───────────────────────────

    public const int StatusOk                = 200;
    public const int StatusAccepted          = 202;   // Patch eingereicht, wartet auf User
    public const int StatusBadRequest        = 400;
    public const int StatusForbidden         = 403;   // Aktion nicht erlaubt
    public const int StatusNotFound          = 404;
    public const int StatusConflict          = 409;   // Patch abgelehnt vom User
    public const int StatusMethodNotAllowed  = 405;
    public const int StatusServerError       = 500;
}
