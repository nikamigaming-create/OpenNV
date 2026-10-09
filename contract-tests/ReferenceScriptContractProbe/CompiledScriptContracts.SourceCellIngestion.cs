using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunSourceCellIngestion()
    {
        const string engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-cell-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "Bytecode.esm");
            var last = Path.Combine(directory, "Override.esp");
            File.WriteAllBytes(first, Join(Tes4(), Record("NPC_", 7, Field("EDID", Text("AuthoredPlayerBase"))),
                Record("STAT", 0x90, Field("EDID", Text("AuthoredBase"))), Record("CELL", 0x800, Field("DATA", [1])),
                SourceCellGroup(6, 0x800, SourceCellGroup(8, 0x800, SourceCellReference(0xa50, true)),
                    SourceCellGroup(9, 0x800, SourceCellReference(0xa10, false)))));
            File.WriteAllBytes(last, Join(Tes4("Bytecode.esm"), Record("CELL", 0x800, Field("DATA", [1])),
                SourceCellGroup(6, 0x800, SourceCellGroup(8, 0x800, SourceCellReference(0xa50, true)),
                    SourceCellGroup(9, 0x800, SourceCellReference(0xa10, false), SourceCellReference(0x01000900, false)))));
            var hashes = new[] { SHA256.HashData(File.ReadAllBytes(first)), SHA256.HashData(File.ReadAllBytes(last)) };
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Override.esp"]);
            var cellSource = new FalloutCellProcessSource(records);
            var cell = Key(0x800);
            var actorSource = FalloutCombatGroupDeclaration.ForExecutable(engine);
            var player = FalloutCombatActorSource.Read(records, actorSource, records.RuntimeFormKey(0x14));
            FalloutCellProcessReference Reference(FalloutFormKey key) => cellSource.Read(cell).References.Single(row => row.Reference == key);
            var declarations = FalloutSourceCellLoaderDeclaration.Read(engine);
            var linkDeclaration = FalloutSourceCellReferenceLinksDeclaration.ForExecutable(engine);
            var prepared = Task.Run(() =>
            {
                var reader = new FalloutSourceCellReferenceIngestion(declarations, records, "authored-source-cell-stack");
                var evidence = reader.ReadEvidence(cellSource.Read(cell));
                var links = new FalloutSourceCellReferenceLinks(linkDeclaration, "authored-source-cell-stack",
                    cellSource.Read, Reference, reader, runtimeActor: _ => player);
                return (Reader: reader, Links: links, Evidence: evidence);
            }).GetAwaiter().GetResult();
            Require(prepared.Evidence.Inputs.Select(row => row.Disposition).SequenceEqual([
                FalloutSourceCellIngestionDisposition.InsertPersistent,
                FalloutSourceCellIngestionDisposition.RetainPersistentMembership,
                FalloutSourceCellIngestionDisposition.SkipEarlierTemporaryProvider,
                FalloutSourceCellIngestionDisposition.InsertWinningTemporary,
                FalloutSourceCellIngestionDisposition.InsertWinningTemporary]),
                "Actual full-reader source stream changed eager same-parent retention or lazy winning-file selection.");
            try
            {
                Reject(() => prepared.Links.Dispose());
                var nativeRoot = new object();
                prepared.Links.PublishNativeOwner(nativeRoot); prepared.Reader.PublishNativeOwner(nativeRoot);
                prepared.Links.PublishNativeOwner(nativeRoot); prepared.Reader.PublishNativeOwner(nativeRoot);
                Reject(() => prepared.Links.PublishNativeOwner(new object()));
                Task.Run(() => Reject(() => prepared.Reader.PublishNativeOwner(nativeRoot))).GetAwaiter().GetResult();
                var before = prepared.Links.Read(cell);
                var third = new FalloutFormKey("Override.esp", 0x900);
                Require(before.OrderedReferences.Select(row => row.Reference).SequenceEqual([third, Key(0xa10), Key(0xa50)]),
                    "CELL head order was replaced by numeric IDs or final winning graph order.");
                var actualParent = (FalloutFormKey?)null;
                prepared.Links.RequireRuntimeActorParent(player, actualParent);
                prepared.Links.InsertRuntimeActor(player, actualParent, cell, () => actualParent = cell,
                    "authored-real-runtime-Player-ParentCELL-setter");
                prepared.Links.RequireRuntimeActorParent(player, actualParent);
                var saved = prepared.Links.Capture();
                Require(saved.Cells.Single().OrderedReferences.First().Reference == player.Reference &&
                    saved.Cells.Single().Members.Count == 3 && saved.Cells.Single().RuntimeActors?.Single().Source == player &&
                    !records.TryGetEffective(player.Reference, out _),
                    "Canonical Player acquired an invented ACHR or lost its genuine typed head membership.");
                var cold = Task.Run(() =>
                {
                    var reader = new FalloutSourceCellReferenceIngestion(declarations, records, "authored-source-cell-stack");
                    var links = new FalloutSourceCellReferenceLinks(linkDeclaration, "authored-source-cell-stack",
                        cellSource.Read, Reference, reader, saved, _ => player);
                    return (Reader: reader, Links: links);
                }).GetAwaiter().GetResult();
                try
                {
                    var coldRoot = new object(); cold.Links.PublishNativeOwner(coldRoot); cold.Reader.PublishNativeOwner(coldRoot);
                    Require(cold.Links.Read(cell).OrderedReferences.SequenceEqual(saved.Cells.Single().OrderedReferences) &&
                        cold.Links.RuntimeActorParent(player) == cell && cold.Links.Capture().Membership == saved.Membership,
                        "Cold canonical/source memberships were replayed, retargeted or reordered.");
                    using var state = JsonDocument.Parse(JsonSerializer.Serialize(cold.Reader.State));
                    Require(state.RootElement.GetProperty("calls").GetInt64() == 0,
                        "Cold source evidence authentication entered an initial insertion stream.");
                    var drift = saved with { Cells = saved.Cells.Select(row => row with { GraphSha256 = new('f', 64) }).ToArray() };
                    Reject(() => FalloutSourceCellReferenceLinks.Validate(drift));
                }
                finally { cold.Links.Dispose(); cold.Reader.Dispose(); }
                var unchanged = prepared.Links.Capture().Membership;
                try
                {
                    prepared.Links.InsertRuntimeActor(player, cell, cell, () => throw new IOException("authored-native-ParentCELL-failure"),
                        "authored-entered-player-rehead");
                }
                catch (IOException) { }
                Require(prepared.Links.SaveBlocker is not null && JsonSerializer.Serialize(prepared.Links.State)
                    .Contains("authored-native-ParentCELL-failure", StringComparison.Ordinal),
                    "Failed actual ParentCELL publication lost its inserted-head prefix.");
                Reject(() => prepared.Links.Read(cell)); Reject(() => prepared.Links.PublishNativeOwner(new object()));
                Require(unchanged == saved.Membership, "Cold authentication altered the warm membership counter.");
            }
            finally { prepared.Links.Dispose(); prepared.Reader.Dispose(); }
            Require(hashes[0].SequenceEqual(SHA256.HashData(File.ReadAllBytes(first))) &&
                hashes[1].SequenceEqual(SHA256.HashData(File.ReadAllBytes(last))), "Source CELL reader changed an authored input.");

            var source = FalloutSourceFrameTimer.Read(SandboxRuntimeReceipt(engine));
            var counter = new SourceCellAuthoredCounter();
            using var timer = new FalloutSourceFrameTimerState(source, counter, Guid.NewGuid());
            var reads = counter.Reads; timer.Resume();
            Require(counter.Reads == reads, "Zero-pause source resume performed an invented OS read.");
            for (var count = 0; count < 256; ++count) timer.Pause();
            Require(timer.Capture().PauseCount == 0, "Source pause byte did not preserve its actual wrap store.");
            Console.WriteLine("OPENNV_SOURCE_CELL_LIVE_PRODUCERS_PASS fullReader=true winningFileFilter=true persistentRetain=true " +
                "headOrder=true typedCanonicalPlayer=true actualThreadPublication=true coldNoReplay=true committedParentPrefix=true " +
                "sourceResumeZero=true sourcePauseWrap=true originalNativeAndWholeMain=UNEXECUTED");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static byte[] SourceCellGroup(int type, uint label, params byte[][] bodies)
    {
        var payload = Join(bodies); var value = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(value, 0); UInt(value, 4, (uint)value.Length);
        UInt(value, 8, label); UInt(value, 12, (uint)type); payload.CopyTo(value, 24); return value;
    }
    private static byte[] SourceCellReference(uint id, bool persistent)
    {
        var value = Record("REFR", id, Field("NAME", U32(0x90)), Field("DATA", new byte[24]));
        if (persistent) UInt(value, 8, 0x400); return value;
    }
    private sealed class SourceCellAuthoredCounter : IFalloutSourceTickCounter
    {
        internal int Reads { get; private set; }
        public string Owner => "authored-source-function-byte-boundary-counter";
        public uint Read() { ++Reads; return 1000; }
        public void Wait(uint milliseconds) => throw new InvalidOperationException("Source pause/resume contract must not wait.");
    }
}
