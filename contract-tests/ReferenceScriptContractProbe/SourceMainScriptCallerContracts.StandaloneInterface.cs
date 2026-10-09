using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunStandaloneInterfaceContracts()
    {
        RunStandaloneDialogContracts();
        var immediate = new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3));
        var source = FalloutStandaloneInterfaceSource.Read(FalloutMainScriptCallerSource.Read(immediate));
        Reject(() => FalloutStandaloneInterfaceSource.Read(FalloutMainScriptCallerSource.Read(new(Engine, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(Engine)))));
        Reject(() => (source with { Contract = new('a', 64) }).Validate());
        Require(!FalloutStandaloneInterfaceSource.MenuGate(0, 3) && !FalloutStandaloneInterfaceSource.MenuGate(1, 1) &&
            FalloutStandaloneInterfaceSource.MenuGate(1, 0x10001) && FalloutStandaloneInterfaceSource.GuiModeTwo(2) &&
            !FalloutStandaloneInterfaceSource.GuiModeTwo(0x10002) && FalloutStandaloneInterfaceSource.ContextKind(0x10003) == 3 &&
            !FalloutStandaloneInterfaceSource.ConsoleOpen(-1) && !FalloutStandaloneInterfaceSource.ConsoleOpen(0) &&
            FalloutStandaloneInterfaceSource.ConsoleOpen(1), "Interface source widths/signed counter were replaced by menu/gameplay Booleans.");
        var first = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid());
        var constructor = first.Capture();
        Require(constructor.Enabled == 0 && constructor.Mode == 1 && constructor.Context == 0 && constructor.OwnManager is null &&
            constructor.DialogByte == 0 && constructor.Console == FalloutConsolePresence.Absent && constructor.MenuSlots.All(value => value == 0),
            "Original interface constructor invented a native manager, dialog or active menu.");
        Reject(() => FalloutStandaloneInterfaceState.RequirePlayable(constructor));
        var manager = new AuthoredInterfaceObject();
        var published = first.PublishManager(manager.Publication(first, FalloutStandaloneInterfaceObjectRole.PipBoyManager), manager.ReadLiving);
        Require(first.Read(FalloutStandaloneInterfaceQuery.MenuGate) == 0 && first.Read(FalloutStandaloneInterfaceQuery.GuiModeTwo) == 0 &&
            first.Read(FalloutStandaloneInterfaceQuery.FirstPredicate) == 0 && first.Read(FalloutStandaloneInterfaceQuery.FinalPredicate) == 0 &&
            first.Read(FalloutStandaloneInterfaceQuery.ForeignMenu) == 0 && first.Read(FalloutStandaloneInterfaceQuery.ContextKind) == 1,
            "Returned manager changed independent quiet interface predicates.");
        var dialogOwner = new AuthoredInterfaceObject();
        var dialog = first.ConstructDialog(dialogOwner.Publication(first, FalloutStandaloneInterfaceObjectRole.DialogMenu), dialogOwner.ReadLiving);
        Require(first.Read(FalloutStandaloneInterfaceQuery.FirstPredicate) == 1,
            "Actual dialog factory was confused with a Pip-Boy open or conversation Boolean.");
        Reject(() => first.CloseDialog(dialog with { Process = Guid.NewGuid() }));
        first.CloseDialog(dialog);
        Require(first.Read(FalloutStandaloneInterfaceQuery.FirstPredicate) == 0 && first.Capture().Dialog?.Retired is null,
            "Source dialog close was inferred to destroy its still-living native object.");
        Reject(() => FalloutStandaloneInterfaceState.RequirePlayable(first.Capture()));
        first.RetireDialog(dialog); dialogOwner.Retire();
        var saved = first.Capture(); FalloutStandaloneInterfaceState.RequirePlayable(saved);
        Reject(() => FalloutStandaloneInterfaceState.Validate(saved with { Mode = 4 }));
        Reject(() => FalloutStandaloneInterfaceState.Validate(saved with { ActiveMenus = Enumerable.Repeat((byte)1, 60).ToArray() }));
        Reject(() => FalloutStandaloneInterfaceState.Validate(saved with { ManagerFields = saved.ManagerFields! with { Selection = 0 } }));
        Reject(() => new FalloutStandaloneInterfaceState(source, "another-stack", Guid.NewGuid(), saved));
        Reject(() => new FalloutStandaloneInterfaceState(source, "authored-stack", first.Process, saved));
        var cold = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid(), saved);
        Reject(() => cold.Read(FalloutStandaloneInterfaceQuery.MenuGate));
        Reject(() => cold.PublishManager(manager.Publication(first, FalloutStandaloneInterfaceObjectRole.PipBoyManager), manager.ReadLiving));
        var coldNative = new AuthoredInterfaceObject(manager.NativeInstance); // Native IDs can recur in another process; factories cannot.
        var rebound = cold.PublishManager(coldNative.Publication(cold, FalloutStandaloneInterfaceObjectRole.PipBoyManager), coldNative.ReadLiving);
        Require(rebound.Identity != published.Identity && rebound.Process != published.Process && cold.Capture().ColdHandoff?.PreviousProcess == first.Process,
            "Cold interface replayed an old native pointer or omitted the genuine process handoff.");
        var current = cold.Capture(); FalloutStandaloneInterfaceState.RequirePlayable(current);
        coldNative.Retire(); Reject(() => cold.Read(FalloutStandaloneInterfaceQuery.MenuGate)); Reject(() => cold.Capture());
        cold.Retire(); first.Retire(); manager.Retire();
        var wrong = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid());
        var other = new AuthoredInterfaceObject();
        Reject(() => wrong.PublishManager(other.Publication(wrong, FalloutStandaloneInterfaceObjectRole.DialogMenu), other.ReadLiving));
        Reject(() => wrong.PublishManager(other.Publication(wrong, FalloutStandaloneInterfaceObjectRole.PipBoyManager), () =>
        { wrong.Retire(); return other.ReadLiving(); }));
        Require(!wrong.Capture().Retired && wrong.Capture().OwnManager is null,
            "Factory lifetime callback reentry retired or published the source constructor.");
        Reject(() => wrong.EnterUnownedConsumer("actual-unowned-tile-manager-return"));
        Reject(() => wrong.Read(FalloutStandaloneInterfaceQuery.MenuGate));
        Require(wrong.Capture().Boundary is not null, "Unknown original transition was silently cleared back to quiet mode.");
        wrong.Retire(); other.Retire();
        Console.WriteLine("OPENNV_FO3_SOURCE_INTERFACE_PASS authoredOnly=true exactObjectRoles=true actualLifecycle=true " +
            "signedConsole=true sourceWidths=true coldFreshFactory=true unownedMenuTransitionRefused=true nativeGameplay=UNEXECUTED");
    }
    private sealed class AuthoredInterfaceObject
    {
        private static ulong _next;
        private bool _living = true;
        internal ulong NativeInstance { get; }
        internal AuthoredInterfaceObject(ulong? native = null) => NativeInstance = native ?? ++_next;
        internal bool ReadLiving() => _living;
        internal void Retire() => _living = false;
        internal FalloutStandaloneInterfaceNativePublication Publication(FalloutStandaloneInterfaceState owner,
            FalloutStandaloneInterfaceObjectRole role, string stack = "authored-stack") => new(Guid.NewGuid(), owner.Process, owner.Source.Identity, stack, role,
                NativeInstance, role == FalloutStandaloneInterfaceObjectRole.DialogMenu ? "menus/dialog/dialog_menu.xml" : "menus/main/hud_main_menu.xml",
                new('a', 64), "actual-authored-object-lifetime");
    }
    private static AuthoredInterfaceObject PublishAuthoredStandaloneInterface(FalloutActorProcessRuntimeState runtime)
    {
        var native = new AuthoredInterfaceObject();
        var owner = runtime.StandaloneInterface;
        owner.PublishManager(native.Publication(owner, FalloutStandaloneInterfaceObjectRole.PipBoyManager, "authored-FO3-Main"), native.ReadLiving);
        return native;
    }
}
