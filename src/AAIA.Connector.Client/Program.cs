using System;
using System.Threading.Tasks;
using AAIA.Connector.Client;

// ══════════════════════════════════════════════════════════════════════════════
// AAIA AI Connector — Beispiel-Client
//
// Dieses Programm zeigt wie ein externer KI-Agent (z.B. ein AAIAS-Modul,
// ein ChatGPT-Plugin oder ein eigener C#-Service) das AAIA Connector Protocol
// nutzt um:
//   1. Server-Status prüfen
//   2. Projekt-Kontext lesen
//   3. Einen Patch-Vorschlag einreichen
//   4. Auf Nutzer-Entscheidung warten
//
// Starte den AAIA Module Manager und aktiviere den Connector-Server
// (Tab "🔌 Connector" → "Server starten"), dann dieses Programm ausführen.
// ══════════════════════════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  AAIA AI Connector — Beispiel-Client");
Console.WriteLine("  Protocol: aaia-connector-v1  |  Port: 39157");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();

using var client = new AaiaConnectorClient(
    connectorId:   "example-agent",
    connectorName: "AAIA Beispiel-Agent");

// ── 1. Server-Erreichbarkeit ──────────────────────────────────────────────────

Console.Write("1. Server erreichbar?  ");
var running = await client.IsServerRunningAsync();
if (!running)
{
    Console.WriteLine("NEIN ✗");
    Console.WriteLine();
    Console.WriteLine("Stelle sicher dass der AAIA Module Manager läuft");
    Console.WriteLine("und der Connector-Server gestartet ist.");
    return;
}
Console.WriteLine("JA ✓");

// ── 2. Capabilities ───────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("2. Server-Capabilities:");
var caps = await client.GetCapabilitiesAsync();
Console.WriteLine($"   Protocol : {caps?.ProtocolVersion}");
Console.WriteLine($"   Server   : v{caps?.ServerVersion}");
Console.WriteLine($"   ID       : {caps?.ConnectorId}");
Console.WriteLine($"   Rechte   : {caps?.Permissions}");

// ── 3. Projekt-Kontext ────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("3. Aktueller Projektzustand:");
var ctx = await client.GetContextSummaryAsync();
if (ctx is null)
{
    Console.WriteLine("   (Kein aktives Projekt im AAIA Module Manager)");
}
else
{
    Console.WriteLine($"   Extension  : {ctx.ExtensionId} ({ctx.DisplayName})");
    Console.WriteLine($"   Schritt    : {ctx.CurrentStep}");
    Console.WriteLine($"   Nächster   : {ctx.NextStep ?? "(keiner)"}");
    Console.WriteLine($"   Fehler     : {ctx.ErrorCount}");
    Console.WriteLine($"   Trust      : {ctx.TrustLevel}");

    if (!ctx.HasErrors)
    {
        Console.WriteLine();
        Console.WriteLine("   ✓ Keine Fehler — kein Patch-Vorschlag nötig.");
        return;
    }
}

// ── 4. Patch-Vorschlag ────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("4. Patch-Vorschlag einreichen:");
Console.WriteLine("   (Nur als Demo — der Inhalt ist ein Platzhalter)");
Console.WriteLine();

var patchRequest = new PatchRequest
{
    Rationale = "Die aaia-extension.json hatte ein ungültiges 'version'-Format. Korrigiert auf SemVer.",
    Patches   =
    [
        new PatchItem
        {
            Kind        = "FullFileReplacement",
            TargetFile  = "src/MyExtension/aaia-extension.json",
            Language    = "json",
            Description = "version-Feld auf SemVer-Format korrigiert",
            Content     = """
                          {
                            "extensionId": "my-extension",
                            "version": "1.0.0",
                            "displayName": "Meine Extension"
                          }
                          """
        }
    ]
};

PatchResponse proposal;
try
{
    proposal = await client.ProposePatchAsync(patchRequest);
}
catch (ConnectorException ex)
{
    Console.WriteLine($"   FEHLER: {ex.Message}");
    if (ex.Details?.Count > 0)
        ex.Details.ForEach(d => Console.WriteLine($"     - {d}"));
    return;
}

Console.WriteLine($"   Proposal-ID : {proposal.ProposalId}");
Console.WriteLine($"   Status      : {proposal.Status}");
Console.WriteLine($"   Nachricht   : {proposal.Message}");
Console.WriteLine();
Console.WriteLine("   ⏳  Warte auf Nutzer-Entscheidung im AAIA Module Manager ...");
Console.WriteLine("      (max. 2 Minuten, dann Abbruch)");

var finalStatus = await client.PollPatchStatusAsync(
    proposal.ProposalId,
    pollIntervalMs: 2_000,
    timeoutMs:      2 * 60 * 1_000);

Console.WriteLine();
if (finalStatus is null)
{
    Console.WriteLine("   ⏰  Timeout — Nutzer hat nicht innerhalb von 2 Minuten entschieden.");
}
else
{
    Console.WriteLine($"   Status      : {finalStatus.Status}");
    Console.WriteLine($"   Genehmigt   : {finalStatus.ApprovedCount}");
    Console.WriteLine($"   Abgelehnt   : {finalStatus.RejectedCount}");
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Demo abgeschlossen.");
