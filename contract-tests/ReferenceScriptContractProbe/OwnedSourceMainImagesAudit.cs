using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedSourceMainImagesAudit
{
    internal static void Run(string dataRoot, string campaign)
    {
        RuntimeLiveContentSource.Configure(dataRoot, campaign);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var defaults = FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(content.FalloutExecutablePath);
        var dof = FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(content.FalloutExecutablePath,
            FalloutInstallationSettings.Read(content));
        dof.Validate();
        FalloutSourceImageSpaceMenuDeclaration? menu = null;
        var menuPrograms = new List<object>();
        if (content.Game == RuntimeLiveContentSource.Fallout3Game)
        {
            menu = FalloutExecutableStringTable.ReadStandaloneImageSpaceMenu(content.FalloutExecutablePath);
            menu.Validate();
            var selectors = FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(content.FalloutExecutablePath);
            foreach (var kind in Enum.GetValues<FalloutMenuBackgroundKind>())
            {
                var record = records.GetEffective(records.RuntimeFormKey(selectors.Form(kind)));
                var program = FalloutImageSpaceModifierReader.Read(record);
                menuPrograms.Add(new { kind = kind.ToString(), program.Form, program.EditorId, program.SourceSha256,
                    winningPlugin = record.Plugin.Name, program.Animated, program.Duration });
            }
            _ = FalloutImageSpaceModifierReader.Read(records.GetEffective(records.RuntimeFormKey(menu.AuxiliaryForm)));
        }
        var profiles = records.EffectiveRecords("IMGS").Where(record => !record.IsDeleted)
            .Select(FalloutImageSpaceReader.Read).Select(FalloutSourceImageSpaceProfile.Winning).ToArray();
        foreach (var profile in profiles) profile.Validate();
        Console.WriteLine("OPENNV_OWNED_SOURCE_MAIN_IMAGES_PASS " + JsonSerializer.Serialize(new
        {
            defaultDeclaration = defaults.DeclarationSha256, defaultWords = defaults.Words.Length,
            dofDeclaration = dof.Source, dofParameters = dof.Parameters.Length,
            menuDeclaration = menu, selectedMenuPrograms = menuPrograms,
            winningProfiles = profiles.Length, profileWords = 38,
            sourceExtents = profiles.GroupBy(profile => profile.SourceByteLength).ToDictionary(group => group.Key, group => group.Count()),
            profilesWithUnwrittenCells = profiles.Count(profile => profile.DefinedWords != FalloutSourceImageSpaceProfile.CompleteWords),
            nativeFactories = "UNEXECUTED", reachedStartupModifiers = "NOT_OBSERVED", ordinaryGameplay = "UNEXECUTED",
            menuSharedCalls = "UNEXECUTED", menuAuxiliaryInterfaceGate = "UNOWNED", finalPixels = "UNEXECUTED",
        }));
    }
}
