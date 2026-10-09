using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentSourceSky(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var snapshot = state.SkyLighting ?? throw new InvalidDataException("Current campaign has no actual Sky continuation.");
        var source = records.OwnedSource ?? throw new InvalidDataException("Saved Sky has no exact current selected source.");
        FalloutSkyTransferDeclaration declaration;
        try { declaration = FalloutSkyTransferDeclaration.Read(source.FalloutExecutablePath); }
        catch (NotSupportedException error)
        {
            if (snapshot.SourceTransfer is not null || snapshot.TransferUnowned != error.Message)
                throw new InvalidDataException("Saved Sky changed an unowned original declaration into a source reset return.", error);
            return;
        }
        if (snapshot.SourceTransfer is not { } saved || snapshot.TransferUnowned is not null)
            throw new InvalidDataException("Current save omitted actual source Sky fields/children/instances.");
        FalloutSkyTransferState.Validate(saved, declaration, source.StackId, records);
        var moons = snapshot.Moons ?? throw new InvalidDataException("Current save omitted its actual Moon field owner.");
        if (moons.CapturedSky != saved.CapturedSky || moons.CapturedProcess != saved.CapturedProcess)
            throw new InvalidDataException("Saved Moons belong to another actual Sky/process epoch.");
        FalloutSkyMoonState.Validate(moons, FalloutMoonSource.Read(declaration), source.StackId, records);
        if (state.ActorProcessRuntime?.CapturedProcess != saved.CapturedProcess)
            throw new InvalidDataException("Saved Sky belongs to another actual Main/Player process epoch.");
    }
}
