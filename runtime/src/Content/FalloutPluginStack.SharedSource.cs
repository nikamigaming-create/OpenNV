using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutPluginStack
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, SharedSourceSlot> OwnedReaders = new();
    private SharedSourceData? _sharedSource;

    private sealed class SharedSourceSlot
    {
        internal SharedSourceData? Data;
    }

    // No session wrapper or mutable gameplay/settings owner is retained here.
    // The backing lifetime is exactly the selected owned source's lifetime.
    private sealed class SharedSourceData(FalloutPluginStack source)
    {
        internal readonly System.Collections.ObjectModel.ReadOnlyCollection<FalloutPluginContext> Plugins = source._plugins;
        internal readonly IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord> Winners = source._winners;
        internal readonly IReadOnlyDictionary<string, int> LoadOrder = source._loadOrderIndices;
        internal readonly IReadOnlyDictionary<string, IReadOnlyList<FalloutFormKey>> WinnerKeys = source._winnerKeysBySignature;
        internal readonly Dictionary<string, IReadOnlyList<FalloutPluginRecord>> Effective = source._effectiveBySignature;
        internal readonly Dictionary<string, IReadOnlyList<FalloutPluginRecord>> Registration = source._registrationBySignature;
        internal readonly object SignatureGate = source._signatureIndexLock;
        internal readonly object IndexGate = source._sourceIndexGate;
        internal readonly Dictionary<(Type Kind, FalloutFormKey? Scope), IFalloutSourceIndex> Indexes = source._sourceIndexes;
        internal readonly object CellGate = source._cellIndexLock;
        internal readonly Dictionary<string, Dictionary<FalloutFormKey, IReadOnlyList<FalloutPluginRecord>>> CellChildren = source._cellChildrenBySignatureAndCell;
        internal readonly int EffectiveCount = source._effectiveRecordCount;
        internal long? EmbeddedQuestScripts = source._embeddedQuestScripts;
        private bool _retired;

        internal void Validate()
        {
            lock (IndexGate)
            {
                ObjectDisposedException.ThrowIf(_retired, this);
                foreach (var context in Plugins) context.Plugin.ValidateReadSource();
            }
        }

        internal void Retire()
        {
            lock (IndexGate)
            {
                if (_retired) return;
                _retired = true;
                Indexes.Clear();
                List<Exception> errors = [];
                foreach (var context in Plugins)
                    try { context.Plugin.Dispose(); }
                    catch (Exception error) { errors.Add(error); }
                if (errors.Count != 0) throw new AggregateException("Owned plugin reader retirement failed.", errors);
            }
        }
    }

    private FalloutPluginStack(SharedSourceData source, RuntimeLiveContentSource owner)
    {
        _sharedSource = source;
        _plugins = source.Plugins;
        _winners = source.Winners;
        _loadOrderIndices = source.LoadOrder;
        _winnerKeysBySignature = source.WinnerKeys;
        _effectiveBySignature = source.Effective;
        _registrationBySignature = source.Registration;
        _signatureIndexLock = source.SignatureGate;
        _sourceIndexGate = source.IndexGate;
        _sourceIndexes = source.Indexes;
        _cellIndexLock = source.CellGate;
        _cellChildrenBySignatureAndCell = source.CellChildren;
        _effectiveRecordCount = source.EffectiveCount;
        OwnedSource = owner;
        PerkParameters = new(this);
        QuestObjects = new(this);
        NumericSettings = new(this, owner);
        _iniSettings = new(() => FalloutInstallationSettings.Read(owner).NumericIni);
        SoundPaths = new(this);
    }

    private static FalloutPluginStack LoadOwnedSession(IReadOnlyList<FalloutPluginSource> sources,
        RuntimeLiveContentSource owner, bool audit, out FalloutPluginStackLoadMetrics metrics)
    {
        if (!sources.SequenceEqual(owner.PluginSources))
            throw new FalloutPluginFormatException("Plugin sources differ from their complete owned source graph.");
        var slot = OwnedReaders.GetValue(owner, _ => new SharedSourceSlot());
        lock (slot)
        {
            if (slot.Data is { } retained)
            {
                retained.Validate();
                metrics = new(TimeSpan.Zero, TimeSpan.Zero);
                var session = new FalloutPluginStack(retained, owner);
                if (audit) session.LoadAllSignatureIndexes();
                return session;
            }
            var first = LoadCore(sources, audit, out metrics);
            var data = new SharedSourceData(first);
            try { owner.RegisterRetirement(data.Retire); }
            catch { first.Dispose(); throw; }
            first._sharedSource = data;
            slot.Data = data;
            return first;
        }
    }
}
