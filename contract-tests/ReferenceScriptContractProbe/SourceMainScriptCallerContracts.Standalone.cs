using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunStandaloneMainContracts()
    {
        StandaloneDeclarations(); StandaloneCurrentAndCold(); StandalonePendingRefusal();
        Console.WriteLine("OPENNV_FO3_SOURCE_MAIN_PLAYER_PASS authoredOnly=true independentMain=true noSteam=true " +
            "interiorSkipsFistp=true coldNoReplay=true mandatoryCurrentOwner=true " +
            "sourcePendingDifferences=true taskUiSceneTls=UNOWNED nativeGameplay=UNEXECUTED");
    }
    private static void StandaloneDeclarations()
    {
        var field = new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3));
        field.Validate();
        Reject(() => (field with { ContractSha256 = FalloutImmediateScriptSource.CurrentContractSha256 }).Validate());
        Reject(() => { using var interpreter = new FalloutScriptEngineContexts(field); });
        Reject(() => FalloutMainScriptCallerSource.Read(field with { EngineSha256 = new('a', 64) }));
        var main = FalloutMainScriptCallerSource.Read(field); var player = FalloutMainPlayerCellSource.Read(main);
        Require(!main.HasNewVegasChildren && !player.HasOuterPendingScalar && !player.HasPlayerMovementBracket && !player.HasSourceRootStore,
            "FO3 imported FNV's absent Main/Player source arms.");
        Require(FalloutSourceMainFamily.GridOperation(FalloutSourceMainFamily.Fallout3) == FalloutFistpOperation.Convert &&
            FalloutSourceMainFamily.GridOperation(Engine) == FalloutFistpOperation.SourceArgument,
            "FO3's direct Float32 stack transport acquired FNV's extra x87 argument conversion.");
        Require(FalloutSourceMainFamily.PendingFinalFlagRequired(FalloutSourceMainFamily.Fallout3, 0, 2) &&
            !FalloutSourceMainFamily.PendingFinalFlagRequired(Engine, 0, 2) &&
            !FalloutSourceMainFamily.PendingFinalFlagRequired(FalloutSourceMainFamily.Fallout3, 2, 0) &&
            FalloutSourceMainFamily.PendingFinalFlagValue(FalloutSourceMainFamily.Fallout3, 0x80) == 1 &&
            FalloutSourceMainFamily.PendingFinalFlagValue(Engine, 0x80) == 0x81,
            "Selected pending flag predicate/whole-byte write lost the actual engine distinction.");
        var pending = FalloutMainPlayerPendingSource.Read(player);
        var raw = FalloutPlayerRawTransferSource.Read(pending);
        var key = new FalloutFormKey("Authored.esm", 0x44);
        var receipt = new FalloutPlayerRawTransferReceipt(raw, FalloutPlayerRawTransferWriter.MoveTo, Guid.NewGuid(),
            key, new('a', 64), new("Authored.esm", 0x45), new('b', 64),
            new(new("Authored.esm", 0x46), 0, new('c', 64), null, null), 1,
            FalloutPlayerTransferVector.Read([123.5f, -4095.75f, 9f]), FalloutPlayerTransferVector.Read([.25f, .5f, .75f]),
            FalloutPlayerTransferVector.Read([1f, 2f, 3f]), "authored-actual-placement-provider");
        var payload = FalloutPlayerRawTransferFactory.MoveTo(receipt);
        Require(payload.PositionX == BitConverter.SingleToUInt32Bits(124.5f) && payload.PositionY == BitConverter.SingleToUInt32Bits(-4093.75f) &&
            payload.RotationX == receipt.TargetRotation.X && payload.RotationY == 0 && payload.RotationZ == receipt.TargetRotation.Z &&
            payload.TransferArgument == 1 && payload.Callback is null && payload.Furniture == receipt.Target && payload.Cell == receipt.ParentCell.Cell && payload.SourceTailWord == 0,
            "FO3 MoveTo constructor/writer lost its captured vector, zero middle rotation or real target/furniture cells.");
        Reject(() => FalloutPlayerRawTransferFactory.Require(payload with { TransferArgument = 0 }, raw));
        Reject(() => FalloutPlayerRawTransferFactory.Require(payload with { SourceTailWord = 8 }, raw));
        var door = FalloutPlayerRawTransferFactory.Door(receipt with { Writer = FalloutPlayerRawTransferWriter.Door,
            Offsets = new(0, 0, 0) });
        Require(door.TransferArgument == 0 && door.Callback?.Context == receipt.RequestSource && door.Furniture is null &&
            door.RotationY == receipt.TargetRotation.Y, "FO3 directed door acquired MoveTo fields or omitted its actual callback/context.");
        var slot = new FalloutPlayerPendingSlot(); slot.BindSourceFamily(player);
        slot.Replaced += _ => throw new InvalidOperationException("FNV overlap assertion is not a FO3 setter child.");
        var first = slot.StoreSource(FalloutPlayerPendingKind.MoveTo, new(key, receipt.Target, 1, 2, 3), null, "first", payload);
        var second = slot.StoreSource(FalloutPlayerPendingKind.MoveTo, new(key, receipt.Target, 1, 2, 3), null, "second", payload);
        Require(slot.Next == second && slot.Capture().Replacement?.Released == first.Identity,
            "FO3 one-slot replacement invoked an absent source assertion or retained FIFO work.");
    }
    private static void StandaloneCurrentAndCold()
    {
        using var warm = new StandaloneFixture();
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(10, .02f).GetAwaiter().GetResult();
        var runtime = warm.Owner.Capture(); var main = runtime.StandaloneMain!;
        FalloutActorProcessRuntimeState.RequireStandaloneMainPlayable(runtime);
        Require(main.MainCaller.LastCall is { Disposition: FalloutMainScriptCallerDisposition.ScopeReturned } &&
            !main.MainCaller.LastCall.Children.Any(child => child.Step is FalloutMainScriptCallerStep.ClockPrelude or FalloutMainScriptCallerStep.SteamCallbacks) &&
            main.MainField is { Frame: 1, LastSite: FalloutMainScriptSampleSite.AfterMainChildren } &&
            main.PlayerCell.LastCall is { Disposition: FalloutMainPlayerCellDisposition.CellUnchanged } &&
            runtime.Fistp.Conversions == 0 && main.PlayerCell.PlayerBracket is null && main.PlayerCell.RootBinding is null &&
            main.PlayerCell.PendingConsumers.ExteriorLoaders is null && main.Constructor is not null,
            "FO3 admitted an excluded source arm, borrowed FNV loaders or failed to construct/return its actual Main child.");
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainPlayerCell(main.PlayerCell with { PlayerBracket = false }));
        Reject(() => FalloutActorProcessRuntimeState.RequireStandaloneMainPlayable(runtime with { StandaloneMain = null }));
        Reject(() => FalloutActorProcessRuntimeState.ValidateStandaloneMain(main with { Source = main.Source with { RuntimeSha256 = new('7', 64) } }, runtime));
        var changed = main.MainCaller.LastCall! with { Children = main.MainCaller.LastCall.Children.Select(child =>
            child.Step == FalloutMainScriptCallerStep.TimedContexts ? child with { Step = FalloutMainScriptCallerStep.SteamCallbacks } : child).ToArray() };
        Reject(() => FalloutActorProcessRuntimeState.ValidateMainScriptCaller(main.MainCaller with { LastCall = changed }));
        using var cold = new StandaloneFixture(runtime, warm.Fade.Capture());
        var before = cold.Owner.Capture().StandaloneMain!;
        Require(before.Constructor is null && before.PreviousConstructor == main.Constructor && before.LastPrelude == main.LastPrelude &&
            before.MainCaller.LastCall == main.MainCaller.LastCall && cold.Host.Log.Count == 0,
            "FO3 cold construction replayed Prologue/Player or promoted a native/current singleton identity.");
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        var next = cold.Owner.Capture(); FalloutActorProcessRuntimeState.RequireStandaloneMainPlayable(next);
        Require(next.StandaloneMain!.Constructor is { } current && current.Process == next.CapturedProcess &&
            current.Identity != main.Constructor!.Identity && next.StandaloneMain.MainCaller.Calls == 2,
            "FO3 next actual getter did not acquire a fresh process constructor while retaining source invocation order.");
    }
    private static void StandalonePendingRefusal()
    {
        using var fixture = new StandaloneFixture();
        var state = fixture.Owner.MainPlayerPendingConsumers;
        var source = FalloutMainPlayerPendingSource.Read(fixture.Owner.MainPlayerCellSource);
        fixture.Slot.StoreSource(FalloutPlayerPendingKind.Empty, null, null, "authored-FO3-empty-pending-allocation",
            new(source.Contract, "authored-complete-raw-empty-payload", null, null, null, 0, 0, 0, 0, 0, 0, 0, null, null, SourceTailWord: 0));
        var consumers = new AuthoredPendingTail(source.Player, state);
        using (fixture.Owner.BindMainPlayerCell(consumers, "authored-FO3-pending-refusal"))
        using (fixture.Owner.BindMainScriptCaller(fixture.Host, "authored-FO3-main-refusal"))
            Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        var captured = fixture.Owner.Capture(); var player = captured.StandaloneMain!.PlayerCell;
        Require(player.LastCall is { Disposition: FalloutMainPlayerCellDisposition.Failed } &&
            player.LastCall.Children.Last() is { Step: FalloutMainPlayerCellStep.PendingWorldPrelude, Returned: null } &&
            player.Pending.Pending is not null && player.PendingConsumers.ExteriorLoaders is null &&
            player.PendingConsumers.Error!.Contains("FO3-TaskManager", StringComparison.Ordinal),
            "Missing FO3 TaskManager was replaced by an empty FNV map or falsely returned/null-stored work.");
        Reject(() => FalloutActorProcessRuntimeState.RequireStandaloneMainPlayable(captured));
        Reject(() => fixture.Owner.ExecuteMainScriptCaller(2, .02f).GetAwaiter().GetResult());
    }
    private sealed class StandaloneFixture : IDisposable
    {
        internal readonly FalloutActorProcessRuntimeState Owner;
        internal readonly FalloutPlayerPendingSlot Slot = new();
        internal readonly FalloutInterfaceFade Fade;
        internal readonly Host Host;
        private readonly IDisposable _fadeLease;
        private readonly AuthoredPlayerCell _player;
        internal StandaloneFixture(FalloutActorProcessRuntimeSnapshot? saved = null, FalloutInterfaceFadeSnapshot? fade = null)
        {
            const string engine = FalloutSourceMainFamily.Fallout3;
            var field = new FalloutImmediateScriptSource(engine, new('1', 64), FalloutImmediateScriptSource.ContractForEngine(engine));
            var main = FalloutMainScriptCallerSource.Read(field);
            FalloutFormKey Key(uint id) => new("Authored.esm", id);
            FalloutCombatActorIdentity Identity(FalloutFormKey key) => new(key, Key(7), "ENGINE_PLAYER", 0, engine, "NPC_", 0, new('4', 64), true);
            Owner = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(engine), "authored-FO3-Main", Key(0x14), Identity, saved);
            Owner.ConstructStandaloneMain(main, Slot, saved?.StandaloneMain, saved);
            var rows = new[] { new FalloutInterfaceFadeCatalogRow(0, FalloutInterfaceFadeRoot.Primary, "textures/interface/faders/a.dds"),
                new FalloutInterfaceFadeCatalogRow(1, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/a.dds"),
                new FalloutInterfaceFadeCatalogRow(2, FalloutInterfaceFadeRoot.Secondary, "textures/interface/faders/b.dds") };
            var source = new FalloutInterfaceFadeSource(new(engine, FalloutInterfaceFadeArithmetic.Float32EachOperation, rows), field.RuntimeSha256,
                rows.Select(row => new FalloutInterfaceFadeTexture(row.Channel, row.TexturePath, new('f', 64))).ToArray(), FalloutInterfaceFadeSource.CurrentContractSha256);
            Fade = new(source, fade); Fade.BindNative(new(_ => { }, _ => { }, _ => { }));
            _fadeLease = Owner.BindScriptFrameFade(Fade);
            Host = new(main) { DuringPrologue = Owner.ExecuteStandaloneMainPrologue, AwaitPlayer = Owner.ExecuteMainPlayerCell };
            _player = new(Owner.MainPlayerCellSource);
        }
        internal IDisposable Bind() => new StandaloneLeases(Owner.BindMainPlayerCell(_player, "authored-FO3-player"),
            Owner.BindMainScriptCaller(Host, "authored-FO3-main"));
        public void Dispose() { _fadeLease.Dispose(); Owner.Dispose(); }
    }
    private sealed class StandaloneLeases(IDisposable player, IDisposable main) : IDisposable
    {
        public void Dispose() { main.Dispose(); player.Dispose(); }
    }
}
