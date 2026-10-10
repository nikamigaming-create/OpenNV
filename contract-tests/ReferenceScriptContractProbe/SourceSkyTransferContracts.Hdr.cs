using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static void HdrProducerContracts()
    {
        foreach (var declaration in new[] { Declaration, StandaloneDeclaration })
        {
            var ini = new FalloutNumericIniSettings([new(FalloutSkyHdrSource.SettingName, FalloutIniCollection.Main, 1)], [], "authored-HDR-INI");
            var owner = new FalloutSkyHdrState(declaration, Guid.NewGuid(), Guid.NewGuid(), ini);
            var native = new AuthoredHdrCapability(owner, true);
            owner.EnterRendererCapability(native);
            Reject(() => owner.Capture());
            owner.SelectRenderer(native);
            var image = Guid.NewGuid();
            var first = new FalloutSkyHdrImageInput(image, owner.Process, Guid.NewGuid(), 1,
                new("Authored.esm", 0x501), 15, new('a', 64), 0x80000000, declaration.EngineSha256, owner.Source.Contract, false, null);
            owner.PublishImage(first);
            Require(owner.MainScalarBits == 0x80000000 && owner.SecondaryScalarBits == 0x3f800000,
                "HDR ordinary image lost its actual signed-zero main/one-secondary source stores.");
            owner.PublishImage(first with { Ordinal = 2, Frame = Guid.NewGuid(), Alternate = true, ScalarBits = 0x40a00000 });
            Require(owner.MainScalarBits == 0x80000000 && owner.SecondaryScalarBits == 0x40a00000,
                "HDR alternate image overwrote the main source cell.");
            Reject(() => owner.PublishImage(first));
            Reject(() => owner.PublishImage(first with { Ordinal = 3, Process = Guid.NewGuid() }));
            var sample = owner.ReadShader(Guid.NewGuid()); FalloutSkyHdrState.RequireReturnedShaderRead(sample);
            var saved = owner.Capture();
            Reject(() => FalloutSkyHdrState.Validate(saved with { Main = saved.Main with { ImageOrdinal = 100 } }, owner.Source, saved.Ini));
            Reject(() => FalloutSkyHdrState.RequireReturnedShaderRead(sample with { RgbMultiplierBits = 0x3f800000 }));
            var cold = new FalloutSkyHdrState(declaration, Guid.NewGuid(), Guid.NewGuid(), ini);
            cold.Restore(saved); Reject(() => cold.ReadShader(Guid.NewGuid()));
            Reject(() => cold.RebindRenderer(native));
            var coldNative = new AuthoredHdrCapability(cold, false);
            cold.EnterRendererCapability(coldNative); cold.RebindRenderer(coldNative);
            var preserved = cold.ReadShader(Guid.NewGuid()); FalloutSkyHdrState.RequireReturnedShaderRead(preserved);
            Require(!preserved.HdrEnabled && preserved.RgbMultiplierBits == 0x80000000,
                "Disabled HDR manufactured a fallback rather than retaining its genuine shader copy.");
            var borrower = Guid.NewGuid(); owner.AttachImageBorrower(borrower);
            Reject(() => owner.BeginCapabilityRetirement(native));
            owner.RetainFailure(new InvalidDataException("authored-original-HDR-failure"));
            owner.DetachImageBorrower(borrower); owner.BeginCapabilityRetirement(native);
            Reject(owner.Retire);
            owner.CompleteCapabilityRetirement(native, () => { }); owner.Retire();
            cold.BeginCapabilityRetirement(coldNative); cold.CompleteCapabilityRetirement(coldNative, () => { }); cold.Retire();
        }
        var mode = FalloutSkyModeSource.Read(StandaloneDeclaration);
        Require(mode.Requested(0, 0x81) is null && mode.Requested(4, 0) == 3 && mode.Requested(4, 1) == 1 && mode.Requested(4, 0x81) == 2,
            "Sky mode aliased CELL residency instead of its actual DATA branch.");
        var inactive = mode.Steps(4, 1).ToArray();
        Require(Array.IndexOf(inactive, FalloutSkyModeStep.CloudReset) < Array.IndexOf(inactive, FalloutSkyModeStep.CloudRelease) &&
            Array.IndexOf(inactive, FalloutSkyModeStep.NightRelease) < Array.IndexOf(inactive, FalloutSkyModeStep.ModeStore) &&
            inactive[^1] == FalloutSkyModeStep.ImageManager, "Sky mode lost actual child/store order.");
        Reject(() => FalloutSkyModeSource.Read(Declaration));
        Console.WriteLine("OPENNV_SOURCE_SKY_HDR_PRODUCER_PASS ordinaryAndAlternate=true exactFloat32=true staleWriterRefused=true coldNativeEpoch=true retainedDisabledField=true independentRetirement=true nativeGpuAndHeadset=UNEXECUTED");
    }
    private sealed class AuthoredHdrCapability(FalloutSkyHdrState owner, bool supported) : IFalloutSkyHdrCapability
    {
        public Guid Identity { get; } = Guid.NewGuid();
        public FalloutSkyHdrCapability Read() => new(Identity, owner.Process, owner.Source.EngineSha256,
            owner.Source.Contract, 1, supported, true, "authored-pure-field-contract;not-native-GPU-evidence");
    }
}
