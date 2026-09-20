using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private RuntimeNativeShotEffects? _goreEffects;
    private string? _goreError;
    private object? _lastSeverEffect;

    private void EmitSeverEffect(FalloutBodyPart part)
    {
        try
        {
            if (part.Sever.ImpactSet is not { } set) return;
            var material = checked((int)_world.HealthSource(_state.Reference).ImpactMaterial);
            var impact = FalloutImpact.Resolve(_records, set, material);
            if (impact is null) return;
            var bone = _skeleton.BoneIndex(part.GoreBone ?? part.Node);
            if (bone < 0) throw new NotSupportedException("Source sever effect has no bound gore bone.");
            var offset = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(new(part.GoreRotation.X, part.GoreRotation.Y, part.GoreRotation.Z), 1),
                GamebryoCoordinate.ConvertVector(new(part.GoreTranslation.X, part.GoreTranslation.Y, part.GoreTranslation.Z)) * _skeleton.UnitsToMetres);
            Transform3D Frame() => _skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(bone) * offset;
            var pose = Frame();
            if (_goreEffects is null)
            {
                _goreEffects = new(_records, _content, _skeleton.UnitsToMetres, null, _mask);
                _actor.AddChild(_goreEffects);
            }
            _goreEffects.Impact(impact, pose.Origin, pose.Basis.Y.Normalized(), -pose.Basis.Y.Normalized(), decal: false, follow: Frame);
            _lastSeverEffect = new
            {
                part = part.Type,
                impact = impact.Form.ToString(),
                impact.Model,
                bone = part.GoreBone,
                sourceDecals = part.Sever.Decals,
                boundary = "source-BPTD-impact;environment-spray-decals-and-debris-unbound"
            };
            _goreError = null;
        }
        catch (Exception error)
        {
            _goreError = error.Message;
            GD.PushError($"OPENNV_SEVER_EFFECT_UNBOUND reference={_state.Reference} part={part.Type} {error.Message}");
        }
    }
}
