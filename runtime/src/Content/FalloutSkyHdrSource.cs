using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Selected original declarations reduced to public field/consumer contracts.
// Device capability, image composition and a returned shader-field read have
// different owners. None is established by registering a renderer.
internal sealed record FalloutSkyHdrSource(string EngineSha256, string SkyContract, string Contract)
{
    internal const string SettingName = "bDoHighDynamicRange:BlurShaderHDR";
    private const string Rules = "source-Sky-HDR/v1;loader-HDR-byte-zero-main-and-secondary-Float32-one;" +
        "renderer-INI-Boolean-and-returned-half-float-RGBA-post-pixel-blend-format-query;" +
        "separate-filter-query-does-not-write-HDR-enable;" +
        "ordinary-current-ImageSpace-trait-eight-copies-main-scalar-before-secondary-one;" +
        "alternate-current-ImageSpace-trait-eight-copies-secondary-without-main-store;" +
        "selected-Sky-resource-load-writes-main-one-only-when-HDR-byte-nonzero;" +
        "SkyShader-render-reads-HDR-byte-before-conditional-main-scalar-copy;" +
        "disabled-HDR-keeps-previous-shader-field-no-zero-one-fallback;" +
        "all-fields-retain-Float32-bits-source-write-order-and-current-process-lease;" +
        "cold-state-is-not-a-native-device-or-returned-frame;flat-and-XR-share-source-fields";
    internal static FalloutSkyHdrSource Read(FalloutSkyTransferDeclaration sky)
    {
        sky.Validate();
        return new(sky.EngineSha256, sky.Contract, FalloutSkyTransferDeclaration.Hash(sky.EngineSha256 + ";" + sky.Contract + ";" + Rules));
    }
    internal void Require(FalloutSkyTransferDeclaration sky)
    {
        if (this != Read(sky)) throw new InvalidDataException("Sky HDR changed its selected original field/producer declaration.");
    }
    internal FalloutSkyHdrIni ReadSetting(FalloutNumericIniSettings settings)
    {
        var entries = settings.Declarations.Where(row => row.Declaration.Name.Equals(SettingName, StringComparison.Ordinal)).ToArray();
        if (entries.Length != 1 || entries[0].Declaration.Kind != 'b' || entries[0].Number is not (0 or 1) ||
            string.IsNullOrWhiteSpace(entries[0].Origin) || string.IsNullOrWhiteSpace(settings.Source))
            throw new NotSupportedException("Source HDR initializer has no unique typed original INI declaration/current value.");
        var value = entries[0];
        return new(value.Declaration, value.Number == 1, value.Origin, settings.Source);
    }
}

internal sealed record FalloutSkyHdrIni(FalloutIniDeclaration Declaration, bool Requested, string Origin, string SettingsSource);

internal enum FalloutSkyHdrWriteKind { Constructor, RendererSelection, OrdinaryImage, AlternateImage, SkyResourceLoad, ShaderCopy }
internal sealed record FalloutSkyHdrWrite(long Ordinal, FalloutSkyHdrWriteKind Kind, uint ValueBits,
    Guid? Frame, Guid? ImageOwner, FalloutFormKey? ImageForm, string? ImageSha256, Guid? Capability,
    long? ImageOrdinal = null, ushort? ImageFormVersion = null, string? AnonymousImageSource = null);

internal enum FalloutSkyHdrCapabilityProvider { NativeDevice = 1, HeadlessNoRenderer = 2 }
internal sealed record FalloutSkyHdrCapability(Guid Identity, Guid Process, string EngineSha256, string Contract,
    long Returned, bool HalfFloatRgbaBlendSupported, bool NativeResourcesCurrent, string Producer,
    FalloutSkyHdrCapabilityProvider Provider = FalloutSkyHdrCapabilityProvider.NativeDevice)
{
    internal const string HeadlessProducer = "Godot-headless/backend-no-rendering-device";
    internal bool Current => Provider switch
    {
        FalloutSkyHdrCapabilityProvider.NativeDevice => NativeResourcesCurrent,
        FalloutSkyHdrCapabilityProvider.HeadlessNoRenderer => !NativeResourcesCurrent && !HalfFloatRgbaBlendSupported && Producer == HeadlessProducer,
        _ => false,
    };
}
internal interface IFalloutSkyHdrCapability
{
    Guid Identity { get; }
    FalloutSkyHdrCapability Read();
}

internal sealed record FalloutSkyHdrImageInput(Guid ImageOwner, Guid Process, Guid Frame, long Ordinal,
    FalloutFormKey? Form, ushort? FormVersion, string DnamSha256, uint ScalarBits, string EngineSha256,
    string Contract, bool Alternate, string? Unowned, string? AnonymousImageSource = null);

internal sealed record FalloutSkyHdrShaderRead(Guid Hdr, Guid Shader, long CopiedAt, long ReadAt,
    FalloutSkyHdrWrite Copy, FalloutSkyHdrCapability Capability);
