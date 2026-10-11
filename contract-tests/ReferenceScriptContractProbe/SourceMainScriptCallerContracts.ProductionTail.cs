using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static int AuditOwnedProductionMainTail(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        using var fixture = new StandaloneFixture(selectedRecords: records, selectedRuntime: runtime.Receipt);
        var owned = records.OwnedSource ?? throw new InvalidDataException("Offline Main requires the selected owned source.");
        var images = new FalloutImageSpaceState();
        images.ConfigureSourceMainImages(fixture.Host.Source, fixture.Owner.ReadMainNativeDeliveryBindings().Process,
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(owned.FalloutExecutablePath),
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(owned.FalloutExecutablePath, FalloutInstallationSettings.Read(owned)));
        images.RebindSourceImagePrograms(records);
        var native = new AuthoredImageNative(images.SourceMainImageIdentity, images.SourceMainImageProcess);
        images.BindSourceMainImageNative(native);
        using var imageAuthority = fixture.Owner.BindSourceMainImages(images);
        Exception? failure = null;
        try
        {
            using var binding = fixture.BindProduction();
            try { fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            var main = fixture.Owner.CaptureMainScriptCaller();
            var clock = fixture.Owner.CaptureSourceCachedTimer();
            var tail = main.CachedTail ?? throw new InvalidDataException("Offline Main omitted the actual production clock tail.");
            var call = main.LastCall ?? throw new InvalidDataException("Offline Main omitted its attempted callback.");
            if (failure is null && (!call.Children.Any(child => child.Step == FalloutMainScriptCallerStep.CachedTailBefore) ||
                    !call.Children.Any(child => child.Step == FalloutMainScriptCallerStep.CachedTailAfter)))
                throw new InvalidDataException("Offline startup skipped the required production clock-tail path.");
            if (failure is not null && (call.Disposition != FalloutMainScriptCallerDisposition.Failed ||
                    call.Children.Last().Returned is not null || call.Children.Last().Error != failure.Message ||
                    fixture.Owner.MainScriptCallerSaveBlocker is null))
                throw new InvalidDataException("Offline production failure lost its prefix or admitted a playable save.");
            if (failure is not null && call.Children.Last().Step is (FalloutMainScriptCallerStep.CachedTailBefore or
                    FalloutMainScriptCallerStep.CachedTimer or FalloutMainScriptCallerStep.CachedTailAfter) &&
                    tail.LastAttempt?.Failure?.StartsWith(failure.GetType().FullName + ": " + failure.Message, StringComparison.Ordinal) != true)
                throw new InvalidDataException("Offline production tail lost its actual entered failure.");
            if (failure is null && (call.Disposition != FalloutMainScriptCallerDisposition.ScopeReturned ||
                    tail.LastAttempt?.Phase != FalloutMainCachedTailPhase.LocalReturned || tail.LastAttempt.Failure is not null))
                throw new InvalidDataException("Offline production Main returned before its actual clock suffix completed.");
            // Passing an isolated component is never evidence that the whole
            // campaign started. Surrounding fixture boundaries stay explicit.
            Console.WriteLine((failure is null ? "OPENNV_OWNED_PRODUCTION_MAIN_TAIL_PASS " : "OPENNV_OWNED_PRODUCTION_MAIN_TAIL_BLOCKED ") +
                JsonSerializer.Serialize(new
                {
                    schema = "opennv-offline-production-main-tail/v1",
                    campaign = RuntimeLiveContentSource.Fallout3Game,
                    productionTail = true,
                    productionMainConsumers = typeof(FalloutCampaignMainScriptConsumers).FullName,
                    status = failure is null ? "local-tail-returned" : "blocked",
                    phase = tail.LastAttempt?.Phase.ToString(),
                    failure = failure?.Message,
                    callDisposition = call.Disposition.ToString(),
                    enteredSteps = call.Children.Select(child => child.Step.ToString()).ToArray(),
                    clockConstructed = clock.Source is not null,
                    saveBlocker = fixture.Owner.MainScriptCallerSaveBlocker,
                    fixtureBoundaries = new[] { "OS input predicates", "interface native factory", "Player children", "image native output" },
                    gameLaunched = false,
                    framesRecorded = false,
                    wholeCampaignStartup = "UNEXECUTED",
                    saveQuitColdContinue = "UNEXECUTED",
                    physicalInputAndRendering = "UNEXECUTED"
                }));
            return failure is null ? 0 : 1;
        }
        finally { images.RetireSourceMainImageNative(native); }
    }
}
