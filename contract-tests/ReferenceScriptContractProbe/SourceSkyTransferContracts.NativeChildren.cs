using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static void NativeChildSourceContracts()
    {
        using var fixture = new SourceFixture();
        foreach (var declaration in new[] { Declaration, StandaloneDeclaration })
        {
            var source = FalloutSkyNativeChildSource.Read(declaration);
            source.Require(declaration);
            Reject(() => source.Require(declaration with { Contract = new('e', 64) }));
            var ini = new FalloutNumericIniSettings([
                new(FalloutSkyNativeChildSource.PrecipitationSetting, FalloutIniCollection.Main, 0)], [], "authored-precipitation-setting");
            var gate = source.Precipitation(ini);
            var factory = new FalloutSkyPrecipitationFactory(source, gate);
            var sky = new FalloutSkyTransferState(declaration, fixture.Records, new(), Stack, .5f);
            sky.BindProcess(Guid.NewGuid()); sky.BindPresentationThread();
            sky.BindPrecipitationFactory(factory);
            var saved = sky.Capture();
            Require(saved.Precipitation.Disposition == FalloutSkyChildDisposition.ConstructorNull &&
                saved.Precipitation.Factory == factory && saved.Precipitation.Source is null,
                "Disabled source precipitation fabricated a native child or omitted its actual typed gate.");
            var cold = new FalloutSkyTransferState(declaration, fixture.Records, new(), Stack, .5f);
            cold.Restore(JsonSerializer.Deserialize<FalloutSkyTransferSnapshot>(JsonSerializer.Serialize(saved))!, Guid.NewGuid());
            cold.BindPresentationThread(); cold.BindPrecipitationFactory(factory);
            Require(cold.Capture().Precipitation.Factory == factory, "Cold source precipitation changed its actual gate/provenance.");
            Reject(() => cold.BindPrecipitationFactory(factory with { Setting = gate with { Number = 1 } }));
            Reject(() => FalloutSkyTransferState.Validate(saved with { Precipitation = saved.Precipitation with
                { Factory = factory with { Setting = gate with { Number = 1 } } } }, declaration, Stack, fixture.Records));
            Reject(() => source.Precipitation(new FalloutNumericIniSettings([
                new(FalloutSkyNativeChildSource.PrecipitationSetting, FalloutIniCollection.Prefs, 0)], [], "authored-foreign-collection")));
            Reject(() => sky.BindPrecipitationFactory(factory with { Setting = gate with { Number = 2 } }));
            sky.Retire(); cold.Retire();
        }
        Console.WriteLine("OPENNV_SOURCE_SKY_NATIVE_CHILD_METADATA_PASS distinctPrecipitation=true typedGate=true disabledFactoryNull=true coldProvenance=true wrongGateRefused=true nativeAndMoonFactories=UNEXECUTED");
    }
}
