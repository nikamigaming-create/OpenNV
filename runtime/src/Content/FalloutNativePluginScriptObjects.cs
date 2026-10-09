using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// These facts are supplied by an actual selected runtime/clock/object owner.
// An absent ModInfo, quest, constructor field or flag producer refuses before
// native publication. The source SCPT alone does not establish runtime words.
internal sealed record FalloutNativeScriptRuntimeFacts(string SourceOwner, string RuntimeSha256,
    string ScriptSha256, ReadOnlyMemory<byte> FormRuntimeBytes, uint LiveFlags, uint RuntimeWord,
    float DelayCounter, float SecondsPassed, NativeNvseSourceObject? Quest,
    IReadOnlyList<NativeNvseSourceObject> Contributors, IReadOnlyList<ReadOnlyMemory<byte>> ReferenceNames,
    IReadOnlyList<NativeNvseScriptVariable> VariableInfo, Action RequireCurrent);

internal static class FalloutNativePluginScriptObjects
{
    internal static NativeNvseScriptAuthority Bind(FalloutPluginStack records, FalloutPluginRecord script,
        Func<FalloutNativeScriptRuntimeFacts> runtime, Func<FalloutFormKey, NativeNvseSourceObject> form)
        => new Authority(records, script, runtime, form);

    private sealed class Authority : NativeNvseScriptAuthority
    {
        private readonly FalloutPluginStack _records;
        private readonly FalloutPluginRecord _script;
        private readonly Func<FalloutNativeScriptRuntimeFacts> _runtime;
        private readonly Func<FalloutFormKey, NativeNvseSourceObject> _form;
        private readonly IReadOnlyList<FalloutPluginSubrecord> _fields;
        private readonly byte[] _header, _code, _editor;
        private readonly byte[]? _text;
        private readonly string _runtimePath, _runtimeSha256;
        private readonly List<(FalloutFormKey? Form, uint Variable)> _references = [];
        private readonly List<NativeNvseScriptVariable> _variables = [];
        internal override string SourceOwner => _script.FormKey + ":" + _script.Plugin.Name + ":" + SourceSha256;
        internal override string SourceSha256 { get; }
        internal override string CodeSha256 { get; }

        internal Authority(FalloutPluginStack records, FalloutPluginRecord script,
            Func<FalloutNativeScriptRuntimeFacts> runtime, Func<FalloutFormKey, NativeNvseSourceObject> form)
        {
            _records = records; _script = script; _runtime = runtime; _form = form;
            if (script.Signature != "SCPT" || script.IsDeleted || !ReferenceEquals(records.GetEffective(script.FormKey), script))
                throw new InvalidDataException("Native Script is not the actual winning live SCPT.");
            _runtimePath = records.OwnedSource?.FalloutExecutablePath ?? throw new NotSupportedException("Native Script has no exact selected runtime.");
            using (var executable = new FileStream(_runtimePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                _runtimeSha256 = Convert.ToHexString(SHA256.HashData(executable));
            _fields = script.ReadSubrecords().ToArray();
            _header = _fields.Single(field => field.Signature == "SCHR").Data.ToArray();
            _code = FalloutNativePluginSourceDeclarations.Body(_fields, _header);
            _editor = SourceString(_fields.Single(field => field.Signature == "EDID").Data.Span);
            var texts = _fields.Where(field => field.Signature == "SCTX").ToArray();
            if (texts.Length > 1) throw new InvalidDataException("Native Script diagnostic text is ambiguous.");
            _text = texts.Length == 0 ? null : SourceString(texts[0].Data.Span);
            SourceSha256 = Convert.ToHexString(SHA256.HashData(script.ReadData()));
            CodeSha256 = Convert.ToHexString(SHA256.HashData(_code));
            FalloutPluginSubrecord? slot = null;
            foreach (var field in _fields)
            {
                if (field.Signature is "SCRO" or "SCRV")
                {
                    if (field.Data.Length != 4) throw new InvalidDataException("Native reference list has an incomplete UInt32 declaration.");
                    var id = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                    _references.Add(field.Signature == "SCRO" ? (id == 0 ? null : script.Plugin.AdjustFormId(id), 0U) : (null, id));
                }
                if (field.Signature == "SLSD")
                {
                    if (slot is not null || field.Data.Length != 24) throw new InvalidDataException("Native variable scalar/name order is invalid.");
                    slot = field;
                }
                if (field.Signature == "SCVR")
                {
                    if (slot is not { } declared) throw new InvalidDataException("Native variable name lacks its source scalar declaration.");
                    _variables.Add(new(declared.Data.ToArray(), SourceString(field.Data.Span))); slot = null;
                }
            }
            if (slot is not null) throw new InvalidDataException("Native Script variable has no complete source name.");
            RequireCurrent();
        }

        internal override NativeNvseScriptSnapshot Read()
        {
            RequireCurrent(); var facts = _runtime(); facts.RequireCurrent();
            if (string.IsNullOrWhiteSpace(facts.SourceOwner) || !StringComparer.OrdinalIgnoreCase.Equals(facts.ScriptSha256, SourceSha256) ||
                !StringComparer.OrdinalIgnoreCase.Equals(facts.RuntimeSha256, _runtimeSha256) || facts.ReferenceNames.Count != _references.Count ||
                facts.VariableInfo.Count != _variables.Count)
                throw new InvalidDataException("Native Script construction facts differ from the actual selected script/runtime.");
            for (var entry = 0; entry < _variables.Count; ++entry)
            {
                var declared = _variables[entry]; var actual = facts.VariableInfo[entry];
                if (actual.ScalarBytes.Length != 24 || !actual.Name.Span.SequenceEqual(declared.Name.Span) ||
                    !actual.ScalarBytes.Span[..4].SequenceEqual(declared.ScalarBytes.Span[..4]) || actual.ScalarBytes.Span[16] != declared.ScalarBytes.Span[16])
                    throw new InvalidDataException("Actual VarInfo constructor fields differ from ordered original identity/name/flag declarations.");
            }
            return new(_records.RuntimeFormId(_script.FormKey), facts.LiveFlags, facts.FormRuntimeBytes,
                _header, _text is null ? null : new ReadOnlyMemory<byte>(_text), _code, facts.RuntimeWord, facts.DelayCounter,
                facts.SecondsPassed, facts.Quest, facts.Contributors,
                _references.Select((row, index) => new NativeNvseScriptReference(facts.ReferenceNames[index],
                    row.Form is { } key ? _form(key) : null, row.Variable)).ToArray(), facts.VariableInfo, _editor, facts.SourceOwner);
        }
        internal override void RequireCurrent()
        {
            if (!_script.Plugin.NativeSourceAvailable || !ReferenceEquals(_records.GetEffective(_script.FormKey), _script) ||
                !StringComparer.OrdinalIgnoreCase.Equals(_records.OwnedSource?.FalloutExecutablePath, _runtimePath))
                throw new InvalidOperationException("Native Script winning record/reader has retired or changed.");
        }
        internal override IDisposable RetainSource()
        {
            var lease = new FileStream(_script.Plugin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            FileStream? executable = null;
            try
            {
                executable = new FileStream(_runtimePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(executable)), _runtimeSha256))
                    throw new InvalidDataException("Native Script runtime source changed before its retained publication.");
                RequireCurrent(); return new SourceLease(lease, executable);
            }
            catch { executable?.Dispose(); lease.Dispose(); throw; }
        }
        private sealed class SourceLease(FileStream record, FileStream executable) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return; _disposed = true;
                try { record.Dispose(); } finally { executable.Dispose(); }
            }
        }
        private static byte[] SourceString(ReadOnlySpan<byte> bytes)
        {
            if (!bytes.IsEmpty && bytes[^1] == 0) bytes = bytes[..^1];
            if (bytes.Contains((byte)0)) throw new InvalidDataException("Native source string has an interior terminator.");
            return bytes.ToArray();
        }
    }
}
