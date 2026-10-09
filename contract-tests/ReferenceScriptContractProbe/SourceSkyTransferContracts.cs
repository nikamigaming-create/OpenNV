using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Stack = "authored-source-Sky-selection";
    private static FalloutSkyTransferDeclaration Declaration => new(Engine, FalloutSkyTransferDeclaration.CurrentContractSha256);
    internal static void Run()
    {
        NativeChildSourceContracts();
        MoonContracts();
        using var fixture = new SourceFixture();
        var images = new FalloutImageSpaceState(); var process = Guid.NewGuid();
        var sky = new FalloutSkyTransferState(Declaration, fixture.Records, images, Stack, .5f);
        sky.BindProcess(process); sky.BindPresentationThread();
        var constructor = sky.Capture();
        Require(constructor.Flags == 32 && constructor.Mode == 4 && constructor.HourBits == BitConverter.SingleToUInt32Bits(10) &&
            constructor.Clouds.Disposition == FalloutSkyChildDisposition.ConstructorNull && constructor.Precipitation.Disposition == FalloutSkyChildDisposition.ConstructorNull &&
            constructor.Images.Count == 0 && constructor.TargetWeather is null, "Source Sky constructor acquired fabricated resources, time or weather.");
        var writes = new List<FalloutSkyResetStep>();
        sky.ResetClimate(fixture.Climate, writes.Add);
        var reset = sky.Capture();
        Require(writes.SequenceEqual(new[] { FalloutSkyResetStep.OverrideNull, FalloutSkyResetStep.PreviousNull, FalloutSkyResetStep.CurrentNull }) &&
            reset.LastCall is { Returned: true, EnteredChild: null, FailureType: null } && reset.LastCall.Completed.SequenceEqual(FalloutSkyTransferState.ResetSteps(Declaration)) &&
            reset.TransitionBits == 0 && (reset.Flags & 0x40) != 0 && (reset.Flags & 8) == 0,
            "Reset did not return its own ordered original stores/children before the climate caller's final bit.");
        var order = new[] { FalloutSourceSkyImageSlot.PreviousPrimary, FalloutSourceSkyImageSlot.CurrentPrimary,
            FalloutSourceSkyImageSlot.PreviousSecondary, FalloutSourceSkyImageSlot.CurrentSecondary };
        Require(reset.Images.Select(image => image.Slot).SequenceEqual(order) && reset.Images.Select(image => image.Identity).Distinct().Count() == 4 &&
            reset.Images.All(image => image.Flags == 1 && image.EnabledByte == 1 && image.Form is null) &&
            reset.Images.Single(image => image.Slot == FalloutSourceSkyImageSlot.CurrentPrimary).WeightBits == BitConverter.SingleToUInt32Bits(.5f) &&
            reset.Images.Single(image => image.Slot == FalloutSourceSkyImageSlot.CurrentSecondary).WeightBits == BitConverter.SingleToUInt32Bits(.5f) &&
            reset.Images.Where(image => image.Slot is FalloutSourceSkyImageSlot.PreviousPrimary or FalloutSourceSkyImageSlot.PreviousSecondary).All(image => image.WeightBits == 0),
            "Source Sky aliased instances, manager order, anonymous program or Float32 weight stores.");
        var freshProcess = Guid.NewGuid(); var coldImages = new FalloutImageSpaceState();
        var cold = new FalloutSkyTransferState(Declaration, fixture.Records, coldImages, Stack, .5f);
        cold.Restore(JsonSerializer.Deserialize<FalloutSkyTransferSnapshot>(JsonSerializer.Serialize(reset))!, freshProcess);
        var restored = cold.Capture();
        Require(restored.CapturedProcess == freshProcess && restored.CapturedSky != reset.CapturedSky && restored.LastCall is null &&
            restored.Handoff?.ImageInstances.Values.ToHashSet().SetEquals(restored.Images.Select(image => image.Identity)) == true &&
            !restored.Images.Select(image => image.Identity).Intersect(reset.Images.Select(image => image.Identity)).Any(),
            "Cold Sky replayed a source call or reused process/instance ownership.");
        Reject(() => FalloutSkyTransferState.Validate(reset with { TargetWeather = fixture.Weather }, Declaration, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(reset with { Images = reset.Images.Take(3).ToArray() }, Declaration, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(reset with { LastCall = reset.LastCall! with { Returned = false } }, Declaration, Stack, fixture.Records));
        Reject(() => FalloutSkyTransferState.Validate(reset, Declaration, "foreign-selection", fixture.Records));
        Reject(() => (Declaration with { Contract = new('f', 64) }).Validate());
        var reused = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f);
        Reject(() => reused.Restore(reset, process)); reused.Retire();
        var failed = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f); failed.BindProcess(Guid.NewGuid());
        Reject(() => failed.ResetClimate(fixture.Climate, step =>
        { if (step == FalloutSkyResetStep.PreviousNull) throw new IOException("authored-store-child-failure"); }));
        Require(failed.LastCall is { Returned: false, EnteredChild: FalloutSkyResetStep.PreviousNull, Error: "authored-store-child-failure" } &&
            failed.LastCall.Completed.SequenceEqual(new[] { FalloutSkyResetStep.DirtyStore, FalloutSkyResetStep.OverrideNull }) &&
            failed.SaveBlocker is not null, "A failed original store was erased or falsely returned.");
        Reject(() => failed.Capture()); failed.Retire();
        var reentrant = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f); reentrant.BindProcess(Guid.NewGuid());
        Reject(() => reentrant.ResetClimate(fixture.Climate, _ => Reject(() => reentrant.ResetClimate(fixture.Climate, _ => { }))));
        Require(reentrant.LastCall is { Returned: false } && reentrant.SaveBlocker is not null,
            "A child swallowed forbidden reentry and certified a source reset return."); reentrant.Retire();
        var external = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f); external.BindProcess(Guid.NewGuid());
        external.MarkExteriorFactoryEntered(); Reject(() => external.ResetClimate(fixture.Climate, _ => { }));
        Require(external.LastCall?.EnteredChild == FalloutSkyResetStep.Precipitation && external.LastCall.Completed.Contains(FalloutSkyResetStep.Clouds) &&
            !external.LastCall.Completed.Contains(FalloutSkyResetStep.ImageInstances), "Absent Precipitation publication was substituted for original constructor null."); external.Retire();
        var slots = FalloutSkyWeatherModifierSlots.Read(fixture.Records, fixture.Weather, Declaration);
        Require(slots.Slots.Count == 6 && slots.Slots[0] == fixture.Modifier && slots.Slots.Skip(1).All(slot => slot is null),
            "Raw binary IAD channel or exact master-owned WTHR modifier link was coerced to another signature/default.");
        using (var deleted = fixture.WithDeletedModifier()) Reject(() => FalloutSkyWeatherModifierSlots.Read(deleted, fixture.Weather, Declaration));
        cold.Retire(); sky.Retire();
        RunStandaloneSkyContracts();
        Console.WriteLine("OPENNV_SOURCE_SKY_TRANSFER_PASS sourceCtorClock10=true orderedReset=true fourDistinctInstances=true " +
            "managerOrder=true anonymousNotForm=true float32Weights=true coldNewEpoch=true failedPrefixRetained=true " +
            "swallowedReentryRefused=true actualBinaryIadWinner=true deletedImadRefused=true nativeCloudsAndPixels=UNEXECUTED " +
            "exteriorMoonWeatherFrameAndCallingThreadFistp=UNOWNED");
    }
    private sealed class SourceFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNVSourceSkyAuthored-" + Guid.NewGuid().ToString("N"));
        private readonly string _base;
        private readonly string _deleted;
        internal readonly FalloutPluginStack Records;
        internal FalloutFormKey Climate => new("Authored.esm", 40);
        internal FalloutFormKey Weather => new("Authored.esm", 41);
        internal FalloutFormKey Modifier => new("Authored.esm", 42);
        internal SourceFixture()
        {
            Directory.CreateDirectory(_directory); _base = Path.Combine(_directory, "Authored.esm"); _deleted = Path.Combine(_directory, "Delete.esp");
            File.WriteAllBytes(_base, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("CLMT", 40, Field("TNAM", [36, 48, 108, 120, 0, 0])),
                Record("WTHR", 41, Field("\0IAD", BitConverter.GetBytes(42u))), Record("IMAD", 42),
                Record("GMST", 43, Field("EDID", Encoding.ASCII.GetBytes("fDaytimeColorExtension\0")), Field("DATA", BitConverter.GetBytes(.5f)))));
            Records = FalloutPluginStack.Load(_directory, ["Authored.esm"]);
        }
        internal FalloutPluginStack WithExtraWeatherImage()
        {
            File.WriteAllBytes(_deleted, Join(Record("TES4", 0, Field("HEDR", new byte[12]),
                Field("MAST", Encoding.ASCII.GetBytes("Authored.esm\0")), Field("DATA", new byte[8])),
                Record("WTHR", 41, Field("\u0004IAD", BitConverter.GetBytes(42u)))));
            return FalloutPluginStack.Load(_directory, ["Authored.esm", "Delete.esp"]);
        }
        internal FalloutPluginStack WithDeletedModifier()
        {
            var tombstone = Record("IMAD", 42); BinaryPrimitives.WriteUInt32LittleEndian(tombstone.AsSpan(8), 0x20);
            File.WriteAllBytes(_deleted, Join(Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Encoding.ASCII.GetBytes("Authored.esm\0")), Field("DATA", new byte[8])), tombstone));
            return FalloutPluginStack.Load(_directory, ["Authored.esm", "Delete.esp"]);
        }
        public void Dispose()
        {
            Records.Dispose(); File.Delete(_base); File.Delete(_deleted); Directory.Delete(_directory);
        }
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var body = Join(fields); var bytes = new byte[24 + body.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)body.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        body.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] body)
    {
        var bytes = new byte[6 + body.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)body.Length)); body.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or IOException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Expected independent source Sky refusal.");
    }
}
