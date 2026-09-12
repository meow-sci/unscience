using System;
using System.Collections.Generic;
using System.Linq;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Coordinates feature ownership without depending on native game types.</summary>
public sealed class SceneSaveCoordinator
{
    private readonly List<ISaveParticipant> _participants;
    private readonly List<(ISaveParticipant Participant, Action Restore, SaveRestoreContext Context)> _prepared = new();
    private readonly Dictionary<string, SaveFeature> _retained = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedReset = new(StringComparer.Ordinal);
    private readonly List<string> _messages = new();
    private SaveDocument? _incoming;
    private readonly Dictionary<string, SaveFeature> _lastKnown = new(StringComparer.Ordinal);
    private Dictionary<string, SaveFeature>? _previousRetained;
    private bool _worldReset;
    private bool _restoreCompleted;
    private bool _loadPending;
    public IReadOnlyList<string> Messages => _messages;
    public int ParticipantCount => _participants.Count;
    public int RetainedCount => _retained.Count;
    public string Status { get; private set; } = "Ready. Use KSA Save / Load to save scene setups.";

    public SceneSaveCoordinator(IEnumerable<ISaveParticipant> participants)
    {
        _participants = participants.OrderBy(p => p.RestoreOrder).ThenBy(p => p.SaveId, StringComparer.Ordinal).ToList();
        if (_participants.Select(p => p.SaveId).Distinct(StringComparer.Ordinal).Count() != _participants.Count)
            throw new ArgumentException("Duplicate save participant identifiers.", nameof(participants));
    }

    public SaveDocument Capture()
    {
        _messages.Clear();
        var document = new SaveDocument();
        foreach (var (id, record) in _retained) document.Features.Add(id, record);
        foreach (var participant in _participants)
        {
            if (_retained.ContainsKey(participant.SaveId))
            {
                Report($"{participant.SaveId}: preserving unrestored saved state; current edits for this feature cannot replace it.");
                continue;
            }
            try
            {
                document.Features[participant.SaveId] = new() { Version = participant.SaveVersion, State = participant.CaptureState() };
                _lastKnown[participant.SaveId] = document.Features[participant.SaveId];
            }
            catch (Exception ex)
            {
                if (_lastKnown.TryGetValue(participant.SaveId, out var previous)) document.Features[participant.SaveId] = previous;
                Report($"{participant.SaveId}: capture failed; {(previous == null ? "no earlier state available" : "earlier state retained")}: {ex.Message}");
            }
        }
        document.Warnings.AddRange(_messages);
        Status = $"Captured {document.Features.Count} feature records" + (_messages.Count == 0 ? "." : $" with {_messages.Count} warnings.");
        return document;
    }

    public void PrepareLoad(SaveDocument? document)
    {
        if (document != null) SaveStorage.Validate(document);
        _previousRetained ??= new(_retained, StringComparer.Ordinal);
        _worldReset = false;
        _restoreCompleted = false;
        _messages.Clear();
        _prepared.Clear();
        _retained.Clear();
        _incoming = document;
        _loadPending = true;
        if (document == null) { Status = "Native save has no Unscience state; scene setups will be cleared."; return; }
        foreach (string warning in document.Warnings) Report($"Saved warning: {warning}");
        foreach (var (id, record) in document.Features)
        {
            var participant = _participants.FirstOrDefault(p => p.SaveId == id);
            if (participant == null || participant.SaveVersion != record.Version)
            {
                _retained[id] = record;
                Report($"{id}: unsupported feature/version {record.Version}; retained for a compatible build.");
                continue;
            }
            try
            {
                var context = new SaveRestoreContext(message => Report($"{id}: {message}"));
                _prepared.Add((participant, participant.PrepareRestore(record.State, context), context));
            }
            catch (Exception ex)
            {
                _retained[id] = record;
                Report($"{id}: invalid saved state retained: {ex.Message}");
            }
        }
    }

    public void ResetWorld()
    {
        if (!_loadPending)
        {
            _messages.Clear();
            _prepared.Clear();
            _retained.Clear();
            _incoming = null;
        }
        _worldReset = true;
        _lastKnown.Clear();
        _failedReset.Clear();
        foreach (var participant in _participants.AsEnumerable().Reverse())
        {
            try { participant.ResetState(); }
            catch (Exception ex)
            {
                _failedReset.Add(participant.SaveId);
                Report($"{participant.SaveId}: cleanup failed; restore skipped: {ex.Message}");
            }
        }
        Status = "Old scene setup cleared.";
    }

    public void RestoreWorld()
    {
        int restored = 0;
        foreach (var (participant, restore, context) in _prepared.OrderBy(p => p.Participant.RestoreOrder))
        {
            try
            {
                if (_failedReset.Contains(participant.SaveId)) throw new InvalidOperationException("Old ownership could not be released.");
                restore();
                if (_incoming?.Features.TryGetValue(participant.SaveId, out var record) == true)
                {
                    _lastKnown[participant.SaveId] = record;
                    if (context.HasWarnings) _retained[participant.SaveId] = record;
                }
                restored++;
            }
            catch (Exception ex)
            {
                if (_incoming?.Features.TryGetValue(participant.SaveId, out var original) == true)
                    _retained[participant.SaveId] = original;
                Report($"{participant.SaveId}: restore failed; saved state retained: {ex.Message}");
            }
        }
        _restoreCompleted = true;
        _prepared.Clear(); // Replay at most once, even for direct DeserializeSave callers.
        Status = $"Restored {restored} feature records" + (_messages.Count == 0 ? "." : $" with {_messages.Count} warnings; see details.");
    }

    public void FinishLoad()
    {
        if (_loadPending && !_worldReset && _previousRetained != null)
        {
            _retained.Clear();
            foreach (var item in _previousRetained) _retained.Add(item.Key, item.Value);
        }
        else if (_loadPending && _worldReset && !_restoreCompleted && _incoming != null)
        {
            foreach (var item in _incoming.Features) _retained[item.Key] = item.Value;
            Report("Native load did not complete; incoming feature records retained for recovery.");
        }
        _previousRetained = null;
        _prepared.Clear();
        _incoming = null;
        _loadPending = false;
    }

    public void UseCurrentSetup()
    {
        foreach (var participant in _participants)
            if (_retained.TryGetValue(participant.SaveId, out var record) && record.Version == participant.SaveVersion)
                _retained.Remove(participant.SaveId);
        Status = "Future saves will capture the current setup. Original save files remain unchanged until overwritten.";
    }

    public void MarkFailure(string message)
    {
        Status = message;
        Report(message);
    }

    public void Report(string message)
    {
        if (_messages.Count < 1024) _messages.Add(message);
        Console.WriteLine($"unscience saves: {message}");
    }

    public void MarkWritten(string name) => Status = $"Saved Unscience setup with '{name}'" + (_messages.Count == 0 ? "." : $" ({_messages.Count} warnings).");
}
