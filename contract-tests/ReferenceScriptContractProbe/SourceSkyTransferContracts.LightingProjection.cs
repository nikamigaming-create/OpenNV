using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static void LightingProjectionOrderContracts()
    {
        using var fixture = new SourceFixture();
        foreach (var declaration in new[] { Declaration, StandaloneDeclaration })
        {
            var sky = new FalloutSkyLightingState(fixture.Records, .5f);
            sky.ConfigureSourceTransfer(declaration, Stack, Guid.NewGuid(), new());
            var alternateWeather = new FalloutFormKey("Authored.esm", 47);
            var cell = new FalloutCellDefinition(new("Authored.esm", 45), "AuthoredExterior", 0,
                (0, 0), new("Authored.esm", 44), null);
            sky.ForceWeather(alternateWeather);
            sky.EnterCell(cell);
            Require(sky.ActiveWeather.Form == fixture.Weather && sky.ExteriorWeather == fixture.Weather &&
                sky.ForcedWeather is null && sky.SourceTransfer!.CurrentWeather is null &&
                sky.SourceTransfer.LastCall is { Returned: true },
                "First climate entry erased selected record-based weather or fabricated a native weather-frame write.");
            var initial = sky.CaptureLightingProjection();
            var transfer = sky.SourceTransfer ?? throw new InvalidDataException("Authored source Sky factory did not return.");
            var sourceIdentity = transfer.Identity;
            var reset = transfer.LastCall;
            sky.RestoreLightingProjection(initial);
            Require(ReferenceEquals(sky.SourceTransfer, transfer) && transfer.Identity == sourceIdentity && transfer.LastCall == reset,
                "Lighting projection restore replaced source lifetime or replayed its actual reset.");
            Reject(() => sky.Restore(new(initial.Climate, initial.Regions, initial.ForcedWeather,
                initial.ExteriorWeather, initial.RandomState, initial.ClimateWeather)));
            sky.EnterCell(cell);
            Require(sky.ActiveWeather.Form == fixture.Weather &&
                sky.CaptureLightingProjection().RandomState == initial.RandomState,
                "Same-climate entry erased or rerolled its retained weather selection.");
            sky.ForceWeather(alternateWeather);
            sky.EnterCell(cell);
            Require(sky.ForcedWeather == alternateWeather && sky.ActiveWeather.Form == alternateWeather,
                "Unchanged climate cleared a retained override without a source reset.");
            var selected = initial with
            {
                Climate = new("Authored.esm", 46),
                ClimateWeather = alternateWeather,
                ExteriorWeather = alternateWeather,
                ForcedWeather = fixture.Weather,
            };
            var beforeRejectedCommit = transfer.LastCall;
            Reject(() => sky.CommitLightingProjection(selected with { Regions = null! }));
            Reject(() => sky.CommitLightingProjection(selected with { ExteriorWeather = fixture.Climate }));
            Require(transfer.LastCall == beforeRejectedCommit && transfer.Climate == initial.Climate &&
                sky.ForcedWeather == alternateWeather && sky.ActiveWeather.Form == alternateWeather,
                "A rejected lighting projection entered source reset or cleared a real retained override.");
            sky.CommitLightingProjection(selected);
            Require(sky.ActiveWeather.Form == alternateWeather && sky.ExteriorWeather == alternateWeather &&
                sky.ForcedWeather is null && sky.SourceTransfer!.Climate == selected.Climate &&
                sky.SourceTransfer.CurrentWeather is null && sky.SourceTransfer.LastCall is { Returned: true },
                "Grid climate commit erased selected weather, restored a cleared override, or admitted a native weather frame.");
            sky.RetireSourceTransfer();
        }
        Console.WriteLine("OPENNV_LIGHTING_PROJECTION_ORDER_PASS firstEntry=true sameClimateCache=true " +
            "gridCommit=true overrideResetPreserved=true nativeWeatherFrame=UNOWNED pixels=UNEXECUTED");
    }

    internal static void AuditOwnedClimateEntry(string dataRoot, string cellId)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var cell = FalloutCellSceneReader.ReadDefinition(records, records.RuntimeFormKey(uint.Parse(cellId,
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)));
        Require((cell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0, "Owned climate entry needs an actual exterior CELL.");
        var globals = FalloutGlobalState.Read(records);
        var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
        // Isolated source-state construction in this audit process. This is
        // owned record/reader proof, not an entered gameplay World/Main frame.
        sky.ConfigureSourceTransfer(content.FalloutExecutablePath, content.StackId, Guid.NewGuid(), new());
        sky.EnterCell(cell, globals);
        var selected = sky.CaptureLightingProjection();
        Require(selected.ExteriorWeather is not null && selected.ExteriorWeather != sky.DefaultWeather &&
            sky.ActiveWeather.Form == selected.ExteriorWeather && sky.SourceTransfer!.CurrentWeather is null &&
            sky.SourceTransfer.LastCall is { Returned: true },
            "Owned first climate entry lost its selected weather or fabricated an original weather-frame pointer.");
        sky.EnterCell(cell, globals);
        Require(sky.ActiveWeather.Form == selected.ExteriorWeather &&
            sky.CaptureLightingProjection().RandomState == selected.RandomState,
            "Owned repeat entry replaced the retained climate cache.");
        var fresh = new FalloutSkyLightingState(records, sky.DaytimeExtension);
        fresh.ConfigureSourceTransfer(content.FalloutExecutablePath, content.StackId, Guid.NewGuid(), new());
        fresh.CommitLightingProjection(selected);
        Require(fresh.ActiveWeather.Form == selected.ExteriorWeather &&
            fresh.SourceTransfer!.CurrentWeather is null && fresh.SourceTransfer.LastCall is { Returned: true },
            "Owned changed-climate commit erased selected weather or certified an original weather frame.");
        fresh.RetireSourceTransfer(); sky.RetireSourceTransfer();
        Console.WriteLine($"OPENNV_OWNED_CLIMATE_ENTRY_PASS cell={cell.FormKey} selectedLightingWeather={selected.ExteriorWeather} " +
            "firstEntry=true sameClimateCache=true changedClimateCommit=true componentOnly=true nativeWeatherFrame=UNOWNED pixels=UNEXECUTED");
    }
}
