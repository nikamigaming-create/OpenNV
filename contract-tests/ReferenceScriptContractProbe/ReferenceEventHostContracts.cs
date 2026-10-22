using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ReferenceEventHostContracts
{
    internal static void Run()
    {
        const string source = "if GetDisabled == 0\n" +
            "if GetCurrentTime > 20 || GetCurrentTime < 6\n" +
            "if IsAnimPlaying Left == 0\nPlayGroup Left 0\nendif\n" +
            "elseif GetCurrentTime > 6 && GetCurrentTime < 20\n" +
            "if IsAnimPlaying Right == 0\nPlayGroup Right 0\nendif\nendif\nendif";
        using (var fixture = new Fixture(source))
        {
            var hourForm = FalloutGameTimeBindings.Read(fixture.Records).Hour;
            var globals = new FalloutGlobalState([new(hourForm, "SyntheticHour", (byte)'f', 12, "synthetic-hour")]);
            var groups = new Dictionary<FalloutFormKey, string>();
            var calls = new List<(FalloutFormKey Reference, string Group, int Initialization)>();
            var host = RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) =>
                throw new NotSupportedException("No furniture owner in this fixture."),
                _ => throw new InvalidDataException("Clock script emitted an effect."), Globals: globals,
                PlayGroup: (reference, group, initialization) =>
                {
                    calls.Add((reference, group, initialization)); groups[reference] = group;
                }, IsAnimPlaying: (reference, group) => groups.GetValueOrDefault(reference) == group), null);
            Require(host.Inventory is null && host.Challenges is null && host.TagSkills is null,
                "An absent gameplay host fabricated eager command owners.");
            var scripts = new FalloutReferenceScripts(fixture.Records, fixture.World, new(fixture.Records), host);
            void Tick(uint id = 0x90)
            {
                var result = scripts.Dispatch(fixture.Key(id), "GameMode");
                Require(result.Error is null, "Source clock script failed: " + result.Error);
            }
            Tick(); Tick();
            Require(calls.SequenceEqual([(fixture.Key(0x90), "Right", 0)]), "Daytime did not select Right exactly once.");
            globals.Set(hourForm, 22); Tick(); Tick();
            Require(calls.Count == 2 && calls[1] == (fixture.Key(0x90), "Left", 0),
                "Nighttime did not select Left exactly once through the shared host.");
            globals.Set(hourForm, 6); Tick(); globals.Set(hourForm, 20); Tick();
            Require(calls.Count == 2, "Boundary hours invented a selection.");
            globals.Set(hourForm, 22);
            fixture.World.SetEnabled(fixture.Key(0x92), false);
            Require(fixture.World.IsEnabled(fixture.Key(0x92)), "Queued disable changed applied state before the world update.");
            fixture.World.AdvanceEnableChanges(0, new(1, 1), _ =>
                throw new InvalidDataException("Nonfading enable change requested a native fade owner."));
            Require(!fixture.World.IsEnabled(fixture.Key(0x92)), "World update did not apply the queued disable.");
            Tick(0x92);
            Require(calls.Count == 2 && !groups.ContainsKey(fixture.Key(0x92)), "Disabled source guard played an animation.");
            fixture.World.SetEnabled(fixture.Key(0x92), true);
            Require(!fixture.World.IsEnabled(fixture.Key(0x92)), "Queued enable changed applied state before the world update.");
            fixture.World.AdvanceEnableChanges(0, new(1, 1), _ =>
                throw new InvalidDataException("Nonfading enable change requested a native fade owner."));
            Require(fixture.World.IsEnabled(fixture.Key(0x92)), "World update did not apply the queued enable.");
            Tick(0x92);
            Require(calls.Count == 3 && calls[2] == (fixture.Key(0x92), "Left", 0), "Re-enabled source lost its ordinary binding.");
            var alwaysOn = JsonSerializer.Serialize(fixture.World.Get(fixture.Key(0x91)).Capture());
            Tick(0x91);
            Require(calls.Count == 3 && fixture.World.Get(fixture.Key(0x91)).Script is null &&
                alwaysOn == JsonSerializer.Serialize(fixture.World.Get(fixture.Key(0x91)).Capture()),
                "Scriptless STAT acquired a clock animation or mutated state.");
            Reject(() => host.IsPlayerTagSkill!("Guns"));
            Reject(() => host.PlayerLevel!());
            Reject(() => host.Command!(fixture.Key(0x90), null!, "MissingGameplayCommand", []));
        }
        using (var fixture = new Fixture("set prefix to prefix + 1\nset suffix to GetButtonPressed\nset prefix to prefix + 100"))
        {
            var host = RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) => false,
                _ => throw new InvalidDataException("Missing gameplay command escaped its host.")), null);
            var scripts = new FalloutReferenceScripts(fixture.Records, fixture.World, new(fixture.Records), host);
            var result = scripts.Dispatch(fixture.Key(0x90), "GameMode");
            Require(result.Error?.Contains("actual gameplay/player host", StringComparison.Ordinal) == true &&
                fixture.World.Get(fixture.Key(0x90)).Read(1) == 1, "Missing gameplay command was accepted or lost its prefix.");
            var stopped = JsonSerializer.Serialize(fixture.World.Capture());
            for (var i = 0; i < 3; ++i)
                Require(scripts.Dispatch(fixture.Key(0x90), "GameMode").Error == result.Error,
                    "A retained source failure lost its diagnostic.");
            Require(stopped == JsonSerializer.Serialize(fixture.World.Capture()), "Retry replayed the prefix or appended source history.");
        }
        using (var fixture = new Fixture("set prefix to prefix + 1\nPlayGroup Left 0\nset suffix to 1"))
        {
            var host = RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) => false,
                _ => throw new InvalidDataException("Animation script escaped its owner."),
                PlayGroup: (_, _, _) => throw new NotSupportedException("Synthetic unsupported animation controller.")), null);
            var scripts = new FalloutReferenceScripts(fixture.Records, fixture.World, new(fixture.Records), host);
            var result = scripts.Dispatch(fixture.Key(0x90), "GameMode");
            Require(result.Error?.Contains("unsupported animation controller", StringComparison.Ordinal) == true &&
                fixture.World.Get(fixture.Key(0x90)).Read(1) == 1 && fixture.World.Get(fixture.Key(0x90)).Read(2) == 0,
                "Animation refusal was accepted or committed its suffix.");
            var stopped = JsonSerializer.Serialize(fixture.World.Capture());
            _ = scripts.Dispatch(fixture.Key(0x90), "GameMode");
            Require(stopped == JsonSerializer.Serialize(fixture.World.Capture()), "Animation refusal replayed its prefix.");
        }
        Console.WriteLine("OPENNV_REFERENCE_EVENT_HOST_PASS playerAbsent=true dayRight=true nightLeft=true boundaries=true " +
            "disabledGuard=true scriptlessStatUnchanged=true missingGameplayRefused=true retainedPrefixNoReplay=true " +
            "nativeAnimationControllerAndRetailPixelsUnverified=true");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException error) when (error.Message.Contains("actual gameplay/player host", StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Missing gameplay owner was not explicitly refused.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("opennv-reference-event-host-");
        internal FalloutPluginStack Records { get; }
        internal FalloutReferenceWorld World { get; }
        internal FalloutFormKey Key(uint id) => new("Events.esm", id);
        internal Fixture(string body)
        {
            var header = new byte[20]; U32(2).CopyTo(header, 12);
            File.WriteAllBytes(Path.Combine(_directory.FullName, "Events.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("ACTI", 1, Field("SCRI", U32(0x50))), Record("STAT", 2),
                Record("SCPT", 0x50, Field("SCHR", header),
                    Field("SLSD", Local(1)), Field("SCVR", Text("prefix")), Field("SLSD", Local(2)), Field("SCVR", Text("suffix")),
                    Field("SCTX", Text("float prefix\nfloat suffix\nbegin GameMode\n" + body + "\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Reference(0x90, 1), Reference(0x91, 2), Reference(0x92, 1))));
            Records = FalloutPluginStack.Load(_directory.FullName, ["Events.esm"]);
            World = new(Records); World.LoadCell(FalloutCellSceneReader.Read(Records, Key(0x80)));
        }
        public void Dispose() { World.Dispose(); Records.Dispose(); _directory.Delete(true); }
    }
    private static byte[] Reference(uint id, uint basis) => Record("REFR", id, Field("NAME", U32(basis)), Field("DATA", new byte[24]));
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Local(uint index) { var bytes = new byte[24]; U32(index).CopyTo(bytes, 0); return bytes; }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] rows)
    {
        var data = Join(rows); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
