using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    private FalloutNativePluginGraphSource? _graphSource;

    private NativeNvseSourceObject CampaignGraphObject(Module module, FalloutFormKey key)
    {
        if (module.ObjectFailure is { } failure) throw new NotSupportedException("Native campaign source graph retained its construction failure: " + failure);
        if (module.Objects.TryGetValue(key, out var existing)) return existing;
        _graphSource ??= new(_records, _quests, _scripts);
        try
        {
            var forms = _graphSource.Forms(key); var events = new Dictionary<FalloutFormKey, NativeNvseLocalContext>(FalloutFormKeyComparer.Instance);
            foreach (var quest in forms.Where(record => record.Signature == "QUST"))
            {
                if (FalloutScriptLocals.AttachedScript(_records, quest) is null) continue;
                if (!module.Locals.TryGetValue(quest.FormKey, out var locals))
                {
                    var actual = FalloutNativePluginLocals.BindCampaign(_records, _quests, _world, _scripts.ScriptValues, quest.FormKey);
                    locals = module.Domain.BindNvseLocalContext(module.Plugin, actual); module.Locals.Add(quest.FormKey, locals);
                }
                events.Add(quest.FormKey, locals);
            }
            var nodes = _graphSource.Plan(forms, events, module.GraphObjects);
            if (nodes.Count != 0)
            {
                var graph = module.Domain.PublishNvseSourceGraph(module.Plugin, nodes); module.Graphs.Add(graph);
                foreach (var (identity, value) in graph.Objects) module.GraphObjects.Add(identity, value);
                foreach (var form in forms)
                {
                    var value = module.GraphObjects[FalloutNativePluginGraphSource.Form(form.FormKey)];
                    if (value.FormId != _records.RuntimeFormId(form.FormKey) || value.Retired || value.Staged ||
                        value.Generation != module.Domain.Generation || value.Module != module.Plugin.Module)
                        throw new InvalidDataException("Actual campaign graph changed a source form/class/generation identity.");
                    module.Objects.TryAdd(form.FormKey, value);
                }
            }
            foreach (var (quest, locals) in events)
                if (locals.Script is null)
                {
                    var attached = FalloutScriptLocals.AttachedScript(_records, _records.GetEffective(quest)) ??
                        throw new InvalidOperationException("Actual quest script retired during native cyclic construction.");
                    module.Domain.AttachNvseLocalScript(module.Plugin, locals, module.Objects[attached.FormKey]);
                }
            return module.Objects[key];
        }
        catch (Exception original)
        {
            module.ObjectFailure = original.ToString();
            // A partially fixed graph or prepared event list never enters an
            // original command. Retain objects until the exact child unloads.
            try { module.Domain.Dispose(); module.Retired = module.Domain.NaturallyRetired; }
            catch (Exception retirement)
            { module.ObjectFailure += "\nNative graph retirement: " + retirement; throw new AggregateException(original, retirement); }
            throw;
        }
    }

    private static void RefreshCampaignGraphs(Module module)
    {
        if (module.ObjectFailure is not null) throw new NotSupportedException("Native source graph has a retained construction fault: " + module.ObjectFailure);
        foreach (var graph in module.Graphs) module.Domain.RefreshNvseSourceGraph(module.Plugin, graph);
    }
}
