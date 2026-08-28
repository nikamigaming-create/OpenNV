using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1ThirdPersonWeaponNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point000001f = 0.000001f;
    internal const float PresentationFloat0Point999f = 0.999f;
    internal const float PresentationFloat1Point001f = 1.001f;
}

internal static class Fo1ThirdPersonWeapon
{
    private const string Schema = "opennv-fo1-third-person-held-weapon/v1";
    private const string VisibilityContract = "tactical-and-third-person-only";

    internal static LoadedWeapon Attach(
        JsonElement source,
        ActorModelSlice.LoadedActor actor)
    {
        if (RequiredString(source, "schema") != Schema ||
            RequiredString(source, "visibility") != VisibilityContract)
            throw new InvalidOperationException("Unexpected Fallout third-person weapon contract.");
        var unitsToMeters = source.GetProperty("unitsToMeters").GetSingle();
        if (unitsToMeters <= 0.0f ||
            MathF.Abs(actor.Root.Scale.X - unitsToMeters) > Fo1ThirdPersonWeaponNumericContracts.PresentationFloat0Point000001f ||
            !Mathf.IsEqualApprox(actor.Root.Scale.X, actor.Root.Scale.Y) ||
            !Mathf.IsEqualApprox(actor.Root.Scale.X, actor.Root.Scale.Z))
            throw new InvalidOperationException(
                "Fallout actor and held weapon do not share one authored Gamebryo unit frame.");

        var asset = source.GetProperty("asset");
        var loaded = VerifiedGltfLoader.Load(
            RequiredString(asset, "model"),
            RequiredString(asset, "sidecar"));
        if (!loaded.SourceSha256.Equals(
                RequiredString(asset, "sourceSha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout held-weapon source hash drifted.");
        var textures = RuntimeMaterialLoader.LoadTextures(source);
        var materialBindings = RuntimeMaterialLoader.Apply(loaded.Scene, asset, textures);
        var meshes = Descendants<MeshInstance3D>(loaded.Scene)
            .Where(mesh => mesh.Mesh is not null)
            .ToArray();
        var surfaces = meshes.Sum(mesh => mesh.Mesh?.GetSurfaceCount() ?? 0);
        var coverage = source.GetProperty("coverage");
        if (meshes.Length < 1 ||
            surfaces != coverage.GetProperty("surfaces").GetInt32() ||
            materialBindings != surfaces)
            throw new InvalidOperationException(
                $"Fallout held-weapon coverage drifted: meshes={meshes.Length} " +
                $"surfaces={surfaces} materials={materialBindings}.");

        var attachment = source.GetProperty("attachment");
        var role = RequiredString(source, "role");
        var boneName = RequiredString(attachment, "skeletonBone");
        var skeletons = Descendants<Skeleton3D>(actor.Root)
            .Where(skeleton => skeleton.FindBone(boneName) >= 0)
            .ToArray();
        if (skeletons.Length != 1)
        {
            var available = Descendants<Skeleton3D>(actor.Root)
                .SelectMany(skeleton => Enumerable.Range(0, skeleton.GetBoneCount())
                    .Select(skeleton.GetBoneName))
                .OrderBy(name => name.ToString(), StringComparer.Ordinal)
                .Select(name => name.ToString());
            throw new InvalidOperationException(
                $"Fallout held weapon requires exactly one actor bone named {boneName}; " +
                $"available={string.Join(',', available)}.");
        }
        var boneAttachment = new BoneAttachment3D
        {
            Name = NodeName(role) + "BoneAttachment",
            BoneName = boneName,
        };
        skeletons[0].AddChild(boneAttachment);
        var grip = new Node3D
        {
            Name = NodeName(role) + "Grip",
            Position = ReadVector(attachment.GetProperty("positionGodotUnits")),
            Quaternion = ReadQuaternion(attachment.GetProperty("rotationQuaternion")),
            Scale = ReadVector(attachment.GetProperty("scale")),
        };
        boneAttachment.AddChild(grip);
        loaded.Scene.Name = NodeName(role);
        grip.AddChild(loaded.Scene);

        var muzzleMarker = OptionalString(attachment, "muzzleMarker");
        var shellMarker = OptionalString(attachment, "shellMarker");

        return new LoadedWeapon(
            loaded.Scene,
            role,
            RequiredString(source, "weaponFormId"),
            RequiredString(source, "weaponEditorId"),
            OptionalString(source, "gameplayPid"),
            loaded.SourceSha256,
            boneName,
            muzzleMarker,
            muzzleMarker is null
                ? Vector3.Zero
                : ReadVector(attachment.GetProperty("muzzlePositionGodotUnits")),
            shellMarker,
            shellMarker is null
                ? Vector3.Zero
                : ReadVector(attachment.GetProperty("shellPositionGodotUnits")),
            meshes.Length,
            surfaces,
            materialBindings,
            coverage.GetProperty("materialTextures").GetInt32());
    }

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fallout held weapon requires {name}.");
        return value;
    }

    private static string? OptionalString(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var property))
            return null;
        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string NodeName(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout held weapon requires a finite 3-vector.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout held weapon requires a finite quaternion.");
        var result = new Quaternion(values[0], values[1], values[2], values[3]);
        if (result.LengthSquared() < Fo1ThirdPersonWeaponNumericContracts.PresentationFloat0Point999f || result.LengthSquared() > Fo1ThirdPersonWeaponNumericContracts.PresentationFloat1Point001f)
            throw new InvalidOperationException("Fallout held-weapon quaternion is not normalized.");
        return result;
    }

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

    internal readonly record struct LoadedWeapon(
        Node3D Root,
        string Role,
        string FormId,
        string EditorId,
        string? GameplayPid,
        string SourceSha256,
        string BoneName,
        string? MuzzleMarker,
        Vector3 MuzzlePositionGodotUnits,
        string? ShellMarker,
        Vector3 ShellPositionGodotUnits,
        int Meshes,
        int Surfaces,
        int MaterialBindings,
        int MaterialTextures);
}
