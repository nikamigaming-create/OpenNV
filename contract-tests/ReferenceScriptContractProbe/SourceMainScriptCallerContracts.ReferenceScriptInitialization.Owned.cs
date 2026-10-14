using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedReferenceScriptExtraInitializer(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerMovementControlSource controls)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Script initialization audit has no actual owned source.");
        var fieldSource = FalloutExecutableStringTable.ReadPackageCommandFields(owned.FalloutExecutablePath, controls);
        var source = FalloutExecutableStringTable.ReadReferenceScriptExtraInitializer(owned.FalloutExecutablePath, fieldSource);
        var actual = common.ReadSceneQueries(records.RuntimeFormKey(0x14), 1);
        var basis = records.GetEffective(actual.Actor.Base);
        var template = basis.ReadSubrecords().Where(value => value.Signature == "TPLT").ToArray();
        Require(actual.Actor.EnginePlayer && !records.TryGetWinner(actual.Actor.Reference, out _) &&
            template.Length == 0 && FalloutScriptLocals.AttachedScript(records, basis) is null,
            "Owned Player declarations require an independent payload/template/Script initialization writer.");
        var items = basis.ReadSubrecords().Where(value => value.Signature == "CNTO").Select(value =>
        {
            if (value.Data.Length != 8) throw new InvalidDataException("Owned Player inventory entry changed its exact FormID/count extent.");
            var key = basis.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span));
            var item = records.GetEffective(key); var script = FalloutScriptLocals.AttachedScript(records, item);
            return new { Form = key, item.Signature, item.Flags, Count = BinaryPrimitives.ReadInt32LittleEndian(value.Data.Span[4..]),
                Sha256 = Convert.ToHexString(SHA256.HashData(item.ReadData())).ToLowerInvariant(), Script = script?.FormKey };
        }).ToArray();
        Require(items.All(value => value.Script is null),
            "Owned contained-item Script callback is a distinct unowned Player field writer.");
        FalloutReferenceScriptExtraInitializationInvocation? entered = null;
        var constructor = Guid.NewGuid();
        var threadContext = new FalloutPlayerThreadContextState(FalloutPlayerThreadContextSource.Reviewed(controls.Main), Guid.NewGuid());
        var fields = new FalloutActorPackageCommandFields(fieldSource, owned.StackId, actual.Actor, actual.Process, actual.Epoch,
            constructor, _ => throw new InvalidOperationException("Owned Script audit cannot fabricate a compiled command scope."));
        var initializer = new FalloutReferenceScriptExtraInitializer(source, records, fields, threadContext, invocation =>
        {
            var current = common.ReadSceneQueries(actual.Actor.Reference, actual.Epoch);
            if (invocation != entered || invocation.Actor != current.Actor || invocation.Process != current.Process ||
                invocation.Epoch != current.Epoch || invocation.Constructor != constructor || current.Stack != owned.StackId ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || threadContext.Process == current.Process)
                throw new InvalidOperationException("Owned independent initializer left its actual borrowed Common and separate thread owners.");
        });
        try
        {
            entered = new(Guid.NewGuid(), actual.Actor, actual.Process, actual.Epoch, constructor, Environment.CurrentManagedThreadId);
            var returned = initializer.Initialize(entered); entered = null;
            var snapshot = fields.Capture();
            Require(returned.Returned && returned.Before is null && returned.Selected is { Script: null } selected && selected.Form == basis.FormKey &&
                returned.ThreadScope is { BeforeKind: 59, EnteredKind: 0x15, RestoredKind: 59, Returned: true } &&
                returned.Stores.Count == 0 && snapshot.Values.ScriptExtra is null && snapshot.Values.PendingScriptExtra is null &&
                snapshot.Operations.Count == 0 && snapshot.Availability.Single(value => value.Available).Lane == FalloutPackageCommandFieldLane.ScriptExtra &&
                !snapshot.Construction.PrecedingWritersClosed && snapshot.Construction.ClosedLanes is null,
                "Owned Script branch did not return independently or promoted unrelated physical/reference owners.");
            return new { source = source.Identity, actual.Actor, borrowedCommon = actual, sourceInventory = items,
                initialized = returned, fields = snapshot, independentScriptLane = "RETURNED", wholeReferenceInitializer = "UNEXECUTED",
                wholePhysicalController = "UNEXECUTED", originalStartup = "UNEXECUTED", authoredInitializationScope = true };
        }
        finally { entered = null; fields.Retire(); threadContext.Retire(); }
    }
}
