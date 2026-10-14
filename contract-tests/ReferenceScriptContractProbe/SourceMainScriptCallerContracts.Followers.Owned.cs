using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedReferenceFollowerMembership(FalloutPluginStack records, ReferencePositionFixture fixture)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Follower source audit needs actual owned records.");
        var source = FalloutExecutableStringTable.ReadReferenceFollowerExtra(owned.FalloutExecutablePath, fixture.Source);
        var read = Read(fixture, source);
        var saved = fixture.Capture();
        new FalloutReferenceFollowerValidation(saved.Position).ValidateRead(read);
        Require(read is { Present: false, Extra: null, Payload: null },
            "Owned reference follower constructor membership changed its actual returned prefix.");
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new ReferencePositionFixture(saved, original: records, controls: fixture.Source.Fields.Controls);
            var current = Read(cold, source); var snapshot = cold.Position.Capture();
            new FalloutReferenceFollowerValidation(snapshot).ValidateRead(current);
            Require(current is { Present: false, Extra: null, Payload: null } && snapshot.Factory != saved.Position.Factory &&
                snapshot.Sequence == saved.Position.Sequence && snapshot.Calls.Count == saved.Position.Calls.Count && cold.Events.Count == 0,
                "Owned cold follower membership replayed a prefix, lookup side effect or Actor payload.");
            saved = cold.Capture();
        }
        return new { source = source.Identity, kind = FalloutReferenceFollowerSource.ExtraKind,
            sameReference = true, membership = "actual-source-constructor-absent", coldCycles = 3,
            nonnullPayload = "UNOWNED", followerActorCallbacks = "UNEXECUTED", wholePlayer = "UNRETURNED" };
    }
}
