using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferenceFollowerMembershipContracts()
    {
        using var fixture = new ReferencePositionFixture();
        var source = FalloutReferenceFollowerSource.Reviewed(fixture.Source);
        Reject(() => fixture.Call(FalloutReferencePositionIngress.PackageCommand, inv => fixture.Position.ReadFollowerExtra(inv, source)));
        fixture.PrepareSavedPosition();
        var read = Read(fixture, source);
        Require(read is { Present: false, Extra: null, Payload: null } && read.Process == fixture.Position.Process &&
            read.Factory == fixture.Position.Factory && read.Constructor == fixture.Position.Constructor,
            "Follower lookup borrowed an empty semantic collection or another reference owner.");
        var validation = new FalloutReferenceFollowerValidation(fixture.Position.Capture()); validation.ValidateRead(read);
        Reject(() => validation.ValidateRead(read with { Present = true, Extra = Guid.NewGuid() }));
        Reject(() => validation.ValidateRead(read with { Payload = Guid.NewGuid() }));
        Reject(() => validation.ValidateRead(read with { ReferenceInvocation = null }));
        var changed = fixture.Position.Capture();
        Reject(() => FalloutSourceReferencePositionState.Validate(changed with
        { Values = changed.Values with { FollowerExtra = Guid.NewGuid() } }, fixture.Source, fixture.Stack, fixture.Actor));

        fixture.Call(FalloutReferencePositionIngress.CellAttachment, inv => fixture.Position.StorePersistentCell(inv, true, fixture.Cell));
        _ = fixture.SaveCurrentPosition();
        Require(Read(fixture, source) is { Present: false, Extra: null, Payload: null },
            "Persistent18 or Saved24 writers invalidated or fabricated type29 membership.");
        fixture.CallPackage(inv =>
        {
            fixture.Fields.ObserveUnownedWriter(inv, FalloutPackageCommandFieldLane.ScriptExtra,
                "authored-independent-Script-Extra-membership-writer");
            return true;
        });
        Require(Read(fixture, source) is { Present: false, Extra: null, Payload: null },
            "Unrelated Script13 membership was used as the follower type29 authority.");
        var saved = fixture.Capture(); var originalCalls = JsonSerializer.Serialize(saved.Position.Calls);
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new ReferencePositionFixture(saved);
            var current = Read(cold, source);
            var snapshot = cold.Position.Capture();
            new FalloutReferenceFollowerValidation(snapshot).ValidateRead(current);
            Require(current is { Present: false, Extra: null, Payload: null } && snapshot.Process != saved.Position.Process &&
                snapshot.Factory != saved.Position.Factory && snapshot.Sequence == saved.Position.Sequence &&
                JsonSerializer.Serialize(snapshot.Calls) == originalCalls,
                "Cold follower membership replayed original constructors/writers or reused a reference lifetime.");
            saved = cold.Capture();
        }
        fixture.Call(FalloutReferencePositionIngress.CopyOrLoad, inv => fixture.Position.ObserveUnownedWriter(inv,
            FalloutReferencePositionLane.FollowerExtra, "authored-unknown-whole-Extra-list-copy"));
        Reject(() => Read(fixture, source));
        new FalloutReferenceFollowerValidation(fixture.Position.Capture()).ValidateRead(read);
        var captured = fixture.Position.Capture();
        Reject(() => new FalloutReferenceFollowerValidation(captured).ValidateRead(read with { Sequence = captured.Sequence }));
        Console.WriteLine("OPENNV_REFERENCE_FOLLOWER_MEMBERSHIP_PASS actualReference=true constructorMembership=true " +
            "independentScriptLane=true unrelatedTypesPreserved=true unknownWriterHeld=true coldCycles=3 followerPayload=UNEXECUTED");
    }

    private static FalloutActorFollowerExtraRead Read(ReferencePositionFixture fixture, FalloutReferenceFollowerSource source) =>
        fixture.CallPackage(_ => fixture.Call(FalloutReferencePositionIngress.PackageCommand,
            inv => fixture.Position.ReadFollowerExtra(inv, source)));
}
