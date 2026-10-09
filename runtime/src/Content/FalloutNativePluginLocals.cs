using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// These factories bind existing campaign variable owners. They never instantiate
// a gameplay reference merely to supply a native pointer.
internal static partial class FalloutNativePluginLocals
{
    internal static NativeNvseLocalAuthority BindReference(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey reference)
    {
        if (!ReferenceEquals(records, world.NativeSourceRecords)) throw new InvalidDataException("Native local world/source identities differ.");
        var instance = world.Retained(reference);
        var definition = instance.Script ?? throw new NotSupportedException("Actual reference has no attached script/local owner.");
        void Current()
        {
            if (!ReferenceEquals(world.Retained(reference), instance) || instance.Deleted || instance.DeletePending ||
                !ReferenceEquals(instance.Script, definition))
                throw new InvalidOperationException("Native event-list reference/attached-script lifetime has retired or changed.");
        }
        return new Authority(records, definition.Record, world.ScriptValues, reference.ToString(), definition.Declarations,
            instance.Read, instance.Write, Current);
    }

    internal static NativeNvseLocalAuthority BindQuest(FalloutPluginStack records, FalloutQuestState quests,
        FalloutScriptValueStore values, FalloutFormKey quest)
    {
        if (!ReferenceEquals(records, quests.NativeSourceRecords)) throw new InvalidDataException("Native local quest/source identities differ.");
        var owner = records.GetEffective(quest);
        if (owner.Signature != "QUST") throw new InvalidDataException("Native quest local owner is not QUST.");
        var script = FalloutScriptLocals.AttachedScript(records, owner) ?? throw new NotSupportedException("Actual quest has no attached script/local owner.");
        var stateIdentity = quests.NativeLocalIdentity(quest);
        void Current()
        {
            if (!ReferenceEquals(records.GetEffective(quest), owner) || !ReferenceEquals(quests.NativeLocalIdentity(quest), stateIdentity))
                throw new InvalidOperationException("Native event-list quest/attached-script source changed.");
        }
        return new Authority(records, script, values, quest.ToString(), FalloutScriptLocals.ReadDeclarations(script),
            index => quests.Variable(quest, index), (index, raw) => quests.SetVariable(quest, index, raw), Current);
    }

    private sealed class Authority : NativeNvseLocalAuthority
    {
        private readonly FalloutPluginStack _records;
        private readonly FalloutPluginRecord _script;
        private readonly FalloutScriptValueStore _values;
        private readonly IReadOnlyDictionary<uint, FalloutScriptLocalDeclaration> _slots;
        private readonly Func<uint, double> _read;
        private readonly Action<uint, double> _write;
        private readonly Action _current;
        private readonly string _localOwner;
        private readonly IReadOnlyDictionary<uint, NativeNvseLocalKind> _nativeKinds;
        internal override string SourceOwner { get; }
        internal override string SourceSha256 { get; }
        internal override string CodeSha256 { get; }
        internal override uint CodeBytes { get; }
        internal override object ValueStoreIdentity => _values;
        internal override IReadOnlyList<NativeNvseLocalDeclaration> Declarations { get; }

        internal Authority(FalloutPluginStack records, FalloutPluginRecord script, FalloutScriptValueStore values,
            string owner, IReadOnlyDictionary<string, FalloutScriptLocalDeclaration> slots,
            Func<uint, double> read, Action<uint, double> write, Action current)
        {
            _records = records; _script = script; _values = values; _read = read; _write = write; _current = current; _localOwner = owner;
            SourceOwner = owner + ":" + script.FormKey;
            var data = script.ReadData(); SourceSha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var fields = script.ReadSubrecords().ToArray();
            var body = FalloutNativePluginSourceDeclarations.Body(fields, fields.Single(field => field.Signature == "SCHR").Data.Span);
            CodeBytes = checked((uint)body.Length);
            CodeSha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            var metadata = FalloutScriptLocals.ReadMetadata(script);
            if (metadata.Select(row => row.Index).Distinct().Count() != metadata.Count)
                throw new NotSupportedException("Original source contains ordered duplicate local entries; its actual per-entry campaign/cold owner is required before native publication.");
            _slots = slots.Values.ToDictionary(row => row.Index);
            var flags = new Dictionary<uint, byte>();
            var declarationOrder = new List<uint>();
            foreach (var field in script.ReadSubrecords().Where(field => field.Signature == "SLSD"))
            {
                if (field.Data.Length != 24) throw new InvalidDataException("Native local SLSD declaration extent is invalid.");
                var index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span); var flag = field.Data.Span[16];
                if (flags.TryGetValue(index, out var previous) && previous != flag) throw new InvalidDataException("Native local storage flags conflict.");
                if (!flags.ContainsKey(index)) declarationOrder.Add(index);
                flags[index] = flag;
            }
            if (declarationOrder.Count != _slots.Count || declarationOrder.Any(index => !_slots.ContainsKey(index)))
                throw new InvalidDataException("Native local field order differs from its actual admitted slot declarations.");
            Declarations = declarationOrder.Select(index => _slots[index]).Select(row => new NativeNvseLocalDeclaration(row.Index, row.Kind switch
            {
                FalloutScriptLocalKind.Form => NativeNvseLocalKind.Form,
                FalloutScriptLocalKind.String => NativeNvseLocalKind.String,
                FalloutScriptLocalKind.Array => NativeNvseLocalKind.Array,
                FalloutScriptLocalKind.Number when flags.GetValueOrDefault(row.Index) == 1 => NativeNvseLocalKind.Integer,
                FalloutScriptLocalKind.Number when flags.GetValueOrDefault(row.Index) == 0 => NativeNvseLocalKind.Number,
                _ => throw new NotSupportedException("Source local kind/storage has no native cell representation."),
            }, flags[row.Index])).ToArray();
            _nativeKinds = Declarations.ToDictionary(row => row.Index, row => row.Kind);
            RequireCurrent(); foreach (var row in Declarations) _ = Read(row.Index);
        }

        internal override IDisposable RetainSource()
        {
            // The live original file is a read-only lease with no write/delete
            // sharing. Closing this lease never changes the owned input.
            var lease = new FileStream(_script.Plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                RequireCurrent();
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(_script.ReadData())), SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Native event-list source payload changed before its live lease.");
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }

        internal override void RequireCurrent()
        {
            _current();
            if (!_script.Plugin.NativeSourceAvailable || !ReferenceEquals(_records.GetEffective(_script.FormKey), _script))
                throw new InvalidOperationException("Native event-list SCPT winner/input-reader lifetime changed.");
        }

        private double Decode(uint index, ulong bits)
        {
            var declaration = _slots.TryGetValue(index, out var value) ? value : throw new InvalidDataException("Native local index has no source declaration.");
            double raw;
            if (declaration.Kind == FalloutScriptLocalKind.Form)
            {
                if ((bits >> 32) != 0) throw new InvalidDataException("Native reference local writes outside its declared 32-bit identity.");
                raw = (uint)bits;
            }
            else raw = BitConverter.UInt64BitsToDouble(bits);
            if (!double.IsFinite(raw)) throw new InvalidDataException("Native local value is non-finite.");
            if (_nativeKinds[index] == NativeNvseLocalKind.Integer &&
                (raw != Math.Truncate(raw) || raw < int.MinValue || raw > int.MaxValue))
                throw new NotSupportedException("Native integer local is outside its source signed scalar storage domain.");
            _ = _values.Read(declaration.Kind, raw);
            return raw;
        }

        internal override ulong Read(uint index)
        {
            RequireCurrent();
            var raw = _read(index); var declaration = _slots[index];
            var bits = declaration.Kind == FalloutScriptLocalKind.Form ? checked((uint)FalloutScriptValue.Form(raw).Number) : BitConverter.DoubleToUInt64Bits(raw);
            _ = Decode(index, bits); return bits;
        }

        internal override Action Prepare(IReadOnlyList<NativeNvseLocalChange> changes)
        {
            RequireCurrent();
            if (changes.Select(change => change.Index).Distinct().Count() != changes.Count) throw new InvalidDataException("Native local mutation contains duplicate slots.");
            var prepared = changes.Select(change =>
            {
                if (Read(change.Index) != change.Before) throw new InvalidOperationException("Campaign local changed before native publication.");
                return (Change: change, Raw: Decode(change.Index, change.After));
            }).ToArray();
            return () =>
            {
                RequireCurrent();
                foreach (var row in prepared)
                    if (Read(row.Change.Index) != row.Change.Before) throw new InvalidOperationException("Campaign local changed during native publication admission.");
                foreach (var row in prepared)
                {
                    var declaration = _slots[row.Change.Index];
                    _values.ValidateLocal(declaration.Kind, row.Raw, _localOwner + ":" + row.Change.Index);
                    _write(row.Change.Index, row.Raw);
                }
            };
        }
    }
}
