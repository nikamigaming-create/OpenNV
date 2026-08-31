using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed class OpeningCigaretteSmokePresentation
{
    internal const string Authority =
        "first-party-presentation-adaptation-owned-anio-tip-anchor";
    private const int PuffCount = 4;
    private const float PuffLifetimeSeconds = 1.5f;
    private const float PuffRadiusMeters = 0.012f;
    private const float PuffHeightMeters = 0.024f;
    private const float PuffRiseMeters = 0.08f;
    private const float PuffDriftMeters = 0.015f;
    private const float PuffOpacity = 0.35f;
    private const float PuffPhaseRadians = MathF.Tau / PuffCount;

    private readonly ActorModelSlice.LoadedSurface _cigarette;
    private readonly Node3D _root;
    private readonly IReadOnlyList<MeshInstance3D> _puffs;
    private float _elapsedSeconds;

    private OpeningCigaretteSmokePresentation(
        ActorModelSlice.LoadedSurface cigarette,
        Node3D root,
        IReadOnlyList<MeshInstance3D> puffs,
        Vector3 tipLocal)
    {
        _cigarette = cigarette;
        _root = root;
        _puffs = puffs;
        TipLocal = tipLocal;
    }

    internal bool Active { get; private set; }
    internal int ActivePuffCount => Active ? _puffs.Count : 0;
    internal float LifetimeSeconds => PuffLifetimeSeconds;
    internal Vector3 TipLocal { get; }
    internal Vector3 TipWorld => _cigarette.Mesh.ToGlobal(TipLocal);
    internal Node3D Root => _root;
    internal ActorModelSlice.LoadedSurface Cigarette => _cigarette;

    internal static OpeningCigaretteSmokePresentation Create(
        Node3D worldParent,
        ActorModelSlice.LoadedSurface cigarette,
        OpeningGuideAnimationObject source)
    {
        if (!cigarette.Role.Equals(source.ComponentRole, StringComparison.Ordinal) ||
            !cigarette.SourceFormId?.Equals(
                source.FormId,
                StringComparison.OrdinalIgnoreCase) == true ||
            !cigarette.AttachmentNode?.Equals(
                source.AttachmentNode,
                StringComparison.Ordinal) == true ||
            !cigarette.RigidShapeTransformBaked)
            throw new InvalidOperationException(
                "Cigarette presentation source binding is incomplete.");
        if (cigarette.Mesh.GetParent() is not Node3D attachment ||
            !attachment.Name.ToString().Equals(
                source.AttachmentNode,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Cigarette runtime mesh is not parented to its source attachment node.");
        var localBounds = cigarette.Mesh.GetAabb();
        if (!localBounds.Position.IsFinite() || !localBounds.Size.IsFinite() ||
            localBounds.Size.IsZeroApprox())
            throw new InvalidOperationException(
                "Cigarette runtime mesh has no finite source geometry bounds.");
        var tipLocal = FarthestLongestAxisEndpoint(localBounds);
        var root = new Node3D
        {
            Name = "OpeningCigaretteSmokePresentation",
            Visible = false,
        };
        worldParent.AddChild(root);
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(Colors.LightGray, PuffOpacity),
        };
        var puffs = new List<MeshInstance3D>();
        for (var index = 0; index < PuffCount; index++)
        {
            var puff = new MeshInstance3D
            {
                Name = $"CigaretteSmokePuff_{index}",
                Mesh = new SphereMesh
                {
                    Radius = PuffRadiusMeters,
                    Height = PuffHeightMeters,
                    Material = material,
                },
            };
            root.AddChild(puff);
            puffs.Add(puff);
        }
        return new OpeningCigaretteSmokePresentation(
            cigarette,
            root,
            puffs,
            tipLocal);
    }

    internal void SetActive(bool active)
    {
        if (Active == active)
            return;
        Active = active;
        _elapsedSeconds = 0.0f;
        _root.Visible = active;
        if (active)
            Update(0.0);
    }

    internal void Update(double delta)
    {
        if (!Active)
            return;
        _elapsedSeconds = (_elapsedSeconds + (float)delta) % PuffLifetimeSeconds;
        _root.GlobalTransform = new Transform3D(Basis.Identity, TipWorld);
        for (var index = 0; index < _puffs.Count; index++)
        {
            var age = (_elapsedSeconds + index * PuffLifetimeSeconds / PuffCount) %
                PuffLifetimeSeconds;
            var progress = age / PuffLifetimeSeconds;
            _puffs[index].Position = new Vector3(
                MathF.Sin(index * PuffPhaseRadians + progress * MathF.Tau) *
                    PuffDriftMeters,
                progress * PuffRiseMeters,
                0.0f);
            _puffs[index].Scale = Vector3.One * (1.0f + progress);
        }
    }

    internal Aabb SmokeWorldBounds() => ActorModelSlice.WorldBounds(_root);

    private static Vector3 FarthestLongestAxisEndpoint(Aabb bounds)
    {
        var center = bounds.GetCenter();
        var axis = (int)bounds.Size.MaxAxisIndex();
        var minimum = center;
        var maximum = center;
        minimum[axis] = bounds.Position[axis];
        maximum[axis] = bounds.End[axis];
        return minimum.LengthSquared() > maximum.LengthSquared() ? minimum : maximum;
    }
}
