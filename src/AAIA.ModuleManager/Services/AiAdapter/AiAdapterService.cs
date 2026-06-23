using System;
using System.Threading;
using System.Threading.Tasks;
using AAIA.ModuleManager.Services.Help;

namespace AAIA.ModuleManager.Services.AiAdapter;

/// <summary>
/// Implementierung des zentralen KI-Adapters.
///
/// Routing-Logik:
///   1. Ziel-Target aus Request lesen
///   2. Effektiven Modus ermitteln (Settings + Key-Verfügbarkeit + Fallback)
///   3. SafetyPolicy prüfen
///   4. Entweder API-Aufruf (IAiProviderService) oder Handoff-Prompt erzeugen
///   5. Ergebnis normalisieren und zurückgeben
/// </summary>
public sealed class AiAdapterService : IAiAdapterService
{
    private readonly AppConfig               _config;
    private readonly AiAdapterSettings       _settings;
    private readonly AaiasConnectionService? _aaias;

    public AiAdapterService(AppConfig config, AiAdapterSettings settings,
        AaiasConnectionService? aaias = null)
    {
        _config   = config;
        _settings = settings;
        _aaias    = aaias;
    }

    // ── Haupt-Methode ─────────────────────────────────────────────────────────

    public async Task<AiAdapterResult> ExecuteAsync(
        AiAdapterRequest request,
        CancellationToken ct = default)
    {
        var target = request.Target;
        var mode   = ResolveMode(target, request.PreferredMode);

        // Kontext auf gewünschte Stufe reduzieren
        var ctx = AiContextBuilder.ApplyContextLevel(
            request.ProjectContext,
            request.ContextLevel);

        // User-Note bereinigen
        var safeNote = AiSafetyPolicy.SanitizeUserNote(request.UserNote);

        // Handoff-Request für Prompt-Generator bauen
        var handoffReq = new AiHandoffRequest
        {
            Target       = request.Task,
            Profile      = MapTarget(target),
            ContextLevel = request.ContextLevel,
        };

        // Prompt erzeugen (für ManualHandoff und als Basis für API)
        var handoffResult = AiHandoffGeneratorService.Generate(ctx, handoffReq);
        if (!handoffResult.Success)
        {
            return new AiAdapterResult
            {
                Success = false,
                Target  = target,
                Mode    = mode,
                Error   = $"Prompt-Generierung fehlgeschlagen: {handoffResult.Error}"
            };
        }

        // User-Note anhängen
        var fullPrompt = string.IsNullOrEmpty(safeNote)
            ? handoffResult.Prompt
            : handoffResult.Prompt + "\n\n---\n**Zusätzliche Hinweise:**\n" + safeNote;

        // Safety-Check auf finalen Prompt
        var warnings = AiSafetyPolicy.Validate(fullPrompt, out bool isCritical);
        if (isCritical)
        {
            return new AiAdapterResult
            {
                Success        = false,
                Target         = target,
                Mode           = mode,
                Prompt         = fullPrompt,
                Error          = "SafetyPolicy: Prompt enthält verbotene Inhalte (Keys/Secrets). Abgebrochen.",
                SafetyWarnings = warnings
            };
        }

        // ManualHandoff: nur Prompt zurückgeben, kein API-Call
        if (mode == AiExecutionMode.ManualHandoff)
        {
            return new AiAdapterResult
            {
                Success        = true,
                Target         = target,
                Mode           = mode,
                Prompt         = fullPrompt,
                Title          = handoffResult.Title,
                PromptLength   = fullPrompt.Length,
                SafetyWarnings = warnings
            };
        }

        // ApiDirect: Provider wählen und aufrufen
        if (mode == AiExecutionMode.ApiDirect)
        {
            var provider = BuildProvider(target);
            if (provider is null)
            {
                // Kein Key vorhanden — graceful fallback auf ManualHandoff
                return new AiAdapterResult
                {
                    Success        = true,
                    Target         = target,
                    Mode           = AiExecutionMode.ManualHandoff,
                    Prompt         = fullPrompt,
                    Title          = handoffResult.Title,
                    PromptLength   = fullPrompt.Length,
                    SafetyWarnings = [..warnings, "Kein API-Key gefunden — ManualHandoff verwendet."]
                };
            }

            try
            {
                var aiRequest = new AiRequest(
                    Messages:     [new ChatMessage("user", fullPrompt)],
                    SystemPrompt: BuildSystemPrompt(target),
                    MaxTokens:    4096);

                var response = await provider.SendAsync(aiRequest, ct);

                return new AiAdapterResult
                {
                    Success        = response.Success,
                    Target         = target,
                    Mode           = mode,
                    Prompt         = fullPrompt,
                    ApiResponse    = response.Success
                        ? AiResponseParser.Normalize(response.Text)
                        : null,
                    Error          = response.Success ? null : response.Error,
                    Title          = handoffResult.Title,
                    PromptLength   = fullPrompt.Length,
                    SafetyWarnings = warnings
                };
            }
            catch (OperationCanceledException)
            {
                return new AiAdapterResult
                {
                    Success = false,
                    Target  = target,
                    Mode    = mode,
                    Prompt  = fullPrompt,
                    Error   = "Abgebrochen."
                };
            }
            catch (Exception ex)
            {
                return new AiAdapterResult
                {
                    Success = false,
                    Target  = target,
                    Mode    = mode,
                    Prompt  = fullPrompt,
                    Error   = ex.Message
                };
            }
        }

        // ConnectorBridge / LocalModel: noch nicht implementiert → ManualHandoff
        return new AiAdapterResult
        {
            Success        = true,
            Target         = target,
            Mode           = AiExecutionMode.ManualHandoff,
            Prompt         = fullPrompt,
            Title          = handoffResult.Title,
            PromptLength   = fullPrompt.Length,
            SafetyWarnings = [..warnings, $"Modus '{mode}' noch nicht implementiert — ManualHandoff verwendet."]
        };
    }

    // ── Capabilities & Mode-Auflösung ─────────────────────────────────────────

    public AiAdapterCapabilities GetCapabilities(AiTarget target)
    {
        var effectiveMode  = ResolveMode(target);
        var isApiKeyPresent = HasApiKey(target);

        return new AiAdapterCapabilities
        {
            Target          = target,
            EffectiveMode   = effectiveMode,
            CanHandoff      = true,
            CanApiDirect    = isApiKeyPresent,
            IsApiKeyPresent = isApiKeyPresent,
            TargetLabel     = TargetLabel(target),
            ModeLabel       = ModeLabel(effectiveMode),
            ModeHint        = isApiKeyPresent
                ? null
                : "Kein API-Key konfiguriert — ManualHandoff wird verwendet."
        };
    }

    public AiExecutionMode ResolveMode(AiTarget target, AiExecutionMode? preferred = null)
    {
        var requested = preferred ?? _settings.ModeFor(target);

        // ApiDirect ohne Key → automatisch ManualHandoff
        if (requested == AiExecutionMode.ApiDirect && !HasApiKey(target))
            return AiExecutionMode.ManualHandoff;

        // AaiasAgent: ApiDirect wenn verbunden, sonst ManualHandoff
        if (target == AiTarget.AaiasAgent)
            return _aaias?.IsConnected == true
                ? AiExecutionMode.ApiDirect
                : AiExecutionMode.ManualHandoff;

        // ConnectorBridge/LocalModel noch nicht implementiert → ManualHandoff
        if (requested is AiExecutionMode.ConnectorBridge or AiExecutionMode.LocalModel
            && target != AiTarget.LocalModel)
            return AiExecutionMode.ManualHandoff;

        return requested;
    }

    // ── Private Hilfsmethoden ─────────────────────────────────────────────────

    private bool HasApiKey(AiTarget target) => target switch
    {
        AiTarget.Claude      => !string.IsNullOrWhiteSpace(_config.ClaudeApiKey),
        AiTarget.ChatGPT     => !string.IsNullOrWhiteSpace(_config.OpenAiApiKey),
        AiTarget.Gemini      => !string.IsNullOrWhiteSpace(_config.GeminiApiKey),
        // AAIAS braucht keinen API-Key — nur eine aktive Verbindung
        AiTarget.AaiasAgent  => _aaias?.IsConnected == true,
        _                    => false
    };

    private IAiProviderService? BuildProvider(AiTarget target) => target switch
    {
        AiTarget.Claude  when HasApiKey(AiTarget.Claude)
            => new ClaudeAiProvider(_config.ClaudeApiKey, _config.ClaudeModel),

        AiTarget.ChatGPT when HasApiKey(AiTarget.ChatGPT)
            => new OpenAiProvider(_config.OpenAiApiKey, _config.OpenAiModel),

        AiTarget.Gemini  when HasApiKey(AiTarget.Gemini)
            => new GeminiAiProvider(_config.GeminiApiKey, _config.GeminiModel),

        // Phase 6.5 — AAIAS als KI-Target (nutzt serverseitiges Modell, kein API-Key nötig)
        AiTarget.AaiasAgent when _aaias?.IsConnected == true
            => new AaiasAiProvider(_aaias),

        _ => null
    };

    private static string BuildSystemPrompt(AiTarget target) => target switch
    {
        AiTarget.Claude  => "Du bist ein erfahrener .NET-Entwickler und kennst Avalonia 11 und MVVM (CommunityToolkit.Mvvm). Antworte direkt und vollständig in C#/AXAML. Kein Pseudocode.",
        AiTarget.ChatGPT => "You are a senior .NET developer familiar with Avalonia 11 and MVVM. Respond directly and completely in C#/AXAML. No pseudocode.",
        AiTarget.Gemini  => "You are a .NET developer reviewing Avalonia 11 MVVM code. Respond with precise analysis and concrete code suggestions.",
        _                => "You are a helpful developer assistant."
    };

    private static AiHandoffProfile MapTarget(AiTarget target) => target switch
    {
        AiTarget.ChatGPT    => AiHandoffProfile.ChatGpt,
        AiTarget.Claude     => AiHandoffProfile.Claude,
        AiTarget.Gemini     => AiHandoffProfile.Gemini,
        AiTarget.Codex      => AiHandoffProfile.Codex,
        _                   => AiHandoffProfile.Claude
    };

    private static string TargetLabel(AiTarget target) => target switch
    {
        AiTarget.ChatGPT    => "ChatGPT",
        AiTarget.Claude     => "Claude",
        AiTarget.Gemini     => "Gemini",
        AiTarget.Codex      => "Codex",
        AiTarget.LocalModel => "Lokales Modell",
        AiTarget.AaiasAgent => "AAIAS-Agent",
        _                   => target.ToString()
    };

    private static string ModeLabel(AiExecutionMode mode) => mode switch
    {
        AiExecutionMode.ManualHandoff  => "Copy-Paste Handoff",
        AiExecutionMode.ApiDirect      => "API-Direktaufruf",
        AiExecutionMode.ConnectorBridge => "Connector/Plugin",
        AiExecutionMode.LocalModel     => "Lokales Modell",
        _                              => mode.ToString()
    };
}
