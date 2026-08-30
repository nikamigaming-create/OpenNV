using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout3;

namespace OpenNV.Runtime.Campaigns.TTW;

internal static class TtwFo3Cg00Stage10ActorSetAdapter
{
    private const string ActorSetSchema =
        "opennv-ttw-fo3-cg00-stage10-actor-set/v1";
    private const string ActorSetStatus =
        "effective-ttw-actors-materialized-for-exact-live-stage10";

    internal static Fo3Vault101BirthPresentationContract Apply(
        Fo3Vault101BirthPresentationContract standaloneWorldHost,
        string actorSetPath,
        Fo3TtwCg00Stage10SurfaceContract surfaceContract)
    {
        var fullPath = Path.GetFullPath(actorSetPath);
        var actorSetBytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(actorSetBytes);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ActorSetSchema ||
            root.GetProperty("status").GetString() != ActorSetStatus ||
            root.GetProperty("campaign").GetString() != "Fallout3" ||
            root.GetProperty("edition").GetString() != "TTW" ||
            root.GetProperty("stage").GetInt32() != 10 ||
            root.GetProperty("standaloneActorArtifactsAccepted").GetBoolean() ||
            root.GetProperty("ownedPayloadsEmbedded").GetBoolean() ||
            root.GetProperty("retailSurfaceAuthority").GetProperty("sha256")
                .GetString() != surfaceContract.Sha256)
            throw new InvalidOperationException(
                "TTW stage-10 actor-set/source authority differs.");
        var actors = root.GetProperty("actors");
        var father = ReadActor(
            actors,
            "father",
            "fo3-vault101-dad-actor-v1",
            surfaceContract.Sha256,
            standaloneWorldHost.CellFormId);
        var doctor = ReadActor(
            actors,
            "doctor",
            "fo3-vault101-doctor-li-actor-v1",
            surfaceContract.Sha256,
            standaloneWorldHost.CellFormId);
        var mother = ReadActor(
            actors,
            "mother",
            "fo3-vault101-mom-actor-v1",
            surfaceContract.Sha256,
            standaloneWorldHost.CellFormId);
        return standaloneWorldHost with
        {
            TtwCg00Stage10ActorSetPath = fullPath,
            TtwCg00Stage10ActorSetSha256 = Hash(actorSetBytes),
            DadActor = standaloneWorldHost.DadActor with
            {
                ScenePath = father.ScenePath,
                SceneSha256 = father.SceneSha256,
                ReferenceFormId = father.ReferenceFormId,
                BaseFormId = father.BaseFormId,
                Name = father.Name,
                RaceFormId = father.RaceFormId,
                HairFormId = father.HairFormId,
                EyesFormId = father.EyesFormId,
                HeadPartFormIds = father.HeadPartFormIds,
                OutfitFormIds = father.OutfitFormIds,
                AuthoredPositionGameUnits = father.PositionGameUnits,
                AuthoredPositionGodotGameUnits = father.PositionGodotUnits,
                AuthoredRotationGodotQuaternion = father.RotationGodotQuaternion,
                Scale = father.Scale,
                IdleAnimationPath = father.IdleAnimationPath,
                Components = father.Components,
                Skins = father.Skins,
                Surfaces = father.Surfaces,
                Textures = father.Textures,
                FaceGenMorphTargets = father.FaceGenMorphTargets,
            },
            DoctorActor = standaloneWorldHost.DoctorActor with
            {
                ScenePath = doctor.ScenePath,
                SceneSha256 = doctor.SceneSha256,
                ReferenceFormId = doctor.ReferenceFormId,
                BaseFormId = doctor.BaseFormId,
                Name = doctor.Name,
                RaceFormId = doctor.RaceFormId,
                HairFormId = doctor.HairFormId,
                EyesFormId = doctor.EyesFormId,
                HeadPartFormIds = doctor.HeadPartFormIds,
                OutfitFormIds = doctor.OutfitFormIds,
                PositionGameUnits = doctor.PositionGameUnits,
                PositionGodotGameUnits = doctor.PositionGodotUnits,
                RotationGodotQuaternion = doctor.RotationGodotQuaternion,
                Scale = doctor.Scale,
                IdleAnimationPath = doctor.IdleAnimationPath,
                Components = doctor.Components,
                Skins = doctor.Skins,
                Surfaces = doctor.Surfaces,
                Textures = doctor.Textures,
                FaceGenMorphTargets = doctor.FaceGenMorphTargets,
            },
            MomActor = standaloneWorldHost.MomActor with
            {
                ScenePath = mother.ScenePath,
                SceneSha256 = mother.SceneSha256,
                ReferenceFormId = mother.ReferenceFormId,
                BaseFormId = mother.BaseFormId,
                Name = mother.Name,
                RaceFormId = mother.RaceFormId,
                HairFormId = mother.HairFormId,
                EyesFormId = mother.EyesFormId,
                HeadPartFormIds = mother.HeadPartFormIds,
                OutfitFormIds = mother.OutfitFormIds,
                AuthoredPositionGodotGameUnits = mother.PositionGodotUnits,
                AuthoredRotationGodotQuaternion = mother.RotationGodotQuaternion,
                Scale = mother.Scale,
                IdleAnimationPath = mother.IdleAnimationPath,
                Components = mother.Components,
                Skins = mother.Skins,
                Surfaces = mother.Surfaces,
                Textures = mother.Textures,
                FaceGenMorphTargets = mother.FaceGenMorphTargets,
            },
        };
    }

    private static Actor ReadActor(
        JsonElement actors,
        string role,
        string expectedRecipe,
        string expectedSurfaceAuthoritySha256,
        string expectedCellFormId)
    {
        var row = actors.GetProperty(role);
        var scenePath = Path.GetFullPath(row.GetProperty("actorScene").GetString()!);
        var expectedSha256 = row.GetProperty("actorSceneSha256").GetString()!;
        var bytes = File.ReadAllBytes(scenePath);
        if (Hash(bytes) != expectedSha256)
            throw new InvalidOperationException(
                $"TTW stage-10 {role} actor scene hash differs.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var coverage = root.GetProperty("coverage");
        var reference = root.GetProperty("reference");
        var actor = root.GetProperty("actor");
        var runtimeSurfaceProjection = root.GetProperty("runtimeSurfaceProjection");
        if (root.GetProperty("schema").GetString() != "opennv-actor-scene/v5" ||
            root.GetProperty("status").GetString() != "skinned-animated" ||
            root.GetProperty("recipe").GetString() != expectedRecipe ||
            root.GetProperty("cellFormId").GetString() != expectedCellFormId ||
            runtimeSurfaceProjection.ValueKind != JsonValueKind.Object ||
            runtimeSurfaceProjection.GetProperty("authoritySha256").GetString() !=
                expectedSurfaceAuthoritySha256)
            throw new InvalidOperationException(
                $"TTW stage-10 {role} actor scene identity differs.");
        return new Actor(
            scenePath,
            expectedSha256,
            reference.GetProperty("formId").GetString()!,
            reference.GetProperty("baseFormId").GetString()!,
            actor.GetProperty("name").GetString()!,
            actor.GetProperty("raceFormId").GetString()!,
            actor.GetProperty("hairFormId").GetString()!,
            actor.GetProperty("eyesFormId").GetString()!,
            ReadStrings(actor.GetProperty("headPartFormIds")),
            ReadStrings(actor.GetProperty("outfitFormIds")),
            ReadVector3(reference.GetProperty("positionGameUnits")),
            ReadVector3(reference.GetProperty("positionGodotUnits")),
            ReadQuaternion(reference.GetProperty("rotationGodotQuaternion")),
            reference.GetProperty("scale").GetSingle(),
            root.GetProperty("idleAnimation").GetString()!,
            coverage.GetProperty("components").GetInt32(),
            coverage.GetProperty("skins").GetInt32(),
            coverage.GetProperty("surfaces").GetInt32(),
            coverage.GetProperty("textures").GetInt32(),
            coverage.GetProperty("faceGenMorphTargets").GetInt32());
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyList<string> ReadStrings(JsonElement values) =>
        values.EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static Vector3 ReadVector3(JsonElement values)
    {
        var components = values.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (components.Length != 3)
            throw new InvalidOperationException(
                "TTW stage-10 actor vector must contain three components.");
        return new Vector3(components[0], components[1], components[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement values)
    {
        var components = values.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (components.Length != 4)
            throw new InvalidOperationException(
                "TTW stage-10 actor quaternion must contain four components.");
        var value = new Quaternion(
            components[0],
            components[1],
            components[2],
            components[3]);
        if (!value.IsNormalized())
            throw new InvalidOperationException(
                "TTW stage-10 actor quaternion must be normalized.");
        return value;
    }

    private sealed record Actor(
        string ScenePath,
        string SceneSha256,
        string ReferenceFormId,
        string BaseFormId,
        string Name,
        string RaceFormId,
        string HairFormId,
        string EyesFormId,
        IReadOnlyList<string> HeadPartFormIds,
        IReadOnlyList<string> OutfitFormIds,
        Vector3 PositionGameUnits,
        Vector3 PositionGodotUnits,
        Quaternion RotationGodotQuaternion,
        float Scale,
        string IdleAnimationPath,
        int Components,
        int Skins,
        int Surfaces,
        int Textures,
        int FaceGenMorphTargets);
}
