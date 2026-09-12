using System;
using System.Collections.Generic;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.PyroLib;

public sealed partial class PyroSubmod
{
    private readonly Dictionary<string, SavedPlumeTemplate> _originalTemplates = new(StringComparer.Ordinal);

    private Dictionary<string, SavedPlumeTemplate> CaptureTemplateChanges()
    {
        var result = new Dictionary<string, SavedPlumeTemplate>(StringComparer.Ordinal);
        foreach (string id in _originalTemplates.Keys)
        {
            var template = PlumeTemplates.Get(id);
            if (template == null) throw new InvalidOperationException($"Edited plume template disappeared: {id}.");
            result[id] = SavedPlumeTemplate.Capture(template);
        }
        return result;
    }

    private void ResetTemplateChanges()
    {
        foreach (var entry in _originalTemplates)
        {
            var template = PlumeTemplates.Get(entry.Key);
            if (template == null) continue;
            entry.Value.Apply(template);
            TemplateRefresher.NotifyTemplateChanged(template, this);
        }
        _originalTemplates.Clear();
    }

    private void RestoreTemplateChanges(Dictionary<string, SavedPlumeTemplate> saved, SaveRestoreContext context)
    {
        foreach (var entry in saved)
        {
            var template = PlumeTemplates.Get(entry.Key);
            if (template == null) { context.Warn($"Shared plume template missing: {entry.Key}."); continue; }
            _originalTemplates.TryAdd(entry.Key, SavedPlumeTemplate.Capture(template));
            entry.Value.Apply(template);
            TemplateRefresher.NotifyTemplateChanged(template, this);
        }
    }
}
