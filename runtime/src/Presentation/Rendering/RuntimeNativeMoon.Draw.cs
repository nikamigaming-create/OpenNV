using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed partial class RuntimeNativeMoon
{
    private readonly FalloutMoonSource _declaration;
    private readonly Guid _sky, _process;
    private RuntimeNativeMoonProperty? _primaryProgram, _shadowProgram;
    private FalloutMoonNativeDrawFields? _draw;

    private static Basis NativeBasis(FalloutCelestialMatrix source)
    {
        source.Validate();
        float F(uint word) => BitConverter.UInt32BitsToSingle(word);
        // C R C^-1 for source (x,y,z) -> Godot (x,z,-y). Godot's Basis
        // constructor takes columns; the persisted declaration retains rows.
        return new(new(F(source.M00), F(source.M20), -F(source.M10)),
            new(F(source.M02), F(source.M22), -F(source.M12)),
            new(-F(source.M01), -F(source.M21), F(source.M11)));
    }
    private void InitializeDrawFields(FalloutSkyCelestialPass primary, FalloutSkyCelestialPass shadow)
    {
        _draw = new(primary.Declaration, shadow.Declaration, FalloutCelestialMatrix.Identity,
            FalloutCelestialRgb.White, FalloutCelestialRgb.White, false, false, null, null, null, null, false);
        _parent!.Basis = NativeBasis(_draw.ParentMatrix);
        RequireDrawFields();
    }
    private FalloutMoonNativeDrawFields RequireDrawFields()
    {
        var draw = _draw ?? throw new InvalidOperationException("Moon omitted its actual matrix/property field owner.");
        _primaryProgram?.RequireMaterial(); _shadowProgram?.RequireMaterial();
        if (_primaryProgram is null || _shadowProgram is null || draw.PrimaryProgram.Identity != _primaryProgram.SourceIdentity ||
            draw.ShadowProgram.Identity != _shadowProgram.SourceIdentity || _parent!.Basis != NativeBasis(draw.ParentMatrix))
            throw new InvalidDataException("Moon changed its exact source programs or actual parent matrix.");
        Colour(_primaryProperty!, draw.PrimaryColor); Colour(_shadowProperty!, draw.ShadowColor);
        Ready(_primaryProperty!, draw.PrimaryDrawAdmitted); Ready(_shadowProperty!, draw.ShadowDrawAdmitted);
        if (draw.PrimaryDrawAdmitted || draw.ShadowDrawAdmitted)
        {
            var sample = draw.LastFrame?.ShaderField ?? throw new InvalidDataException("Moon draw lost its actual live shader field read.");
            FalloutMoonDrawMath.RequireShaderRead(sample, _declaration, _sky, _process);
            foreach (var property in new[] { _primaryProperty!, _shadowProperty! })
            {
                using var raw = property.GetShaderParameter("source_Moon_rgb_multiplier");
                if (raw.VariantType != Variant.Type.Float || BitConverter.SingleToUInt32Bits((float)raw.AsDouble()) != sample.RgbMultiplierBits)
                    throw new InvalidDataException("Moon material drifted from its actual source HDR-conditioned field.");
            }
        }
        return draw;
        static void Colour(ShaderMaterial material, FalloutCelestialRgb expected)
        {
            using var raw = material.GetShaderParameter("source_Moon_colour");
            if (raw.VariantType != Variant.Type.Vector3 ||
                FalloutCelestialRgb.Read(new(raw.AsVector3().X, raw.AsVector3().Y, raw.AsVector3().Z)) != expected)
                throw new InvalidDataException("Moon changed its actual property RGB source stores.");
        }
        static void Ready(ShaderMaterial material, bool expected)
        {
            using var raw = material.GetShaderParameter("source_Moon_draw_admitted");
            if (raw.VariantType != Variant.Type.Bool || raw.AsBool() != expected)
                throw new InvalidDataException("Moon native draw lost its current returned-input admission.");
        }
    }
    private void SetDrawAdmission(bool primary, bool shadow)
    {
        _primaryProperty!.SetShaderParameter("source_Moon_draw_admitted", primary);
        _draw = _draw! with { PrimaryDrawAdmitted = primary };
        _shadowProperty!.SetShaderParameter("source_Moon_draw_admitted", shadow);
        _draw = _draw with { ShadowDrawAdmitted = shadow };
    }
    private void ApplySourceDrawFields(FalloutMoonFrame frame, float angle, float primaryAlpha, float shadowAlpha)
    {
        Require(); _ = Capture();
        if (new[] { angle, primaryAlpha, shadowAlpha, frame.NightAlpha }.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Moon native frame lost an actual finite Float32 input.");
        var upper = FalloutCelestialRgb.Read(frame.UpperSkyColor); upper.Validate();
        var input = new FalloutMoonNativeFrameFields(BitConverter.SingleToUInt32Bits(angle),
            BitConverter.SingleToUInt32Bits(primaryAlpha), BitConverter.SingleToUInt32Bits(shadowAlpha),
            BitConverter.SingleToUInt32Bits(frame.NightAlpha), upper, frame.SkyMode, frame.ShaderField,
            _primaryHasTexture, _shadowHasTexture, false, false);
        _draw = _draw! with { LastFrame = input };
        SetDrawAdmission(false, false);
        // The source has already advanced phase/angle before this field child.
        // Modes outside two/three return without writing the parent or properties.
        if (frame.SkyMode is not (2 or 3)) { Returned(); return; }
        if (!_primaryHasTexture) { _parent!.Visible = false; Returned(); return; }
        var matrix = FalloutMoonDrawMath.ParentRotation(_declaration, angle, _settings.ZOffset);
        _parent!.Basis = NativeBasis(matrix); _draw = _draw with
        {
            ParentMatrix = matrix,
            MatrixWrite = input with { SourceFieldsReturned = true }
        };
        // This early return happens after the matrix store. The individual
        // children/property fields retain the previous frame's values.
        if (primaryAlpha <= 0 && shadowAlpha <= 0) { _parent.Visible = false; Returned(); return; }
        _parent.Visible = true;
        _primaryChild!.Visible = primaryAlpha > 0 && _primaryHasTexture;
        if (_primaryChild.Visible)
        {
            _primaryProperty!.SetShaderParameter("source_Moon_alpha", primaryAlpha);
            _primaryAlpha = input.PrimaryFadeBits;
            _draw = _draw with { PrimaryWrite = input with { SourceFieldsReturned = true } };
        }
        _shadowChild!.Visible = shadowAlpha > 0 && _shadowHasTexture;
        if (_shadowChild.Visible)
        {
            _shadowProperty!.SetShaderParameter("source_Moon_colour", new Vector3(upper.Value.X, upper.Value.Y, upper.Value.Z));
            _draw = _draw with { ShadowColor = upper };
            _shadowProperty.SetShaderParameter("source_Moon_alpha", shadowAlpha); _shadowAlpha = input.ShadowFadeBits;
            _draw = _draw with { ShadowWrite = input with { SourceFieldsReturned = true } };
        }
        Returned();
        if (!_primaryChild.Visible && !_shadowChild.Visible) return;
        // Pixel admission follows the actual local field writes. If the
        // distinct HDR-conditioned live writer is absent, keep the written
        // prefix and original failure; no default scalar or white draw appears.
        FalloutMoonDrawMath.RequireShaderRead(frame.ShaderField, _declaration, _sky, _process);
        var multiplier = BitConverter.UInt32BitsToSingle(frame.ShaderField!.RgbMultiplierBits);
        _primaryProperty!.SetShaderParameter("source_Moon_rgb_multiplier", multiplier);
        _shadowProperty!.SetShaderParameter("source_Moon_rgb_multiplier", multiplier);
        _draw = _draw with { LastFrame = input with { SourceFieldsReturned = true, ShaderInputsReturned = true }, ColdAwaitingFrame = false };
        SetDrawAdmission(_primaryChild.Visible, _shadowChild.Visible);
        _ = Capture();
        void Returned() => _draw = _draw! with { LastFrame = input with { SourceFieldsReturned = true } };
    }
    private void RestoreDrawFields(FalloutMoonNativeDrawFields saved)
    {
        if (_draw is null || saved.PrimaryProgram != _draw.PrimaryProgram || saved.ShadowProgram != _draw.ShadowProgram)
            throw new InvalidDataException("Cold Moon changed its selected source package/program declarations.");
        // All source property/matrix fields persist. Native draw admission is
        // process-local; a prior frame/HDR read cannot authorize the new scene.
        _draw = saved with { PrimaryDrawAdmitted = false, ShadowDrawAdmitted = false, ColdAwaitingFrame = true };
        _parent!.Basis = NativeBasis(saved.ParentMatrix);
        _primaryProperty!.SetShaderParameter("source_Moon_colour", new Vector3(saved.PrimaryColor.Value.X, saved.PrimaryColor.Value.Y, saved.PrimaryColor.Value.Z));
        _shadowProperty!.SetShaderParameter("source_Moon_colour", new Vector3(saved.ShadowColor.Value.X, saved.ShadowColor.Value.Y, saved.ShadowColor.Value.Z));
        SetDrawAdmission(false, false);
        _ = RequireDrawFields();
    }
    private static void RetireSourceProperty(ref RuntimeNativeMoonProperty? owner, ref ShaderMaterial? alias)
    {
        if (owner is null) return;
        try { owner.Retire(); owner = null; }
        finally { alias = owner?.CurrentMaterial; }
    }
}
