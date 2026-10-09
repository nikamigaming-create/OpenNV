using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class MainFrameDispatchContracts
{
    internal static void Run()
    {
        foreach (var image in FalloutMainFrameDeclaration.Executables)
        {
            var source = FalloutMainFrameDeclaration.ForExecutable(image);
            Priority(source); MainWindow(source); Routing(source); PackedTask(source);
        }
        Console.WriteLine("OPENNV_MAIN_FRAME_DISPATCH_CONTRACT_PASS independentMainWord=true sourceWindowOrder=true failedWindowNoTrailingClear=true sourceSettingReadOrder=true signedMaxBeforeAbs=true priorityByteWrap=true exactCellInstanceCache=true coldCacheInvalidation=true originalFloat32Conversion=true packedFullIntegerPriority=true originalCasRefusal=true queuedOrdinaryCaller=true nativeActorsAndOriginalFrame=unexecuted opaqueConsumers=unowned");
    }
    private static void Priority(FalloutMainFrameDeclaration source)
    {
        using var priority = new FalloutQueuedReferencePriority(source, "authored-main-frame");
        var cell = new FalloutQueuedPriorityCell(new(new("FrameFixture.esm", 0x910), 0, Digest("authored-CELL"),
            new("FrameFixture.esm", 0x911), Digest("authored-WRLD")), Guid.NewGuid(), false, -10, -1);
        var reads = 0;
        FalloutQueuedPriorityInputs Inputs() => new(() => { reads++; return new(5u, "authored-source-grid-setting"); },
            new(0, "authored-source-grid-x"), new(0, "authored-source-grid-y"), null, "authored-native-caller");
        Require(priority.Read(cell, Inputs) == 1 && reads == 4, "Negative signed axes became absolute Chebyshev distance or lost repeated source reads.");
        Require(priority.Read(cell, () => throw new InvalidDataException("Cache hit consumed a later source field.")) == 1,
            "Exact source CELL pointer cache did not precede source inputs.");
        Reject(() => priority.Read(cell with { X = 2 }, Inputs));
        Require(priority.Read(cell with { Instance = Guid.NewGuid(), X = 2, Y = 0 }, Inputs) == 3,
            "A newly allocated source CELL inherited a same-FormKey cache.");
        Require(priority.Read(cell with { Instance = Guid.NewGuid(), X = 26, Y = 0 }, Inputs) == 1,
            "Original priority arithmetic lost byte wrap before the authored clamp.");
        var changingReads = 0;
        var inside = cell with { Instance = Guid.NewGuid(), Interior = true, X = null, Y = null };
        Require(priority.Read(inside, () => new(() => new(++changingReads < 3 ? 5u : 1u, "authored-live-grid-setting"),
            new(null, "interior-unconsumed"), new(null, "interior-unconsumed"), null, "authored-interior")) == 3 && changingReads == 4,
            "Interior priority cached one setting value or consumed an exterior owner.");
        var saved = priority.Capture();
        using var cold = new FalloutQueuedReferencePriority(source, "authored-main-frame", saved);
        var calls = 0;
        Require(cold.Read(inside, () => { calls++; return Inputs(); }) == 1 && calls == 1 &&
            cold.Capture().CapturedProcess != saved.CapturedProcess, "Cold source priority reused an old native CELL pointer.");
        Require(FalloutQueuedReferencePriority.ConvertSourcePosition(BitConverter.SingleToUInt32Bits(4095.6f), FalloutSourceFloatRounding.NearestEven) == 4096 &&
            FalloutQueuedReferencePriority.ConvertSourcePosition(BitConverter.SingleToUInt32Bits(4095.6f), FalloutSourceFloatRounding.TowardZero) == 4095 &&
            FalloutQueuedReferencePriority.ConvertSourcePosition(0x7fc12345, FalloutSourceFloatRounding.Down) == int.MinValue,
            "Source Float32 FISTP transport used spatial floor/default rounding or accepted nonfinite conversion as zero.");
        Reject(() => priority.Read(cell with { Instance = Guid.NewGuid() }, () => Inputs() with
        { Position = new(0, 0, new(null, "unowned-original-rounding-mode")) }));
    }
    private static void MainWindow(FalloutMainFrameDeclaration source)
    {
        var player = new FalloutFormKey("FrameFixture.esm", 0x14);
        FalloutCombatActorIdentity Identity(FalloutFormKey key) => new(key, new("FrameFixture.esm", 7),
            "ENGINE_PLAYER", 0, source.ExecutableSha256, "NPC_", 0, Digest("authored-player-base"), true);
        using var runtime = new FalloutActorProcessRuntimeState(FalloutActorProcessRuntimeDeclaration.ForExecutable(source.ExecutableSha256),
            "authored-main-frame", player, Identity);
        Require(runtime.Capture().MainFrame.Word == source.ConstructorWord && !runtime.MainPermitsForcedQueue.Require(),
            "Actual constructor word inherited pause/AI/cohort state.");
        var invocation = runtime.BeginMain(FalloutMainProcessOperation.SourceFullUpdate, "authored-full-update");
        var consumers = new FrameConsumers(() => Require(runtime.MainForcedProcessing.Require() && runtime.MainPermitsForcedQueue.Require(),
            "Source window did not independently permit queue creation during forced update."));
        runtime.ExecuteMainQueueWindow(true, consumers);
        Require(consumers.Calls.SequenceEqual(new[] { "ctx5", "first", "world", "task", "context", "player-cell", "world-final", "child1", "second", "release", "child2" }) &&
            runtime.MainForcedProcessing.Require() && !runtime.MainPermitsForcedQueue.Require(), "Main ordered consumers cleared/merged independent flags.");
        runtime.CompleteMain(invocation, "authored-full-update");
        var failed = new FalloutActorProcessRuntimeState(FalloutActorProcessRuntimeDeclaration.ForExecutable(source.ExecutableSha256),
            "authored-main-frame", player, Identity);
        var failure = new FrameConsumers(() => { }, "second");
        Reject(() => failed.ExecuteMainQueueWindow(false, failure));
        var saved = failed.Capture();
        Require((saved.MainFrame.Word & 8) != 0 && !failure.Calls.Contains("release") && !failure.Calls.Contains("child2") && failed.SaveBlocker is not null,
            "Failed original consumer ran the trailing clear/release or claimed save readiness.");
        FalloutActorProcessRuntimeState.Validate(saved); Reject(failed.Dispose);
        var cold = new FalloutActorProcessRuntimeState(FalloutActorProcessRuntimeDeclaration.ForExecutable(source.ExecutableSha256),
            "authored-main-frame", player, Identity, saved);
        Require(cold.Capture().MainFrame.Word == saved.MainFrame.Word && cold.SaveBlocker is not null,
            "Cold Main frame replayed/cleared a failed original consumer.");
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with { MainFrame = saved.MainFrame with { Word = saved.MainFrame.Word & ~8u } }));
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with { MainFrame = saved.MainFrame with { Word = saved.MainFrame.Word ^ 1u } }));
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with { MainFrame = saved.MainFrame with
        { Windows = saved.MainFrame.Windows.Select(window => window with { Next = FalloutMainQueueFrameStep.WorldPrelude }).ToArray() } }));
    }
    private static void Routing(FalloutMainFrameDeclaration source)
    {
        FalloutActorProcessFact<T> Missing<T>() where T : struct => new(null, "authored-unconsumed-inline-field");
        Require(FalloutSourceQueuedReferenceDispatch.Read(source, false, () => throw new InvalidDataException("Ordinary caller read OS thread."),
            () => Missing<uint>(), () => Missing<bool>(), "authored-ordinary-native-load").Route == FalloutSourceQueuedDispatch.Queued,
            "False ordinary source caller entered an inline arm.");
        Require(FalloutSourceQueuedReferenceDispatch.Read(source, true, () => new(11u, "actual-thread"), () => new(12u, "main-thread"),
            () => throw new InvalidDataException("Unequal-thread arm read later gate."), "authored-inline-request").Route == FalloutSourceQueuedDispatch.Queued,
            "Actual different OS threads became same/inline.");
        Reject(() => FalloutSourceQueuedReferenceDispatch.Read(source, true, () => new(11u, "actual-thread"), () => new(11u, "main-thread"),
            () => Missing<bool>(), "authored-inline-request"));
    }
    private static void PackedTask(FalloutMainFrameDeclaration source)
    {
        Require(FalloutQueuedTaskPriorities.ReplacePriority(0x1122334455667788, 1) == 0x1122334455017788 &&
            FalloutQueuedTaskPriorities.ReplacePriority(0x1122334455667788, 256) == 0x1122334456007788 &&
            FalloutQueuedTaskPriorities.ReplacePriority(0x01000000, -1) == 0x00ff0000,
            "Full signed integer priority became a byte-only store or erased unrelated key bits.");
        using var priorities = new FalloutQueuedTaskPriorities(source, "authored-main-frame");
        var identity = Guid.NewGuid(); priorities.Construct(identity, 3);
        priorities.Reprioritize(identity, 256);
        Require(priorities.ReadPriority(identity) == 0, "Constructor/source repriority lost packed lane arithmetic.");
        var consumer = new KeyConsumer { State = 1, Key = 3UL << 16 };
        var result = FalloutQueuedTaskPriorities.ChangePriority(identity, consumer.Key, 2, consumer);
        Require(result.Accepted && result.Requeued && consumer.Calls.SequenceEqual(new[] { "read", "cas:2:1", "remove", "key", "cas:0:2", "enqueue" }),
            "Original queued repriority lost remove/store/requeue or CAS ordering.");
        consumer = new() { State = 3, Key = 3UL << 16 };
        result = FalloutQueuedTaskPriorities.ChangePriority(identity, consumer.Key, 2, consumer);
        Require(!result.Accepted && consumer.Key == 3UL << 16 && consumer.Calls.SequenceEqual(["read"]),
            "Original entered task state was overwritten or called a later manager consumer.");
        priorities.SourceDispatchEntered(identity, "authored-actual-native-request");
        Reject(() => priorities.Reprioritize(identity, 4));
        priorities.Retire(identity);
        FalloutQueuedTaskPriorities? guarded = null;
        var recursive = new KeyConsumer { State = 0, BeforeRead = () => Reject(() => guarded!.Construct(Guid.NewGuid(), 1)) };
        guarded = new(source, "authored-reentry", consumer: recursive);
        var guardedIdentity = Guid.NewGuid(); guarded.Construct(guardedIdentity, 1);
        Reject(() => guarded.Reprioritize(guardedIdentity, 2));
        Require(recursive.Calls.SequenceEqual(["read"]) && guarded.SaveBlocker is not null,
            "Caught actual task-provider reentry published a key or lost the original failure.");
        guarded.Retire(guardedIdentity); guarded.Dispose();
    }
    private sealed class FrameConsumers(Action check, string? fail = null) : IFalloutMainQueueFrameConsumers
    {
        internal readonly List<string> Calls = [];
        public string Owner => "authored-selected-source-window";
        private void Enter(string value) { check(); Calls.Add(value); if (value == fail) throw new IOException("authored-original-consumer-failure"); }
        public void SetWorkingContextFive() => Enter("ctx5"); public void WalkFirstEntries() => Enter("first");
        public void WorldPrelude() => Enter("world"); public void TaskPrelude() => Enter("task");
        public void TaskContext() => Enter("context"); public void PlayerCurrentCell() => Enter("player-cell");
        public void WorldFinal() => Enter("world-final"); public void FirstChild() => Enter("child1");
        public void WalkSecondEntries() => Enter("second"); public void ReleaseArray() => Enter("release");
        public void FinalChild() => Enter("child2");
    }
    private sealed class KeyConsumer : IFalloutSourceTaskPriorityConsumer
    {
        internal int State; internal ulong Key; internal readonly List<string> Calls = [];
        internal Action? BeforeRead;
        public int ReadState(Guid task) { Calls.Add("read"); BeforeRead?.Invoke(); return State; }
        public int CompareExchangeState(Guid task, int replacement, int expected)
        { Calls.Add("cas:" + replacement + ":" + expected); var before = State; if (before == expected) State = replacement; return before; }
        public void StoreKey(Guid task, ulong key) { Calls.Add("key"); Key = key; }
        public void Remove(Guid task) => Calls.Add("remove");
        public void Enqueue(Guid task) { Calls.Add("enqueue"); State = 1; }
    }
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Require(bool value, string failure) { if (!value) throw new InvalidDataException(failure); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or IOException) { return; }
        throw new InvalidDataException("Unowned/foreign source frame/dispatch state was accepted.");
    }
}
