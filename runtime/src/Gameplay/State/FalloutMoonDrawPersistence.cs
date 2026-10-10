using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutMoonDrawPersistence
{
    internal static void RequireFields(FalloutMoonNativeFields native)
    {
        var draw = native.Draw ?? throw new InvalidDataException("Moon omitted its current source programs/matrix/property fields.");
        draw.ParentMatrix.Validate(); draw.PrimaryColor.Validate(); draw.ShadowColor.Validate();
        if (draw.PrimaryColor != FalloutCelestialRgb.White || draw.PrimaryProgram.Role != 6 || draw.ShadowProgram.Role != 7 ||
            draw.PrimaryProgram.Identity == draw.ShadowProgram.Identity ||
            draw.ColdAwaitingFrame && (draw.PrimaryDrawAdmitted || draw.ShadowDrawAdmitted))
            throw new InvalidDataException("Moon introduced another property role, RGB writer or cold draw admission.");
        foreach (var program in new[] { draw.PrimaryProgram, draw.ShadowProgram })
            if (string.IsNullOrWhiteSpace(program.PackagePath) || string.IsNullOrWhiteSpace(program.ResourceSource) ||
                new[] { program.SourceContract, program.PackageSha256, program.VertexSha256, program.PixelSha256, program.Identity }
                    .Any(value => value is null || value.Length != 64 || !value.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Moon source program metadata is incomplete.");
        foreach (var frame in new[] { draw.LastFrame, draw.MatrixWrite, draw.PrimaryWrite, draw.ShadowWrite }.OfType<FalloutMoonNativeFrameFields>())
        {
            frame.UpperSkyColor.Validate();
            if (!frame.SourceFieldsReturned || new[] { frame.AngleBits, frame.PrimaryFadeBits, frame.ShadowFadeBits, frame.NightAlphaBits }
                    .Any(word => !float.IsFinite(BitConverter.UInt32BitsToSingle(word))))
                throw new InvalidDataException("Moon source frame did not return its exact finite field writes.");
        }
        if (draw.LastFrame is null && (draw.MatrixWrite is not null || draw.PrimaryWrite is not null || draw.ShadowWrite is not null) ||
            draw.PrimaryDrawAdmitted && (native.ParentHidden || native.PrimaryHidden || !native.PrimaryHasTexture) ||
            draw.ShadowDrawAdmitted && (native.ParentHidden || native.ShadowHidden || !native.ShadowHasTexture) ||
            (draw.PrimaryDrawAdmitted || draw.ShadowDrawAdmitted) && draw.LastFrame is not { ShaderInputsReturned: true })
            throw new InvalidDataException("Moon draw invented a source field return, geometry visibility or shader input return.");
    }

    internal static void Validate(FalloutMoonNativeFields native, FalloutMoonSource source, FalloutMoonSettings settings,
        Guid sky, Guid process, FalloutMoonProgramIdentity primary, FalloutMoonProgramIdentity shadow)
    {
        RequireFields(native); var draw = native.Draw!;
        if (draw.PrimaryProgram != primary || draw.ShadowProgram != shadow)
            throw new InvalidDataException("Saved Moon changed its exact selected SDP bytes/program/constant declarations.");
        if (draw.MatrixWrite is { } matrix)
        {
            Active(matrix);
            if (!matrix.PrimaryHasTexture || draw.ParentMatrix != FalloutMoonDrawMath.ParentRotation(source,
                    BitConverter.UInt32BitsToSingle(matrix.AngleBits), settings.ZOffset))
                throw new InvalidDataException("Saved Moon matrix is not its actual retained source matrix write.");
        }
        else if (draw.ParentMatrix != FalloutCelestialMatrix.Identity)
            throw new InvalidDataException("Moon matrix changed before its source writer.");
        if (draw.PrimaryWrite is { } primaryWrite)
        {
            Active(primaryWrite);
            if (!primaryWrite.PrimaryHasTexture || BitConverter.UInt32BitsToSingle(primaryWrite.PrimaryFadeBits) <= 0 ||
                native.PrimaryAlphaBits != primaryWrite.PrimaryFadeBits)
                throw new InvalidDataException("Moon primary opacity changed outside its visible source property writer.");
        }
        else if (native.PrimaryAlphaBits != BitConverter.SingleToUInt32Bits(1f))
            throw new InvalidDataException("Moon primary opacity changed before its source writer.");
        if (draw.ShadowWrite is { } shadowWrite)
        {
            Active(shadowWrite);
            if (!shadowWrite.PrimaryHasTexture || !shadowWrite.ShadowHasTexture ||
                BitConverter.UInt32BitsToSingle(shadowWrite.ShadowFadeBits) <= 0 ||
                native.ShadowAlphaBits != shadowWrite.ShadowFadeBits || draw.ShadowColor != shadowWrite.UpperSkyColor)
                throw new InvalidDataException("Moon shadow lost its distinct upper-sky RGB/opacity source write.");
        }
        else if (native.ShadowAlphaBits != BitConverter.SingleToUInt32Bits(1f) || draw.ShadowColor != FalloutCelestialRgb.White)
            throw new InvalidDataException("Moon shadow changed before its source property writer.");
        if (draw.LastFrame is { } last)
        {
            if (last.Mode is 2 or 3)
            {
                var primaryFade = BitConverter.UInt32BitsToSingle(last.PrimaryFadeBits);
                var shadowFade = BitConverter.UInt32BitsToSingle(last.ShadowFadeBits);
                var parentHidden = !last.PrimaryHasTexture || primaryFade <= 0 && shadowFade <= 0;
                if (native.ParentHidden != parentHidden || !parentHidden &&
                    (native.PrimaryHidden != (primaryFade <= 0 || !last.PrimaryHasTexture) ||
                     native.ShadowHidden != (shadowFade <= 0 || !last.ShadowHasTexture)))
                    throw new InvalidDataException("Moon lost its original parent/child visibility branch.");
            }
            if (last.ShaderInputsReturned)
            {
                Active(last);
                var sample = last.ShaderField ?? throw new InvalidDataException("Returned Moon draw omitted its actual HDR field read.");
                // A cold recreation retains the historical field receipt but
                // disables drawing. Only a new live frame may admit that scene.
                FalloutMoonDrawMath.RequireShaderRead(sample, source,
                    draw.ColdAwaitingFrame ? sample.Sky : sky, draw.ColdAwaitingFrame ? sample.Process : process);
                if (sample.Sky == Guid.Empty || sample.Process == Guid.Empty || !last.PrimaryHasTexture ||
                    BitConverter.UInt32BitsToSingle(last.PrimaryFadeBits) <= 0 && BitConverter.UInt32BitsToSingle(last.ShadowFadeBits) <= 0)
                    throw new InvalidDataException("Moon draw used another or hidden source field arm.");
            }
        }
        else if (native.ParentHidden || native.PrimaryHidden || native.ShadowHidden || draw.PrimaryDrawAdmitted || draw.ShadowDrawAdmitted)
            throw new InvalidDataException("Moon introduced a visibility/draw writer before an actual source frame.");
        static void Active(FalloutMoonNativeFrameFields frame)
        {
            if (frame.Mode is not (2 or 3)) throw new InvalidDataException("Moon field writer crossed the source mode early return.");
        }
    }

    internal static FalloutMoonNativeFields PrepareCold(FalloutMoonNativeFields saved)
    {
        RequireFields(saved);
        return saved with { Draw = saved.Draw! with { PrimaryDrawAdmitted = false, ShadowDrawAdmitted = false, ColdAwaitingFrame = true } };
    }
}
