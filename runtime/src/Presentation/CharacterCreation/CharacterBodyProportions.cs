using Godot;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

/// <summary>
/// Immutable body definition shared by character creation, saved appearance
/// state, and the visible gameplay actor. Values are radial scale factors except
/// Height, which is the actor-root vertical scale.
/// </summary>
internal sealed record CharacterBodyProportions(
    string Id,
    float Height,
    float Chest,
    float Shoulders,
    float Waist,
    float Arms,
    float Thighs,
    float Calves)
{
    internal const float Minimum = 0.70f;
    internal const float Maximum = 1.35f;
    internal const float Step = 0.01f;
    internal const float Jump = 0.05f;

    internal static CharacterBodyProportions Neutral(string id) => new(
        id,
        1.0f,
        1.0f,
        1.0f,
        1.0f,
        1.0f,
        1.0f,
        1.0f);

    internal void Validate(string authority)
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(authority) ||
            Values.Any(value => !float.IsFinite(value) || value is < Minimum or > Maximum))
            throw new InvalidOperationException(
                $"Character body proportions are outside the admitted range: {authority}.");
    }

    internal CharacterBodyProportions With(string role, float value)
    {
        var admitted = Math.Clamp(value, Minimum, Maximum);
        return role switch
        {
            "height" => this with { Height = admitted },
            "chest" => this with { Chest = admitted },
            "shoulders" => this with { Shoulders = admitted },
            "waist" => this with { Waist = admitted },
            "arms" => this with { Arms = admitted },
            "thighs" => this with { Thighs = admitted },
            "calves" => this with { Calves = admitted },
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    internal float Value(string role) => role switch
    {
        "height" => Height,
        "chest" => Chest,
        "shoulders" => Shoulders,
        "waist" => Waist,
        "arms" => Arms,
        "thighs" => Thighs,
        "calves" => Calves,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    internal IReadOnlyList<float> Values =>
        [Height, Chest, Shoulders, Waist, Arms, Thighs, Calves];
}

internal static class CharacterBodyRig
{
    internal static void Apply(
        Node3D actorRoot,
        Skeleton3D skeleton,
        CharacterBodyProportions proportions,
        Node metadataOwner,
        string authority)
    {
        proportions.Validate(authority);
        actorRoot.Scale = new Vector3(1.0f, proportions.Height, 1.0f);
        // Bone scales are local and inherit through the Bip01 hierarchy. Treat
        // creator values as absolute silhouette targets, then express each one
        // as a ratio to its parent target. Applying the same chest multiplier
        // to Spine1 and Spine2 used to compound at the neck and create a hard
        // collar step. The upper chest now eases halfway back to the neutral
        // head width before Neck cancels the remaining inherited scale.
        var upperChest = MathF.Sqrt(proportions.Chest);
        ScaleBone(skeleton, "Bip01 Pelvis", proportions.Waist, authority);
        ScaleBone(
            skeleton,
            "Bip01 Spine1",
            Ratio(proportions.Chest, proportions.Waist),
            authority);
        ScaleBone(
            skeleton,
            "Bip01 Spine2",
            Ratio(upperChest, proportions.Chest),
            authority);
        ScaleBone(
            skeleton,
            "Bip01 Neck",
            Ratio(1.0f, upperChest),
            authority);
        ScaleBones(
            skeleton,
            ["Bip01 L Clavicle", "Bip01 R Clavicle"],
            Ratio(proportions.Shoulders, upperChest),
            authority);
        ScaleBones(
            skeleton,
            ["Bip01 L UpperArm", "Bip01 R UpperArm"],
            Ratio(proportions.Arms, proportions.Shoulders),
            authority);
        ScaleBones(
            skeleton,
            ["Bip01 L Forearm", "Bip01 R Forearm"],
            1.0f,
            authority);
        ScaleBones(
            skeleton,
            ["Bip01 L Thigh", "Bip01 R Thigh"],
            Ratio(proportions.Thighs, proportions.Waist),
            authority);
        ScaleBones(
            skeleton,
            ["Bip01 L Calf", "Bip01 R Calf"],
            Ratio(proportions.Calves, proportions.Thighs),
            authority);
        metadataOwner.SetMeta("body_proportion_profile", proportions.Id);
        metadataOwner.SetMeta("body_height_scale", proportions.Height);
        metadataOwner.SetMeta("body_chest_scale", proportions.Chest);
        metadataOwner.SetMeta("body_shoulder_scale", proportions.Shoulders);
        metadataOwner.SetMeta("body_waist_scale", proportions.Waist);
        metadataOwner.SetMeta("body_arm_scale", proportions.Arms);
        metadataOwner.SetMeta("body_thigh_scale", proportions.Thighs);
        metadataOwner.SetMeta("body_calf_scale", proportions.Calves);
        metadataOwner.SetMeta("body_upper_chest_taper_scale", upperChest);
        metadataOwner.SetMeta("body_head_scale", 1.0f);
    }

    internal static Skeleton3D ResolveSkeleton(Node root, string authority)
    {
        var matches = Descendants<Skeleton3D>(root)
            .Where(value => value.FindBone("Bip01 Pelvis") >= 0)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Character body rig requires one authored Bip01 skeleton: {authority}; " +
                $"found {matches.Length}.");
        return matches[0];
    }

    private static void ScaleBones(
        Skeleton3D skeleton,
        IReadOnlyList<string> names,
        float radialScale,
        string authority)
    {
        foreach (var name in names)
        {
            var index = skeleton.FindBone(name);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Character body rig bone is absent: {authority}/{name}.");
            skeleton.SetBonePoseScale(index, new Vector3(radialScale, 1.0f, radialScale));
        }
    }

    private static void ScaleBone(
        Skeleton3D skeleton,
        string name,
        float radialScale,
        string authority) =>
        ScaleBones(skeleton, [name], radialScale, authority);

    private static float Ratio(float absoluteScale, float parentScale) =>
        absoluteScale / MathF.Max(parentScale, 0.0001f);

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
