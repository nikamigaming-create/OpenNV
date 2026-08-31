using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout3;

namespace OpenNV.Runtime.Campaigns.TTW;

internal sealed record TtwFo3Cg00Stage10MaterializedParticipantIdentity(
    string Role,
    string FormKey,
    string RuntimeFormId,
    string PackageFormKey,
    string PackageRuntimeFormId,
    string IdleFormKey,
    string IdleRuntimeFormId,
    string SequenceName,
    string AnimationLogicalPath,
    string AnimationSourceSha256,
    string ActorScenePath,
    string ActorSceneSha256);

internal sealed record TtwFo3Cg00Stage10MaterializedSceneIdentity(
    string SourceAuthority,
    string SourceProfilePath,
    string SourceProfileSha256,
    string SourceNamespacePath,
    string SourceNamespaceSha256,
    string OpeningProfilePath,
    string OpeningProfileSha256,
    string ProjectionPath,
    string ProjectionSha256,
    string PluginStackId,
    string SaveCompatibilityId,
    string CellFormKey,
    string CellRuntimeFormId,
    Vector3 CellOriginGameUnits,
    string BirthPresentationManifestPath,
    string BirthPresentationManifestSha256,
    bool EffectiveMemberClosureMaterialized,
    bool StandaloneFallout3ArtifactsAccepted,
    bool StandaloneNewVegasArtifactsAccepted,
    IReadOnlyDictionary<string, TtwFo3Cg00Stage10MaterializedParticipantIdentity>
        Participants);

internal sealed record TtwFo3Cg00Stage10ParticipantPlan(
    string Role,
    string FormKey,
    string RuntimeFormId,
    string PackageFormKey,
    string PackageRuntimeFormId,
    string IdleFormKey,
    string IdleRuntimeFormId,
    string SequenceName,
    float SequenceBeginTimeSeconds,
    float SequenceEndTimeSeconds,
    int SequenceCycleType,
    float SequencePhaseSeconds,
    Transform3D LocalRenderedTransform);

internal sealed record TtwFo3Cg00Stage10WorldPlan(
    string ContractPath,
    string ContractSha256,
    string ProjectionPath,
    string ProjectionSha256,
    string PluginStackId,
    string SaveCompatibilityId,
    Transform3D LocalCameraTransform,
    Transform3D LocalCamera1stTransform,
    float NearGameUnits,
    float FarGameUnits,
    float HorizontalFovDegrees,
    float VerticalFovDegrees,
    IReadOnlyDictionary<string, TtwFo3Cg00Stage10ParticipantPlan> Participants,
    bool InteractiveLaunchReady,
    string InteractiveLaunchBlocker);

internal sealed record TtwFo3Cg00Stage10RuntimeParticipant(
    Node3D RenderedRoot,
    ActorModelSlice.LoadedAnimation Animation);

internal sealed record TtwFo3Cg00Stage10RuntimeScene(
    Fo3Vault101BirthSceneCoverage Coverage,
    Node3D Camera1stRoot,
    TtwFo3Cg00Stage10MaterializedSceneIdentity Identity,
    IReadOnlyDictionary<string, TtwFo3Cg00Stage10RuntimeParticipant> Participants);

internal sealed record TtwFo3Cg00Stage10WorldApplyResult(
    string ContractSha256,
    string ProjectionSha256,
    IReadOnlyDictionary<string, double> PublishedPhasesSeconds,
    bool InteractiveLaunchReady,
    string InteractiveLaunchBlocker);

internal static class TtwFo3Cg00Stage10WorldAdapter
{
    internal const string MaterializedSourceAuthority =
        "owned-ttw-effective-record-and-member-closure-no-standalone-artifacts";
    internal const string InteractiveLaunchBlocker =
        "ttw-world-transition-save-and-post-stage10-command-runtime-not-proven";
    internal const string RootMetadataProjectionSha256 =
        "opennv_ttw_projection_sha256";
    internal const string RootMetadataPluginStackId =
        "opennv_ttw_plugin_stack_id";
    internal const string RootMetadataSaveCompatibilityId =
        "opennv_ttw_save_compatibility_id";
    internal const string RootMetadataMemberClosureMaterialized =
        "opennv_ttw_effective_member_closure_materialized";
    internal const string ParticipantMetadataFormKey =
        "opennv_ttw_reference_form_key";
    internal const string ParticipantMetadataRuntimeFormId =
        "opennv_ttw_runtime_form_id";
    internal const string ParticipantMetadataActorSceneSha256 =
        "opennv_ttw_actor_scene_sha256";

    private const string ExpectedCellFormKey = "Fallout3.esm:028138";
    private const float RuntimePhaseToleranceSeconds = 1.0e-4f;
    private const float ProjectionTolerance = 1.0e-4f;
    private const float FovDiameterToRadius = 0.5f;
    private const int RotationValueCount = 9;
    private const int ViewportLeftIndex = 0;
    private const int ViewportRightIndex = 1;
    private const int ViewportTopIndex = 2;
    private const int ViewportBottomIndex = 3;

    private static readonly IReadOnlyDictionary<string, string> ExpectedNpcFormKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["father"] = "Fallout3.esm:0290a7",
            ["doctor"] = "Fallout3.esm:0290a5",
            ["mother"] = "Fallout3.esm:05ede0",
        };

    internal static TtwFo3Cg00Stage10WorldPlan LoadProductionPlan(
        string contractPath,
        string projectionPath,
        TtwFo3Cg00Stage10MaterializedSceneIdentity sceneIdentity)
    {
        var contract = TtwFo3Cg00RetailStage10Contract.Load(contractPath);
        var opening = TtwFo3OpeningContract.Load(contract.OpeningProfilePath);
        var projection = TtwFo3ProfileProjectionContract.Load(projectionPath);
        ValidateOwnedInputs(contract, opening, projection);
        if (!opening.References.TryGetValue(ExpectedCellFormKey, out var cell) ||
            cell.RuntimeFormId != sceneIdentity.CellRuntimeFormId)
            throw new InvalidOperationException(
                "TTW stage-10 materialized Vault101d runtime FormID differs.");
        return BuildPlan(
            contract,
            projection.Path,
            projection.Sha256,
            sceneIdentity,
            verifyMaterializedArtifacts: true);
    }

    internal static TtwFo3Cg00Stage10WorldPlan BuildSyntheticPlanForTests(
        TtwFo3Cg00RetailStage10Contract contract,
        string projectionPath,
        string projectionSha256,
        TtwFo3Cg00Stage10MaterializedSceneIdentity sceneIdentity) =>
        BuildPlan(
            contract,
            projectionPath,
            projectionSha256,
            sceneIdentity,
            verifyMaterializedArtifacts: false);

    internal static TtwFo3Cg00Stage10WorldApplyResult Apply(
        TtwFo3Cg00Stage10WorldPlan plan,
        TtwFo3Cg00Stage10RuntimeScene scene)
    {
        ValidateRuntimeScene(plan, scene);
        var viewport = scene.Coverage.Camera.GetViewport().GetVisibleRect().Size;
        var sourceAspect = plan.HorizontalFovDegrees == 0.0f
            ? float.NaN
            : MathF.Tan(
                    Mathf.DegToRad(plan.HorizontalFovDegrees) * FovDiameterToRadius) /
                MathF.Tan(
                    Mathf.DegToRad(plan.VerticalFovDegrees) * FovDiameterToRadius);
        var runtimeAspect = viewport.Y > 0.0f ? viewport.X / viewport.Y : float.NaN;
        if (!float.IsFinite(runtimeAspect) ||
            MathF.Abs(sourceAspect - runtimeAspect) > ProjectionTolerance)
            throw new InvalidOperationException(
                "TTW stage-10 runtime viewport aspect differs from the observed frustum.");

        var cellWorld = scene.Coverage.CellRoot.GlobalTransform;
        var cellWorldScale = cellWorld.Basis.Scale;
        if (!cellWorldScale.IsFinite() || cellWorldScale.X <= 0.0f ||
            !cellWorldScale.IsEqualApprox(Vector3.One * cellWorldScale.X))
            throw new InvalidOperationException(
                "TTW stage-10 world publication is not uniformly scaled.");
        var cameraWorld = cellWorld * plan.LocalCameraTransform;
        var cameraBasis = cameraWorld.Basis.Orthonormalized();
        if (!cameraBasis.IsFinite() || cameraBasis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                "TTW stage-10 observed camera basis is invalid after scene composition.");

        var runtimeRows = plan.Participants.Select(pair =>
        {
            var binding = scene.Participants[pair.Key];
            var expected = pair.Value;
            ValidateAnimation(binding.Animation, expected);
            return (Plan: expected, Runtime: binding);
        }).ToArray();

        scene.Coverage.Camera.GlobalTransform = new Transform3D(
            cameraBasis,
            cameraWorld.Origin);
        scene.Coverage.Camera.Fov = plan.VerticalFovDegrees;
        scene.Coverage.Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        var unitsToMeters = cellWorldScale.X;
        scene.Coverage.Camera.Near = plan.NearGameUnits * unitsToMeters;
        scene.Coverage.Camera.Far = plan.FarGameUnits * unitsToMeters;
        scene.Coverage.Camera.Current = true;
        scene.Camera1stRoot.Transform = plan.LocalCamera1stTransform;

        var phases = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var player in scene.Participants.Values
                     .Select(value => value.Animation.Player)
                     .Distinct())
            player.Stop();
        foreach (var row in runtimeRows)
        {
            row.Runtime.RenderedRoot.Transform = row.Plan.LocalRenderedTransform;
            row.Runtime.RenderedRoot.Visible = true;
            row.Runtime.Animation.Player.Play(row.Runtime.Animation.RuntimeName);
            row.Runtime.Animation.Player.Seek(row.Plan.SequencePhaseSeconds, update: true);
            var published = row.Runtime.Animation.Player.CurrentAnimationPosition;
            if (row.Runtime.Animation.Player.CurrentAnimation.ToString() !=
                    row.Runtime.Animation.RuntimeName ||
                Math.Abs(published - row.Plan.SequencePhaseSeconds) >
                    RuntimePhaseToleranceSeconds)
                throw new InvalidOperationException(
                    $"TTW stage-10 {row.Plan.Role} controller phase did not publish exactly.");
            phases.Add(row.Plan.Role, published);
        }

        scene.Coverage.Camera.SetMeta(
            "opennv_ttw_stage10_contract_sha256",
            plan.ContractSha256);
        scene.Coverage.Camera.SetMeta("opennv_ttw_runtime_ready", false);
        scene.Coverage.Camera.SetMeta(
            "opennv_ttw_runtime_blocker",
            plan.InteractiveLaunchBlocker);
        return new TtwFo3Cg00Stage10WorldApplyResult(
            plan.ContractSha256,
            plan.ProjectionSha256,
            phases,
            InteractiveLaunchReady: false,
            plan.InteractiveLaunchBlocker);
    }

    private static TtwFo3Cg00Stage10WorldPlan BuildPlan(
        TtwFo3Cg00RetailStage10Contract contract,
        string projectionPath,
        string projectionSha256,
        TtwFo3Cg00Stage10MaterializedSceneIdentity scene,
        bool verifyMaterializedArtifacts)
    {
        ValidateSceneIdentity(
            contract,
            projectionPath,
            projectionSha256,
            scene,
            verifyMaterializedArtifacts);
        ValidateProjection(contract.ActiveCamera);
        var camera = new Transform3D(
            GamebryoCoordinate.ConvertCameraBasis(
                Rotation(contract.ActiveCamera.WorldTransform),
                "TTW CG00 stage10 active NiCamera"),
            LocalTranslation(
                contract.ActiveCamera.WorldTransform,
                scene.CellOriginGameUnits));
        var camera1st = LocalTransform(
            contract.Camera1stWorldTransform,
            scene.CellOriginGameUnits,
            "TTW CG00 stage10 Camera1st");
        var participants = contract.Participants.ToDictionary(
            pair => pair.Key,
            pair => new TtwFo3Cg00Stage10ParticipantPlan(
                pair.Key,
                pair.Value.FormKey,
                pair.Value.RuntimeFormId,
                pair.Value.PackageFormKey,
                pair.Value.PackageRuntimeFormId,
                pair.Value.IdleFormKey,
                pair.Value.IdleRuntimeFormId,
                pair.Value.SequenceName,
                Float(pair.Value.SequenceBeginTimeSeconds, "sequence begin"),
                Float(pair.Value.SequenceEndTimeSeconds, "sequence end"),
                pair.Value.SequenceCycleType,
                Float(pair.Value.LastScaledTimeSeconds, "sequence phase"),
                LocalTransform(
                    pair.Value.RenderedWorldTransform,
                    scene.CellOriginGameUnits,
                    $"TTW CG00 stage10 {pair.Key} rendered root")),
            StringComparer.Ordinal);
        return new TtwFo3Cg00Stage10WorldPlan(
            contract.ContractPath,
            contract.ContractSha256,
            Path.GetFullPath(projectionPath),
            projectionSha256,
            contract.PluginStackId,
            contract.SaveCompatibilityId,
            camera,
            camera1st,
            Float(contract.ActiveCamera.NearGameUnits, "camera near"),
            Float(contract.ActiveCamera.FarGameUnits, "camera far"),
            Float(contract.ActiveCamera.HorizontalFovDegrees, "horizontal FOV"),
            Float(contract.ActiveCamera.VerticalFovDegrees, "vertical FOV"),
            participants,
            InteractiveLaunchReady: false,
            InteractiveLaunchBlocker);
    }

    private static void ValidateOwnedInputs(
        TtwFo3Cg00RetailStage10Contract contract,
        TtwFo3OpeningContract opening,
        TtwFo3ProfileProjectionContract projection)
    {
        if (!Path.GetFullPath(opening.Path).Equals(
                Path.GetFullPath(contract.OpeningProfilePath),
                StringComparison.OrdinalIgnoreCase) ||
            opening.Sha256 != contract.OpeningProfileSha256 ||
            opening.SourceProfileSha256 != contract.SourceProfileSha256 ||
            opening.SourceNamespaceSha256 != contract.SourceNamespaceSha256 ||
            opening.PluginStackId != contract.PluginStackId ||
            opening.SaveCompatibilityId != contract.SaveCompatibilityId)
            throw new InvalidOperationException(
                "TTW stage-10 observation/opening identity differs.");
        projection.ValidateSourceBinding(
            contract.SourceProfilePath,
            contract.SourceProfileSha256,
            contract.SourceNamespacePath,
            contract.SourceNamespaceSha256,
            contract.PluginStackId,
            contract.SaveCompatibilityId,
            opening.CacheCompatibilityId);
        foreach (var pair in ExpectedNpcFormKeys)
        {
            if (!opening.References.TryGetValue(pair.Value, out var source) ||
                source.RuntimeFormId != contract.Participants[pair.Key].RuntimeFormId)
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} opening/effective reference join differs.");
        }
        if (!opening.References.TryGetValue(ExpectedCellFormKey, out _))
            throw new InvalidOperationException(
                "TTW stage-10 Vault101d opening/effective reference join differs.");
    }

    private static void ValidateSceneIdentity(
        TtwFo3Cg00RetailStage10Contract contract,
        string projectionPath,
        string projectionSha256,
        TtwFo3Cg00Stage10MaterializedSceneIdentity scene,
        bool verifyArtifacts)
    {
        if (scene.SourceAuthority != MaterializedSourceAuthority ||
            !scene.EffectiveMemberClosureMaterialized ||
            scene.StandaloneFallout3ArtifactsAccepted ||
            scene.StandaloneNewVegasArtifactsAccepted ||
            !SamePath(scene.SourceProfilePath, contract.SourceProfilePath) ||
            scene.SourceProfileSha256 != contract.SourceProfileSha256 ||
            !SamePath(scene.SourceNamespacePath, contract.SourceNamespacePath) ||
            scene.SourceNamespaceSha256 != contract.SourceNamespaceSha256 ||
            !SamePath(scene.OpeningProfilePath, contract.OpeningProfilePath) ||
            scene.OpeningProfileSha256 != contract.OpeningProfileSha256 ||
            !SamePath(scene.ProjectionPath, projectionPath) ||
            scene.ProjectionSha256 != projectionSha256 ||
            scene.PluginStackId != contract.PluginStackId ||
            scene.SaveCompatibilityId != contract.SaveCompatibilityId ||
            scene.CellFormKey != ExpectedCellFormKey ||
            !IsFormId(scene.CellRuntimeFormId) ||
            !scene.CellOriginGameUnits.IsFinite() ||
            !scene.Participants.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(contract.Participants.Keys))
            throw new InvalidOperationException(
                "TTW stage-10 materialized scene identity differs or admits standalone artifacts.");
        foreach (var pair in contract.Participants)
        {
            var observed = pair.Value;
            var materialized = scene.Participants[pair.Key];
            if ((ExpectedNpcFormKeys.TryGetValue(pair.Key, out var expectedFormKey) &&
                    observed.FormKey != expectedFormKey) ||
                materialized.Role != pair.Key ||
                materialized.FormKey != observed.FormKey ||
                materialized.RuntimeFormId != observed.RuntimeFormId ||
                materialized.PackageFormKey != observed.PackageFormKey ||
                materialized.PackageRuntimeFormId != observed.PackageRuntimeFormId ||
                materialized.IdleFormKey != observed.IdleFormKey ||
                materialized.IdleRuntimeFormId != observed.IdleRuntimeFormId ||
                materialized.SequenceName != observed.SequenceName)
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} materialized identity differs.");
            if (verifyArtifacts)
            {
                VerifyFile(
                    materialized.ActorScenePath,
                    materialized.ActorSceneSha256,
                    $"{pair.Key} actor scene");
            }
        }
        if (verifyArtifacts)
            VerifyFile(
                scene.BirthPresentationManifestPath,
                scene.BirthPresentationManifestSha256,
                "Vault 101 birth presentation");
    }

    private static void ValidateRuntimeScene(
        TtwFo3Cg00Stage10WorldPlan plan,
        TtwFo3Cg00Stage10RuntimeScene scene)
    {
        var identity = scene.Identity;
        if (identity.SourceAuthority != MaterializedSourceAuthority ||
            !identity.EffectiveMemberClosureMaterialized ||
            identity.StandaloneFallout3ArtifactsAccepted ||
            identity.StandaloneNewVegasArtifactsAccepted ||
            identity.ProjectionSha256 != plan.ProjectionSha256 ||
            identity.PluginStackId != plan.PluginStackId ||
            identity.SaveCompatibilityId != plan.SaveCompatibilityId ||
            !SamePath(
                identity.BirthPresentationManifestPath,
                scene.Coverage.Contract.ManifestPath) ||
            identity.BirthPresentationManifestSha256 !=
                scene.Coverage.Contract.ManifestSha256 ||
            !scene.Participants.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(plan.Participants.Keys) ||
            scene.Participants.Values.Select(value => value.RenderedRoot)
                .Distinct().Count() != plan.Participants.Count ||
            scene.Participants.Values.Select(value => value.Animation.Player)
                .Distinct().Count() != plan.Participants.Count ||
            scene.Participants.Values.Any(value =>
                ReferenceEquals(value.RenderedRoot, scene.Camera1stRoot)) ||
            !scene.Coverage.CellRoot.IsInsideTree() ||
            !scene.Coverage.Camera.IsInsideTree() ||
            !scene.Camera1stRoot.IsInsideTree() ||
            !SceneRootMetadataMatches(scene.Coverage.CellRoot, identity))
            throw new InvalidOperationException(
                "TTW stage-10 runtime scene is not the materialized TTW world.");
        var expectedNpcRoots = new Dictionary<string, Node3D>(StringComparer.Ordinal)
        {
            ["father"] = scene.Coverage.DadActor.Placement,
            ["doctor"] = scene.Coverage.DoctorActor.Placement,
            ["mother"] = scene.Coverage.MomActor.Placement,
        };
        foreach (var pair in plan.Participants)
        {
            var runtime = scene.Participants[pair.Key];
            var source = identity.Participants[pair.Key];
            if (source.Role != pair.Key ||
                source.FormKey != pair.Value.FormKey ||
                source.RuntimeFormId != pair.Value.RuntimeFormId ||
                source.PackageFormKey != pair.Value.PackageFormKey ||
                source.PackageRuntimeFormId != pair.Value.PackageRuntimeFormId ||
                source.IdleFormKey != pair.Value.IdleFormKey ||
                source.IdleRuntimeFormId != pair.Value.IdleRuntimeFormId ||
                source.SequenceName != pair.Value.SequenceName)
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} runtime identity differs from the plan.");
            if (expectedNpcRoots.TryGetValue(pair.Key, out var expectedRoot) &&
                !ReferenceEquals(expectedRoot, runtime.RenderedRoot))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} runtime root differs from the birth scene actor.");
            if (!MetadataEquals(
                    runtime.RenderedRoot,
                    ParticipantMetadataFormKey,
                    source.FormKey) ||
                !MetadataEquals(
                    runtime.RenderedRoot,
                    ParticipantMetadataRuntimeFormId,
                    source.RuntimeFormId) ||
                !MetadataEquals(
                    runtime.RenderedRoot,
                    ParticipantMetadataActorSceneSha256,
                    source.ActorSceneSha256) ||
                !ActorModelSlice.NormalizeAnimationPath(runtime.Animation.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(source.AnimationLogicalPath),
                    StringComparison.OrdinalIgnoreCase) ||
                !runtime.Animation.SourceSha256.Equals(
                    source.AnimationSourceSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} runtime provenance is absent.");
        }

        var npcArtifacts = new Dictionary<string, (string Path, string Sha256)>(
            StringComparer.Ordinal)
        {
            ["father"] = (
                scene.Coverage.Contract.DadActor.ScenePath,
                scene.Coverage.Contract.DadActor.SceneSha256),
            ["doctor"] = (
                scene.Coverage.Contract.DoctorActor.ScenePath,
                scene.Coverage.Contract.DoctorActor.SceneSha256),
            ["mother"] = (
                scene.Coverage.Contract.MomActor.ScenePath,
                scene.Coverage.Contract.MomActor.SceneSha256),
        };
        foreach (var pair in npcArtifacts)
        {
            var expected = identity.Participants[pair.Key];
            if (!SamePath(expected.ActorScenePath, pair.Value.Path) ||
                !expected.ActorSceneSha256.Equals(
                    pair.Value.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} actor artifact differs from the birth scene.");
        }
        if (!MetadataEquals(
                scene.Camera1stRoot,
                ParticipantMetadataFormKey,
                plan.Participants["player"].FormKey) ||
            !MetadataEquals(
                scene.Camera1stRoot,
                RootMetadataProjectionSha256,
                identity.ProjectionSha256) ||
            !scene.Coverage.CellRoot.Scale.IsEqualApprox(
                Vector3.One * scene.Coverage.CellRoot.Scale.X) ||
            scene.Coverage.CellRoot.Scale.X <= 0.0f)
            throw new InvalidOperationException(
                "TTW stage-10 Camera1st or world-unit publication differs.");
    }

    private static void ValidateAnimation(
        ActorModelSlice.LoadedAnimation animation,
        TtwFo3Cg00Stage10ParticipantPlan expected)
    {
        if (animation.SequenceName != expected.SequenceName ||
            MathF.Abs(animation.StartSeconds - expected.SequenceBeginTimeSeconds) >
                RuntimePhaseToleranceSeconds ||
            MathF.Abs(animation.StopSeconds - expected.SequenceEndTimeSeconds) >
                RuntimePhaseToleranceSeconds ||
            animation.CycleType != expected.SequenceCycleType)
            throw new InvalidOperationException(
                $"TTW stage-10 {expected.Role} materialized IDLE interval differs.");
    }

    private static void ValidateProjection(TtwFo3Cg00Stage10Camera camera)
    {
        if (Math.Abs(camera.WorldTransform.Scale - 1.0) > ProjectionTolerance ||
            Math.Abs(camera.LeftGameUnits + camera.RightGameUnits) > ProjectionTolerance ||
            Math.Abs(camera.TopGameUnits + camera.BottomGameUnits) > ProjectionTolerance ||
            camera.ViewportNormalized.Count != 4 ||
            Math.Abs(camera.ViewportNormalized[ViewportLeftIndex]) > ProjectionTolerance ||
            Math.Abs(camera.ViewportNormalized[ViewportRightIndex] - 1.0) >
                ProjectionTolerance ||
            Math.Abs(camera.ViewportNormalized[ViewportTopIndex] - 1.0) >
                ProjectionTolerance ||
            Math.Abs(camera.ViewportNormalized[ViewportBottomIndex]) > ProjectionTolerance)
            throw new InvalidOperationException(
                "TTW stage-10 observed projection is not exactly publishable by Camera3D.");
    }

    private static Transform3D LocalTransform(
        TtwFo3Cg00Stage10Transform source,
        Vector3 origin,
        string label) => new(
            GamebryoCoordinate.ConvertBasis(
                Rotation(source),
                Float(source.Scale, $"{label} scale"),
                label),
            LocalTranslation(source, origin));

    private static Vector3 LocalTranslation(
        TtwFo3Cg00Stage10Transform source,
        Vector3 origin) =>
        GamebryoCoordinate.ConvertVector(new Vector3(
            Float(source.TranslationGameUnits[0], "translation X"),
            Float(source.TranslationGameUnits[1], "translation Y"),
            Float(source.TranslationGameUnits[2], "translation Z")) - origin);

    private static float[] Rotation(TtwFo3Cg00Stage10Transform source)
    {
        if (source.RotationRowMajor.Count != RotationValueCount)
            throw new InvalidOperationException("TTW stage-10 rotation length differs.");
        return source.RotationRowMajor
            .Select(value => Float(value, "rotation"))
            .ToArray();
    }

    private static float Float(double value, string label)
    {
        var result = checked((float)value);
        if (!float.IsFinite(result))
            throw new InvalidOperationException($"TTW stage-10 {label} is not finite float.");
        return result;
    }

    private static bool SceneRootMetadataMatches(
        Node3D root,
        TtwFo3Cg00Stage10MaterializedSceneIdentity identity) =>
        MetadataEquals(root, RootMetadataProjectionSha256, identity.ProjectionSha256) &&
        MetadataEquals(root, RootMetadataPluginStackId, identity.PluginStackId) &&
        MetadataEquals(root, RootMetadataSaveCompatibilityId, identity.SaveCompatibilityId) &&
        root.HasMeta(RootMetadataMemberClosureMaterialized) &&
        root.GetMeta(RootMetadataMemberClosureMaterialized).AsBool();

    private static bool MetadataEquals(Node node, string key, string expected) =>
        node.HasMeta(key) && node.GetMeta(key).AsString() == expected;

    private static bool SamePath(string left, string right) =>
        Path.GetFullPath(left).Equals(
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFormId(string value) =>
        value.Length == sizeof(uint) * 2 && value.All(Uri.IsHexDigit);

    private static void VerifyFile(string path, string sha256, string label)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new InvalidOperationException($"TTW {label} is absent.");
        using var stream = File.OpenRead(resolved);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != sha256)
            throw new InvalidOperationException($"TTW {label} hash differs.");
    }
}
