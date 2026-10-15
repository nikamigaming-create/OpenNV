using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunLoadedPlayerAnimationContracts()
    {
        RunLoadedPlayerAnimationRetirementContracts();
        using var fixture = new PlayerReturnTailFixture();
        var source = AuthoredLoadedAnimation(fixture.State.Source);
        source.Validate();
        Require(FalloutLoadedAnimationResources.TryGroup(source, "meshes/fixture/PASneak1HPIdle.kf", "Idle") == 0x9400 &&
            FalloutLoadedAnimationResources.TryGroup(source, "meshes/fixture/Idle.kf", "unrecognized") is null,
            "Animation group parser changed actual encoded prefixes or invalid-name return.");
        var model = PlayerOpacityContracts.CreateReferenceColdModel(); var logical = model.ConstructLogicalScene();
        var nif = LoadedAnimationFixture(logical.Root.Name);
        var sequence = nif.ReadControllerSequence(0);
        var clock = new FalloutLoadedAnimationSequenceClock(sequence);
        Require(clock.Activate(0x3f800000, true) && !clock.Activate(0x3f800000, true), "Source sequence activated twice.");
        Require(clock.Update(Bits(10)) == 0 && clock.Update(Bits(11)) == 1 && clock.Update(Bits(12)) == 0,
            "Sequence did not retain its first absolute time and source loop range.");
        Require(clock.DeactivateImmediately() && !clock.DeactivateImmediately(), "Immediate deactivation changed its source state twice.");
        var saved = JsonSerializer.Deserialize<FalloutLoadedAnimationClockSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var cold = new FalloutLoadedAnimationSequenceClock(sequence, saved);
            Require(cold.Capture() == saved && !cold.Active, "Cold sequence replayed activation or source updates.");
            saved = JsonSerializer.Deserialize<FalloutLoadedAnimationClockSnapshot>(JsonSerializer.Serialize(cold.Capture()))!;
        }
        Reject(() => FalloutLoadedAnimationSequenceClock.Validate(saved with { Active = 1 }, sequence));
        var resource = new FalloutLoadedAnimationResource(new(source.Identity, Guid.NewGuid(), "meshes/fixture/idle.kf",
            nif.Sha256, 0, 0, sequence.Name, 0, "authored-original-NIF-reader"), nif, sequence);
        var binding = new FalloutLoadedAnimationSequenceBinding(resource, logical);
        Require(binding.TransformChannelCount == 1 && binding.UnboundChannels.Count == 0, "Pure source binding did not resolve its real graph target.");
        binding.ApplySourceTime(0); logical.UpdateWorldAndBounds();
        Require(logical.Root.Local.Translation == new FalloutNifVector3(1, 2, 3), "Animation sampler did not mutate the authoritative logical transform.");
        fixture.EnterLoad((invocation, guard) =>
        {
            var resources = new FalloutActorAnimationResourceState(source, invocation, FalloutLoadedAnimationRole.Common,
                logical, guard, () => { });
            var cleanup = resources.ClearForAnimationLoad(invocation, guard);
            Require(cleanup.Returned && cleanup.Visited == 20 && cleanup.Cleared == 80 && resources.Capture().Primary.All(row => row.Payload is null),
                "Actual resource constructor/cleanup did not execute every source empty-row store.");
            resources.ObserveEnteredWriter("authored-real-slot-writer", () => guard(invocation));
            Reject(() => resources.ClearForAnimationLoad(invocation, guard));
            var commonContext = new FalloutSourceAnimationContextState(source.Tail, invocation, guard, () => { }, FalloutLoadedAnimationRole.Common);
            fixture.Initializer.SetLoadedAnimationFlag(source, invocation, false, null, guard);
            var flag = fixture.Initializer.SetLoadedAnimationFlag(source, invocation, true, commonContext, guard);
            var capturedFlags = fixture.Initializer.Capture();
            Require((flag.After & FalloutLoadedAnimationSource.AnimationReferenceFlag) != 0 &&
                (flag.Before & ~FalloutLoadedAnimationSource.AnimationReferenceFlag) == (flag.After & ~FalloutLoadedAnimationSource.AnimationReferenceFlag),
                "Loaded animation wrote another reference flag or used an independent word.");
            for (var cycle = 0; cycle < 3; cycle++)
            {
                capturedFlags = JsonSerializer.Deserialize<OpenNV.Runtime.World.Cells.FalloutReferenceScriptExtraInitializerSnapshot>(JsonSerializer.Serialize(capturedFlags))!;
                OpenNV.Runtime.World.Cells.FalloutReferenceScriptExtraInitializer.Validate(capturedFlags, fixture.Initializer.Source, fixture.Stack, fixture.Actor);
            }
            Reject(() => OpenNV.Runtime.World.Cells.FalloutReferenceScriptExtraInitializer.Validate(capturedFlags with
                { AnimationFlagWrites = null }, fixture.Initializer.Source, fixture.Stack, fixture.Actor));
            fixture.Fields.ObserveEnteredFirstPersonAnimationLoad(invocation, guard);
            Reject(() => fixture.Fields.StoreConstructedFirstPersonAnimation(invocation, commonContext, guard));
            var first = new FalloutSourceAnimationContextState(source.Tail, invocation, guard, () => { });
            Require(fixture.Fields.StoreConstructedFirstPersonAnimation(invocation, first, guard).After == first.Identity,
                "First pointer did not store its actual independently allocated context.");
            var outer = fixture.Scene.ThreadContext.EnterAnimationInitialization(Guid.NewGuid(), Guid.NewGuid(),
                FalloutLoadedAnimationScopeKind.PlayerLoad, () => guard(invocation));
            var inner = fixture.Scene.ThreadContext.EnterAnimationInitialization(Guid.NewGuid(), first.Factory,
                FalloutLoadedAnimationScopeKind.Bind, () => guard(invocation));
            Require(inner.Scope.BeforeKind == 52 && inner.Scope.EnteredKind == 51 && inner.Parent == outer.Scope.Invocation,
                "Real animation binding did not nest inside the actual Player kind.");
            Reject(() => fixture.Scene.ThreadContext.ReturnAnimationInitialization(outer, () => guard(invocation)));
            inner = fixture.Scene.ThreadContext.ReturnAnimationInitialization(inner, () => guard(invocation));
            outer = fixture.Scene.ThreadContext.ReturnAnimationInitialization(outer, () => guard(invocation));
            Require(inner.Scope.RestoredKind == 52 && outer.Scope.RestoredKind == outer.Scope.BeforeKind,
                "Nested animation scopes did not restore their actual prior kinds.");
            first.Retire(); commonContext.Retire();
            var healthy = true; var releases = 0; var retiredBindings = 0;
            var node = new FalloutReferenceModelDataNode(new(invocation.ReferenceProcess, logical.Factory, 123), logical,
                () => { if (!healthy) throw new InvalidOperationException("authored-failed-model-child"); },
                () => ++releases, () => ++retiredBindings, () => guard(invocation));
            var failedContext = new FalloutSourceAnimationContextState(source.Tail, invocation, guard,
                () => { if (!healthy) throw new InvalidOperationException("authored-failed-lifetime"); });
            failedContext.EnterModelBinding(invocation, node, guard);
            Reject(node.RetireBinding);
            healthy = false;
            failedContext.Retire(); failedContext.Retire();
            Require(failedContext.Retired && failedContext.RetainedModelFactory is null && releases == 1 && retiredBindings == 1,
                "Failed AnimData did not drain its exact retained graph before retiring the native binding once.");
        });
        Console.WriteLine("OPENNV_LOADED_ANIMATION_CONTRACT_PASS pureCSharpPose=true sourceResourceCleanup=true independentPointers=true nestedKinds=true coldClock3=true gameplay=UNEXECUTED");
        static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    }
    internal static void AuditOwnedLoadedPlayerAnimation(string executable, FalloutPlayerReturnTailSource tail)
    {
        var source = FalloutExecutableStringTable.ReadLoadedPlayerAnimation(executable, tail);
        Require(source.Groups.Count == 183 && source.NodeNames.Count == 8 && source.ResourceNodeNames.Count == 5,
            "Owned source lost the actual animation group/node tables.");
        Console.WriteLine($"OPENNV_OWNED_LOADED_ANIMATION_SOURCE_PASS groups={source.Groups.Count} nodes={source.NodeNames.Count} resourceNodes={source.ResourceNodeNames.Count} source={source.Identity} gameplay=UNEXECUTED");
    }
    private static FalloutLoadedAnimationSource AuthoredLoadedAnimation(FalloutPlayerReturnTailSource tail) =>
        new(tail, FalloutLoadedAnimationSource.ContractDigest,
            ["FixtureRoot", null, null, null, null, null, null, null], ["", "Sneak", "Swim", "Fly"],
            ["", "H2H", "1HM", "2HM", "1HP", "2HR", "2HA", "2HH", "2HL", "1GT", "1MD", "1LM"],
            Enumerable.Range(0, 183).Select(index => new FalloutLoadedAnimationGroup((byte)index,
                index == 0 ? "Idle" : "AuthoredGroup" + index, false, 1, [0, 1, -1, 0, 0])).ToArray(),
            "FixtureRoot", ["FixtureRoot", "FixtureLeft", "FixtureRight", "FixtureHead", "FixtureWeapon"]);
    private static FalloutNifFile LoadedAnimationFixture(string target)
    {
        byte[] Bytes(Action<BinaryWriter> emit)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); emit(writer); return stream.ToArray(); }
        var blocks = new[]
        {
            (Type: "NiControllerSequence", Data: Bytes(writer =>
            {
                writer.Write(0); writer.Write(1); writer.Write(0); writer.Write(1); writer.Write(-1); writer.Write((byte)7);
                writer.Write(1); writer.Write(-1); writer.Write(2); writer.Write(-1); writer.Write(-1);
                writer.Write(1f); writer.Write(-1); writer.Write(0U); writer.Write(1f); writer.Write(0f); writer.Write(2f);
                writer.Write(-1); writer.Write(1); writer.Write((ushort)0);
            })),
            (Type: "NiTransformInterpolator", Data: Bytes(writer =>
            {
                writer.Write(1f); writer.Write(2f); writer.Write(3f);
                for (var index = 0; index < 5; index++) writer.Write(float.MinValue);
                writer.Write(-1);
            }))
        };
        var strings = new[] { "Idle", target, "NiTransformController" };
        var bytes = Bytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Length);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Data.Length);
            writer.Write(strings.Length); writer.Write(strings.Max(value => value.Length));
            foreach (var value in strings) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Data); writer.Write(1U); writer.Write(0);
        });
        return FalloutNifFile.Read(bytes);
    }
}
