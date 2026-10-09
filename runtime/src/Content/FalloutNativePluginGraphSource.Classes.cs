using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginGraphSource
{
    // Source-backed class fields and child allocations are produced here.
    // The full-byte composer independently refuses any uncovered runtime
    // field; source metadata cannot stand in for a parser handle or child class.
    private sealed partial class ModAuthority : NativeNvseGraphDataAuthority
    {
        private readonly FalloutNativePluginGraphSource _source;
        private readonly FalloutPluginContext _mod;
        private readonly NativeNvseSourceGraphContext _context;
        private readonly IReadOnlyDictionary<string, NativeNvseSourceObject> _retained;
        private readonly byte[] _header, _name, _path;
        private readonly FalloutNativePluginGraphMetadata _metadata = new();
        private readonly uint _editor, _author, _description, _masterNames, _masterSizes, _masterArray;
        private readonly int _masterCount;
        internal override string SourceOwner => "actual-selected-plugin-reader:" + _mod.Plugin.Name + ":" + SourceSha256;
        internal override string SourceSha256 => _mod.Sha256;
        internal ModAuthority(FalloutNativePluginGraphSource source, FalloutPluginContext mod, NativeNvseSourceGraphContext context,
            IReadOnlyDictionary<string, NativeNvseSourceObject> retained)
        {
            _source = source; _mod = mod; _context = context; _retained = retained;
            _name = Ansi(mod.Plugin.Name); _path = Ansi(Path.GetDirectoryName(mod.Plugin.Path) ?? throw new InvalidDataException("Actual ModInfo path has no directory."));
            var fields = mod.Plugin.Records.Single(record => record.Signature == "TES4").ReadSubrecords().ToArray();
            var header = fields.Where(field => field.Signature == "HEDR").ToArray();
            if (header.Length != 1 || header[0].Data.Length != 12) throw new InvalidDataException("Native ModInfo has no exact original HEDR.");
            _header = header[0].Data.ToArray(); _editor = _metadata.CString(_name);
            _author = _metadata.Reserve(8); _description = _metadata.Reserve(8);
            _metadata.String(_author, OptionalString("CNAM")); _metadata.String(_description, OptionalString("SNAM"));
            var names = new List<uint>(); var sizes = new List<uint>(); FalloutPluginSubrecord? pending = null;
            foreach (var field in fields)
            {
                if (field.Signature == "MAST")
                {
                    if (pending is not null) throw new InvalidDataException("Native ModInfo master lacks its original size declaration.");
                    pending = field;
                }
                else if (field.Signature == "DATA" && pending is { } master)
                {
                    if (field.Data.Length != 8) throw new InvalidDataException("Native ModInfo master size is not UInt64.");
                    var text = FalloutNativePluginSourceDeclarations.SourceString(master.Data.Span);
                    if (names.Count >= mod.Plugin.Masters.Count || !text.AsSpan().SequenceEqual(Ansi(mod.Plugin.Masters[names.Count])))
                        throw new InvalidDataException("Native ModInfo master identity/order differs from the actual reader.");
                    names.Add(_metadata.CString(text)); var at = _metadata.Reserve(8); _metadata.Put(at, field.Data.Span); sizes.Add(at); pending = null;
                }
            }
            if (pending is not null || names.Count != mod.Plugin.Masters.Count) throw new InvalidDataException("Native ModInfo master declarations are incomplete.");
            _masterCount = names.Count; _masterNames = _metadata.List(names, relativeTargets: true); _masterSizes = _metadata.List(sizes, relativeTargets: true);
            _masterArray = _masterCount == 0 ? 0 : _metadata.Reserve(checked(_masterCount * 4));
            RequireCurrent();
            byte[] OptionalString(string signature)
            {
                var rows = fields.Where(field => field.Signature == signature).ToArray();
                if (rows.Length > 1) throw new InvalidDataException("Native ModInfo has competing source strings: " + signature);
                return rows.Length == 0 ? [] : FalloutNativePluginSourceDeclarations.SourceString(rows[0].Data.Span);
            }
        }
        internal override NativeNvseSourceGraphMetadata Metadata(uint basis)
        {
            RequireCurrent();
            for (var index = 0; index < _masterCount; ++index)
                _metadata.Word(checked(_masterArray + (uint)index * 4), Get(_source.Master(_mod.Plugin.Masters[index])).Address);
            return new(_editor, _metadata.Bytes(basis));
        }
        internal override NativeNvseDataSnapshot Read()
        {
            RequireCurrent(); var basis = _context.Metadata(Mod(_mod)); var bytes = Metadata(basis).Bytes;
            var fields = new List<NativeNvseDataField>
            {
                Fixed(0x20, 260, _name, "actual-source-filename"), Fixed(0x124, 260, _path, "actual-source-directory"),
                new(0x3dc, _header, "original-TES4-HEDR"), new(0x3ec, bytes.AsMemory(checked((int)_masterNames), 8), "original-ordered-MAST-names"),
                new(0x3f4, bytes.AsMemory(checked((int)_masterSizes), 8), "original-ordered-MAST-DATA-sizes"),
                FalloutNativePluginGraphMetadata.WordField(0x3fc, checked((uint)_masterCount), "actual-selected-master-reader-count"),
                FalloutNativePluginGraphMetadata.WordField(0x400, _masterCount == 0 ? 0 : checked(basis + _masterArray), "actual-native-master-reader-array"),
                FalloutNativePluginGraphMetadata.ByteField(0x40c, checked((byte)_mod.LoadOrderIndex), "actual-source-runtime-namespace-index"),
                new(0x410, bytes.AsMemory(checked((int)_author), 8), "original-source-author"),
                new(0x418, bytes.AsMemory(checked((int)_description), 8), "original-source-description"),
            };
            fields.AddRange(LoadedFileFields());
            // Status/alias/map/BSFile, buffer/current-group cursors,
            // find-data, loaded flags and the remaining lifecycle fields need
            // their real original-compatible reader owner. Deliberately leave
            // them uncovered: constructor zero is not loaded reader state.
            return new(NativeNvseSourceClass.ModInfo, 0, 0x42c, "source-ModInfo-0x42c/reader-fields-unowned", fields,
                _name, _mod.Plugin.Masters.Select(name => Get(_source.Master(name))).ToArray());
        }
        private NativeNvseSourceObject Get(FalloutPluginContext context) => Object(Mod(context), _context, _retained);
        internal override void RequireCurrent()
        {
            if (!_source._records.Plugins.Contains(_mod) || !_mod.Plugin.NativeSourceAvailable)
                throw new InvalidOperationException("Actual native contributor reader has retired.");
        }
        internal override IDisposable RetainSource() => RetainModLoadedSource();
    }

    private sealed class QuestAuthority : NativeNvseGraphDataAuthority
    {
        private readonly FalloutNativePluginGraphSource _source;
        private readonly FalloutPluginRecord _quest;
        private readonly NativeNvseSourceGraphContext _context;
        private readonly IReadOnlyDictionary<FalloutFormKey, NativeNvseLocalContext> _events;
        private readonly IReadOnlyDictionary<string, NativeNvseSourceObject> _retained;
        private readonly FalloutPluginSubrecord[] _fields;
        private readonly FalloutNativePluginGraphMetadata _metadata = new();
        private readonly byte[] _editor;
        private readonly uint _editorText, _editorString, _icon, _fullName, _contributors;
        private readonly object _state;
        internal override string SourceOwner => "actual-campaign-quest:" + _quest.FormKey + ":" + SourceSha256;
        internal override string SourceSha256 { get; }
        internal QuestAuthority(FalloutNativePluginGraphSource source, FalloutPluginRecord quest, NativeNvseSourceGraphContext context,
            IReadOnlyDictionary<FalloutFormKey, NativeNvseLocalContext> events, IReadOnlyDictionary<string, NativeNvseSourceObject> retained)
        {
            _source = source; _quest = quest; _context = context; _events = events; _retained = retained;
            _fields = quest.ReadSubrecords().ToArray(); SourceSha256 = Convert.ToHexString(SHA256.HashData(quest.ReadData()));
            _state = source._quests.NativeLocalIdentity(quest.FormKey); _editor = String("EDID", required: true);
            _editorText = _metadata.CString(_editor); _editorString = _metadata.Reserve(8); _metadata.String(_editorString, _editor);
            _icon = _metadata.Reserve(8); _metadata.String(_icon, String("ICON"));
            _fullName = _metadata.Reserve(8); _metadata.String(_fullName, String("FULL"));
            _contributors = _metadata.List(new uint[source.Contributors(quest).Count]);
            RequireCurrent();
        }
        internal override NativeNvseSourceGraphMetadata Metadata(uint basis)
        {
            RequireCurrent();
            _metadata.PointerListValues(_contributors, _source.Contributors(_quest).Select(value => Get(Mod(value)).Address).ToArray());
            return new(_editorText, _metadata.Bytes(basis));
        }
        internal override NativeNvseDataSnapshot Read()
        {
            RequireCurrent(); var attached = FalloutScriptLocals.AttachedScript(_source._records, _quest);
            var data = _fields.Where(field => field.Signature == "DATA").ToArray();
            if (data.Length != 1 || data[0].Data.Length is not (2 or 8)) throw new InvalidDataException("Native Quest DATA is absent, ambiguous or malformed.");
            if (_source._quests.IsCompleted(_quest.FormKey))
                throw new NotSupportedException("Completed native Quest flags need their exact selected completion transition owner.");
            var currentStage = _source._quests.Stage(_quest.FormKey);
            if (currentStage is < 0 or > byte.MaxValue) throw new NotSupportedException("Actual Quest stage is outside the native UInt8 field owner.");
            var basis = _context.Metadata(Form(_quest.FormKey)); var bytes = Metadata(basis).Bytes; var dependencies = new List<NativeNvseSourceObject>();
            var flags = checked((byte)((data[0].Data.Span[0] & ~1) | (_source._quests.IsRunning(_quest.FormKey) ? 1 : 0)));
            var fields = new List<NativeNvseDataField>
            {
                FalloutNativePluginGraphMetadata.ByteField(4, 71, "actual-source-QUST-class"),
                new(5, _context.AllocationBytes(Form(_quest.FormKey), 5, 3), "actual-native-allocation-unspecified-TESForm-padding"),
                FalloutNativePluginGraphMetadata.WordField(8, _source.LoadedFlags(_quest), "original-ordered-source-form-flags"),
                FalloutNativePluginGraphMetadata.WordField(12, _source._records.RuntimeFormId(_quest.FormKey), "actual-source-runtime-FormID"),
                new(16, bytes.AsMemory(checked((int)_contributors), 8), "actual-source-contributor-reader-list"),
                FalloutNativePluginGraphMetadata.WordField(28, attached is null ? 0 : Get(Form(attached.FormKey)).Address, "actual-resolved-attached-script"),
                FalloutNativePluginGraphMetadata.ByteField(32, attached is null ? (byte)0 : (byte)1, "actual-source-SCRI-resolution"),
                new(33, _context.AllocationBytes(Form(_quest.FormKey), 33, 3), "actual-native-allocation-unspecified-scriptable-padding"),
                new(40, bytes.AsMemory(checked((int)_icon), 8), "original-source-ICON"), new(52, bytes.AsMemory(checked((int)_fullName), 8), "original-source-FULL"),
                FalloutNativePluginGraphMetadata.ByteField(60, flags, "actual-campaign-running/source-flags"),
                FalloutNativePluginGraphMetadata.ByteField(61, data[0].Data.Span[1], "original-source-priority"),
                new(62, _context.AllocationBytes(Form(_quest.FormKey), 62, 2), "actual-native-allocation-unspecified-Quest-padding"),
                FalloutNativePluginGraphMetadata.WordField(64, BitConverter.SingleToUInt32Bits(FalloutQuestScriptInitialization.ProcessingDelay(_quest)), "actual-source-processing-delay"),
                FalloutNativePluginGraphMetadata.WordField(92, attached is null ? 0 : EventList().EventList, "actual-campaign-native-event-list"),
                FalloutNativePluginGraphMetadata.ByteField(96, checked((byte)currentStage), "actual-campaign-current-stage"),
                new(97, _context.AllocationBytes(Form(_quest.FormKey), 97, 3), "actual-native-allocation-unspecified-stage-padding"),
                new(100, bytes.AsMemory(checked((int)_editorString), 8), "original-source-editor-name"),
            };
            // A genuinely empty source collection has the native empty head.
            // A declared child is never replaced with an empty successful list.
            EmptyHead(68, "INDX", "QuestStageInfo/QuestStageItem"); EmptyHead(76, "QOBJ", "BGSQuestObjective/VariableInfo union");
            EmptyHead(84, "CTDA", "native Condition parameter/reference projection");
            foreach (var contributor in _source.Contributors(_quest)) dependencies.Add(Get(Mod(contributor)));
            if (attached is not null) dependencies.Add(Get(Form(attached.FormKey)));
            return new(NativeNvseSourceClass.Quest, _source._records.RuntimeFormId(_quest.FormKey), 108,
                "source-TESQuest-0x6c/actual-campaign-components", fields, _editor, dependencies);
            void EmptyHead(int at, string signature, string owner)
            {
                if (_fields.Any(field => field.Signature == signature))
                    throw new NotSupportedException("Actual native Quest declares " + signature + "; its complete " + owner + " child owner is absent.");
                fields.Add(new(at, new byte[8], "actual-source-absent-" + signature + "/empty-native-list"));
            }
            NativeNvseLocalContext EventList()
            {
                if (!_events.TryGetValue(_quest.FormKey, out var events) || events.Retired || events.EventList == 0)
                    throw new NotSupportedException("Actual scripted Quest has no prepared campaign native event list.");
                return events;
            }
        }
        private NativeNvseSourceObject Get(string key) => Object(key, _context, _retained);
        private byte[] String(string signature, bool required = false)
        {
            var rows = _fields.Where(field => field.Signature == signature).ToArray();
            if (rows.Length > 1 || required && rows.Length == 0) throw new InvalidDataException("Native Quest source string is absent or ambiguous: " + signature);
            return rows.Length == 0 ? [] : FalloutNativePluginSourceDeclarations.SourceString(rows[0].Data.Span);
        }
        internal override void RequireCurrent()
        {
            _source._declarations.Current(_quest);
            if (!ReferenceEquals(_source._quests.NativeLocalIdentity(_quest.FormKey), _state)) throw new InvalidOperationException("Native Quest changed its actual campaign state owner.");
        }
        internal override IDisposable RetainSource() => SourceLease.Open(_quest.Plugin.Path,
            _source._records.OwnedSource!.FalloutExecutablePath, _source._construction.Value.RuntimeSha256, RequireCurrent);
    }

    private static byte[] Ansi(string text)
    {
        if (text.Any(character => character is > (char)127 or '\0'))
            throw new NotSupportedException("Native filename/directory needs its actual selected ANSI locale conversion owner.");
        return Encoding.ASCII.GetBytes(text);
    }
    private static NativeNvseDataField Fixed(int offset, int capacity, ReadOnlySpan<byte> value, string owner)
    {
        if (value.Length >= capacity) throw new NotSupportedException("Actual native source path exceeds its declared class buffer.");
        // A terminated source prefix does not establish unused buffer tails
        // or plugin-added fields that occupy them. Keep those fields uncovered.
        var bytes = new byte[checked(value.Length + 1)]; value.CopyTo(bytes); return new(offset, bytes, owner);
    }
}
