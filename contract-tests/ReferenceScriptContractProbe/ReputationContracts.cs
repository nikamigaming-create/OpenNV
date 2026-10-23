using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ReputationContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Reputation.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("REPU", 1, Field("EDID", Text("FirstRep")), Field("DATA", BitConverter.GetBytes(100f))),
                Record("REPU", 2, Field("DATA", BitConverter.GetBytes(200f))),
                Record("REPU", 3, Field("DATA", new byte[3])),
                Record("REPU", 4, Field("DATA", BitConverter.GetBytes(float.NaN))),
                Record("FACT", 5), Record("REPU", 6),
                Record("ACTI", 7, Field("SCRI", BitConverter.GetBytes(8u))),
                Record("SCPT", 8, Field("SCHR", new byte[20]), Field("SCRO", BitConverter.GetBytes(1u)),
                    Field("SCTX", Text("begin OnActivate\nSetReputation FirstRep 1 -25.25\n" +
                        "SetReputation FirstRep 0 250\nif GetReputation FirstRep 1 != -25.25\nUnownedBadCommand\nendif\n" +
                        "if GetReputation FirstRep 0 != 250\nUnownedBadCommand\nendif\nend"))),
                Record("SCPT", 9, Field("SCHR", new byte[20]), Field("SCRO", BitConverter.GetBytes(1u)),
                    Field("SCTX", Text("begin OnActivate\nAddReputation FirstRep 1 5\nend"))),
                Record("ACTI", 10, Field("SCRI", BitConverter.GetBytes(9u))),
                Record("SCPT", 11, Field("SCHR", new byte[20]), Field("SCRO", BitConverter.GetBytes(1u)),
                    Field("SCTX", Text("begin OnActivate\nRemoveReputation FirstRep 0 5\nend"))),
                Record("ACTI", 12, Field("SCRI", BitConverter.GetBytes(11u))),
                Record("QUST", 13, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(14u))),
                Record("SCPT", 14, Field("SCHR", QuestHeader()), Field("SCRO", BitConverter.GetBytes(1u)),
                    Field("SCTX", Text("begin GameMode\nif GetReputation FirstRep 1 != -25.25\nUnownedBadCommand\nendif\n" +
                        "SetReputation FirstRep 0 -8.5\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Record("REFR", 0x90, Field("NAME", BitConverter.GetBytes(7u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x91, Field("NAME", BitConverter.GetBytes(10u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x92, Field("NAME", BitConverter.GetBytes(12u)), Field("DATA", new byte[24])))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Reputation.esm"]);
            using var world = new FalloutReferenceWorld(records);
            Require(world.Reputation(Key(1), 0) == 0 && world.Reputation(Key(1), 1) == 0, "Fresh reputation was not source zero.");
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x80)));
            var effects = 0;
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => ++effects));
            Require(scripts.Activate(Key(0x90), records.RuntimeFormKey(0x14)).Error is null &&
                world.Reputation(Key(1), 1) == -25.25f && world.Reputation(Key(1), 0) == 250 && effects == 0,
                "Actual Set/GetReputation script path clamped, selected the wrong axis or emitted Add feedback.");
            Require(world.Reputation(Key(2), 0) == 0, "A reputation mutation changed its sibling.");
            var capture = RoundTrip(world.CaptureReputations());
            foreach (var (reference, command) in new[] { (0x91u, "AddReputation"), (0x92u, "RemoveReputation") })
            {
                var result = scripts.Activate(Key(reference), records.RuntimeFormKey(0x14));
                Require(result.Error?.Contains(command, StringComparison.OrdinalIgnoreCase) == true &&
                    JsonSerializer.Serialize(world.CaptureReputations()) == JsonSerializer.Serialize(capture),
                    "Unbound reputation effect was hidden by the independent setter or partly changed reputation.");
            }
            using var precision = new FalloutReferenceWorld(records);
            precision.SetReputation(Key(2), 1, 1.1);
            Require(precision.Reputation(Key(2), 1) == (float)1.1, "Setter did not use source float precision.");
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreReputations(capture);
            Require(JsonSerializer.Serialize(cold.CaptureReputations()) == JsonSerializer.Serialize(capture) &&
                cold.Reputation(Key(1), 0) == 250 && cold.Reputation(Key(1), 1) == -25.25f && cold.Reputation(Key(2), 1) == 0,
                "Cold reputation lost exact values or the fresh baseline.");
            foreach (var form in new uint[] { 3, 4, 5, 6 }) Reject(() => world.SetReputation(Key(form), 1, 5));
            foreach (var amount in new[] { double.NaN, double.PositiveInfinity, double.MaxValue })
                Reject(() => world.SetReputation(Key(1), 1, amount));
            foreach (var axis in new[] { -1d, .5, 2, double.NaN }) Reject(() => world.SetReputation(Key(1), axis, 1));
            Require(JsonSerializer.Serialize(world.CaptureReputations()) == JsonSerializer.Serialize(capture), "Rejected write mutated reputation.");
            foreach (var invalid in new[]
            {
                capture with { Values = [capture.Values[0], capture.Values[0]] },
                capture with { Values = [capture.Values[0] with { SourceSha256 = new string('0', 64) }] },
                capture with { Values = [capture.Values[0] with { Positive = float.NaN }] },
                capture with { Values = [capture.Values[0] with { Positive = null, Negative = null }] },
                capture with { Values = [capture.Values[0] with { Form = Key(5) }] },
            })
            {
                Reject(() => cold.RestoreReputations(invalid));
                Require(JsonSerializer.Serialize(cold.CaptureReputations()) == JsonSerializer.Serialize(capture), "Rejected restore partly mutated reputation.");
            }
            var quests = new FalloutQuestState(records);
            var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                defaultProcessingDelay: 0, references: world);
            questScripts.Advance(.1);
            Require(questScripts.Capture().Instances.Single().Error is null &&
                world.Reputation(Key(1), 0) == -8.5f && cold.Reputation(Key(1), 0) == 250,
                "Quest Get/SetReputation did not use the shared object-script owner or changed a cold sibling.");
            cold.RestoreReputations(null);
            Reject(() => cold.Reputation(Key(1), 0)); Reject(() => cold.Reputation(Key(1), 1));
            cold.SetReputation(Key(1), 1, 12.125);
            Require(cold.Reputation(Key(1), 1) == 12.125f, "Explicit legacy setter did not establish its own axis.");
            Reject(() => cold.Reputation(Key(1), 0)); Reject(() => cold.Reputation(Key(2), 1));
            using var legacyCold = new FalloutReferenceWorld(records);
            legacyCold.RestoreReputations(RoundTrip(cold.CaptureReputations()));
            Require(legacyCold.Reputation(Key(1), 1) == 12.125f && !legacyCold.CaptureReputations().FreshBaseline,
                "Cold legacy state invented a fresh reputation baseline.");
            Reject(() => legacyCold.Reputation(Key(1), 0));
            var old = JsonSerializer.Deserialize<FalloutNativeCampaignState>("{}")! with
            { Schema = FalloutNativeCampaignSave.BeforeReputationSchema, References = [] };
            var migrated = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(old);
            Require(migrated.Schema == FalloutNativeCampaignSave.ExpectedSchema && migrated.Reputations is null,
                "Previous campaign schema invented reputation history.");
            Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(old with { Reputations = capture }));
            var current = migrated with { Reputations = capture };
            Require(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current).Reputations == capture,
                "Current campaign admission changed reputation state.");
            Console.WriteLine("OPENNV_REPUTATION_STATE_PASS sourceZero=true actualScriptGetSet=true questAndObjectShared=true setterUnclamped=true " +
                "axisAndSiblingIsolation=true cold=true legacyUnknown=true explicitAxisRecovery=true invalidRestoreAtomic=true " +
                "oldSchemaInjectionRefused=true addRemoveCrimeAndNotification=unbound ordinaryAndRetail=unverified");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutReputationsSnapshot RoundTrip(FalloutReputationsSnapshot value) =>
        JsonSerializer.Deserialize<FalloutReputationsSnapshot>(JsonSerializer.Serialize(value))!;
    private static FalloutFormKey Key(uint value) => new("Reputation.esm", value);
    private static byte[] QuestHeader() { var bytes = new byte[20]; bytes[16] = 1; return bytes; }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] values)
    {
        var data = Join(values); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid or unknown reputation was admitted.");
    }
}
