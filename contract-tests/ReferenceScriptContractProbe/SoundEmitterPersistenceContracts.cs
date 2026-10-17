using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class SoundEmitterPersistenceContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var reference = new FalloutFormKey("Pose.esm", 0x900);
        var source = FalloutSoundRecordReader.Read(records, new("Pose.esm", 0x6a));
        var history = new FalloutAnimationSoundEvents(reference);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], new(7), true);
        var generation = history.Begin(records, selected, "Sound:FixtureLoop Joint", false, [source.LogicalPath]);
        history.BindMedia(generation, new string('a', 64));
        var bone = new FalloutAnimationSoundBoneEmitterSnapshot(reference, "meshes\\fixture\\skeleton.nif", new string('b', 64), 1, "Joint");
        var playback = new FalloutAnimationSoundPlaybackSnapshot(new(120, 32000, 1, 48000,
            FalloutSoundLoop.Read(source), 33.25, 2, true, false), "Skeleton/SourceSoundBone_1", true, bone);
        history.BindPlayback(generation, () => playback);
        var saved = JsonSerializer.Deserialize<FalloutAnimationSoundEventsSnapshot>(JsonSerializer.Serialize(history.Capture()))!;
        var cold = new FalloutAnimationSoundEvents(reference);
        cold.Restore(saved, records);
        Require(!cold.CanCapture && cold.Events.Count == 1 && cold.Events[0].Playback == playback,
            "Typed bone identity was lost or mistaken for a reconstructed native owner.");
        cold.BindPlayback(generation, () => playback);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved),
            "Typed emitter JSON restoration redrew a sound request or changed its fractional source clock.");
        foreach (var invalid in new[]
        {
            bone with { Reference = new("Pose.esm", 0x902) }, bone with { SkeletonPath = "meshes/fixture/skeleton.nif" },
            bone with { SkeletonPath = "meshes\\..\\skeleton.nif" }, bone with { SkeletonPath = "sound\\skeleton.nif" },
            bone with { SkeletonSha256 = "missing" }, bone with { Block = -1 }, bone with { Name = "" }
        }) Reject(() => (saved with { Events = [saved.Events[0] with { Playback = playback with { SourceBone = invalid } }] }).Validate());
        // Old stable root/node paths keep their old schema shape. Anonymous
        // legacy history can still be read; its native binding stays refused.
        (saved with { Events = [saved.Events[0] with { Playback = playback with { SourceBone = null, EmitterPath = "." } }] }).Validate();
        (saved with { Events = [saved.Events[0] with { Playback = playback with { SourceBone = null, EmitterPath = "@Node3D@1" } }] }).Validate();
        RejectLegacySchema(saved);
        EnableOwnership();
        Console.WriteLine("OPENNV_SOUND_EMITTER_PERSISTENCE_CONTRACT_PASS typedIdentity=true sourceClock=true noNativeOwnerInJson=true identityShapeRefused=true legacyNamedShape=true legacyAnonymousReadOnly=true futureSchemaRefused=true authoritativeXesp=true disposedOwnerRefused=true");
    }

    private static void RejectLegacySchema(FalloutAnimationSoundEventsSnapshot sounds)
    {
        var validate = typeof(FalloutNativeCampaignSave).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var schema in new[] { FalloutNativeCampaignSave.FinishedRadioSchema, FalloutNativeCampaignSave.PlayerAudioSchema })
        foreach (var player in new[] { false, true })
        {
            var state = new FalloutNativeCampaignState(schema, "", default, "", 0, "", null!, null!, [], [], [], [], [], [], [],
                References: player ? null : [new(sounds.Reference, new("Pose.esm", 0x800), new("Pose.esm", 0x10), null, null,
                    new Dictionary<uint, double>(), null,
                    AnimationSoundEvents: sounds)],
                PlayerPackageAudio: player ? new(sounds, 7) : null);
            try { validate.Invoke(null, [state, ""]); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidDataException rejected &&
                rejected.Message == "Legacy campaign schema contains future source sound bone continuation.") { continue; }
            throw new InvalidDataException("Legacy campaign schema accepted future typed source emitter continuation.");
        }
    }

    private static void EnableOwnership()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-emitter-enable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Emitter.esm"), EnablePlugin());
            using var records = FalloutPluginStack.Load(directory, ["Emitter.esm"]);
            var world = new FalloutReferenceWorld(records);
            try
            {
                var parent = new FalloutFormKey("Emitter.esm", 1);
                var normal = world.Get(new("Emitter.esm", 2)).AnimationSoundEvents;
                var opposite = world.Get(new("Emitter.esm", 3)).AnimationSoundEvents;
                normal.RequireEnabledSourceEmitter(); Reject(opposite.RequireEnabledSourceEmitter);
                world.SetEnabled(parent, false);
                normal.RequireEnabledSourceEmitter(); // Queued disable is not applied source state.
                world.AdvanceEnableChanges(0, new(1, 1), _ => false);
                Require(world.Get(normal.Reference).Enabled && !world.IsEnabled(normal.Reference),
                    "Enable fixture did not retain its own enabled flag beneath the disabled XESP parent.");
                Reject(normal.RequireEnabledSourceEmitter); opposite.RequireEnabledSourceEmitter();
                var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
                using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
                Reject(cold.Get(normal.Reference).AnimationSoundEvents.RequireEnabledSourceEmitter);
                cold.Get(opposite.Reference).AnimationSoundEvents.RequireEnabledSourceEmitter();
                world.Dispose();
                try { normal.RequireEnabledSourceEmitter(); }
                catch (ObjectDisposedException) { return; }
                throw new InvalidDataException("Retired world was mistaken for an enabled source emitter owner.");
            }
            finally { world.Dispose(); }
        }
        finally { File.Delete(Path.Combine(directory, "Emitter.esm")); Directory.Delete(directory); }
    }

    private static byte[] EnablePlugin()
    {
        static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        static byte[] Field(string name, byte[] payload)
        {
            var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(bytes, 6); return bytes;
        }
        static byte[] Record(string name, uint id, params byte[][] fields)
        {
            var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)payload.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); payload.CopyTo(bytes, 24); return bytes;
        }
        static byte[] Reference(uint id, byte? opposite = null) => Record("REFR", id,
            Field("NAME", BitConverter.GetBytes(10U)), Field("DATA", new byte[24]),
            opposite is { } flags ? Field("XESP", [1, 0, 0, 0, flags, 0xa5, 0x5a, 0xff]) : []);
        var references = Join(Reference(1), Reference(2, 0), Reference(3, 1));
        var group = new byte[24 + references.Length]; "GRUP"u8.CopyTo(group);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), checked((uint)group.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6); references.CopyTo(group, 24);
        return Join(Record("TES4", 0), Record("STAT", 10), Record("CELL", 20, Field("DATA", [1])), group);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned or malformed source sound emitter was admitted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
