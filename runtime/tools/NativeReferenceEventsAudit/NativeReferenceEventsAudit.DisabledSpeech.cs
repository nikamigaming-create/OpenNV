using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void DisabledSpeech(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
        var topic = Key(0x740); var parent = Key(0x940); var child = Key(0x941); var opposite = Key(0x942);
        var enabled = Key(0x943); var player = Key(0x14);
        var configuration = RuntimeConfiguration.Load();
        var reported = new List<string>();
        RuntimeNativeSpeech Create(FalloutReferenceWorld state)
        {
            var speech = new RuntimeNativeSpeech();
            speech.ReportDivergence = reported.Add;
            speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, _ => 0, references: state);
            speech.SayToCompleted += (_, _) => throw new InvalidDataException("Disabled speech invented SayToDone.");
            speech.InfoCompleted += _ => throw new InvalidDataException("Disabled speech invented INFO completion.");
            speech.PrepareSubtitle = _ => throw new InvalidDataException("Disabled speech invented subtitles.");
            speech.ExecuteResults = (_, _, _) => throw new InvalidDataException("Disabled speech invented results.");
            AddChild(speech); speech.SetProcess(false);
            return speech;
        }
        void RequireMissing(FalloutFormKey speaker, FalloutFormKey listener)
        {
            var speech = Create(world);
            try
            {
                try { speech.SayTo(speaker, listener, topic); throw new InvalidOperationException("Missing enabled actor was accepted."); }
                catch (NotSupportedException error) when (error.Message.Contains("resident runtime actors", StringComparison.Ordinal)) { }
                Require(speech.Error is not null, "Missing enabled actor lost its retained fault.");
            }
            finally { speech.Free(); }
        }
        var active = Create(world);
        try
        {
            var before = JsonSerializer.Serialize(world.Capture());
            active.SayTo(parent, player, topic);
            active.Say(parent, topic);
            active.SayTo(child, player, topic);
            active.SayTo(enabled, parent, topic);
            active._Process(1);
            var state = JsonSerializer.SerializeToElement(active.State);
            Require(active.Error is null && !active.Active && active.Subtitle is null && !active.IsTalking(parent) &&
                state.GetProperty("disabledCommands").GetInt64() == 4 && state.GetProperty("channels").GetArrayLength() == 0 &&
                state.GetProperty("completedCommands").GetInt64() == 0 && state.GetProperty("said").GetArrayLength() == 0 &&
                before == JsonSerializer.Serialize(world.Capture()), "Disabled participants started, completed or mutated speech/state.");
            RequireMissing(opposite, player);
            RequireMissing(enabled, player);
            Require(world.SetEnabled(parent, true), "Fixture enable request was lost.");
            active.SayTo(parent, player, topic); // Applied state is still disabled.
            world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            RequireMissing(parent, player);
            RequireMissing(child, player);
            Require(world.SetEnabled(parent, false), "Fixture disable request was lost.");
            RequireMissing(parent, player); // Queued disable is still enabled.
            world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            active.SayTo(child, player, topic);
            using var restored = new FalloutReferenceWorld(records);
            restored.Restore(world.Capture());
            var cold = Create(restored);
            try
            {
                cold.SayTo(child, player, topic); cold._Process(1);
                Require(cold.Error is null && !cold.Active &&
                    JsonSerializer.SerializeToElement(cold.State).GetProperty("disabledCommands").GetInt64() == 1,
                    "Cold disabled-parent speech differed from applied state.");
            }
            finally { cold.Free(); }
            Require(reported.Count == 5 && JsonSerializer.SerializeToElement(active.State).GetProperty("disabledCommands").GetInt64() == 6,
                "Queued and applied enable changes used different speech participation rules.");
            GD.Print("OPENNV_NATIVE_DISABLED_SPEECH_PASS say=true sayTo=true noVoice=true noCompletion=true enabledMissingRejected=true parent=true opposite=true queued=true cold=true");
        }
        finally { active.Free(); }
    }

    private static byte[] DisabledSpeechReferences()
    {
        byte[] Actor(uint id, uint flags = 0, uint? parent = null, bool opposite = false)
        {
            var link = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(link, parent ?? 0); link[4] = opposite ? (byte)1 : (byte)0;
            var bytes = Record("ACRE", id, Field("NAME", BitConverter.GetBytes(0x701u)), Field("DATA", new byte[24]),
                parent is not null ? Field("XESP", link) : []);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
            return bytes;
        }
        return Actor(0x940, 0x800).Concat(Actor(0x941, parent: 0x940))
            .Concat(Actor(0x942, parent: 0x940, opposite: true)).Concat(Actor(0x943)).ToArray();
    }
}
