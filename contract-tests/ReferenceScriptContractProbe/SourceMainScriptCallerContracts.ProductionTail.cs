using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ??
        throw new InvalidDataException("Offline clock serialization lost its actual source state.");
    internal static int AuditOwnedProductionMainTail(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        using var fixture = new StandaloneFixture(selectedRecords: records, selectedRuntime: runtime.Receipt);
        var owned = records.OwnedSource ?? throw new InvalidDataException("Offline Main requires the selected owned source.");
        var images = new FalloutImageSpaceState();
        var globals = FalloutGlobalState.Read(records);
        var gameTime = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
            FalloutCalendar.Read(owned.FalloutExecutablePath));
        images.ConstructDoubleVisionClock(FalloutExecutableStringTable.ReadDoubleVisionPhase(owned.FalloutExecutablePath),
            gameTime, fixture.Owner.ReadMainNativeDeliveryBindings().Process);
        images.ConfigureSourceMainImages(fixture.Host.Source, fixture.Owner.ReadMainNativeDeliveryBindings().Process,
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(owned.FalloutExecutablePath),
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(owned.FalloutExecutablePath, FalloutInstallationSettings.Read(owned)));
        images.RebindSourceImagePrograms(records);
        var native = new AuthoredImageNative(images.SourceMainImageIdentity, images.SourceMainImageProcess);
        images.BindSourceMainImageNative(native);
        using var imageAuthority = fixture.Owner.BindSourceMainImages(images);
        Exception? failure = null;
        var serializedCold = false;
        try
        {
            using var binding = fixture.BindProduction();
            fixture.Session.SetSceneIsComplex(true);
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
            if (failure is null)
            {
                RequireOwnedClockChannels(fixture, images);
                var saved = Copy(fixture.Owner.Capture());
                using var cold = new StandaloneFixture(saved, Copy(fixture.Fade.Capture()), selectedRecords: records, selectedRuntime: runtime.Receipt);
                cold.Session.Restore(Copy(fixture.Session.Capture()));
                var restored = cold.Owner.CaptureMainScriptCaller().CachedTail!.RendererClock!;
                Require(restored.CapturedProcess != tail.RendererClock!.CapturedProcess &&
                    restored with { CapturedProcess = tail.RendererClock.CapturedProcess } == tail.RendererClock,
                    "Owned cold renderer construction replayed publication or reset an authoritative source cell.");
                var coldGlobals = FalloutGlobalState.Read(records); coldGlobals.Restore(Copy(globals.Capture()));
                var coldTime = new FalloutGameTime(coldGlobals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(owned.FalloutExecutablePath));
                coldTime.Restore(Copy(gameTime.Capture()));
                var coldImages = new FalloutImageSpaceState();
                var coldProcess = cold.Owner.ReadMainNativeDeliveryBindings().Process;
                coldImages.RestoreDoubleVisionClock(Copy(images.CaptureDoubleVisionClock()),
                    FalloutExecutableStringTable.ReadDoubleVisionPhase(owned.FalloutExecutablePath), coldTime, coldProcess);
                coldImages.ConfigureSourceMainImages(cold.Host.Source, coldProcess,
                    FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(owned.FalloutExecutablePath),
                    FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(owned.FalloutExecutablePath, FalloutInstallationSettings.Read(owned)),
                    Copy(images.CaptureSourceMainImages()));
                coldImages.RebindSourceImagePrograms(records);
                var coldNative = new AuthoredImageNative(coldImages.SourceMainImageIdentity, coldProcess);
                coldImages.BindSourceMainImageNative(coldNative);
                using var coldAuthority = cold.Owner.BindSourceMainImages(coldImages);
                try
                {
                    using var coldBinding = cold.BindProduction();
                    cold.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
                    RequireOwnedClockChannels(cold, coldImages);
                    serializedCold = true;
                }
                finally { coldImages.RetireSourceMainImageNative(coldNative); coldImages.RetireDoubleVisionClock(); }
            }
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
                    fourIndependentChannels = failure is null,
                    serializedCold,
                    saveBlocker = fixture.Owner.MainScriptCallerSaveBlocker,
                    fixtureBoundaries = new[] { "OS input predicates", "interface native factory", "Player children", "image native output", "native simulation/audio rate consumer" },
                    gameLaunched = false,
                    framesRecorded = false,
                    wholeCampaignStartup = "UNEXECUTED",
                    saveQuitColdContinue = "UNEXECUTED",
                    physicalInputAndRendering = "UNEXECUTED"
                }));
            return failure is null ? 0 : 1;
        }
        finally { images.RetireSourceMainImageNative(native); images.RetireDoubleVisionClock(); }
    }
    private static void RequireOwnedClockChannels(StandaloneFixture fixture, FalloutImageSpaceState images)
    {
        var caller = fixture.Owner.CaptureMainScriptCaller();
        var tail = caller.CachedTail!;
        var timer = fixture.Owner.CaptureSourceCachedTimer();
        var renderer = tail.RendererClock ?? throw new InvalidDataException("Owned Main omitted its current/cold renderer clock.");
        var phase = images.CaptureDoubleVisionClock();
        Require(tail.LastAttempt is { Phase: FalloutMainCachedTailPhase.LocalReturned, Failure: null } &&
            renderer.LastPublication is { Prefix: FalloutRendererClockPrefix.Returned, Failure: null, SceneIsComplex: true } &&
            renderer.Steps <= 1 && renderer.Publications == caller.Calls && phase.Samples == caller.Calls &&
            tail.ChannelBits[0] == phase.SecondsBits && tail.ChannelBits[1] == BitConverter.SingleToUInt32Bits(timer.ScaledSeconds) &&
            tail.ChannelBits[2] == BitConverter.SingleToUInt32Bits((float)timer.CachedMilliseconds / renderer.Configuration.Source.MillisecondsPerSecond) &&
            tail.ChannelBits[3] == BitConverter.SingleToUInt32Bits(timer.ScaledSeconds),
            "Owned Main clock channels lost their independent actual timer/calendar/scene-complexity owners or cold history.");
    }
}
