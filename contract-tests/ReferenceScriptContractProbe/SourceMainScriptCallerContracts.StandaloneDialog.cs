using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunStandaloneDialogContracts()
    {
        var source = FalloutStandaloneInterfaceSource.Read(FalloutMainScriptCallerSource.Read(new(
            FalloutSourceMainFamily.Fallout3, new('1', 64), FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3))));
        const string root = "<menu name='Authored'><class>&DialogMenu;</class><stackingtype>&no_click_past;</stackingtype>" +
            "<include src='authored-controls.xml'/><rect name='LastZero'><id>0</id></rect>" +
            "<template name='NotReturned'><rect name='TemplateZero'><id>0</id></rect></template></menu>";
        const string controls = "<rect name='FirstZero'><id>0</id></rect><text name='Speaker'><id>1</id></text>" +
            "<text name='Response'><id>2</id></text><hotrect name='Topics'><id>3</id></hotrect>" +
            "<rect name='NoGlow'><id>&noglow_branch;</id></rect><image name='Scrollbar'><id>&generic;</id></image>";
        FalloutStandaloneDialogDocument Document(string menu = root, string returned = controls) => FalloutStandaloneDialogSource.Read(path =>
            Encoding.UTF8.GetBytes(path == FalloutStandaloneDialogSource.Path ? menu : path == "menus/prefabs/authored-controls.xml" ? returned :
                throw new FileNotFoundException("No authored dependency.", path)));
        var document = Document();
        var declaration = FalloutStandaloneDialogSource.Bind(source, "authored-stack", document);
        Require(declaration.Resources.Count == 2 && declaration.Controls.Count == 7 &&
            declaration.Controls.Count(row => row.Id == 0) == 2 && declaration.Controls.Any(row => row.Id == uint.MaxValue) &&
            declaration.Controls.Any(row => row.Id == 111) && FalloutStandaloneDialogSource.Registers(declaration.StackingBits),
            "Actual menu expansion lost prefab, ignored control, or callback-overwrite identities.");
        Reject(() => Document(root.Replace("authored-controls.xml", "../outside.xml", StringComparison.Ordinal)));
        Reject(() => FalloutStandaloneDialogSource.Bind(source, "authored-stack", Document(root.Replace("&DialogMenu;", "&OtherMenu;", StringComparison.Ordinal))));
        Reject(() => FalloutStandaloneDialogSource.Bind(source, "authored-stack", Document(root.Replace("&no_click_past;", "NaN", StringComparison.Ordinal))));
        Reject(() => FalloutStandaloneDialogSource.Validate(source, "another-stack", declaration));
        Reject(() => FalloutStandaloneDialogSource.Validate(source, "authored-stack", declaration with { Contract = new('a', 64) }));

        var state = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid());
        var manager = new AuthoredInterfaceObject();
        state.PublishManager(manager.Publication(state, FalloutStandaloneInterfaceObjectRole.PipBoyManager), manager.ReadLiving);
        var native = new AuthoredInterfaceObject();
        var dialog = state.ConstructDialog(native.Publication(state, FalloutStandaloneInterfaceObjectRole.DialogMenu) with
            { ResourceSha256 = declaration.Resources[0].Sha256 }, native.ReadLiving);
        state.PublishDialogTiles(dialog, declaration, native.ReadLiving);
        var constructed = state.ReadDialogState(dialog);
        Require(constructed.Root is null && constructed.Lifecycle == 4 && constructed.Flags == 256 && constructed.MenuIdentity == 0 &&
            constructed.Controls[0] == constructed.ReturnedTiles.Last(row => row.Id == 0).Identity,
            "Menu construction invented a bound root or ignored the original last returned field store.");
        var child = new AuthoredDialogChildren(state) { SceneFailure = true };
        Reject(() => state.OpenDialog(dialog, new("Authored.esm", 7), child));
        var failed = state.Capture();
        Require(failed.Mode == 3 && failed.MenuSlots[0] == 1009 && failed.Menus.StackWrites.Single() is
            { Slot: 0, Returned: false, RequiresSceneByte: true, Failure: not null } &&
            failed.Menus.Dialogs.Single() is { Step: FalloutStandaloneDialogStep.SceneByteEntered, Speaker: null, Failure: not null } &&
            child.ActorEntries == 0,
            "Failed dependent scene child rolled back a committed stack prefix or skipped to actor publication.");
        Reject(() => state.OpenDialog(dialog, new("Authored.esm", 7), child));
        Reject(() => FalloutStandaloneInterfaceState.RequirePlayable(failed));
        var write = failed.Menus.StackWrites.Single();
        Reject(() => FalloutStandaloneInterfaceState.Validate(failed with { Menus = failed.Menus with
            { StackWrites = [write with { Returned = true }] } }));
        Reject(() => FalloutStandaloneInterfaceState.Validate(failed with { Mode = 1, MenuSlots = new uint[10] }));
        state.RetireDialog(dialog); native.Retire();
        var retired = state.Capture();
        Require(retired.Mode == 3 && retired.Failure == failed.Failure && retired.Menus.Dialogs.Single().Retired is not null,
            "Native retirement manufactured original stack removal or cleared the entered source error.");
        Reject(() => FalloutStandaloneInterfaceState.RequirePlayable(retired));
        state.Retire(); manager.Retire();

        CheckAuthoredDialogBranch(source, Document(root, controls.Replace("<id>2</id>", "<id>22</id>", StringComparison.Ordinal)),
            FalloutStandaloneDialogStep.ControlsRequired, expectedActorEntries: 0);
        CheckAuthoredDialogBranch(source, Document(root.Replace("&no_click_past;", "&click_past;", StringComparison.Ordinal)),
            FalloutStandaloneDialogStep.ActorPreparationEntered, expectedActorEntries: 1, expectedPushes: 0);
        var swallowing = AuthoredDialog(source, declaration);
        Reject(() => swallowing.State.OpenDialog(swallowing.Dialog, new("Authored.esm", 7),
            new AuthoredDialogChildren(swallowing.State) { SwallowSceneBoundary = true }));
        Require(!swallowing.State.Capture().Menus.StackWrites.Single().Returned,
            "A swallowed source-child boundary certified its original native return.");
        swallowing.State.Retire(); swallowing.Native.Retire(); swallowing.Manager.Retire();

        var quiet = AuthoredDialog(source, declaration);
        quiet.State.RetireDialog(quiet.Dialog); quiet.Native.Retire();
        var quietSave = quiet.State.Capture(); FalloutStandaloneInterfaceState.RequirePlayable(quietSave);
        var cold = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid(), quietSave);
        Reject(() => cold.Read(FalloutStandaloneInterfaceQuery.MenuGate));
        var coldManager = new AuthoredInterfaceObject(quiet.Manager.NativeInstance);
        cold.PublishManager(coldManager.Publication(cold, FalloutStandaloneInterfaceObjectRole.PipBoyManager), coldManager.ReadLiving);
        var coldSave = cold.Capture(); FalloutStandaloneInterfaceState.RequirePlayable(coldSave);
        Require(coldSave.Menus.Dialogs.Single().Process == quiet.State.Process && coldSave.CapturedProcess != quiet.State.Process &&
            coldSave.Menus.Dialogs.Single().Retired is not null && coldSave.Menus.Dialogs.Single().Root is null,
            "Cold native rebind promoted historical source controls into a current menu root.");
        Reject(() => FalloutStandaloneInterfaceState.Validate(coldSave with { Menus = coldSave.Menus with
            { Dialogs = [coldSave.Menus.Dialogs.Single() with { Process = coldSave.CapturedProcess }] } }));
        cold.Retire(); coldManager.Retire(); quiet.State.Retire(); quiet.Manager.Retire();

        var reentrant = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid());
        var reentrantNative = new AuthoredInterfaceObject();
        Reject(() => reentrant.PublishManager(reentrantNative.Publication(reentrant, FalloutStandaloneInterfaceObjectRole.PipBoyManager), () =>
        { Reject(reentrant.Retire); return reentrantNative.ReadLiving(); }));
        Require(reentrant.Capture().OwnManager is null && reentrant.Capture().Failure is not null,
            "Swallowed native-reader reentry published a false current factory return.");
        reentrant.Retire(); reentrantNative.Retire();

        Require(FalloutStandaloneMenuInput.ConstructorWord(uint.MaxValue) == 252 &&
            FalloutStandaloneMenuInput.Bind(0xdeadbeef, 17, 1009, 0x87654321, false) is { Word: 0x65432122, Deferred: false, QueriedMessage: true } &&
            FalloutStandaloneMenuInput.Bind(0, 17, 1009, 0, true) is { Word: 0x21, Deferred: true } &&
            FalloutStandaloneMenuInput.Bind(2, 17, 1009, 0, true) is { Word: 0x22, Deferred: false } &&
            FalloutStandaloneMenuInput.Bind(0xdeadbeef, 29, 1009, 0, null) is { Word: 0xdeadbeef, QueriedMessage: false } &&
            FalloutStandaloneMenuInput.Bind(uint.MaxValue, 17, 1000, 0x1000000, null) is { Word: 254, QueriedMessage: false },
            "Source menu control widths, class bits, message lookup or constructor masking changed.");
        Reject(() => FalloutStandaloneMenuInput.Bind(0, 17, 1009, 0, null));
        var quarter = FalloutStandaloneMenuTransitions.Advance(0, Bits(1), Bits(.5f), 4, Bits(2));
        Require(quarter.Elapsed == Bits(.25f) && quarter.Progress == Bits(.25f) && !quarter.Remove &&
            FalloutStandaloneMenuTransitions.Duration(0x7fc00001) == 0 && FalloutStandaloneMenuTransitions.Duration(0x80000000) == 0,
            "Original Float32 transition transport lost independent scaling, NaN or signed-zero operand order.");
        var reached = FalloutStandaloneMenuTransitions.Advance(0, Bits(1), Bits(2), 0, null);
        var zeroOverZero = FalloutStandaloneMenuTransitions.Advance(0, 0, 0, 0, null);
        Require(reached.Progress == Bits(1) && reached.Remove && float.IsNaN(BitConverter.UInt32BitsToSingle(zeroOverZero.Progress)) &&
            !zeroOverZero.Remove, "Transition exhaustion or unordered arithmetic was fabricated as a native return.");
        Reject(() => FalloutStandaloneMenuTransitions.Advance(0, Bits(1), Bits(1), 4, null));
        Reject(() => FalloutStandaloneMenuTransitions.Advance(0, Bits(1), Bits(1), 0, Bits(1)));
        Console.WriteLine("OPENNV_FO3_SOURCE_DIALOG_PASS authoredOnly=true expandedSourceControls=true committedFailure=true " +
            "sourceOverwriteOrder=true realNativeReentryRefusal=true sourceInputWidths=true floatTransportOnly=true nativeGameplay=UNEXECUTED");
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static (FalloutStandaloneInterfaceState State, FalloutStandaloneInterfaceObject Dialog,
        AuthoredInterfaceObject Manager, AuthoredInterfaceObject Native) AuthoredDialog(FalloutStandaloneInterfaceSource source,
        FalloutStandaloneDialogDeclaration declaration)
    {
        var state = new FalloutStandaloneInterfaceState(source, "authored-stack", Guid.NewGuid());
        var manager = new AuthoredInterfaceObject();
        state.PublishManager(manager.Publication(state, FalloutStandaloneInterfaceObjectRole.PipBoyManager), manager.ReadLiving);
        var native = new AuthoredInterfaceObject();
        var dialog = state.ConstructDialog(native.Publication(state, FalloutStandaloneInterfaceObjectRole.DialogMenu) with
            { ResourceSha256 = declaration.Resources[0].Sha256 }, native.ReadLiving);
        state.PublishDialogTiles(dialog, declaration, native.ReadLiving);
        return (state, dialog, manager, native);
    }
    private static void CheckAuthoredDialogBranch(FalloutStandaloneInterfaceSource source, FalloutStandaloneDialogDocument document,
        FalloutStandaloneDialogStep expected, int expectedActorEntries, int expectedPushes = 1)
    {
        var fixture = AuthoredDialog(source, FalloutStandaloneDialogSource.Bind(source, "authored-stack", document));
        var child = new AuthoredDialogChildren(fixture.State);
        Reject(() => fixture.State.OpenDialog(fixture.Dialog, new("Authored.esm", 7), child));
        var captured = fixture.State.Capture();
        Require(captured.Menus.Dialogs.Single().Step == expected && captured.Menus.StackWrites.Count == expectedPushes &&
            child.ActorEntries == expectedActorEntries, "Unrelated source stacking/control arm crossed the wrong real consumer.");
        fixture.State.Retire(); fixture.Native.Retire(); fixture.Manager.Retire();
    }
    private sealed class AuthoredDialogChildren(FalloutStandaloneInterfaceState state) : IFalloutStandaloneDialogChildren
    {
        public string Owner => "authored-actual-Dialog-child-value-proof";
        public string Source => state.Source.Identity;
        internal bool SceneFailure, SwallowSceneBoundary;
        internal int ActorEntries;
        public void StoreMenuSceneByte(FalloutStandaloneDialogInvocation invocation, byte value)
        {
            invocation.Require(FalloutStandaloneDialogStep.SceneByteEntered);
            Require(invocation.Owner == state && value == 1, "Authored source scene child changed invocation or store.");
            if (SwallowSceneBoundary) Reject(() => state.EnterDialogBoundary(invocation, "authored-missing-scene-child"));
            if (SceneFailure) throw new IOException("Authored independent scene child failed after the real slot store.");
        }
        public void StoreTileSelectionLocus(FalloutStandaloneDialogInvocation invocation, Guid tile, uint valueBits) =>
            throw new InvalidOperationException("The independently constructed null selection must not issue a tile write.");
        public void PrepareActors(FalloutStandaloneDialogInvocation invocation, FalloutFormKey speaker)
        {
            invocation.Require(FalloutStandaloneDialogStep.ActorPreparationEntered); ActorEntries++;
            state.EnterDialogBoundary(invocation, "authored-original-actor-child-unowned");
        }
    }
}
