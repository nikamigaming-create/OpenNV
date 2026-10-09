using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// The campaign owns each entry, including repeated IDs. Its delegates must
// address the original ordinal, not an index dictionary or copied state store.
internal sealed record FalloutNativePluginOrderedLocalOwner(string SourceOwner, string ScriptSha256,
    object ValueStoreIdentity, IReadOnlyList<NativeNvseLocalDeclaration> Declarations,
    Func<int, ulong> ReadEntry, Func<IReadOnlyList<NativeNvseLocalEntryChange>, Action> PrepareEntries,
    Action RequireCurrent);

internal static partial class FalloutNativePluginLocals
{
    internal static NativeNvseLocalAuthority BindOrdered(FalloutPluginStack records, FalloutPluginRecord script,
        FalloutScriptValueStore values, FalloutNativePluginOrderedLocalOwner owner)
        => new OrderedAuthority(records, script, values, owner);

    private sealed class OrderedAuthority : NativeNvseLocalAuthority
    {
        private readonly FalloutPluginStack _records;
        private readonly FalloutPluginRecord _script;
        private readonly FalloutScriptValueStore _values;
        private readonly FalloutNativePluginOrderedLocalOwner _owner;
        private readonly FalloutScriptLocalDeclaration[] _semantic;
        internal override string SourceOwner => _owner.SourceOwner;
        internal override string SourceSha256 { get; }
        internal override string CodeSha256 { get; }
        internal override uint CodeBytes { get; }
        internal override object ValueStoreIdentity => _values;
        internal override IReadOnlyList<NativeNvseLocalDeclaration> Declarations { get; }

        internal OrderedAuthority(FalloutPluginStack records, FalloutPluginRecord script, FalloutScriptValueStore values,
            FalloutNativePluginOrderedLocalOwner owner)
        {
            _records = records; _script = script; _values = values; _owner = owner;
            if (script.Signature != "SCPT" || script.IsDeleted || !ReferenceEquals(owner.ValueStoreIdentity, values) ||
                string.IsNullOrWhiteSpace(owner.SourceOwner)) throw new InvalidDataException("Ordered locals have no exact actual campaign/value owner.");
            SourceSha256 = Convert.ToHexString(SHA256.HashData(script.ReadData()));
            if (!StringComparer.OrdinalIgnoreCase.Equals(SourceSha256, owner.ScriptSha256))
                throw new InvalidDataException("Ordered local owner is bound to different original source bytes.");
            var fields = script.ReadSubrecords().ToArray();
            var body = new ReadOnlyMemory<byte>(FalloutNativePluginSourceDeclarations.Body(fields,
                fields.Single(field => field.Signature == "SCHR").Data.Span));
            CodeSha256 = Convert.ToHexString(SHA256.HashData(body.Span)); CodeBytes = checked((uint)body.Length);
            var kinds = FalloutScriptLocals.ReadStorageKinds(script, FalloutScriptDeclarationAuthority.CompiledVanilla);
            var semantic = new List<FalloutScriptLocalDeclaration>(); var declarations = new List<NativeNvseLocalDeclaration>();
            foreach (var metadata in FalloutScriptLocals.ReadMetadata(script))
            {
                if (metadata.Ordinal != declarations.Count || !kinds.TryGetValue(metadata.Index, out var storageKind))
                    throw new InvalidDataException("Ordered local source/name/semantic identities differ.");
                var declaration = new FalloutScriptLocalDeclaration(metadata.Index, storageKind);
                var flag = metadata.StorageFlags;
                var kind = declaration.Kind switch
                {
                    FalloutScriptLocalKind.Form => NativeNvseLocalKind.Form,
                    FalloutScriptLocalKind.String => NativeNvseLocalKind.String,
                    FalloutScriptLocalKind.Array => NativeNvseLocalKind.Array,
                    FalloutScriptLocalKind.Number when flag == 1 => NativeNvseLocalKind.Integer,
                    FalloutScriptLocalKind.Number when flag == 0 => NativeNvseLocalKind.Number,
                    _ => throw new NotSupportedException("Ordered source local has no exact typed native scalar owner.")
                };
                declarations.Add(new(metadata.Index, kind, flag)); semantic.Add(declaration);
            }
            if (!declarations.SequenceEqual(owner.Declarations))
                throw new InvalidDataException("Ordered local campaign entries differ from complete original source order/flags/types.");
            Declarations = declarations.ToArray(); _semantic = semantic.ToArray(); RequireCurrent();
        }
        internal override void RequireCurrent()
        {
            _owner.RequireCurrent();
            if (!_script.Plugin.NativeSourceAvailable || !ReferenceEquals(_records.GetEffective(_script.FormKey), _script))
                throw new InvalidOperationException("Ordered native local source/reader lifetime changed.");
        }
        internal override IDisposable RetainSource()
        {
            var file = new FileStream(_script.Plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try { RequireCurrent(); return file; } catch { file.Dispose(); throw; }
        }
        internal override ulong Read(uint index)
        {
            for (var entry = 0; entry < Declarations.Count; ++entry)
                if (Declarations[entry].Index == index) return ReadEntry(entry);
            throw new InvalidDataException("Native local lookup has no original matching entry.");
        }
        internal override ulong ReadEntry(int entry)
        {
            RequireCurrent(); if ((uint)entry >= Declarations.Count) throw new InvalidDataException("Native local ordinal is outside its source list.");
            var bits = _owner.ReadEntry(entry);
            if (NeedsValueStore(entry)) _ = Decode(entry, bits);
            return bits;
        }
        // Native Number/Integer cells retain exact UInt64 storage, including
        // mixed reference bits. Reached numeric operands own scalar conversion.
        private bool NeedsValueStore(int entry) => _semantic[entry].Kind != FalloutScriptLocalKind.Number;
        private double Decode(int entry, ulong bits)
        {
            var kind = _semantic[entry].Kind;
            // Reference-table resolution observes the low UInt32 of the same
            // real payload. An integer/float view may own its other bits; do
            // not rewrite them while merely reading an alias.
            var raw = kind == FalloutScriptLocalKind.Form ? (uint)bits : BitConverter.UInt64BitsToDouble(bits);
            if (!double.IsFinite(raw))
                throw new NotSupportedException("Ordered native local is outside its actual scalar domain.");
            _ = _values.Read(kind, raw); return raw;
        }
        internal override Action Prepare(IReadOnlyList<NativeNvseLocalChange> changes)
            => PrepareEntries(changes.Select(change => new NativeNvseLocalEntryChange(
                Enumerable.Range(0, Declarations.Count).First(entry => Declarations[entry].Index == change.Index), change.Index, change.Before, change.After)).ToArray());
        internal override Action PrepareEntries(IReadOnlyList<NativeNvseLocalEntryChange> changes)
        {
            RequireCurrent();
            if (changes.Select(change => change.Entry).Distinct().Count() != changes.Count) throw new InvalidDataException("Native local publication repeats an entry ordinal.");
            foreach (var change in changes)
            {
                if ((uint)change.Entry >= Declarations.Count || Declarations[change.Entry].Index != change.Index || ReadEntry(change.Entry) != change.Before)
                    throw new InvalidOperationException("Native ordered local changed before admission.");
                if (NeedsValueStore(change.Entry)) _ = Decode(change.Entry, change.After);
            }
            var publication = _owner.PrepareEntries(changes.ToArray());
            return () =>
            {
                RequireCurrent();
                foreach (var change in changes)
                    if (ReadEntry(change.Entry) != change.Before) throw new InvalidOperationException("Actual ordered campaign local changed during native admission.");
                foreach (var change in changes)
                    if (NeedsValueStore(change.Entry)) _values.ValidateLocal(_semantic[change.Entry].Kind, Decode(change.Entry, change.After),
                        SourceOwner + ":entry:" + change.Entry);
                publication(); RequireCurrent();
                foreach (var change in changes)
                    if (ReadEntry(change.Entry) != change.After) throw new InvalidOperationException("Actual ordered native publication lacks its campaign receipt.");
            };
        }
    }
}
