using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private BoneAttachment3D? _weaponAttachment;
    private NativeActorWeaponAttachment? _equippedObject;
    private Node3D? _weaponRoot;
    private Transform3D _weaponRest;
    private RuntimeNativeNifAnimation? _actionClip;
    private string? _actionGroup;
    private int? _actionVariant;
    private string? _actionPath, _actionHash;
    private double _actionSeconds;
    private bool _drawn = true;

    internal object ActionSelection => new
    {
        path = _actionPath,
        sha256 = _actionHash,
        seconds = _actionSeconds,
        sequence = _actionClip?.Sequence.Name,
        variant = _actionVariant,
        boundary = "source-KF;shared-action-selection;retail-global-RNG-phase-and-view-mapping-unmatched"
    };

    internal IReadOnlyList<string> ActionVariants(string group)
    {
        if (Weapon is null) throw new InvalidOperationException("Weapon action has no equipped weapon.");
        var baseName = Weapon.AnimationGroup + group;
        if (group.StartsWith("attack", StringComparison.Ordinal))
        {
            var candidates = _content.ActorAnimations.Variants(_directory, baseName);
            if (candidates.Count != 0) return candidates;
            var opposite = group.Contains("attackleft", StringComparison.Ordinal)
                ? group.Replace("attackleft", "attackright", StringComparison.Ordinal)
                : group.Contains("attackright", StringComparison.Ordinal)
                    ? group.Replace("attackright", "attackleft", StringComparison.Ordinal)
                    : null;
            if (opposite is not null)
            {
                var oppositeBase = Weapon.AnimationGroup + opposite;
                candidates = _content.ActorAnimations.Variants(_directory, oppositeBase);
                if (candidates.Count != 0) return candidates;
            }
        }
        return [_content.ActorAnimations.Require(_directory, baseName)];
    }

    internal RuntimeNativeNifAnimation PrepareAction(string group, int? variant = null)
    {
        var candidates = ActionVariants(group);
        if (variant is null && candidates.Count != 1)
            throw new NotSupportedException($"Player attack {group} requires an explicit variant selection owner.");
        var index = variant ?? 0;
        if (index < 0 || index >= candidates.Count) throw new InvalidDataException("Player attack variant is outside its source catalog.");
        return ClipPath(candidates[index]);
    }

    internal void SetAction(string? group, double seconds, int? variant = null)
    {
        if (group is null)
        {
            if (_actionClip is not null)
            {
                _actionClip = null;
                _actionGroup = null;
                _actionVariant = null; _actionPath = null; _actionHash = null;
            }
        }
        else
        {
            if (_actionGroup != group || _actionVariant != variant || _actionClip is null)
            {
                _actionClip = PrepareAction(group, variant);
                _actionPath = ActionVariants(group)[variant ?? 0];
                _actionHash = Convert.ToHexString(SHA256.HashData(Read(_actionPath)));
                _actionGroup = group;
                _actionVariant = variant;
            }
        }
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
