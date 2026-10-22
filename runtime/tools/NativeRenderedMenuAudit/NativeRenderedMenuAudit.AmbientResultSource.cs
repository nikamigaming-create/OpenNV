using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;

public partial class NativeRenderedMenuAudit
{
    // Entirely original test records, never copied from an owned plugin. The
    // actual participants and body resources still come from the read-only game.
    private sealed class AmbientResultSource : IDisposable
    {
        internal const string TopicEditorId = "OpenNVAmbientFailureProbeTopic";
        private const string QuestEditorId = "OpenNVAmbientFailureProbeQuest";
        private const uint QuestId = 0x01000800, ScriptId = 0x01000801, TopicId = 0x01000802, InfoId = 0x01000803;
        private readonly string _path;
        private readonly IReadOnlyDictionary<string, byte[]> _ownedHashes;
        private readonly byte[] _syntheticHash;
        private bool _disposed;
        internal string Root { get; }
        internal string Name { get; }
        internal FalloutFormKey Quest { get; }

        internal AmbientResultSource(IReadOnlyList<FalloutPluginSource> owned)
        {
            _ownedHashes = owned.ToDictionary(source => source.AbsolutePath, source => HashFile(source.AbsolutePath), StringComparer.OrdinalIgnoreCase);
            var bytes = Build();
            _syntheticHash = SHA256.HashData(bytes);
            Name = "OpenNVAmbientResultProbe.esp";
            Root = Directory.CreateTempSubdirectory("opennv-ambient-result-").FullName;
            _path = Path.GetFullPath(Path.Combine(Root, Name));
            Quest = new(Name, QuestId & 0x00ffffff);
            var created = false;
            try
            {
                using var file = new FileStream(_path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None);
                created = true;
                file.Write(bytes);
            }
            catch
            {
                DeleteOwnedFile(created);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_syntheticHash.AsSpan().SequenceEqual(HashFile(_path)) ||
                    _ownedHashes.Any(pair => !pair.Value.AsSpan().SequenceEqual(HashFile(pair.Key))))
                    throw new InvalidDataException("Ambient result fixture changed synthetic or owned plugin bytes.");
                GD.Print($"OPENNV_NATIVE_AMBIENT_RESULT_SOURCE_UNCHANGED ownedPlugins={_ownedHashes.Count} " +
                    $"syntheticPlugin=true originalRecords=true syntheticSha256={Convert.ToHexString(_syntheticHash)}");
            }
            finally { DeleteOwnedFile(); }
        }

        private void DeleteOwnedFile(bool deleteFile = true)
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directory = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(directory), temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(directory).StartsWith("opennv-ambient-result-", StringComparison.Ordinal) ||
                !string.Equals(Path.GetDirectoryName(_path), directory, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(_path) != Name)
                throw new InvalidOperationException("Ambient fixture cleanup path escaped its original temporary file.");
            if (deleteFile) File.Delete(_path);
            Directory.Delete(directory, recursive: false);
        }

        private static byte[] HashFile(string path)
        {
            using var file = File.OpenRead(path);
            return SHA256.HashData(file);
        }

        private static byte[] Build()
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 0x804);
            var scriptHeader = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(scriptHeader.AsSpan(12), 1);
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
            var response = new byte[24]; response[12] = 1;
            var info = Record("INFO", InfoId, Field("DATA", [1, 0, 0, 0]), Field("QSTI", U32(QuestId)),
                Field("TRDT", response), Field("NAM1", Text("Original ambient result failure fixture.")),
                Field("SCRO", U32(QuestId)),
                Field("SCTX", Text($"set {QuestEditorId}.Counter to {QuestEditorId}.Counter + 1")));
            var group = new byte[24 + info.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), TopicId);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 7);
            info.CopyTo(group, 24);
            return Join(Record("TES4", 0, Field("HEDR", header), Field("MAST", Text("FalloutNV.esm")), Field("DATA", new byte[8])),
                Record("SCPT", ScriptId, Field("EDID", Text("OpenNVAmbientFailureProbeScript")), Field("SCHR", scriptHeader),
                    Field("SLSD", local), Field("SCVR", Text("Counter")), Field("SCTX", Text("short Counter"))),
                Record("QUST", QuestId, Field("EDID", Text(QuestEditorId)), Field("DATA", [1, 100]), Field("SCRI", U32(ScriptId))),
                Record("DIAL", TopicId, Field("EDID", Text(TopicEditorId)), Field("DATA", [1, 0]), Field("PNAM", BitConverter.GetBytes(50f))), group);
        }

        private static byte[] U32(uint value) => BitConverter.GetBytes(value);
        private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
        private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        private static byte[] Field(string signature, byte[] data)
        {
            var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6);
            return bytes;
        }
        private static byte[] Record(string signature, uint id, params byte[][] fields)
        {
            var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24);
            return bytes;
        }
    }
}
