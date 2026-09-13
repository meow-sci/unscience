using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Feature-owned, detached scene state. Implement on an ISubmod or expose via ISaveParticipantSource.</summary>
public interface ISaveParticipant
{
    string SaveId { get; }
    int SaveVersion => 1;
    int RestoreOrder => 100;
    JsonElement CaptureState();
    /// <summary>Release old-world ownership synchronously, before native objects are destroyed.</summary>
    void ResetState();
    /// <summary>Validate/deserialize without modifying the world; return replay to run after native reconstruction.</summary>
    Action PrepareRestore(JsonElement state, SaveRestoreContext context);
}

public interface ISaveParticipantSource
{
    IEnumerable<ISaveParticipant> SaveParticipants { get; }
}

/// <summary>Adapter for controllers which already have explicit capture/apply APIs.</summary>
public sealed class SaveParticipant<T>(string id, Func<T> capture, Action reset,
    Action<T, SaveRestoreContext> restore, int order = 100, Action<T>? validate = null) : ISaveParticipant
{
    public string SaveId => id;
    public int RestoreOrder => order;
    public JsonElement CaptureState() => SaveJson.ToElement(capture());
    public void ResetState() => reset();
    public Action PrepareRestore(JsonElement state, SaveRestoreContext context)
    {
        T value = SaveJson.FromElement<T>(state);
        validate?.Invoke(value);
        return () => restore(value, context);
    }
}

public sealed class SaveRestoreContext
{
    private readonly Action<string> _report;
    public SaveRestoreContext(Action<string> report) => _report = report;
    public bool HasWarnings { get; private set; }
    public void Warn(string message) { HasWarnings = true; _report(message); }
    public void Info(string message) => _report(message);
    public void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
