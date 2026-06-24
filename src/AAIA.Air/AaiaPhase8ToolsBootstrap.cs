using System.Text.Json;

namespace AAIA.Air;

/// <summary>Registriert ausschließlich die in Phase 8.4 freigegebenen AIR-Werkzeuge.</summary>
internal static class AaiaPhase8ToolsBootstrap
{
    private const int MaxPageSize = 100;
    private const int MaxIdempotencyIdLength = 100;

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static void RegisterAll(AiRuntimeService runtime, AiToolRegistry registry)
    {
        RegisterMessaging(runtime, registry);
        RegisterExecutions(runtime, registry);
        RegisterResources(runtime, registry);
    }

    private static void RegisterMessaging(AiRuntimeService runtime, AiToolRegistry registry)
    {
        registry.Register(Tool(
            "aaia.message.inbox", "Liest ausschließlich die Inbox der aktuellen Session.",
            AiRiskLevel.Green, AiPermission.Read,
            Schema("""{"type":"object","properties":{"unacknowledgedOnly":{"type":"boolean"},"limit":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}"""),
            (inv, _) =>
            {
                var unreadOnly = ReadBool(inv.Input, "unacknowledgedOnly");
                var limit = ReadLimit(inv.Input);
                if (!runtime.Messages.TryReadInbox(inv.Session.SessionId, unreadOnly, out var inbox))
                    return Task.FromResult(AiToolResult.Fail("Inbox nicht verfügbar.", AiPhase8ErrorCodes.NotFound));
                return Task.FromResult(AiToolResult.Ok(new
                {
                    messages = inbox.TakeLast(limit).Select(delivery => new
                    {
                        id = delivery.Message.Id,
                        sender = delivery.Message.Sender,
                        receiver = delivery.Message.Receiver,
                        subject = delivery.Message.Subject,
                        payload = delivery.Message.Payload,
                        priority = delivery.Message.Priority.ToString(),
                        correlationId = delivery.Message.CorrelationId,
                        timestampUtc = delivery.Message.TimestampUtc,
                        deliveredAtUtc = delivery.DeliveredAtUtc,
                        acknowledgedAtUtc = delivery.AcknowledgedAtUtc
                    }).ToArray()
                }));
            }));

        registry.Register(Tool(
            "aaia.message.send", "Sendet eine Nachricht als aktuelle Session.",
            AiRiskLevel.Yellow, AiPermission.Collaborate,
            Schema("""{"type":"object","properties":{"receiver":{"type":"string","maxLength":100},"subject":{"type":"string","maxLength":200},"payload":{"type":"string","maxLength":65536},"priority":{"type":"string","enum":["Low","Normal","High","Urgent"]},"correlationId":{"type":"string","maxLength":100},"idempotencyId":{"type":"string","minLength":1,"maxLength":100}},"required":["receiver","idempotencyId"],"additionalProperties":false}"""),
            (inv, _) =>
            {
                var receiver = ReadString(inv.Input, "receiver");
                var idempotencyId = ReadString(inv.Input, "idempotencyId");
                if (string.IsNullOrWhiteSpace(receiver) || !ValidIdempotencyId(idempotencyId))
                    return Task.FromResult(BadInput("receiver oder gültige idempotencyId fehlt."));
                if (!TryEnum(inv.Input, "priority", AiMessagePriority.Normal, out AiMessagePriority priority))
                    return Task.FromResult(BadInput("priority ist ungültig."));

                var result = runtime.Idempotency.Execute(
                    inv.Session.ClientId,
                    inv.ToolName,
                    idempotencyId!,
                    inv.Input.GetRawText(),
                    () => runtime.Messages.TrySend(
                        inv.Session.SessionId,
                        receiver!,
                        ReadString(inv.Input, "subject") ?? "",
                        ReadString(inv.Input, "payload") ?? "",
                        priority,
                        ReadString(inv.Input, "correlationId"),
                        out var message,
                        out var error)
                            ? AiToolResult.Ok(new { messageId = message!.Id, receiver = message.Receiver, sent = true })
                            : AiToolResult.Fail(error ?? "Nachricht abgelehnt.", AiPhase8ErrorCodes.MessageRejected),
                    result => ReadString(result.Payload, "messageId"),
                    resultId => AiToolResult.Ok(new { messageId = resultId, replayed = true }));
                return Task.FromResult(result);
            }));

        registry.Register(Tool(
            "aaia.message.acknowledge", "Bestätigt eine Nachricht in der eigenen Inbox.",
            AiRiskLevel.Yellow, AiPermission.Collaborate,
            Schema("""{"type":"object","properties":{"messageId":{"type":"string","maxLength":100}},"required":["messageId"],"additionalProperties":false}"""),
            (inv, _) =>
            {
                var messageId = ReadString(inv.Input, "messageId");
                if (string.IsNullOrWhiteSpace(messageId)) return Task.FromResult(BadInput("messageId fehlt."));
                var ok = runtime.Messages.TryAcknowledge(inv.Session.SessionId, messageId!, out var error);
                return Task.FromResult(ok
                    ? AiToolResult.Ok(new { acknowledged = true, messageId })
                    : AiToolResult.Fail(error ?? "Nachricht nicht gefunden.", AiPhase8ErrorCodes.NotFound));
            }));
    }

    private static void RegisterExecutions(AiRuntimeService runtime, AiToolRegistry registry)
    {
        registry.Register(Tool(
            "aaia.execution.list", "Listet ausschließlich von der aktuellen Session erzeugte Executions.",
            AiRiskLevel.Green, AiPermission.Read,
            Schema("""{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}"""),
            (inv, _) => Task.FromResult(AiToolResult.Ok(new
            {
                executions = runtime.Scheduler.List()
                    .Where(item => item.Request.SubmittedBySessionId == inv.Session.SessionId)
                    .TakeLast(ReadLimit(inv.Input))
                    .Select(ExecutionView)
                    .ToArray()
            }))));

        registry.Register(Tool(
            "aaia.execution.get", "Liest eine eigene Execution.",
            AiRiskLevel.Green, AiPermission.Read,
            Schema("""{"type":"object","properties":{"executionId":{"type":"string","maxLength":100}},"required":["executionId"],"additionalProperties":false}"""),
            (inv, _) =>
            {
                var execution = runtime.Scheduler.Get(ReadString(inv.Input, "executionId") ?? "");
                if (execution is null) return Task.FromResult(AiToolResult.Fail("Execution nicht gefunden.", AiPhase8ErrorCodes.NotFound));
                if (execution.Request.SubmittedBySessionId != inv.Session.SessionId)
                    return Task.FromResult(AiToolResult.Fail("Execution gehört einer anderen Session.", AiPhase8ErrorCodes.NotOwner));
                return Task.FromResult(AiToolResult.Ok(ExecutionView(execution)));
            }));

        registry.Register(Tool(
            "aaia.execution.enqueue", "Reiht einen vorhandenen Pending-Task ein.",
            AiRiskLevel.Yellow, AiPermission.Schedule,
            Schema("""{"type":"object","properties":{"taskId":{"type":"string","maxLength":100},"priority":{"type":"string","enum":["Low","Normal","High","Critical"]},"requiredRole":{"type":"string"},"requiredCapabilities":{"type":"array","maxItems":32,"items":{"type":"string","maxLength":100}},"notBeforeUtc":{"type":"string","format":"date-time"},"maxAttempts":{"type":"integer","minimum":1,"maximum":10},"idempotencyId":{"type":"string","minLength":1,"maxLength":100}},"required":["taskId","idempotencyId"],"additionalProperties":false}"""),
            (inv, _) =>
            {
                var taskId = ReadString(inv.Input, "taskId");
                var idempotencyId = ReadString(inv.Input, "idempotencyId");
                if (string.IsNullOrWhiteSpace(taskId) || !ValidIdempotencyId(idempotencyId))
                    return Task.FromResult(BadInput("taskId oder gültige idempotencyId fehlt."));
                if (!TryEnum(inv.Input, "priority", AiExecutionPriority.Normal, out AiExecutionPriority priority))
                    return Task.FromResult(BadInput("priority ist ungültig."));
                if (!TryOptionalEnum(inv.Input, "requiredRole", out AiRole? role))
                    return Task.FromResult(BadInput("requiredRole ist ungültig."));
                if (!TryStringArray(inv.Input, "requiredCapabilities", 32, out var capabilities))
                    return Task.FromResult(BadInput("requiredCapabilities ist ungültig oder zu groß."));
                if (!TryOptionalUtc(inv.Input, "notBeforeUtc", out var notBeforeUtc))
                    return Task.FromResult(BadInput("notBeforeUtc ist ungültig."));
                var maxAttempts = ReadInt(inv.Input, "maxAttempts") ?? 3;
                if (maxAttempts is < 1 or > 10) return Task.FromResult(BadInput("maxAttempts liegt außerhalb 1..10."));

                var result = runtime.Idempotency.Execute(
                    inv.Session.ClientId,
                    inv.ToolName,
                    idempotencyId!,
                    inv.Input.GetRawText(),
                    () =>
                    {
                        try
                        {
                            var execution = runtime.Scheduler.Enqueue(
                                taskId!, priority, role, capabilities, notBeforeUtc, maxAttempts,
                                submittedBySessionId: inv.Session.SessionId,
                                submittedByClientId: inv.Session.ClientId);
                            return AiToolResult.Ok(ExecutionView(execution));
                        }
                        catch (InvalidOperationException ex)
                        {
                            return AiToolResult.Fail(ex.Message, AiPhase8ErrorCodes.ExecutionRejected);
                        }
                    },
                    result => ReadString(result.Payload, "executionId"),
                    resultId => AiToolResult.Ok(new { executionId = resultId, replayed = true }));
                return Task.FromResult(result);
            }));

        registry.Register(Tool(
            "aaia.execution.cancel", "Bricht eine eigene Execution ab.",
            AiRiskLevel.Orange, AiPermission.Schedule,
            Schema("""{"type":"object","properties":{"executionId":{"type":"string","maxLength":100}},"required":["executionId"],"additionalProperties":false}"""),
            (inv, _) =>
            {
                var executionId = ReadString(inv.Input, "executionId");
                if (string.IsNullOrWhiteSpace(executionId)) return Task.FromResult(BadInput("executionId fehlt."));
                var execution = runtime.Scheduler.Get(executionId!);
                if (execution is null) return Task.FromResult(AiToolResult.Fail("Execution nicht gefunden.", AiPhase8ErrorCodes.NotFound));
                if (execution.Request.SubmittedBySessionId != inv.Session.SessionId)
                    return Task.FromResult(AiToolResult.Fail("Execution gehört einer anderen Session.", AiPhase8ErrorCodes.NotOwner));
                return Task.FromResult(runtime.Scheduler.Cancel(executionId!)
                    ? AiToolResult.Ok(new { cancelled = true, executionId })
                    : AiToolResult.Fail("Execution ist bereits abgeschlossen.", AiPhase8ErrorCodes.ExecutionRejected));
            }));
    }

    private static void RegisterResources(AiRuntimeService runtime, AiToolRegistry registry)
    {
        registry.Register(Tool(
            "aaia.resource.list", "Liest redigierte Ressourcenprofile ohne Provider- und Kostendetails.",
            AiRiskLevel.Green, AiPermission.Read,
            Schema("""{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}"""),
            (inv, _) =>
            {
                var reservations = runtime.Resources.ListReservations()
                    .Where(r => r.State == AiReservationState.Reserved)
                    .GroupBy(r => r.ResourceId)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                var resources = runtime.Resources.Registry.ListProfiles()
                    .Take(ReadLimit(inv.Input))
                    .Select(profile =>
                    {
                        var telemetry = runtime.Resources.Registry.GetTelemetry(profile.ResourceId);
                        return new AiResourcePublicSnapshot
                        {
                            ResourceId = profile.ResourceId,
                            Kind = profile.Kind,
                            Enabled = profile.Enabled,
                            Locality = profile.Locality,
                            Capabilities = profile.Capabilities,
                            Capacity = profile.Capacity,
                            TelemetryAvailable = telemetry is not null,
                            Healthy = telemetry?.Healthy ?? false,
                            Throttled = telemetry?.Throttled ?? false,
                            ActiveReservations = reservations.GetValueOrDefault(profile.ResourceId)
                        };
                    }).ToArray();
                return Task.FromResult(AiToolResult.Ok(new { resources }));
            }));

        registry.Register(Tool(
            "aaia.resource.status", "Liest aggregierten Ressourcen- und Budgetstatus.",
            AiRiskLevel.Green, AiPermission.Read,
            Schema("""{"type":"object","properties":{},"additionalProperties":false}"""),
            (inv, _) =>
            {
                var profiles = runtime.Resources.Registry.ListProfiles();
                var reservations = runtime.Resources.ListReservations();
                var budgets = runtime.Resources.ListBudgets();
                var status = new AiResourcePublicStatus
                {
                    ResourceCount = profiles.Count,
                    EnabledCount = profiles.Count(p => p.Enabled),
                    HealthyCount = profiles.Count(p => runtime.Resources.Registry.GetTelemetry(p.ResourceId) is { Healthy: true, Throttled: false }),
                    ActiveReservations = reservations.Count(r => r.State == AiReservationState.Reserved),
                    BudgetCount = budgets.Count,
                    ExhaustedBudgetCount = budgets.Count(b => b.Spent + b.Reserved >= b.Budget.HardLimit)
                };
                return Task.FromResult(AiToolResult.Ok(status));
            }));
    }

    private static AiToolDefinition Tool(
        string name,
        string description,
        AiRiskLevel risk,
        AiPermission permission,
        JsonElement schema,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> handler) => new()
        {
            Name = name,
            Version = "1.0.0",
            Since = "8.4.0",
            Description = description,
            RiskLevel = risk,
            RequiredPermissions = permission,
            RequiredCapabilities = new[] { AiCapabilities.Mcp },
            InputSchema = schema,
            Handler = handler
        };

    private static object ExecutionView(AiExecutionSnapshot item) => new
    {
        executionId = item.Request.Id,
        taskId = item.Request.TaskId,
        state = item.State.ToString(),
        priority = item.Request.Priority.ToString(),
        attemptCount = item.AttemptCount,
        maxAttempts = item.Request.MaxAttempts,
        enqueuedAtUtc = item.Request.EnqueuedAtUtc,
        updatedAtUtc = item.UpdatedAtUtc,
        lastError = item.LastError,
        resourceId = item.ResourceId
    };

    private static AiToolResult BadInput(string message) => AiToolResult.Fail(message, AiPhase8ErrorCodes.BadInput);

    private static string? ReadString(JsonElement input, string property)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement input, string property)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadInt(JsonElement input, string property)
        => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static int ReadLimit(JsonElement input) => Math.Clamp(ReadInt(input, "limit") ?? 50, 1, MaxPageSize);

    private static bool ValidIdempotencyId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaxIdempotencyIdLength;

    private static bool TryEnum<T>(JsonElement input, string property, T fallback, out T value) where T : struct, Enum
    {
        var raw = ReadString(input, property);
        if (raw is null)
        {
            value = fallback;
            return true;
        }
        return Enum.TryParse(raw, true, out value) && Enum.IsDefined(value);
    }

    private static bool TryOptionalEnum<T>(JsonElement input, string property, out T? value) where T : struct, Enum
    {
        var raw = ReadString(input, property);
        if (raw is null)
        {
            value = null;
            return true;
        }
        if (Enum.TryParse<T>(raw, true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryStringArray(JsonElement input, string property, int maxItems, out string[] values)
    {
        values = Array.Empty<string>();
        if (!input.TryGetProperty(property, out var element)) return true;
        if (element.ValueKind != JsonValueKind.Array) return false;
        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) || item.GetString()!.Length > 100)
                return false;
            result.Add(item.GetString()!);
            if (result.Count > maxItems) return false;
        }
        values = result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static bool TryOptionalUtc(JsonElement input, string property, out DateTime? value)
    {
        value = null;
        var raw = ReadString(input, property);
        if (raw is null) return true;
        if (!DateTimeOffset.TryParse(raw, out var parsed)) return false;
        value = parsed.UtcDateTime;
        return true;
    }
}
