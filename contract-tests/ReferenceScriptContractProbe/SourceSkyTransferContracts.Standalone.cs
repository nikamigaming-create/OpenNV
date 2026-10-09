using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static FalloutSkyTransferDeclaration StandaloneDeclaration =>
        new(FalloutSourceMainFamily.Fallout3, FalloutSkyTransferDeclaration.StandaloneContractSha256);

    private static void RunStandaloneSkyContracts()
    {
        using var fixture = new SourceFixture();
        var source = StandaloneDeclaration;
        source.Validate();
        Require(source.IsStandalone && source.WeatherImageSlots == 4 && source.Contract != Declaration.Contract,
            "Standalone Sky inherited the other executable's rule set or six-slot image layout.");
        Reject(() => (source with { Contract = Declaration.Contract }).Validate());
        Reject(() => (Declaration with { Contract = source.Contract }).Validate());
        Reject(() => (source with { EngineSha256 = new('a', 64) }).Validate());
        Reject(() => _ = source.NoonBits);

        var sky = new FalloutSkyTransferState(source, fixture.Records, new(), Stack, .5f);
        sky.BindProcess(Guid.NewGuid()); sky.BindPresentationThread();
        var initial = sky.Capture();
        Require(initial.Flags == 0x20 && initial.HourBits == BitConverter.SingleToUInt32Bits(10) &&
            initial.StandaloneBaseTimes == new FalloutSkyStandaloneBaseTimes(0, 0) && initial.Images.Count == 0,
            "Standalone constructor substituted time, live children or extended caches for its actual base cells.");
        Reject(() => new FalloutSkyTransferState(source, fixture.Records, new(), Stack, .25f));
        var fields = new List<FalloutSkyResetStep>();
        sky.ResetClimate(fixture.Climate, step =>
        {
            Require(sky.Flags == 0x20 && sky.Climate == fixture.Climate,
                "Standalone climate dirty/transition bits were stored before the three source weather null writes.");
            fields.Add(step);
        });
        var saved = sky.Capture();
        var steps = new[] { FalloutSkyResetStep.OverrideNull, FalloutSkyResetStep.PreviousNull, FalloutSkyResetStep.CurrentNull,
            FalloutSkyResetStep.CombinedFlagsStore, FalloutSkyResetStep.TransitionZero, FalloutSkyResetStep.Clouds,
            FalloutSkyResetStep.Precipitation, FalloutSkyResetStep.ImageInstances };
        Require(saved.LastCall is { Returned: true } && saved.LastCall.Completed.SequenceEqual(steps) &&
            fields.SequenceEqual(steps.Take(3)) && saved.Flags == 0x61 && saved.TransitionBits == 0 &&
            saved.StandaloneBaseTimes == new FalloutSkyStandaloneBaseTimes(BitConverter.SingleToUInt32Bits(6), BitConverter.SingleToUInt32Bits(20)) &&
            saved.TimeCaches == new FalloutSkyTimeCaches(BitConverter.SingleToUInt32Bits(5.5f), BitConverter.SingleToUInt32Bits(8),
                BitConverter.SingleToUInt32Bits(18), BitConverter.SingleToUInt32Bits(20.5f)),
            "Standalone reset lost ordered combined-store or one of the six separately dirty time cells.");
        Require(saved.Images.Count == 4 && saved.Images.Single(row => row.Slot == FalloutSourceSkyImageSlot.CurrentPrimary) is
            { WeightBits: 0x3f800000, Program: FalloutSourceSkyImageProgramKind.AnonymousEngineDefault, Form: null } &&
            saved.Images.Where(row => row.Slot != FalloutSourceSkyImageSlot.CurrentPrimary).All(row => row.WeightBits == 0),
            "Standalone day interval incorrectly interpolated FNV noon or aliased an anonymous program to a FormID.");
        var beforeUnchanged = sky.LastCall;
        sky.ResetClimate(fixture.Climate, _ => throw new InvalidDataException("Unchanged nonforced climate entered reset."));
        Require(ReferenceEquals(sky.LastCall, beforeUnchanged), "Unchanged source climate manufactured another reset invocation.");

        var cold = new FalloutSkyTransferState(source, fixture.Records, new(), Stack, .5f);
        cold.Restore(JsonSerializer.Deserialize<FalloutSkyTransferSnapshot>(JsonSerializer.Serialize(saved))!, Guid.NewGuid());
        var restored = cold.Capture();
        Require(restored.StandaloneBaseTimes == saved.StandaloneBaseTimes && restored.TimeCaches == saved.TimeCaches &&
            restored.Flags == saved.Flags && restored.LastCall is null && restored.CapturedProcess != saved.CapturedProcess &&
            restored.CapturedSky != saved.CapturedSky && !restored.Images.Select(row => row.Identity).Intersect(saved.Images.Select(row => row.Identity)).Any(),
            "Cold standalone Sky reset caches, replayed a source return or retained native/process instance identity.");
        Reject(() => FalloutSkyTransferState.Validate(saved with { StandaloneBaseTimes = null }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved with { StandaloneBaseTimes = new(0, BitConverter.SingleToUInt32Bits(20)) }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved with { Flags = saved.Flags | 0x100 }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved with { TimeCaches = saved.TimeCaches with { SunriseStart = BitConverter.SingleToUInt32Bits(5) } }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved with { LastCall = saved.LastCall! with { Completed = FalloutSkyTransferState.ResetSteps(Declaration) } }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved with { LastCall = saved.LastCall! with { Context = saved.LastCall!.Context with { Origin = FalloutSkyResetOrigin.PlayerTransfer } } }, source, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(saved, source, "another-selection", fixture.Records));
        var failed = new FalloutSkyTransferState(source, fixture.Records, new(), Stack, .5f); failed.BindProcess(Guid.NewGuid());
        Reject(() => failed.ResetClimate(fixture.Climate, step =>
        { if (step == FalloutSkyResetStep.PreviousNull) throw new IOException("authored-standalone-store-failure"); }));
        Require(failed.Flags == 0x20 && failed.LastCall is { Returned: false, EnteredChild: FalloutSkyResetStep.PreviousNull } &&
            failed.LastCall.Completed.SequenceEqual(new[] { FalloutSkyResetStep.OverrideNull }) && failed.SaveBlocker is not null,
            "Failed standalone null store committed later source flags or erased the genuine prefix.");
        Reject(() => failed.Capture()); failed.Retire();
        var exterior = new FalloutSkyTransferState(source, fixture.Records, new(), Stack, .5f); exterior.BindProcess(Guid.NewGuid());
        exterior.MarkExteriorFactoryEntered(); Reject(() => exterior.ResetClimate(fixture.Climate, _ => { }));
        Require(exterior.LastCall?.EnteredChild == FalloutSkyResetStep.Precipitation && !exterior.LastCall.Completed.Contains(FalloutSkyResetStep.ImageInstances),
            "Standalone native Precipitation was admitted from a renderer registration or constructor-null substitute."); exterior.Retire();
        var slots = FalloutSkyWeatherModifierSlots.Read(fixture.Records, fixture.Weather, source);
        Require(slots.Slots.Count == 4 && slots.Slots[0] == fixture.Modifier,
            "Standalone WTHR image channels lost exact master-owned slots.");
        using (var deleted = fixture.WithDeletedModifier()) Reject(() => FalloutSkyWeatherModifierSlots.Read(deleted, fixture.Weather, source));
        using (var extra = fixture.WithExtraWeatherImage())
        {
            Reject(() => FalloutSkyWeatherModifierSlots.Read(extra, fixture.Weather, source));
            Require(FalloutSkyWeatherModifierSlots.Read(extra, fixture.Weather, Declaration).Slots[4] == fixture.Modifier,
                "The standalone extra-channel refusal contaminated the independently declared six-channel source.");
        }
        AssertImageRegistrationPrefix(fixture, source);
        AssertStandaloneImageIntervals();
        cold.Retire(); sky.Retire();
        Console.WriteLine("OPENNV_SOURCE_SKY_STANDALONE_PASS selectedDeclaration=true combinedStoreAfterNulls=true " +
            "sixCacheCells=true exactGmst=true fourSlotDay=true float32EachOperation=true intervalBoundaryAndOrder=true " +
            "failedPrefix=true interleavedImageRegistration=true coldNewEpoch=true sourceDriftRefused=true nativeCloudMoonTlsWeatherFrameAndPixels=UNOWNED");
    }

    private static void AssertImageRegistrationPrefix(SourceFixture fixture, FalloutSkyTransferDeclaration source)
    {
        var images = new FalloutImageSpaceState();
        var sky = new FalloutSkyTransferState(source, fixture.Records, images, Stack, .5f); sky.BindProcess(Guid.NewGuid());
        // An independent existing manager owner makes the second primary-pair
        // registration fail. This is real C# ownership, with no native callback.
        var occupied = Guid.NewGuid(); images.RegisterSourceSkyInstance(sky, occupied, 1);
        try
        {
            Reject(() => sky.ResetClimate(fixture.Climate, _ => { }));
            var instances = JsonSerializer.SerializeToElement(sky.State).GetProperty("images");
            Require(sky.LastCall is { Returned: false, EnteredChild: FalloutSkyResetStep.ImageInstances } && instances.GetArrayLength() == 2 &&
                instances.EnumerateArray().All(row => row.GetProperty("Slot").GetInt32() == (int)FalloutSourceSkyImageSlot.CurrentPrimary ||
                    row.GetProperty("Slot").GetInt32() == (int)FalloutSourceSkyImageSlot.PreviousPrimary),
                "A failed primary-pair registration allocated the original source's later secondary pair.");
            Reject(() => sky.Capture());
        }
        finally { sky.Retire(); images.RetireSourceSkyInstance(sky, occupied); }
    }

    private static void AssertStandaloneImageIntervals()
    {
        var boundaries = new[] { (Time: 5.5f, Slot: 3), (Time: 8f, Slot: 1), (Time: 18f, Slot: 1), (Time: 20.5f, Slot: 3) };
        foreach (var row in boundaries)
        {
            var sample = FalloutSkyStandaloneInterpolation.Sample(row.Time, 5.5f, 8, 18, 20.5f);
            Require(sample.Primary == row.Slot && !sample.Interpolated && sample.PrimaryWeight == 1,
                "Standalone interval changed original strict/inclusive boundary ownership.");
        }
        Require(FalloutSkyStandaloneInterpolation.Sample(6.75f, 5.5f, 8, 18, 20.5f) is { Primary: 0, Secondary: 1, Interpolated: true, PrimaryWeight: 1 } &&
            FalloutSkyStandaloneInterpolation.Sample(19.25f, 5.5f, 8, 18, 20.5f) is { Primary: 2, Secondary: 3, Interpolated: true, PrimaryWeight: 1 } &&
            FalloutSkyStandaloneInterpolation.Sample(10, 7, 16, 8, 14) is { Primary: 0, Secondary: 3, Interpolated: true },
            "Standalone midpoint or source branch order was normalized to another algorithm.");
        // Independently reduced source arithmetic: widening this one-minus
        // subtraction changes the result to the adjacent 0x3f1f49f5 cell.
        var precise = FalloutSkyStandaloneInterpolation.Sample(BitConverter.UInt32BitsToSingle(0x40d66666),
            BitConverter.UInt32BitsToSingle(0x40b55555), BitConverter.UInt32BitsToSingle(0x40e55555), 18, 21);
        Require(BitConverter.SingleToUInt32Bits(precise.PrimaryWeight) == 0x3f1f49f6,
            "Standalone source interpolation widened a Float32 intermediate.");
        Require(BitConverter.SingleToUInt32Bits(FalloutSkyStandaloneInterpolation.ExtendedStart(0, .5f)) == 0 &&
            BitConverter.SingleToUInt32Bits(FalloutSkyStandaloneInterpolation.ExtendedEnd(25, .5f, 23.99f)) == BitConverter.SingleToUInt32Bits(23.99f),
            "Standalone time extension lost its original clamp/store declaration.");
        Reject(() => FalloutSkyStandaloneInterpolation.Sample(float.NaN, 6, 8, 18, 20));
    }
}
