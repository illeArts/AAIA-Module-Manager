using System;
using System.Collections.Generic;
using System.Linq;
using AAIA.Air.Roles;

namespace AAIA.Air.Collaboration;

/// <summary>
/// Koordiniert die Zusammenarbeit mehrerer KIs: wer arbeitet woran, wer wartet, wer
/// reviewt, wer testet. Verteilt Aufgaben nach VERANTWORTUNG (Rolle) und Verfügbarkeit —
/// nicht modellspezifisch. Macht aus mehreren KIs ein koordiniertes Team statt nur
/// paralleler Werkzeuge.
/// </summary>
public sealed class AiCollaborationManager
{
    private readonly AiSessionManager _sessions;
    private readonly AiBlackboard _blackboard;

    public AiCollaborationManager(AiSessionManager sessions, AiBlackboard blackboard)
    {
        _sessions = sessions;
        _blackboard = blackboard;
    }

    /// <summary>Aktive Sessions mit einer bestimmten Rolle.</summary>
    public IReadOnlyList<AiSession> WithRole(AiRole role)
        => _sessions.Active.Where(s => s.HasRole(role)).ToList();

    /// <summary>
    /// Schlägt eine Session für eine Rolle vor — die mit den wenigsten aktiven Locks
    /// (= am wenigsten ausgelastet). Optional eine Session ausschließen (z. B. den Autor).
    /// </summary>
    public AiSession? SuggestAssignee(AiRole role, string? excludeSessionId = null)
        => _sessions.Active
            .Where(s => s.HasRole(role) && s.SessionId != excludeSessionId)
            .OrderBy(s => s.ActiveLocks.Count)
            .ThenBy(s => s.ConnectedAt)
            .FirstOrDefault();

    /// <summary>Schlägt einen Reviewer vor (Reviewer-Rolle, nicht der Autor).</summary>
    public AiSession? SuggestReviewer(string authorSessionId)
        => SuggestAssignee(AiRole.Reviewer, authorSessionId);

    /// <summary>Schlägt einen Tester vor.</summary>
    public AiSession? SuggestTester(string? excludeSessionId = null)
        => SuggestAssignee(AiRole.Tester, excludeSessionId);

    /// <summary>Wer arbeitet aktuell an welchem Thema (aus dem Blackboard).</summary>
    public IReadOnlyDictionary<string, string> WhoIsWorkingOn(string project)
        => _blackboard.List(project)
            .Where(e => e.Status == AiWorkItemStatus.InProgress && e.OwnerClientName is not null)
            .ToDictionary(e => e.Topic, e => e.OwnerClientName!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Themen, die blockiert sind und auf etwas/jemanden warten.</summary>
    public IReadOnlyList<string> Waiting(string project)
        => _blackboard.List(project)
            .Where(e => e.Status == AiWorkItemStatus.Blocked)
            .Select(e => e.Topic)
            .ToList();
}
