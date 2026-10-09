using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// Plans only actual selected records and contributor readers. There is no
// named-plugin admission, copied campaign value store, or TESForm stand-in.
internal sealed partial class FalloutNativePluginGraphSource
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutQuestState _quests;
    private readonly FalloutQuestScripts _scripts;
    private readonly FalloutNativePluginSourceDeclarations _declarations;
    private readonly Lazy<FalloutNativeScriptConstruction> _construction;
    internal FalloutNativePluginGraphSource(FalloutPluginStack records, FalloutQuestState quests, FalloutQuestScripts scripts)
    {
        _records = records; _quests = quests; _scripts = scripts; _declarations = new(records);
        if (!ReferenceEquals(quests.NativeSourceRecords, records)) throw new InvalidDataException("Native source graph changed its actual quest/source owner.");
        _construction = new(() => FalloutExecutableStringTable.ReadScriptConstruction(records.OwnedSource?.FalloutExecutablePath ??
            throw new NotSupportedException("Native source graph has no selected original executable.")));
    }

    internal static string Form(FalloutFormKey key) => "form:" + key.ToString().ToUpperInvariant();
    internal static string Mod(FalloutPluginContext context) => "mod:" + context.Plugin.Name.ToUpperInvariant();
    internal IReadOnlyList<FalloutPluginRecord> Forms(FalloutFormKey root)
    {
        var seen = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance); var pending = new Queue<FalloutFormKey>();
        var result = new List<FalloutPluginRecord>(); pending.Enqueue(root);
        while (pending.TryDequeue(out var key))
        {
            if (!seen.Add(key)) continue;
            var record = _records.GetEffective(key); _declarations.Current(record); result.Add(record);
            if (record.Signature == "SCPT")
            {
                var script = _declarations.Script(key);
                foreach (var reference in script.References) if (reference.Form is { } target) pending.Enqueue(target);
                if (Clock(script).Quest is { } quest) pending.Enqueue(quest);
            }
            else if (record.Signature == "QUST")
            {
                if (FalloutScriptLocals.AttachedScript(_records, record) is { } attached) pending.Enqueue(attached.FormKey);
            }
            else throw new NotSupportedException("Actual native graph class has no complete layout/method/lifetime owner: " + record.FormKey + ":" + record.Signature);
        }
        return result;
    }

    internal IReadOnlyList<NativeNvseSourceGraphNode> Plan(IReadOnlyList<FalloutPluginRecord> forms,
        IReadOnlyDictionary<FalloutFormKey, NativeNvseLocalContext> eventLists,
        IReadOnlyDictionary<string, NativeNvseSourceObject> retained)
    {
        var nodes = new List<NativeNvseSourceGraphNode>(); var mods = new HashSet<FalloutPluginContext>();
        foreach (var record in forms)
        {
            if (retained.ContainsKey(Form(record.FormKey))) continue;
            if (record.Signature == "SCPT")
            {
                var script = _declarations.Script(record.FormKey);
                foreach (var contributor in script.Contributors) Collect(contributor);
                nodes.Add(new(Form(record.FormKey), NativeNvseSourceClass.Script, _records.RuntimeFormId(record.FormKey), 84,
                    context => new ScriptAuthority(this, script, context, retained)));
            }
            else if (record.Signature == "QUST")
            {
                foreach (var contributor in Contributors(record)) Collect(contributor);
                nodes.Add(new(Form(record.FormKey), NativeNvseSourceClass.Quest, _records.RuntimeFormId(record.FormKey), 108,
                    context => new QuestAuthority(this, record, context, eventLists, retained)));
            }
            else throw new NotSupportedException("Native source plan has an unowned class: " + record.Signature);
        }
        foreach (var context in _records.Plugins.Where(mods.Contains))
        {
            if (retained.ContainsKey(Mod(context))) continue;
            nodes.Add(new(Mod(context), NativeNvseSourceClass.ModInfo, 0, 0x42c,
                graph => new ModAuthority(this, context, graph, retained)));
        }
        return nodes;
        void Collect(FalloutPluginContext context)
        {
            if (!mods.Add(context)) return;
            foreach (var master in context.Plugin.Masters) Collect(Master(master));
        }
    }

    private FalloutPluginContext Master(string name) => _records.Plugins.SingleOrDefault(context =>
        StringComparer.OrdinalIgnoreCase.Equals(context.Plugin.Name, name)) ?? throw new InvalidDataException("Native ModInfo master has no actual selected source reader: " + name);
    private IReadOnlyList<FalloutPluginContext> Contributors(FalloutPluginRecord record)
    {
        var contributors = new List<FalloutPluginContext>();
        foreach (var context in _records.Plugins)
            foreach (var declaration in context.Plugin.Records.Where(value => value.Signature != "TES4" &&
                FalloutFormKeyComparer.Instance.Equals(value.FormKey, record.FormKey)))
            {
                if (declaration.Signature != record.Signature) throw new InvalidDataException("Native contributor changed its real class.");
                if (declaration.IsDeleted) throw new NotSupportedException("Native contributor deletion/recreation lifetime is unowned: " + record.FormKey);
                if ((_declarations.PluginFlags(context) & 1) != 0) contributors.RemoveAll(value => (_declarations.PluginFlags(value) & 1) == 0);
                if (!contributors.Contains(context)) contributors.Add(context);
            }
        return contributors;
    }
    private uint LoadedFlags(FalloutPluginRecord record)
    {
        var flags = 8U;
        foreach (var context in _records.Plugins)
            foreach (var declaration in context.Plugin.Records.Where(value => value.Signature != "TES4" &&
                FalloutFormKeyComparer.Instance.Equals(value.FormKey, record.FormKey)))
            {
                if (declaration.IsDeleted) throw new NotSupportedException("Native loaded form flags include an unowned deleted/recreated lifetime.");
                flags = declaration.Flags | (flags & 0x4000);
            }
        return flags;
    }
    private FalloutNativeScriptClockOwner Clock(FalloutNativeScriptSourceDeclaration declaration)
    {
        var kind = BinaryPrimitives.ReadUInt16LittleEndian(declaration.Header.AsSpan(16));
        if (kind == 1) return _scripts.NativeDefinitionClock(declaration.Record.FormKey);
        if (kind is not (0 or 0x100)) throw new NotSupportedException("Original Script type has no actual runtime constructor/clock owner.");
        var source = _construction.Value;
        return new(BitConverter.UInt32BitsToSingle(source.InitialDelayBits), BitConverter.UInt32BitsToSingle(source.InitialElapsedBits),
            null, source.DeclarationOwner, () => _declarations.Current(declaration.Record));
    }
    private static NativeNvseSourceObject Object(string key, NativeNvseSourceGraphContext context,
        IReadOnlyDictionary<string, NativeNvseSourceObject> retained)
        => retained.TryGetValue(key, out var existing) ? existing : context.Object(key);

    private sealed class ScriptAuthority(FalloutNativePluginGraphSource source, FalloutNativeScriptSourceDeclaration declaration,
        NativeNvseSourceGraphContext context, IReadOnlyDictionary<string, NativeNvseSourceObject> retained) : NativeNvseScriptAuthority
    {
        internal override string SourceOwner => declaration.Record.FormKey + ":" + declaration.Record.Plugin.Name + ":" + SourceSha256;
        internal override string SourceSha256 => declaration.Sha256;
        internal override string CodeSha256 => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(declaration.Code));
        internal override NativeNvseScriptSnapshot Read()
        {
            RequireCurrent(); var initialization = source._construction.Value; var clock = source.Clock(declaration); clock.RequireCurrent();
            var key = Form(declaration.Record.FormKey);
            return new(source._records.RuntimeFormId(declaration.Record.FormKey), declaration.LoadedFlags, context.AllocationBytes(key, 5, 3),
                declaration.Header, declaration.Text is { } text ? new ReadOnlyMemory<byte>(text) : null, declaration.Code,
                initialization.RuntimeWord, clock.Remaining, clock.Elapsed, clock.Quest is { } quest ? Get(Form(quest)) : null,
                declaration.Contributors.Select(value => Get(Mod(value))).ToArray(),
                declaration.References.Select(value => new NativeNvseScriptReference(ReadOnlyMemory<byte>.Empty,
                    value.Form is { } form ? Get(Form(form)) : null, value.Variable)).ToArray(), declaration.Variables,
                declaration.EditorId, initialization.DeclarationOwner + ":" + clock.SourceOwner);
            NativeNvseSourceObject Get(string target) => Object(target, context, retained);
        }
        internal override void RequireCurrent() => source._declarations.Current(declaration.Record);
        internal override IDisposable RetainSource() => SourceLease.Open(declaration.Record.Plugin.Path,
            source._records.OwnedSource!.FalloutExecutablePath, source._construction.Value.RuntimeSha256, RequireCurrent);
    }

    private sealed class SourceLease(FileStream record, FileStream executable) : IDisposable
    {
        internal static SourceLease Open(string recordPath, string runtimePath, string runtimeSha, Action current)
        {
            var record = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read); FileStream? runtime = null;
            try
            {
                runtime = new FileStream(runtimePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(runtime)), runtimeSha))
                    throw new InvalidDataException("Native construction source changed before publication.");
                current(); return new(record, runtime);
            }
            catch { runtime?.Dispose(); record.Dispose(); throw; }
        }
        public void Dispose() { try { record.Dispose(); } finally { executable.Dispose(); } }
    }
}
