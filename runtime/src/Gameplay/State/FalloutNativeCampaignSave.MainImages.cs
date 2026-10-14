using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateSourceMainImageShape(FalloutNativeCampaignState state)
    {
        var main = state.ActorProcessRuntime?.StandaloneMain;
        if (main is null)
        {
            if (state.SourceMainImages is not null) throw new InvalidDataException("Saved source images belong to another Main family.");
            return;
        }
        var saved = state.SourceMainImages ?? throw new InvalidDataException("Current standalone save omitted its actual Main image manager.");
        FalloutImageSpaceState.ValidateSourceMainImages(saved);
        FalloutStandaloneMenuSaveContract.Validate(main.Interface, saved.MenuStack, main.Scene.MenuKindScopes, main.Scene.InputManagerQuery);
        if (saved.Process != state.ActorProcessRuntime!.CapturedProcess || saved.Last is { Returned: false } ||
            saved.Last is { } image && image.ClockAfter != main.MainCaller.ContextTimeBits)
            throw new InvalidDataException("Saved image manager changed its actual current Main process/context clock.");
        var sky = state.SkyLighting?.SourceTransfer ?? throw new InvalidDataException("Saved Main images lost the actual Sky owner.");
        foreach (var instance in sky.Images)
        {
            var row = saved.Instances.SingleOrDefault(row => row.Identity == instance.Identity);
            if (row is null || row.Retired is not null || row.Sky != sky.CapturedSky || row.SkyRegistration != instance.ManagerOrdinal ||
                row.Form != instance.Form || row.Program != instance.ProgramSha256 || row.Weight != instance.WeightBits || row.Enabled != instance.EnabledByte)
                throw new InvalidDataException("Saved Main list differs from the actual Sky instance fields.");
        }
    }

    private static void ValidateCurrentSourceMainImages(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateSourceMainImageShape(state);
        if (state.SourceMainImages is not { } saved) return;
        var selected = records.OwnedSource ?? throw new InvalidDataException("Saved source images have no owned source selection.");
        var defaults = FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(selected.FalloutExecutablePath);
        var dof = FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(selected.FalloutExecutablePath, FalloutInstallationSettings.Read(selected));
        var main = state.ActorProcessRuntime!.StandaloneMain!.Source;
        var identity = FalloutAdvancementRuntimeReceipt.Hash(main.Identity + "\0" + defaults.DeclarationSha256 + "\0" + dof.Source + "\0source-image-space-Main/v1");
        if (saved.Source != identity || saved.Default.Source != defaults.DeclarationSha256 || !saved.Default.Words.SequenceEqual(defaults.Words) ||
            saved.DofConstructor.Source != dof.Source)
            throw new InvalidDataException("Saved source image constructor changed its selected original declaration.");
        foreach (var profile in new[] { saved.Primary, saved.Override }.OfType<FalloutSourceImageSpaceProfile>())
        {
            if (profile.Form is not { } form) throw new InvalidDataException("Saved explicit image selector fabricated an anonymous profile.");
            var current = FalloutSourceImageSpaceProfile.Winning(FalloutImageSpaceReader.Read(records.GetEffective(form)));
            if (current.Source != profile.Source || current.FormVersion != profile.FormVersion || !current.Words.SequenceEqual(profile.Words) ||
                current.DefinedWords != profile.DefinedWords || current.SourceByteLength != profile.SourceByteLength)
                throw new InvalidDataException("Saved image selector differs from its complete winning profile.");
        }
        foreach (var row in saved.Instances.Where(row => row.Form is not null))
            if (FalloutImageSpaceModifierReader.Read(records.GetEffective(row.Form!.Value)).SourceSha256 != row.Program)
                throw new InvalidDataException("Saved image instance differs from its actual winning modifier program.");
        var hdr = state.SkyLighting?.SourceTransfer?.Hdr;
        foreach (var writer in new[] { hdr?.Main, hdr?.Secondary, hdr?.ShaderCopy }.OfType<FalloutSkyHdrWrite>())
            if (writer.AnonymousImageSource is { } anonymous && anonymous != defaults.DeclarationSha256)
                throw new InvalidDataException("Saved Sky scalar invented an anonymous image constructor.");
    }
}
