using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class ActorProcessRuntimeContracts
{
    private static readonly string[] Images =
    ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
     "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];
    private static readonly FalloutFormKey Player = Key(0x14), Npc = Key(0x901), Creature = Key(0x902);
    private const string Stack = "authored-process-runtime-selection";
    internal static void Run()
    {
        foreach (var image in Images)
        {
            var source = FalloutActorProcessRuntimeDeclaration.ForExecutable(image);
            RuntimeInputs(source); CommonFactory(source); SourceBody(source); FailedFactory(source);
        }
        Console.WriteLine("OPENNV_ACTOR_PROCESS_RUNTIME_CONTRACT_PASS sourceMainInvocation=true playerTravelCounterDistinct=true failedHourNoDecrement=true neutralLifeNotPhysicalBoolean=true commonActualFactory=true packageAndValueProviderRetained=true oldLeaseClearedBeforeNew=true undefinedConstructorLaneRetained=true sourceBptdNodeAndBoneLodLookup=true nullableLookup=true coldNewProcessNoReplay=true driftAndReentryRefused=true nativePublication=unexecuted middleCopyAndTravelCompletion=unowned");
    }
    private static void RuntimeInputs(FalloutActorProcessRuntimeDeclaration source)
    {
        var owner = new FalloutActorProcessRuntimeState(source, Stack, Player, key => Identity(source, key));
        owner.Construct(Npc); owner.Construct(Creature);
        Require(owner.PlayerTravelCounter.Require() == 0 && !owner.MainForcedProcessing.Require() && owner.Life(Npc).Require() == 0,
            "Source constructor inputs inherited physical, CELL or cohort state.");
        var invocation = owner.BeginMain(FalloutMainProcessOperation.SourceFullUpdate, "authored-actual-Main-update");
        Require(owner.MainForcedProcessing.Require() && owner.SaveBlocker is not null, "Entered Main consumer omitted its real set bit.");
        Reject(() => owner.CompleteMain(Guid.NewGuid(), "authored-actual-Main-update"));
        owner.CompleteMain(invocation, "authored-actual-Main-update");
        Require(!owner.MainForcedProcessing.Require() && owner.SaveBlocker is null, "Returned Main consumer did not clear its source flag.");
        var saved = RoundTrip(owner.Capture());
        using var cold = new FalloutActorProcessRuntimeState(source, Stack, Player, key => Identity(source, key), saved);
        Require(cold.Capture().CapturedProcess != saved.CapturedProcess && cold.Capture().MainOperations.SequenceEqual(saved.MainOperations) &&
            cold.Capture().ColdHandoff?.PreviousProcess == saved.CapturedProcess, "Cold input replayed a source producer or reused its process epoch.");
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with { MainForcedProcessing = true }));
        Reject(() => FalloutActorProcessRuntimeState.Validate(saved with
        { Actors = saved.Actors.Select(actor => actor.Source.Reference == Creature ? actor with { Value = 2 } : actor).ToArray() }));
        Reject(() => new FalloutActorProcessRuntimeState(source, Stack, Player, key => Identity(source, key) with
        { BaseSha256 = Digest("foreign actual base") }, saved));
        owner.RetainUnownedLifeTransition(Npc, "authored-actual-physical-transition-without-original-code-writer");
        Reject(() => owner.Life(Npc).Require());
        var failed = RoundTrip(owner.Capture()); FalloutActorProcessRuntimeState.Validate(failed);
        Require(failed.Actors.Single(actor => actor.Source.Reference == Npc).Value is null && owner.Life(Creature).Require() == 0,
            "An unowned actor life write guessed a code or altered an unrelated source actor.");
        owner.Dispose();

        var travel = new FalloutActorProcessRuntimeState(source, Stack, Player, key => Identity(source, key));
        var hour = travel.EnterTravel(new(2.75f, "authored-original-travel-calculation"), "authored-actual-travel");
        Require(travel.PlayerTravelCounter.Require() == 2, "Original stored-hour signed truncation changed.");
        Reject(() => travel.EnterTravelWorldHour(Guid.NewGuid(), "authored-actual-travel"));
        Reject(() => travel.EnterTravelWorldHour(hour, "authored-actual-travel"));
        var entered = RoundTrip(travel.Capture()); FalloutActorProcessRuntimeState.Validate(entered);
        Require(entered.PlayerTravelCounter == 2 && entered.Travel is { ConsumerEntered: true, CompletedWorldHours: 0, Failure: not null },
            "An entered unowned hour decremented the real travel counter.");
        Reject(() => FalloutActorProcessRuntimeState.Validate(entered with
        { Travel = entered.Travel! with { CompletedWorldHours = 1 } }));
        Reject(travel.Dispose);
    }
    private static void CommonFactory(FalloutActorProcessRuntimeDeclaration source)
    {
        using var perception = Perception(source); perception.Construct(Npc); perception.Construct(Creature);
        var state = Gameplay(Npc); var body = Body(source, Npc); var reads = 0;
        using var common = new FalloutActorProcessCommonState(source, Stack, key => Identity(source, key), key =>
        { reads++; return key == Npc ? state : Gameplay(key); }, key => key == Npc ? body : Body(source, key));
        common.Construct(Player, 1, FalloutDetectionProcessLevel.High); common.Construct(Npc, 1, FalloutDetectionProcessLevel.Low);
        common.Construct(Creature, 1, FalloutDetectionProcessLevel.Low);
        using var manager = Manager(source, perception, common); manager.Construct(Npc); manager.Construct(Creature);
        var before = common.Capture(); var old = before.Current.Single(actor => actor.Source.Reference == Npc).Gameplay!.Ownership;
        manager.EnsureHigh(Npc, 1, "authored-real-Update3D-factory");
        var saved = RoundTrip(common.Capture()); FalloutActorProcessCommonState.Validate(saved); common.RequireActors(manager.Capture().Actors);
        var current = saved.Current.Single(actor => actor.Source.Reference == Npc);
        Require(manager.Capture().Factory?.Phase == FalloutActorProcessFactoryPhase.Complete && current.Epoch == 2 &&
            current.Phase == FalloutProcessCommonPhase.Initialized && current.Gameplay?.Ownership != old &&
            saved.Retired.Single().Gameplay is null && saved.Transfer is { OldRetired: true, Published: true, Initialized: true } &&
            current.Scalars.FinalFirstBits is null && current.Scalars.ClockBits == 0xbf800000u &&
            saved.Transfer.GameplaySha256 == FalloutActorProcessCommonState.GameplayHash(state) && reads == 3,
            "Actual factory lost ordered old/new ownership, live gameplay state or a constructor-unwritten field.");
        Require(saved.Current.Single(actor => actor.Source.Reference == Creature) == before.Current.Single(actor => actor.Source.Reference == Creature),
            "A process factory altered an unrelated creature's real common owner.");
        var hash = FalloutActorProcessCommonState.GameplayHash(state);
        using var restored = new FalloutActorProcessCommonState(source, Stack, key => Identity(source, key), _ =>
            throw new InvalidDataException("Cold restoration replayed gameplay copy."), _ => null, saved);
        Require(restored.Capture().CapturedProcess != saved.CapturedProcess && restored.Capture().Transfer == saved.Transfer &&
            FalloutActorProcessCommonState.GameplayHash(state) == hash, "Cold common restoration replayed a factory or mutated the real provider.");
        Reject(() => FalloutActorProcessCommonState.Validate(saved with
        { Current = saved.Current.Select(actor => actor.Source.Reference == Npc ? actor with
            { Scalars = actor.Scalars with { FinalFirstBits = 0 } } : actor).ToArray() }));
        Reject(() => FalloutActorProcessCommonState.Validate(saved with
        { Current = saved.Current.Select(actor => actor.Source.Reference == Creature ? actor with
            { Gameplay = current.Gameplay } : actor).ToArray() }));
        Reject(() => restored.ObserveExistingHighBody(Npc, 1));
        manager.Retire(Npc, "authored-actual-reference-retirement");
        common.RetireActor(Npc, manager.Read(Npc).Epoch, "authored-actual-reference-retirement");
        common.RequireActors(manager.Capture().Actors); FalloutActorProcessCommonState.Validate(RoundTrip(common.Capture()));
        Require(common.Capture().Current.Single(actor => actor.Source.Reference == Npc) is
            { Epoch: 3, Phase: FalloutProcessCommonPhase.Retired, Gameplay: null },
            "Source actor retirement kept its invalidated process epoch or living common lease.");
    }
    private static void FailedFactory(FalloutActorProcessRuntimeDeclaration source)
    {
        var perception = Perception(source); perception.Construct(Npc);
        var common = new FalloutActorProcessCommonState(source, Stack, key => Identity(source, key), Gameplay, _ => null);
        common.Construct(Player, 1, FalloutDetectionProcessLevel.High); common.Construct(Npc, 1, FalloutDetectionProcessLevel.Low);
        var manager = Manager(source, perception, common); manager.Construct(Npc);
        Reject(() => manager.EnsureHigh(Npc, 1, "authored-missing-live-body-after-publication"));
        var saved = RoundTrip(common.Capture()); FalloutActorProcessCommonState.Validate(saved);
        Require(saved.Transfer is { OldRetired: true, Published: true, Initialized: false, Failure: not null } &&
            saved.Current.Single(actor => actor.Source.Reference == Npc).Epoch == 2 && saved.Pending is null && common.SaveBlocker is not null,
            "Missing High body discarded the actual published source prefix.");
        Reject(common.Dispose); Reject(manager.Dispose);

        FalloutActorProcessRuntimeState? reentrant = null; var trigger = false;
        reentrant = new(source, Stack, Player, key =>
        {
            if (trigger) { try { reentrant!.Construct(Creature); } catch (InvalidOperationException) { } }
            return Identity(source, key);
        });
        trigger = true; Reject(() => reentrant.Construct(Npc));
        Require(!reentrant.HasActor(Npc) && !reentrant.HasActor(Creature), "Caught identity reentry committed a fake constructor.");
        reentrant.Dispose();
    }
    private static void SourceBody(FalloutActorProcessRuntimeDeclaration source)
    {
        var body = Body(source, Npc);
        Require(body.Bip01Block == 0 && body.BoneLodController == 3 && body.HeadBlock == 1 && body.TorsoBlock == 0 &&
            body.Parts.Single(part => part.PartType == 1).Block == 1, "Actual NIF/controller/BPTD source lookup selected a different object.");
        var missing = FalloutActorProcessBodySource.Read(source, Npc, "meshes/authored-skeleton.nif",
            FalloutNifFile.Read(Skeleton("Unrelated", false)), Parts(), Digest("authored-bptd"), "authored-original-High-init");
        Require(missing.Bip01Block is null && missing.BoneLodController is null && missing.TorsoBlock is null && missing.HeadBlock == 1,
            "Legal absent source names acquired guessed nodes/controllers.");
        Reject(() => FalloutActorProcessBodySource.Read(source, Npc, "meshes/authored-skeleton.nif",
            FalloutNifFile.Read(Skeleton("Bip01", true, cycle: true)), Parts(), Digest("authored-bptd"), "authored-original-High-init"));
        Require(!FalloutActorProcessCommonState.BodyEquivalent(body, body with { BoneLodController = 2 }),
            "Controller transport drift became a matching High body declaration.");
    }
    private static FalloutActorProcessBodyBinding Body(FalloutActorProcessRuntimeDeclaration source, FalloutFormKey actor) =>
        FalloutActorProcessBodySource.Read(source, actor, "meshes/authored-skeleton.nif", FalloutNifFile.Read(Skeleton("Bip01", true)),
            Parts(), Digest("authored-bptd"), "authored-source-body-declaration-only");
    private static FalloutBodyPartData Parts()
    {
        var gore = new FalloutBodyPartGore(0, null, null, 0, null, 0);
        FalloutBodyPart Part(byte type, string node) => new(type, "authored-part", node, node, 0, 1, 0, -1, 0, 0, 0,
            gore, gore, default, default, null, 0, "");
        return new(Key(0x1d), [Part(0, "Bip01"), Part(1, "Head")]);
    }
    private static byte[] Skeleton(string root, bool controllers, bool cycle = false)
    {
        byte[] Node(int name, int controller, params int[] children) => Bytes(writer =>
        {
            writer.Write(name); writer.Write(0U); writer.Write(controller); writer.Write(14U);
            foreach (var number in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(number);
            writer.Write(0U); writer.Write(-1); writer.Write((uint)children.Length);
            foreach (var child in children) writer.Write(child); writer.Write(0U);
        });
        void Time(BinaryWriter writer, int next)
        { writer.Write(next); writer.Write((ushort)0); writer.Write(1f); writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(0); }
        var blocks = new List<(string Type, byte[] Data)> { ("NiNode", Node(0, controllers ? 2 : -1, 1)), ("NiNode", Node(1, -1)) };
        if (controllers)
        {
            blocks.Add(("NiTransformController", Bytes(writer => { Time(writer, cycle ? 2 : 3); writer.Write(-1); })));
            blocks.Add(("NiBSBoneLODController", Bytes(writer => { Time(writer, -1); writer.Write(0U); writer.Write(1U); writer.Write(1U); writer.Write(1U); writer.Write(1); })));
        }
        return Bytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write((uint)blocks.Count); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            var types = blocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToArray();
            writer.Write((ushort)types.Length);
            void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
            foreach (var type in types) Text(type);
            foreach (var block in blocks) writer.Write((ushort)Array.IndexOf(types, block.Type));
            foreach (var block in blocks) writer.Write(block.Data.Length);
            writer.Write(2); writer.Write(Math.Max(root.Length, 4)); Text(root); Text("Head"); writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Data); writer.Write(1U); writer.Write(0);
        });
    }
    private static byte[] Bytes(Action<BinaryWriter> write)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
    private static FalloutProcessGameplayState Gameplay(FalloutFormKey actor) => new(actor, null, null, null, null, null, null, null,
        null, null, null, null, new Dictionary<string, FalloutActorValue> { ["health"] = new(37, 2, -1, 3) });
    private static FalloutActorProcessManager Manager(FalloutActorProcessRuntimeDeclaration source, FalloutActorPerception perception,
        FalloutActorProcessCommonState common) => new(FalloutActorProcessDeclaration.ForExecutable(source.ExecutableSha256), Stack, Player,
        perception, key => Identity(source, key), Actor, new((_, _) => throw Missing(), (_, _) => throw Missing(), (_, _) => throw Missing(),
            () => throw Missing(), () => throw Missing(), _ => throw Missing(), _ => throw Missing(), _ => throw Missing(),
            (_, _, _) => throw Missing(), _ => throw Missing(), () => throw Missing(), _ => throw Missing(),
            common.Copy, common.RetireOld, common.InitializeNew, key => new(key, new(true, "authored-source-registration-argument"),
                new(false, "authored-source-normal-mode"), "authored-actual-constructor")));
    private static FalloutActorPerception Perception(FalloutActorProcessRuntimeDeclaration source) => new(
        FalloutActorPerceptionDeclaration.ForExecutable(source.ExecutableSha256), Stack, Player, 3, Actor, key => Identity(source, key),
        new(_ => throw Missing(), (_, _, _, _) => throw Missing(), _ => throw Missing(), _ => throw Missing(), () => throw Missing(), () => throw Missing()));
    private static FalloutCombatActorIdentity Identity(FalloutActorProcessRuntimeDeclaration source, FalloutFormKey key) =>
        new(key, Key(key == Player ? 7u : key == Creature ? 0x702u : 0x701u), key == Player ? "ENGINE_PLAYER" : key == Creature ? "ACRE" : "ACHR", 0,
            key == Player ? source.ExecutableSha256 : Digest("authored-reference:" + key), key == Creature ? "CREA" : "NPC_", 0,
            Digest("authored-base:" + key), key == Player);
    private static bool Actor(FalloutFormKey key) => key == Player || key == Npc || key == Creature;
    private static FalloutFormKey Key(uint id) => new("ActorProcessFixture.esm", id);
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static NotSupportedException Missing() => new("Authored pure fixture has no native/source sensory completion.");
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Foreign, malformed or unowned actual process state was accepted.");
    }
}
