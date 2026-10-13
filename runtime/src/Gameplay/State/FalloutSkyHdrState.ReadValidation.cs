using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyHdrState
{
    internal bool NeedsRendererHandoff => _selected && _nativeCapability is null && _selection is not null;
    internal static void RequireReturnedShaderRead(FalloutMoonShaderFieldSample sample)
    {
        var returned = sample.HdrRead ?? throw new NotSupportedException("source-Sky-shader-field-returned-current-producer-unowned");
        var capability = returned.Capability; var copy = returned.Copy;
        var expected = FalloutSkyHdrSource.Read(new(sample.EngineSha256, sample.SourceContract));
        if (returned.Hdr == Guid.Empty || returned.Shader == Guid.Empty || returned.CopiedAt < 1 ||
            returned.ReadAt != sample.Changed || returned.CopiedAt >= returned.ReadAt || copy.Ordinal != returned.CopiedAt ||
            copy.Kind != FalloutSkyHdrWriteKind.ShaderCopy || copy.ValueBits != sample.RgbMultiplierBits ||
            copy.Frame is null || copy.Frame == Guid.Empty || copy.Capability != capability.Identity ||
            capability.Process != sample.Process || capability.EngineSha256 != sample.EngineSha256 ||
            capability.Contract != expected.Contract || !float.IsFinite(BitConverter.UInt32BitsToSingle(copy.ValueBits)) ||
            capability.Returned < 1 || !capability.NativeResourcesCurrent || capability.Provider != FalloutSkyHdrCapabilityProvider.NativeDevice || string.IsNullOrWhiteSpace(capability.Producer))
            throw new InvalidDataException("Sky shader read lost its actual current copy/read/capability association.");
        if (sample.HdrEnabled && copy.Frame != sample.Frame)
            throw new InvalidDataException("Enabled HDR did not copy its actual main scalar on this shader call.");
        // Disabled HDR has no store. A real earlier write, retained in the same
        // shader owner and read under the current capability, remains valid.
        if (!sample.HdrEnabled && returned.CopiedAt >= returned.ReadAt)
            throw new InvalidDataException("Disabled HDR fabricated a current source shader write.");
    }
}
