using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private BoneAttachment3D? _weaponAttachment;
    private Node3D? _weaponRoot;
    private Transform3D _weaponRest;
    private RuntimeNativeNifAnimation? _actionClip;
    private double _actionSeconds;
    private bool _drawn = true;

    internal RuntimeNativeNifAnimation PrepareAction(string group)
    {
        if (Weapon is null) throw new InvalidOperationException("Weapon action has no equipped weapon.");
        return Clip(Weapon.AnimationGroup + group);
    }

    internal void SetAction(string? group, double seconds)
    {
        _actionClip = group is null ? null : PrepareAction(group);
        _actionSeconds = seconds;
    }

    internal void SetDrawn(bool drawn)
    {
        if (_drawn == drawn || _weaponAttachment is null || _weaponRoot is null) return;
        if (drawn)
        {
            _weaponAttachment.BoneName = "Weapon";
            _weaponRoot.Transform = _weaponRest;
            _weaponAttachment.Visible = true;
        }
        else if (_first) _weaponAttachment.Visible = false;
        else
        {
            var clip = PrepareAction("unequip");
            var detach = clip.TextKeys.Single(key => key.Value.Trim().Equals("Detach", StringComparison.OrdinalIgnoreCase));
            var parenting = clip.TextKeys.Where(key => key.Value.Trim().StartsWith("prn:", StringComparison.OrdinalIgnoreCase)).OrderBy(key => key.Time).Last();
            var parent = parenting.Value.Trim()[4..].Trim();
            // At Detach the source Weapon channel changes coordinate frame:
            // its translation/rotation are now local to the prn bone. Applying
            // the old hand's global pose a second time displaces the holster.
            Skeleton.Node.ResetBonePoses();
            Skeleton.Node.SetBonePose(Skeleton.BoneIndex(_idle!.Sequence.TargetName), Transform3D.Identity);
            RuntimeNativeNifAnimation.ApplyLayers((_idle!, _idle!.Sequence.StartTime), (clip, Math.Max(detach.Time, parenting.Time)));
            _ = Skeleton.BoneIndex(parent);
            _weaponAttachment.BoneName = parent;
            _weaponRoot.Transform = Skeleton.Node.GetBonePose(Skeleton.BoneIndex("Weapon")) * _weaponRest;
        }
        _drawn = drawn;
    }

    private void PublishWeaponActionParent()
    {
        if (_first || _actionClip is null || _weaponAttachment is null || _weaponRoot is null) return;
        var name = _actionClip.Sequence.Name;
        var unequipping = name.Equals("Unequip", StringComparison.OrdinalIgnoreCase);
        if (!unequipping && !name.Equals("Equip", StringComparison.OrdinalIgnoreCase)) return;
        var eventName = unequipping ? "Detach" : "Attach";
        var transition = _actionClip.TextKeys.Single(key => key.Value.Trim().Equals(eventName, StringComparison.OrdinalIgnoreCase));
        var time = SourceTime(_actionClip, _actionSeconds);
        var holstered = unequipping ? time >= transition.Time : time < transition.Time;
        if (holstered)
        {
            var parent = PrepareAction("unequip").TextKeys.Where(key => key.Value.Trim().StartsWith("prn:", StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key.Time).Last().Value.Trim()[4..].Trim();
            _ = Skeleton.BoneIndex(parent);
            _weaponAttachment.BoneName = parent;
            _weaponRoot.Transform = Skeleton.Node.GetBonePose(Skeleton.BoneIndex("Weapon")) * _weaponRest;
        }
        else
        {
            _weaponAttachment.BoneName = "Weapon";
            _weaponRoot.Transform = _weaponRest;
        }
    }
}
