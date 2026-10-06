using OpenNV.Runtime.Content;
using System.Text.Json;

internal static class FiniteSoundRegistryContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var caller = new FalloutFormKey("Pose.esm", 0x900);
        var sound = new FalloutFormKey("Pose.esm", 0x68);
        var source = FalloutSoundRecordReader.Read(records, sound);
        var random = new FalloutSoundRandomState(57);
        var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], random, true, true);
        var ledger = new FalloutAnimationSoundEvents(caller);
        long Begin()
        {
            var generation = ledger.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
            ledger.BindMedia(generation, new string('a', 64));
            return generation;
        }
        var registry = new FalloutSoundVoices(records);
        var authoredStops = 0; var cancellations = 0; var graphRetirements = 0;
        var finished = Begin();
        using (var voice = registry.Register(sound, caller, "synthetic-native-finished", () => true,
            () => { authoredStops++; ledger.Complete(finished, FalloutAnimationSoundEnd.SourceStopped); },
            () => { cancellations++; ledger.Cancel(finished, "Source graph retired."); }))
        {
            Require(!ledger.CanCapture, "A live registry binding admitted unfinished audio.");
            ledger.Complete(finished, FalloutAnimationSoundEnd.NativeFinished);
        }
        var completed = ledger.Capture();
        Require(registry.ActiveVoices == 0 && completed.Events[0].End == FalloutAnimationSoundEnd.NativeFinished,
            "Native completion retained a transient voice or lost its source receipt.");
        var stopped = Begin();
        using (registry.Register(sound, caller, "synthetic-source-stop", () => true,
            () => { authoredStops++; ledger.Complete(stopped, FalloutAnimationSoundEnd.SourceStopped); },
            () => { cancellations++; ledger.Cancel(stopped, "Source graph retired."); })) registry.Stop(sound, caller);
        Require(authoredStops == 1 && cancellations == 0 && ledger.Events[1].End == FalloutAnimationSoundEnd.SourceStopped,
            "Authored StopSound lost its distinct completion.");
        var interrupted = Begin();
        using var binding = registry.Register(sound, caller, "synthetic-graph-retirement", () => true,
            () => { authoredStops++; ledger.Complete(interrupted, FalloutAnimationSoundEnd.SourceStopped); },
            () => { cancellations++; ledger.Cancel(interrupted, "Source graph retired."); });
        using var lifetime = registry.BindRetirement(() =>
        {
            Require(registry.ActiveVoices == 0 && ledger.Events.Last().End == FalloutAnimationSoundEnd.Cancelled,
                "Graph retirement ran before its actual cancelled voice receipt.");
            graphRetirements++;
        });
        registry.Retire(); registry.Retire();
        Require(authoredStops == 1 && cancellations == 1 && graphRetirements == 1 && !ledger.CanCapture &&
            JsonSerializer.Serialize(ledger.Events[0]) == JsonSerializer.Serialize(completed.Events[0]) && ledger.PartialLanes.Count() == 2,
            "Graph retirement invented source stop, lost settled history or repeated teardown.");
        Reject(() => ledger.Capture());
        Reject(() => ledger.Complete(interrupted, FalloutAnimationSoundEnd.NativeFinished));
        var restored = new FalloutAnimationSoundEvents(caller); restored.Restore(completed, records);
        Require(restored.Capture().Events.Count == 1 && JsonSerializer.Serialize(restored.Events[0]) == JsonSerializer.Serialize(completed.Events[0]) &&
            random.State == selected.RandomAfter, "Cold settled audio replayed a voice or consumed source RNG.");
        Console.WriteLine("OPENNV_FINITE_SOUND_REGISTRY_CONTRACT_PASS activeRefused=true nativeFinished=true sourceStopDistinct=true graphCancelled=true cancelledImmutable=true history=true partial=true coldNoReplay=true rng=true");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Cancelled native sound entered a complete receipt.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
