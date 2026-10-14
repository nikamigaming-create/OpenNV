using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerPackageCommandFieldContracts()
    {
        using (var unclosed = new PackageFieldFixture(admitted: false))
        {
            Reject(() => unclosed.Call(invocation => unclosed.Fields.ReadCurrentPosition(invocation)));
            Reject(() => unclosed.Call(invocation => unclosed.Fields.MarkPackageEvent(invocation, unclosed.Package, 0x200)));
            Require(unclosed.Fields.SaveBlocker is not null && unclosed.Fields.Capture().Values.Position == new FalloutPackageRawPosition(0, 0, 0),
                "Constructor-only fields bypassed their unclosed pre-bootstrap writers.");
        }
        using (var independent = new PackageFieldFixture(admitted: false,
                   closedLanes: [FalloutPackageCommandFieldLane.ScriptExtra, FalloutPackageCommandFieldLane.Animation]))
        {
            var marker = independent.Call(invocation => independent.Fields.MarkPackageEvent(invocation, independent.Package, 0x200));
            var animation = independent.Call(invocation => independent.Fields.ReadAnimationContext(invocation, independent.ReadCommon, independent.ReadView));
            Require(marker is { Returned: true, Result: false } && animation is { Returned: true, Context: null } &&
                independent.Fields.Capture().Availability.Where(value => value.Available).Select(value => value.Lane)
                    .SequenceEqual(new[] { FalloutPackageCommandFieldLane.ScriptExtra, FalloutPackageCommandFieldLane.Animation }),
                "Independent source lane admission opened unrelated constructor position/cell fields.");
            Reject(() => independent.Call(invocation => independent.Fields.ReadCurrentPosition(invocation)));
        }
        using (var partial = new PackageFieldFixture())
        {
            var script = new FalloutFormKey("WeaponFixture.esm", 0x303);
            partial.FailAtGuardCall = 4;
            Reject(() => partial.Call(invocation => partial.Fields.AttachScript(invocation, script)));
            var saved = Roundtrip(partial.Fields.Capture());
            Require(saved.Values.ScriptExtra is null && saved.Values.PendingScriptExtra is { Script: not null } pending &&
                pending.Script == script && saved.Operations.Single().Stores.Count == 2 && !saved.Operations.Single().Returned,
                "Failed Script Extra constructor published membership or erased its committed identity/form stores.");
            using var cold = new PackageFieldFixture(saved, partial.Actual.Common.Capture());
            Require(cold.Fields.Capture().Values.PendingScriptExtra?.Identity == saved.Values.PendingScriptExtra!.Identity &&
                cold.Fields.Capture().Values.ScriptExtra is null && cold.Fields.SaveBlocker is not null,
                "Cold partial Script Extra constructor retried stores or replaced its actual pending object.");
        }
        using (var fields = new PackageFieldFixture())
        {
            var pendingDestination = new FalloutPackageRawPosition(0x45000000, 0x45000000, 0);
            fields.Call(invocation => fields.Fields.StoreCurrentPosition(invocation, new(0x3f800000, 0x40000000, 0x40400000)));
            var current = fields.Call(invocation => fields.Fields.ReadCurrentPosition(invocation));
            Require(current == new FalloutPackageRawPosition(0x3f800000, 0x40000000, 0x40400000) && current != pendingDestination,
                "Current reference position borrowed a queued future destination.");
            Require(!fields.Call(invocation => fields.Fields.PublishScriptEventList(invocation, Guid.NewGuid(), Guid.NewGuid(), [])),
                "Event-list setter created a missing Script Extra.");
            var absent = fields.Call(invocation => fields.Fields.MarkPackageEvent(invocation, fields.Package, 0x200));
            Require(absent is { Returned: true, Result: false } && absent.Stores.Count == 0 && fields.Fields.Capture().Values.ScriptExtra is null,
                "An absent original event list manufactured a row or Extra instance.");
            fields.Call(invocation => fields.Fields.AttachScript(invocation, new("WeaponFixture.esm", 0x303)));
            var first = new FalloutPackageScriptEventRow(Guid.NewGuid(), null, 1);
            var matching = new FalloutPackageScriptEventRow(Guid.NewGuid(), fields.Package, 4);
            var last = new FalloutPackageScriptEventRow(Guid.NewGuid(), null, 8);
            fields.Call(invocation => fields.Fields.PublishScriptEventList(invocation, Guid.NewGuid(), Guid.NewGuid(), [first, matching, last]));
            var mark = fields.Call(invocation => fields.Fields.MarkPackageEvent(invocation, fields.Package, 0x200));
            var rows = fields.Fields.Capture().Values.ScriptExtra!.Rows;
            Require(mark.Stores.Count == 2 && mark.Stores[0].Row == matching.Identity && mark.Stores[1].Row == first.Identity &&
                rows[0]!.Mask == 0x201 && rows[1]!.Mask == 0x204 && rows[2]!.Mask == 8,
                "Package mark lost matching/first-wildcard mask order or wrote unrelated rows.");
            var unrelated = fields.Call(invocation => fields.Fields.MarkPackageEvent(invocation, new("WeaponFixture.esm", 0x399), 0x800));
            Require(unrelated.Stores[0].Row == last.Identity && unrelated.Stores[1].Row == first.Identity && rows.Count == 3,
                "No-match marker allocated a missing row instead of retaining empty/wildcard semantics.");
            fields.Call(invocation => fields.Fields.PublishScriptEventList(invocation, Guid.NewGuid(), Guid.NewGuid(),
                [null, matching, first]));
            var stopped = fields.Call(invocation => fields.Fields.MarkPackageEvent(invocation, fields.Package, 0x200));
            Require(stopped is { Returned: true, Result: false } && stopped.Stores.Count == 0,
                "Null data pointer was confused with a wildcard form or skipped to later rows.");
            fields.Fields.Capture();
        }
        using (var fields = new PackageFieldFixture())
        {
            fields.Call(invocation => fields.Fields.StoreCurrentPosition(invocation, new(1, 2, 3)));
            fields.Call(invocation => fields.Fields.StoreRawRotationZ(invocation, 0x3f800000));
            fields.Call(invocation => fields.Fields.StoreCurrentParentCell(invocation, fields.Cell));
            var first = fields.Call(invocation => fields.Fields.SavePosition(invocation, fields));
            Require(first is { Returned: true, ChangeEntered: true, WorldspaceReturned: true } && first.After is
                { Space: not null, RotationZ: 0x3f800000 } && first.After.Space == fields.Cell && first.Stores.Count == 7,
                "New saved-position Extra omitted source stores or its real change callback.");
            fields.Call(invocation => fields.Fields.StoreCurrentPosition(invocation, new(4, 5, 6)));
            fields.Call(invocation => fields.Fields.StoreRawRotationZ(invocation, 0x40000000));
            fields.Worldspace = new("WeaponFixture.esm", 0x402);
            var changed = fields.Call(invocation => fields.Fields.SavePosition(invocation, fields));
            Require(changed.After!.Identity == first.After!.Identity && changed.After.Space == fields.Worldspace &&
                changed.After.Position == new FalloutPackageRawPosition(4, 5, 6) && changed.After.RotationZ == 0x3f800000 && changed.Stores.Count == 4,
                "Existing saved-position update replaced its row, borrowed pending data or rewrote retained rotation.");
            var saved = Roundtrip(fields.Fields.Capture());
            var originalOperations = JsonSerializer.Serialize(saved.Operations);
            var common = fields.Actual.Common.Capture();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var cold = new PackageFieldFixture(saved, common);
                Require(JsonSerializer.Serialize(cold.Fields.Capture().Operations) == originalOperations &&
                    cold.Fields.Capture().Publications.Count == cycle + 2 && cold.Callbacks.Count == 0,
                    "Cold command fields replayed a constructor, command, worldspace or change callback.");
                cold.Call(invocation => cold.Fields.ReadAnimationContext(invocation, cold.ReadCommon, cold.ReadView));
                saved = Roundtrip(cold.Fields.Capture()); common = cold.Actual.Common.Capture();
                originalOperations = JsonSerializer.Serialize(saved.Operations);
            }
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with
            { Values = saved.Values with { RotationZ = 0 } }, fields.Source, fields.Stack, fields.Actor));
            var lineage = saved.Publications.Select(value => value.Process).ToArray();
            var wrong = saved.Publications.Select((value, index) => index == 0 ? value with { Constructor = Guid.Empty } : value).ToArray();
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { Publications = wrong }, fields.Source, fields.Stack, fields.Actor, lineage));
            Reject(() => FalloutActorPackageCommandFields.Validate(saved, fields.Source, fields.Stack, fields.Actor, [Guid.NewGuid()]));
            var moved = saved.Operations.Select((value, index) => index == saved.Operations.Count - 1 ?
                value with { Invocation = value.Invocation with { Process = saved.Publications[0].Process } } : value).ToArray();
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { Operations = moved }, fields.Source, fields.Stack, fields.Actor, lineage));
        }
        foreach (var child in new[] { "Worldspace", "change", "reentry" })
        {
            using var failed = new PackageFieldFixture();
            failed.FailChild = child;
            Reject(() => failed.Call(invocation => failed.Fields.SavePosition(invocation, failed)));
            var prefix = failed.Fields.LastSavedPositionCall ?? throw new InvalidDataException("Failed saved position omitted its current receipt.");
            Require(!prefix.Returned && prefix.Failure is not null && (child == "change" ?
                    prefix.After is not null && prefix.Stores.Count == 7 && prefix.ChangeEntered : prefix.After is null && prefix.Stores.Count == 0),
                "Failed worldspace/change consumer erased preceding fields or fabricated later stores.");
            var saved = Roundtrip(failed.Fields.Capture());
            using var cold = new PackageFieldFixture(saved, failed.Actual.Common.Capture());
            Require(JsonSerializer.Serialize(cold.Fields.LastSavedPositionCall) == JsonSerializer.Serialize(prefix) && cold.Fields.SaveBlocker is not null && cold.Callbacks.Count == 0,
                "Cold failed prefix replayed effects or forgot the whole command remains unreturned.");
        }
        using (var fields = new PackageFieldFixture())
        {
            fields.BorrowedRotation = new(fields.Source.Controls.Identity, fields.Stack, Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), fields.Actor.Reference, fields.Actor.Base, 3, 0x3f800000);
            var borrowed = fields.Call(invocation => fields.Fields.SavePosition(invocation, fields));
            Require(borrowed.RotationRead == fields.BorrowedRotation && borrowed.RotationZ == 0x3f800000 &&
                borrowed.After!.RotationZ == 0x3f800000 && fields.Fields.Capture().Values.RotationZ == 0,
                "Saved position used its duplicate constructor rotation instead of the actual borrowed raw-reference field.");
            var saved = Roundtrip(fields.Fields.Capture());
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with
            {
                Operations = [saved.Operations[0] with
                { SavedPositionCall = borrowed with { RotationRead = fields.BorrowedRotation with { Factory = Guid.Empty } } }]
            }, fields.Source, fields.Stack, fields.Actor));
        }
        using (var fields = new PackageFieldFixture())
        {
            var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(fields.Source.Controls.Main),
                fields.Stack, Guid.NewGuid());
            var sceneConstructor = scene.Capture().Constructor;
            var view = fields.Call(invocation => scene.ReadPackageCommandAnimationView(invocation, fields.RequireCurrent));
            Require(view.Process == scene.Process && view.Process != fields.Process && view.Constructor == sceneConstructor && view.View == 0,
                "Package view did not borrow the actual scene constructor or collapsed Common and Main process owners.");
            var read = fields.Call(invocation => fields.Fields.ReadAnimationContext(invocation, fields.ReadCommon,
                () => scene.ReadPackageCommandAnimationView(invocation, fields.RequireCurrent)));
            Require(read is { Returned: true, Context: null, Selection: FalloutPackageAnimationSelection.Process, Common: not null },
                "Animation getter borrowed first-person node/TLS instead of the actual Common field.");
            Reject(() => fields.Call(invocation => fields.Fields.ReadAnimationContext(invocation,
                () => fields.ReadCommon() with { AnimationContext = Guid.NewGuid() }, fields.ReadView)));
            Require(fields.Fields.LastAnimationRead is { Returned: false, Context: not null, Failure: FalloutActorPackageCommandFields.AnimationBoundary },
                "Non-null animation context returned without its real later effects.");
            fields.Fields.Capture();
            fields.Call(invocation => fields.Fields.ObserveUnownedWriter(invocation, FalloutPackageCommandFieldLane.Animation, "authored-observed-context-writer"));
            var called = false;
            Reject(() => fields.Call(invocation => fields.Fields.ReadAnimationContext(invocation, () => { called = true; return fields.ReadCommon(); }, fields.ReadView)));
            Require(!called, "Unowned context publication became null because no renderer exists.");
            scene.Retire();
            Reject(() => fields.Call(invocation => scene.ReadPackageCommandAnimationView(invocation, fields.RequireCurrent)));
        }
        Require(FalloutActorPackageCommandFields.SelectAnimation(1, Guid.NewGuid(), Guid.NewGuid()) == FalloutPackageAnimationSelection.Override &&
            FalloutActorPackageCommandFields.SelectAnimation(0, null, Guid.NewGuid()) == FalloutPackageAnimationSelection.FirstPerson &&
            FalloutActorPackageCommandFields.SelectAnimation(1, null, Guid.NewGuid()) == FalloutPackageAnimationSelection.Process &&
            FalloutActorPackageCommandFields.SelectAnimation(0, null, null) == FalloutPackageAnimationSelection.Process,
            "Independent override, view, first-person animation and Common selection were collapsed.");
        Console.WriteLine("OPENNV_PACKAGE_COMMAND_FIELDS_PASS authoredScopeOnly=true currentNotPending=true noEventAllocation=true " +
            "savedRotationRetained=true independentAnimation=true failedStoresPreserved=true coldCycles=3 originalCommand=UNEXECUTED");
    }
    private static FalloutPackageCommandFieldSnapshot Roundtrip(FalloutPackageCommandFieldSnapshot value) =>
        JsonSerializer.Deserialize<FalloutPackageCommandFieldSnapshot>(JsonSerializer.Serialize(value)) ??
            throw new InvalidDataException("Package command field snapshot could not roundtrip.");

    private sealed class PackageFieldFixture : IFalloutPackageReferenceFieldConsumers, IDisposable
    {
        internal readonly WeaponContinuationFixture Actual;
        internal readonly FalloutActorPackageCommandFields Fields;
        internal readonly FalloutFormKey Package = new("WeaponFixture.esm", 0x302), Cell = new("WeaponFixture.esm", 0x401);
        internal FalloutFormKey? Worldspace;
        internal FalloutPackageCommandRawRotationRead? BorrowedRotation;
        private readonly Guid _viewProcess = Guid.NewGuid(), _viewConstructor = Guid.NewGuid();
        internal string? FailChild;
        internal int? FailAtGuardCall;
        private int _guardCalls;
        internal List<string> Callbacks { get; } = [];
        private FalloutPackageCommandFieldInvocation? _current;
        public FalloutPackageCommandFieldsSource Source { get; }
        public string Stack => Actual.Stack;
        public FalloutCombatActorIdentity Actor => ReadCommon().Actor;
        public Guid Process => ReadCommon().Process;
        public long Epoch => 1;
        public Guid Factory { get; } = Guid.NewGuid();
        internal PackageFieldFixture(FalloutPackageCommandFieldSnapshot? saved = null, FalloutProcessCommonSnapshot? common = null, bool admitted = true,
            IReadOnlyList<FalloutPackageCommandFieldLane>? closedLanes = null)
        {
            Actual = new(common); Source = FalloutPackageCommandFieldsSource.Reviewed(Actual.Source.Controls);
            Fields = new(Source, Stack, Actor, Process, Epoch, Guid.NewGuid(), RequireCurrent,
                saved?.Construction ?? new("authored-complete-constructor-and-preceding-writer-fixture", admitted, ClosedLanes: closedLanes), saved);
        }
        internal FalloutProcessSceneQuery ReadCommon() => Actual.Common.ReadSceneQueries(Actual.Player, 1);
        internal FalloutPackageCommandAnimationViewRead ReadView() =>
            new(_viewProcess, Stack, Source.Controls.Main.EngineSha256, _viewConstructor, 0);
        internal T Call<T>(Func<FalloutPackageCommandFieldInvocation, T> action)
        {
            if (_current is not null) throw new InvalidOperationException("Authored source command reentered.");
            _current = new(Guid.NewGuid(), Actor, Process, Epoch, Environment.CurrentManagedThreadId);
            _guardCalls = 0;
            try { return action(_current); } finally { _current = null; }
        }
        internal void Call(Action<FalloutPackageCommandFieldInvocation> action) => Call(invocation => { action(invocation); return true; });
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation)
        {
            if (_current != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored command fields lost their actual entered caller.");
            if (++_guardCalls == FailAtGuardCall) throw new NotSupportedException("authored-source-constructor-field-guard-failure");
        }
        public FalloutFormKey? ReadWorldspace(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? parentCell)
        {
            RequireCurrent(invocation); Callbacks.Add("Worldspace");
            if (FailChild == "Worldspace") throw new NotSupportedException(FalloutActorPackageCommandFields.WorldspaceBoundary);
            if (FailChild == "reentry") { try { Fields.Capture(); } catch (InvalidOperationException) { } }
            return Worldspace;
        }
        public FalloutPackageCommandRawRotationRead? ReadRawRotationZ(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); return BorrowedRotation; }
        public void ReferenceChanged(FalloutPackageCommandFieldInvocation invocation, uint mask)
        {
            RequireCurrent(invocation); Callbacks.Add("change");
            Require(mask == FalloutPackageCommandFieldsSource.ReferenceChangedMask, "Reference change lost its actual source mask.");
            if (FailChild == "change") throw new NotSupportedException(FalloutActorPackageCommandFields.ReferenceChangeBoundary);
        }
        public void Dispose() { Fields.Retire(); Actual.Dispose(); }
    }
}
