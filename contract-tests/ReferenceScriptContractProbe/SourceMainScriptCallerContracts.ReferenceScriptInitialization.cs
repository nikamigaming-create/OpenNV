using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferenceScriptExtraInitializationContracts()
    {
        using (var fixture = new ReferenceScriptInitializationFixture())
        {
            var construction = fixture.Fields.Construction;
            var call = fixture.Initialize(); var saved = Roundtrip(fixture.Fields.Capture());
            Require(call is { Returned: true, WholeReferenceInitializerReturned: false, WholePhysicalControllerReturned: false,
                Before: null, Selected.Script: null, ThreadScope: { BeforeKind: 59, EnteredKind: 0x15, RestoredKind: 59, Returned: true } } &&
                call.FlagsBefore == FalloutReferenceScriptExtraInitializerSource.ConstructorFlags &&
                call.FlagsAfter == (call.FlagsBefore | FalloutReferenceScriptExtraInitializerSource.InitializedFlag) &&
                saved.Values.ScriptExtra is null && saved.Values.PendingScriptExtra is null && call.Stores.Count == 0 &&
                saved.Availability.Single(value => value.Available).Lane == FalloutPackageCommandFieldLane.ScriptExtra &&
                saved.Construction == construction && !saved.Construction.PrecedingWritersClosed && saved.Construction.ClosedLanes is null &&
                saved.Operations.Count == 0 && saved.ScriptInitializationOperations?.Select(value => value.Kind).SequenceEqual(
                    new[] { FalloutScriptExtraInitializationFieldOperationKind.Begin, FalloutScriptExtraInitializationFieldOperationKind.Read,
                        FalloutScriptExtraInitializationFieldOperationKind.Admit }) == true,
                "Independent Script initialization changed constructor admission, fabricated a command or opened sibling field lanes.");
            var mark = fixture.Command(invocation => fixture.Fields.MarkPackageEvent(invocation, fixture.Package, 0x200));
            Require(mark is { Returned: true, Result: false } && mark.Stores.Count == 0,
                "Returned no-Script initialization allocated an event row for StartEvent.");
            var repeated = fixture.Initialize();
            Require(repeated.Returned && repeated.Reads.Count == 1 && repeated.ThreadScope is null && repeated.Stores.Count == 0,
                "Current initialized8 skip replayed Script attachment or a thread scope.");
            saved = Roundtrip(fixture.Fields.Capture()); var common = fixture.Common.Capture();
            var history = JsonSerializer.Serialize(saved.ScriptInitialization!.Calls);
            var fieldsHistory = JsonSerializer.Serialize(saved.ScriptInitializationOperations);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var cold = new ReferenceScriptInitializationFixture(saved: saved, common: common);
                var restored = Roundtrip(cold.Fields.Capture());
                Require(cold.InitializationGuards == 0 && JsonSerializer.Serialize(restored.ScriptInitialization!.Calls) == history &&
                    JsonSerializer.Serialize(restored.ScriptInitializationOperations) == fieldsHistory &&
                    restored.ScriptInitialization.Publications.Count == cycle + 2 && restored.Values.ScriptExtra is null &&
                    restored.ScriptInitialization.Process != saved.ScriptInitialization!.Process &&
                    restored.ScriptInitialization.Publications[^1].ThreadProcess != saved.ScriptInitialization.Publications[^1].ThreadProcess &&
                    restored.ScriptInitialization.Publications[^1].ThreadFactory != saved.ScriptInitialization.Publications[^1].ThreadFactory,
                    "Cold Script initialization replayed a source call or conflated old Common/Scene/thread factories.");
                var scope = cold.ThreadContext.EnterMenuAllocation(Guid.NewGuid(), Guid.NewGuid(), () => { });
                Require(scope.BeforeKind == 59 && scope.EnteredKind == 13,
                    "Historical Script kind21 promoted a current cold thread or widened menu kind13.");
                cold.ThreadContext.ReturnMenuAllocation(scope, () => { });
                Require(cold.Command(invocation => cold.Fields.MarkPackageEvent(invocation, cold.Package, 0x200)) is { Returned: true, Result: false },
                    "Cold returned Script lane lost its fresh narrow owner binding.");
                saved = Roundtrip(cold.Fields.Capture()); common = cold.Common.Capture();
            }
            var original = saved.ScriptInitialization!;
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { ScriptInitialization = null }, fixture.FieldSource, fixture.Stack, fixture.Actor));
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { Construction = saved.Construction with
            { ClosedLanes = [FalloutPackageCommandFieldLane.Position] } }, fixture.FieldSource, fixture.Stack, fixture.Actor));
            Reject(() => FalloutReferenceScriptExtraInitializer.Validate(original with { ReferenceFlags = 8 }, fixture.Source, fixture.Stack, fixture.Actor));
            var first = original.Calls[0];
            Reject(() => FalloutReferenceScriptExtraInitializer.Validate(original with { Calls = [first with
            { ThreadScope = first.ThreadScope! with { EnteredKind = 15 } }, .. original.Calls.Skip(1)] }, fixture.Source, fixture.Stack, fixture.Actor));
            Reject(() => FalloutReferenceScriptExtraInitializer.Validate(original with { Calls = [first with
            { ThreadScope = first.ThreadScope! with { Process = first.Invocation.Process } }, .. original.Calls.Skip(1)] }, fixture.Source, fixture.Stack, fixture.Actor));
            var receipts = saved.ScriptInitializationOperations!.ToArray(); receipts[0] = receipts[0] with { FieldsFactory = Guid.NewGuid() };
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { ScriptInitializationOperations = receipts }, fixture.FieldSource, fixture.Stack, fixture.Actor));
            var foreign = new FalloutReferenceScriptExtraInitializationInvocation(Guid.NewGuid(), fixture.Actor, fixture.Fields.Process,
                1, fixture.Fields.Constructor, Environment.CurrentManagedThreadId);
            var before = JsonSerializer.Serialize(fixture.Initializer.Capture());
            Task.Run(() => Reject(() => fixture.Initializer.Initialize(foreign))).GetAwaiter().GetResult();
            Require(before == JsonSerializer.Serialize(fixture.Initializer.Capture()), "Foreign initialization mutated an actual source call history.");
        }
        using (var existing = new ReferenceScriptInitializationFixture(admitScript: true))
        {
            existing.Command(invocation => existing.Fields.AttachScript(invocation, existing.Script));
            var extra = existing.Fields.Capture().Values.ScriptExtra!;
            var call = existing.Initialize();
            Require(call.Returned && call.Before?.Identity == extra.Identity && call.Selected is null && call.Stores.Count == 0 &&
                call.Reads.All(value => value.Step is not (FalloutReferenceScriptExtraInitializationStep.Template or
                    FalloutReferenceScriptExtraInitializationStep.Script)) && existing.Fields.Capture().Values.ScriptExtra?.Identity == extra.Identity,
                "Existing nonnull Script lost its source bypass or reallocated its Extra.");
        }
        using (var scripted = new ReferenceScriptInitializationFixture(scripted: true))
        {
            Reject(() => scripted.Initialize()); var call = scripted.Initializer.LastCall!; var saved = Roundtrip(scripted.Fields.Capture());
            Require(call is { Returned: false, Step: FalloutReferenceScriptExtraInitializationStep.EventListFactory,
                Failure: FalloutReferenceScriptExtraInitializer.EventListBoundary, ThreadScope: { Returned: false, RestoredKind: null } } &&
                call.Stores.Count == 4 && saved.Values.ScriptExtra is { EventList: null, EventFactory: null, Rows.Count: 0 } extra &&
                extra.Script == scripted.Script && saved.Availability.All(value => !value.Available) &&
                !saved.ScriptInitializationOperations!.Any(value => value.Kind == FalloutScriptExtraInitializationFieldOperationKind.Admit),
                "Unowned list/local-variable factory erased real attachment or fabricated restoration/admission.");
            using var cold = new ReferenceScriptInitializationFixture(scripted: true, saved: saved, common: scripted.Common.Capture());
            Require(cold.InitializationGuards == 0 && cold.Fields.Capture().Values.ScriptExtra?.Identity == saved.Values.ScriptExtra!.Identity &&
                cold.Initializer.SaveBlocker == FalloutReferenceScriptExtraInitializer.EventListBoundary,
                "Cold failed event-list factory replayed attachment or accepted an unreturned source scope.");
            Reject(() => cold.Initialize());
            Require(cold.Initializer.LastCall is { Returned: false, ThreadScope: null, Reads.Count: 0 },
                "Failed prior initialized8 prefix admitted a later cold source skip.");
            cold.Fields.Capture();
        }
        using (var partial = new ReferenceScriptInitializationFixture(scripted: true))
        {
            partial.FailAtInitializationGuard = 18; Reject(() => partial.Initialize());
            var saved = Roundtrip(partial.Fields.Capture());
            Require(saved.Values.ScriptExtra is null && saved.Values.PendingScriptExtra is { Script: not null } &&
                partial.Initializer.LastCall is { Returned: false, Step: FalloutReferenceScriptExtraInitializationStep.ScriptAttachment, Stores.Count: 2 },
                "Partial Script constructor erased its identity/form prefix or published an unreturned Extra.");
            using var cold = new ReferenceScriptInitializationFixture(scripted: true, saved: saved, common: partial.Common.Capture());
            Require(cold.Fields.Capture().Values.PendingScriptExtra?.Identity == saved.Values.PendingScriptExtra!.Identity && cold.InitializationGuards == 0,
                "Cold partial constructor minted another Extra or replayed a committed field store.");
        }
        foreach (var writer in Enum.GetValues<FalloutReferenceScriptExtraWriter>())
        {
            using var observed = new ReferenceScriptInitializationFixture(); observed.Initialize();
            observed.Observe(writer); Reject(() => observed.Initialize());
            Require(observed.Initializer.LastCall is { Returned: false, Reads.Count: 0 } &&
                observed.Fields.Capture().Availability.All(value => !value.Available),
                "Unowned flags/template/copy/load/callback writer borrowed last known bytes or kept current lane admission.");
        }
        using (var commandWriter = new ReferenceScriptInitializationFixture())
        {
            commandWriter.Initialize(); commandWriter.Command(invocation =>
            { commandWriter.Fields.ObserveUnownedWriter(invocation, FalloutPackageCommandFieldLane.ScriptExtra, "authored-command-Script-copy-writer"); return true; });
            Reject(() => commandWriter.Initialize());
            Require(commandWriter.Initializer.LastCall is { Returned: false, Reads.Count: 0 } && commandWriter.Fields.SaveBlocker is not null,
                "Command-scope unowned Script writer was repaired by a later initializer flag8 skip.");
            commandWriter.Fields.Capture();
        }
        using (var payload = new ReferenceScriptInitializationFixture(referencePayload: true))
        {
            Reject(() => payload.Initialize());
            Require(payload.Initializer.LastCall is { Returned: false, Reads.Count: 0,
                Failure: "source-engine-reference-record-initialization-writer-unbound" } && payload.Fields.Capture().Values.ScriptExtra is null,
                "Winning engine reference payload bypassed its independent copy/load writer.");
        }
        using (var reentry = new ReferenceScriptInitializationFixture())
        {
            reentry.DuringInitializationGuard = () => Reject(() => reentry.Initializer.Capture());
            Reject(() => reentry.Initialize()); reentry.DuringInitializationGuard = null;
            Require(reentry.Initializer.LastCall is { Returned: false, Reads.Count: 0 } && reentry.Fields.Capture().Values.ScriptExtra is null,
                "Swallowed initializer capture reentry published a successful Script field branch.");
        }
        Console.WriteLine("OPENNV_REFERENCE_SCRIPT_INITIALIZATION_PASS independentLane=true sourceFlags=true kind21=true menuKind13Separate=true " +
            "existingBypass=true noAllocation=true attachedFailedFactory=true partialConstructor=true coldCycles=3 writerBarrier=true reentryRefused=true " +
            "wholeReference=UNEXECUTED wholeController=UNEXECUTED originalStartup=UNEXECUTED");
    }

    private sealed class ReferenceScriptInitializationFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNV-ScriptInitialization-" + Guid.NewGuid().ToString("N"));
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private FalloutReferenceScriptExtraInitializationInvocation? _initialization;
        private FalloutPackageCommandFieldInvocation? _command;
        internal readonly string Stack = new('6', 64);
        internal readonly FalloutFormKey Script = new("ScriptInitializationFixture.esm", 0x300), Package = new("ScriptInitializationFixture.esm", 0x301);
        internal readonly FalloutPluginStack Records;
        internal readonly FalloutCombatActorIdentity Actor;
        internal readonly FalloutActorProcessCommonState Common;
        internal readonly FalloutPackageCommandFieldsSource FieldSource;
        internal readonly FalloutReferenceScriptExtraInitializerSource Source;
        internal readonly FalloutActorPackageCommandFields Fields;
        internal readonly FalloutPlayerThreadContextState ThreadContext;
        internal readonly FalloutReferenceScriptExtraInitializer Initializer;
        internal int InitializationGuards;
        internal int? FailAtInitializationGuard;
        internal Action? DuringInitializationGuard;

        internal ReferenceScriptInitializationFixture(bool scripted = false, bool admitScript = false, bool referencePayload = false,
            FalloutPackageCommandFieldSnapshot? saved = null, FalloutProcessCommonSnapshot? common = null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Path.Combine(_directory, "ScriptInitializationFixture.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("EDID", Encoding.ASCII.GetBytes("Player\0")), scripted ? Field("SCRI", BitConverter.GetBytes(0x300u)) : []),
                Record("SCPT", 0x300, Field("EDID", Encoding.ASCII.GetBytes("AttachedScript\0"))),
                referencePayload ? Record("ACHR", 0x14, Field("NAME", BitConverter.GetBytes(7u))) : []));
            Records = FalloutPluginStack.Load(_directory, ["ScriptInitializationFixture.esm"]);
            var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
                FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            var basis = Records.GetEffective(Records.RuntimeFormKey(7));
            Actor = new(Records.RuntimeFormKey(0x14), basis.FormKey, "ENGINE_PLAYER", 0, main.EngineSha256, basis.Signature, basis.Flags,
                Convert.ToHexString(SHA256.HashData(basis.ReadData())).ToLowerInvariant(), true);
            FieldSource = FalloutPackageCommandFieldsSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(main));
            Source = FalloutReferenceScriptExtraInitializerSource.Reviewed(FieldSource);
            Common = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), Stack, _ => Actor,
                _ => throw new InvalidOperationException("Script initialization cannot invent an Actor value/body pool."),
                _ => throw new InvalidOperationException("Script initialization cannot construct a physical controller."), common);
            if (common is null) Common.Construct(Actor.Reference, 1, FalloutDetectionProcessLevel.High);
            var actual = Common.ReadSceneQueries(Actor.Reference, 1);
            Fields = new(FieldSource, Stack, Actor, actual.Process, 1, Guid.NewGuid(), RequireCommand,
                saved?.Construction ?? new("authored-actual-Player-field-constructor", ClosedLanes:
                    admitScript ? [FalloutPackageCommandFieldLane.ScriptExtra] : null), saved);
            ThreadContext = new(FalloutPlayerThreadContextSource.Reviewed(main), Guid.NewGuid());
            Initializer = new(Source, Records, Fields, ThreadContext, RequireInitialization);
        }
        internal FalloutReferenceScriptExtraInitializationCall Initialize()
        {
            _initialization = new(Guid.NewGuid(), Actor, Fields.Process, 1, Fields.Constructor, _thread);
            InitializationGuards = 0;
            try { return Initializer.Initialize(_initialization); } finally { _initialization = null; }
        }
        internal void Observe(FalloutReferenceScriptExtraWriter writer)
        {
            _initialization = new(Guid.NewGuid(), Actor, Fields.Process, 1, Fields.Constructor, _thread);
            try { Initializer.ObserveUnownedWriter(_initialization, writer, "authored-unowned-" + writer); }
            finally { _initialization = null; }
        }
        internal T Command<T>(Func<FalloutPackageCommandFieldInvocation, T> action)
        {
            _command = new(Guid.NewGuid(), Actor, Fields.Process, 1, _thread);
            try { return action(_command); } finally { _command = null; }
        }
        internal void Command(Action<FalloutPackageCommandFieldInvocation> action) => Command(invocation => { action(invocation); return true; });
        private void RequireCommand(FalloutPackageCommandFieldInvocation invocation)
        {
            if (_command != invocation || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Authored command left its independent actual scope.");
        }
        private void RequireInitialization(FalloutReferenceScriptExtraInitializationInvocation invocation)
        {
            if (_initialization != invocation || _thread != Environment.CurrentManagedThreadId || ThreadContext.Process == Fields.Process ||
                Common.ReadSceneQueries(Actor.Reference, 1).Process != invocation.Process)
                throw new InvalidOperationException("Authored initializer left its actual independent Actor/Common/Scene/thread owners.");
            if (++InitializationGuards == FailAtInitializationGuard) throw new NotSupportedException("authored-Script-initializer-field-guard-failure");
            DuringInitializationGuard?.Invoke();
        }
        private static byte[] Record(string signature, uint form, params byte[][] fields)
        {
            var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), form);
            data.CopyTo(bytes, 24); return bytes;
        }
        private static byte[] Field(string signature, byte[] data)
        {
            var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
        }
        private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        public void Dispose()
        {
            try { Fields.Retire(); ThreadContext.Retire(); Common.Dispose(); Records.Dispose(); }
            finally { Directory.Delete(_directory, true); }
        }
    }
}
