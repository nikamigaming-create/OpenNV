using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class PlayerWorldOrientationContracts
{
    private static FalloutReferenceWorldProjectionSnapshot CompleteProjection(Fixture owner) =>
        new(FalloutReferenceWorldProjectionSnapshot.CurrentSchema, owner.Publication.Capture(), null, null);

    private static void CampaignSaveContracts(Fixture owner)
    {
        var saved = RoundTrip(CompleteProjection(owner)); var reference = RoundTrip(owner.Reference.Capture());
        FalloutWorldProjectionSaveContract.ValidatePlayer(saved, reference, owner.Source.Controls.Main, owner.Stack, reference.CapturedProcess);
        FalloutWorldProjectionSaveContract.ValidateShape(null, null);
        Reject(() => FalloutWorldProjectionSaveContract.ValidateShape(saved, null));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(saved, null, owner.Source.Controls.Main, owner.Stack, reference.CapturedProcess));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(saved, reference with { ProjectionWorld = Guid.NewGuid() },
            owner.Source.Controls.Main, owner.Stack, reference.CapturedProcess));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(saved, reference with { ProjectionRootFactory = Guid.NewGuid() },
            owner.Source.Controls.Main, owner.Stack, reference.CapturedProcess));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(saved, reference, owner.Source.Controls.Main, owner.Stack, Guid.NewGuid()));
        Reject(() => FalloutWorldProjectionSaveContract.ValidatePlayer(saved, reference with { PlayerFovReturned = false },
            owner.Source.Controls.Main, owner.Stack, reference.CapturedProcess));
        Reject(() => (saved with { Schema = "opennv-source-world-projection-publication/unowned" }).Validate());
        Reject(() => (saved with { Publication = null! }).Validate());
        Reject(() => (saved with { Publication = saved.Publication with { CurrentRootFactory = Guid.NewGuid() } }).Validate());
        var empty = saved with { Publication = saved.Publication with { Current = null, CurrentRootFactory = null, Replacements = 0 } };
        empty.Validate(); Reject(() => FalloutWorldProjectionSaveContract.RequireComplete(empty));
        Require(owner.TargetReads == 1 && owner.Root.Capture().Stores == 1 && owner.Reference.Capture().Stores == 3,
            "Complete save validation replayed constructor, viewport, source placement or setter work.");
    }
}
