using Godot;

using OpenNV.Runtime.SceneGraph;


namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1MobNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point0001f = 0.0001f;
    internal const int PresentationInt64 = 64;
}

internal partial class Fo1Mob : Node3D
{
    private Sprite3D _sprite = null!;
    private ShaderMaterial _spriteMaterial = null!;
    private MeshInstance3D _hostileMarker = null!;
    private MeshInstance3D _hostileBeacon = null!;
    private Label3D _healthLabel = null!;
    private Node3D? _creatureRoot;
    private AnimationPlayer? _creatureAnimation;
    private readonly List<StandardMaterial3D> _creatureMaterials = [];
    private Vector3 _creatureBaseScale = Vector3.One;
    private float _creatureGroundOffset;
    private IReadOnlyDictionary<string, string> _animationRoles = new Dictionary<string, string>();
    private int _creatureHiddenGoreMeshes;
    private bool _deathPoseSettled;
    private bool _readabilityEnabled = true;
    private bool _selected;
    private bool _readabilityTactical = true;
    private int _readabilityDistanceHexes = int.MaxValue;
    private Fo1RuntimeProfile _runtimeProfile = null!;

    internal int Serial { get; private set; }
    internal string DisplayName { get; private set; } = "";
    internal string Pid { get; private set; } = "";
    internal string PrototypeSha256 { get; private set; } = "";
    internal int Tile { get; private set; }
    internal int HitPoints { get; private set; }
    internal int MaximumHitPoints { get; private set; }
    internal int ActionPoints { get; private set; }
    internal int MaximumActionPoints { get; private set; }
    internal int ArmorClass { get; private set; }
    internal int MeleeDamage { get; private set; }
    internal int Sequence { get; private set; }
    internal int Team { get; private set; }
    internal int AiPacket { get; private set; }
    internal bool Alive => HitPoints > 0;
    internal bool Alerted { get; private set; }
    internal float CreatureUnitsToMeters => _creatureRoot is null ? 0.0f : _creatureBaseScale.X;
    internal float CreatureSelectionMultiplier => _creatureRoot is null
        ? 0.0f
        : _creatureRoot.Scale.X / _creatureBaseScale.X;
    internal int CreatureHiddenGoreMeshes => _creatureHiddenGoreMeshes;
    internal float CreatureGroundErrorMeters => _creatureRoot is null
        ? 0.0f
        : MathF.Abs(
            _creatureRoot.Position.Y -
            _creatureGroundOffset * CreatureSelectionMultiplier);
    internal bool HostileMarkerDepthTested =>
        _hostileMarker.MaterialOverride is StandardMaterial3D material && !material.NoDepthTest;
    internal bool HostileMarkerVisible => _hostileMarker.Visible;
    internal bool HostileBeaconVisible => _hostileBeacon.Visible;
    internal bool HostileLabelVisible => _healthLabel.Visible;
    internal int ReadabilityDistanceHexes => _readabilityDistanceHexes;
    internal bool CorpseVisible =>
        !Alive && (_creatureRoot is null
            ? _sprite.Visible
            : NodeTraversal.Descendants<MeshInstance3D>(_creatureRoot).Any(mesh => mesh.Visible));
    internal float CorpseGroundErrorMeters
    {
        get
        {
            if (Alive || _creatureRoot is null)
                return 0.0f;
            return MathF.Abs(WorldBounds(_creatureRoot).Position.Y - GlobalPosition.Y);
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!Alive && _creatureRoot is not null)
            GroundCorpse();
    }

    internal void Configure(
        int serial,
        string displayName,
        string pid,
        string prototypeSha256,
        int tile,
        int hitPoints,
        int maximumHitPoints,
        int actionPoints,
        int maximumActionPoints,
        int armorClass,
        int meleeDamage,
        int sequence,
        int team,
        int aiPacket,
        int rotation,
        Texture2D texture,
        float pixelSize,
        Vector2 offset,
        Fo1CreatureModel.Template? creatureTemplate,
        Fo1RuntimeProfile runtimeProfile)
    {
        if (string.IsNullOrWhiteSpace(pid) ||
            prototypeSha256.Length != Fo1MobNumericContracts.PresentationInt64 ||
            !prototypeSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException(
                "Fallout critter has no hash-bound source prototype.");
        _runtimeProfile = runtimeProfile;
        Serial = serial;
        DisplayName = displayName;
        Pid = pid;
        PrototypeSha256 = prototypeSha256;
        Tile = tile;
        HitPoints = Math.Clamp(hitPoints, 0, maximumHitPoints);
        MaximumHitPoints = maximumHitPoints;
        ActionPoints = Math.Max(0, actionPoints);
        MaximumActionPoints = Math.Max(maximumActionPoints, ActionPoints);
        ArmorClass = armorClass;
        MeleeDamage = Math.Max(runtimeProfile.Gameplay.MinimumDamage, meleeDamage);
        Sequence = sequence;
        Team = team;
        AiPacket = aiPacket;
        Name = $"FO1_MOB_{serial}_{displayName.Replace(" ", "_")}";
        _sprite = new Sprite3D
        {
            Name = "SourceCritterSprite",
            Texture = texture,
            PixelSize = pixelSize,
            Offset = offset,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Scale = Vector3.One * runtimeProfile.Mob.SourceSpriteScale,
            Visible = creatureTemplate is null,
        };
        _spriteMaterial = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_prepass_alpha;
                    uniform sampler2D source_texture : source_color, filter_nearest;
                    uniform vec3 highlight_color : source_color;
                    uniform float highlight_mix : hint_range(0.0, 1.0);
                    void fragment() {
                        vec4 source = texture(source_texture, UV);
                        ALBEDO = mix(source.rgb, highlight_color, highlight_mix);
                        ALPHA = source.a;
                    }
                    """,
            },
        };
        _spriteMaterial.SetShaderParameter("source_texture", texture);
        _spriteMaterial.SetShaderParameter(
            "highlight_color",
            new Vector3(
                runtimeProfile.Mob.SourceHighlight.NormalColor.R,
                runtimeProfile.Mob.SourceHighlight.NormalColor.G,
                runtimeProfile.Mob.SourceHighlight.NormalColor.B));
        _spriteMaterial.SetShaderParameter(
            "highlight_mix",
            runtimeProfile.Mob.SourceHighlight.NormalMix);
        _sprite.MaterialOverride = _spriteMaterial;
        AddChild(_sprite);
        if (creatureTemplate is not null)
        {
            var creature = creatureTemplate.Instantiate();
            _creatureRoot = creature.Root;
            _creatureRoot.Name = "OwnedNVCrGiantRat";
            _creatureBaseScale = _creatureRoot.Scale;
            _creatureGroundOffset = -creature.Bounds.Position.Y;
            SetCreatureScale(1.0f);
            _creatureRoot.Rotation = new Vector3(
                0.0f,
                Mathf.DegToRad(-rotation * runtimeProfile.Mob.RotationDegreesPerSourceStep),
                0.0f);
            _creatureAnimation = creature.Player;
            _animationRoles = creature.AnimationRoles;
            _creatureHiddenGoreMeshes = ConfigureIntactCreatureVisibility(
                _creatureRoot,
                runtimeProfile.Mob.IntactHiddenMeshNameFragments,
                creature.SourceShapesByRuntimeNodeName);
            if (_creatureHiddenGoreMeshes != runtimeProfile.Mob.ExpectedIntactHiddenMeshes)
                throw new InvalidOperationException(
                    $"Fallout intact giant-rat gore-cap coverage drift: {_creatureHiddenGoreMeshes}");
            PrepareCreatureMaterials(_creatureRoot);
            AddChild(_creatureRoot);
            PlayAnimation("idle");
        }
        var markerProfile = runtimeProfile.Mob.HostileMarker;
        var hostileMaterial = Fo1HexVisuals.Material(markerProfile.NormalColor, true);
        hostileMaterial.NoDepthTest = false;
        hostileMaterial.EmissionEnabled = true;
        hostileMaterial.Emission = markerProfile.NormalEmissionColor;
        hostileMaterial.EmissionEnergyMultiplier = markerProfile.EmissionEnergy;
        _hostileMarker = new MeshInstance3D
        {
            Name = "HostileHexMarker",
            Mesh = Fo1HexVisuals.BuildRingMesh(
                markerProfile.InnerRadiusMeters,
                markerProfile.OuterRadiusMeters),
            MaterialOverride = hostileMaterial,
            Position = Vector3.Up * markerProfile.YOffsetMeters,
            Visible = false,
        };
        AddChild(_hostileMarker);
        var beaconProfile = runtimeProfile.Mob.HostileBeacon;
        var beaconMaterial = Fo1HexVisuals.Material(beaconProfile.Color, true);
        beaconMaterial.NoDepthTest = true;
        beaconMaterial.EmissionEnabled = true;
        beaconMaterial.Emission = beaconProfile.EmissionColor;
        beaconMaterial.EmissionEnergyMultiplier = beaconProfile.EmissionEnergy;
        _hostileBeacon = new MeshInstance3D
        {
            Name = "HostileBeacon",
            Mesh = new CylinderMesh
            {
                TopRadius = beaconProfile.TopRadiusMeters,
                BottomRadius = beaconProfile.BottomRadiusMeters,
                Height = beaconProfile.HeightMeters,
                RadialSegments = beaconProfile.RadialSegments,
            },
            MaterialOverride = beaconMaterial,
            Position = Vector3.Up * beaconProfile.YOffsetMeters,
            Visible = false,
        };
        AddChild(_hostileBeacon);
        _healthLabel = new Label3D
        {
            Name = "HostileHealthLabel",
            Text = "",
            Position = Vector3.Up * runtimeProfile.Mob.HealthLabel.YOffsetMeters,
            FontSize = runtimeProfile.Mob.HealthLabel.FontSize,
            PixelSize = runtimeProfile.Mob.HealthLabel.PixelSize,
            Modulate = runtimeProfile.Mob.HealthLabel.NormalColor,
            OutlineSize = runtimeProfile.Mob.HealthLabel.OutlineSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_healthLabel);
        SetTile(tile);
    }

    internal void SetTile(int tile)
    {
        Tile = tile;
        Position = Fo1HexMath.Center(tile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
    }

    internal void MoveTo(int tile)
    {
        var target = Fo1HexMath.Center(tile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        var direction = target - Position;
        direction.Y = 0.0f;
        if (_creatureRoot is not null && direction.LengthSquared() > Fo1MobNumericContracts.PresentationFloat0Point0001f)
            _creatureRoot.Rotation = new Vector3(0.0f, MathF.Atan2(direction.X, direction.Z), 0.0f);
        Tile = tile;
        PlayAnimation("move");
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(
            this,
            "position",
            target,
            _runtimeProfile.Mob.Animation.MoveSeconds);
        tween.TweenCallback(Callable.From(() => PlayAnimation("idle")));
    }

    internal int TakeDamage(int damage)
    {
        var applied = Math.Min(HitPoints, Math.Max(0, damage));
        HitPoints -= applied;
        PlayAnimation("hit");
        _healthLabel.Text = Alive && _selected ? "▼" : "";
        if (!Alive)
        {
            var sourceHighlight = _runtimeProfile.Mob.SourceHighlight;
            _spriteMaterial.SetShaderParameter(
                "highlight_color",
                new Vector3(
                    sourceHighlight.DefeatedColor.R,
                    sourceHighlight.DefeatedColor.G,
                    sourceHighlight.DefeatedColor.B));
            _spriteMaterial.SetShaderParameter("highlight_mix", sourceHighlight.DefeatedMix);
            _sprite.RotationDegrees = new Vector3(
                0.0f,
                0.0f,
                sourceHighlight.DefeatedRollDegrees);
            if (_creatureRoot is not null)
            {
                _creatureAnimation?.Stop(keepState: true);
                SetCreatureScale(1.0f);
                SetCreatureHighlight(false, defeated: true);
                var startRotation = _creatureRoot.Rotation;
                var targetRotation = new Vector3(
                    startRotation.X,
                    startRotation.Y,
                    Mathf.DegToRad(_runtimeProfile.Mob.Animation.DeathRollDegrees));
                var deathTween = CreateTween();
                deathTween.SetTrans(Tween.TransitionType.Quad);
                deathTween.SetEase(Tween.EaseType.Out);
                deathTween.TweenProperty(
                    _creatureRoot,
                    "rotation",
                    targetRotation,
                    _runtimeProfile.Mob.Animation.DeathRollSeconds);
                deathTween.TweenCallback(Callable.From(() =>
                {
                    GroundCorpse();
                    _deathPoseSettled = true;
                }));
            }
            _hostileMarker.Visible = false;
            _hostileBeacon.Visible = false;
            _healthLabel.Modulate = _runtimeProfile.Mob.HealthLabel.DefeatedColor;
            _healthLabel.Visible = false;
        }
        else
        {
            var timer = GetTree().CreateTimer(_runtimeProfile.Mob.Animation.HitHoldSeconds);
            timer.Timeout += () => PlayAnimation("idle");
        }
        return applied;
    }

    internal void PlayAttack()
    {
        PlayAnimation("attack");
        var timer = GetTree().CreateTimer(_runtimeProfile.Mob.Animation.AttackHoldSeconds);
        timer.Timeout += () => PlayAnimation("idle");
    }

    internal void Alert()
    {
        if (Alive)
            Alerted = true;
    }

    internal void ResetActionPoints()
    {
        ActionPoints = MaximumActionPoints;
    }

    internal bool SpendActionPoint()
    {
        if (ActionPoints <= 0)
            return false;
        ActionPoints--;
        return true;
    }

    internal void SetSelected(bool selected)
    {
        if (!Alive)
            return;
        _selected = selected;
        var sourceHighlight = _runtimeProfile.Mob.SourceHighlight;
        var sourceColor = selected ? sourceHighlight.SelectedColor : sourceHighlight.NormalColor;
        _spriteMaterial.SetShaderParameter(
            "highlight_color",
            new Vector3(sourceColor.R, sourceColor.G, sourceColor.B));
        _spriteMaterial.SetShaderParameter(
            "highlight_mix",
            selected ? sourceHighlight.SelectedMix : sourceHighlight.NormalMix);
        _sprite.Scale = Vector3.One * (selected
            ? _runtimeProfile.Mob.SelectedSourceSpriteScale
            : _runtimeProfile.Mob.SourceSpriteScale);
        SetCreatureScale(selected ? _runtimeProfile.Mob.SelectedCreatureScale : 1.0f);
        SetCreatureHighlight(selected, defeated: false);
        var markerMaterial = _hostileMarker.MaterialOverride as StandardMaterial3D;
        if (markerMaterial is not null)
        {
            markerMaterial.AlbedoColor = selected
                ? _runtimeProfile.Mob.HostileMarker.SelectedColor
                : _runtimeProfile.Mob.HostileMarker.NormalColor;
            markerMaterial.Emission = selected
                ? _runtimeProfile.Mob.HostileMarker.SelectedEmissionColor
                : _runtimeProfile.Mob.HostileMarker.NormalEmissionColor;
        }
        _hostileMarker.Scale = selected
            ? new Vector3(
                _runtimeProfile.Mob.HostileMarker.SelectedScale,
                1.0f,
                _runtimeProfile.Mob.HostileMarker.SelectedScale)
            : Vector3.One;
        _hostileBeacon.Scale = selected
            ? Vector3.One * _runtimeProfile.Mob.HostileBeacon.SelectedScale
            : Vector3.One;
        _healthLabel.Modulate = selected
            ? _runtimeProfile.Mob.HealthLabel.SelectedColor
            : _runtimeProfile.Mob.HealthLabel.NormalColor;
        _healthLabel.Text = selected
            ? "▼"
            : "";
        _healthLabel.Scale = Vector3.One * (selected
            ? _runtimeProfile.Mob.HealthLabel.SelectedScale
            : _runtimeProfile.Mob.HealthLabel.NormalScale);
        ApplyReadability();
    }

    internal object Report() => new
    {
        serial = Serial,
        name = DisplayName,
        pid = Pid,
        prototypeSha256 = PrototypeSha256,
        tile = Tile,
        hitPoints = HitPoints,
        maximumHitPoints = MaximumHitPoints,
        actionPoints = ActionPoints,
        maximumActionPoints = MaximumActionPoints,
        armorClass = ArmorClass,
        meleeDamage = MeleeDamage,
        sequence = Sequence,
        team = Team,
        aiPacket = AiPacket,
        alerted = Alerted,
        alive = Alive,
        presentation = _creatureRoot is null ? "source-sprite" : "owned-fnv-skinned-3d",
        creatureUnitsToMeters = CreatureUnitsToMeters,
        creatureSelectionMultiplier = CreatureSelectionMultiplier,
        creatureGroundErrorMeters = CreatureGroundErrorMeters,
        corpseGroundErrorMeters = CorpseGroundErrorMeters,
        corpseVisible = CorpseVisible,
        deathPoseSettled = _deathPoseSettled,
        hostileMarkerDepthTested = HostileMarkerDepthTested,
        hostileMarkerVisible = HostileMarkerVisible,
        hostileBeaconVisible = HostileBeaconVisible,
        hostileLabelVisible = HostileLabelVisible,
        readabilityDistanceHexes = ReadabilityDistanceHexes,
        hiddenGoreMeshes = CreatureHiddenGoreMeshes,
    };

    internal void SetReadabilityMarkersVisible(bool visible)
    {
        _readabilityEnabled = visible;
        ApplyReadability();
    }

    internal void UpdateReadability(int playerTile, bool tactical)
    {
        _readabilityDistanceHexes = Fo1HexMath.Distance(playerTile, Tile);
        _readabilityTactical = tactical;
        ApplyReadability();
    }

    private void ApplyReadability()
    {
        var maximumRange = _readabilityTactical
            ? _runtimeProfile.Mob.Readability.TacticalRangeHexes
            : _runtimeProfile.Mob.Readability.PerspectiveRangeHexes;
        var inRange = _readabilityDistanceHexes <= maximumRange;
        var visible = _readabilityEnabled && Alive && inRange;
        _hostileMarker.Visible = visible;
        _hostileBeacon.Visible = visible &&
            (_selected ||
                _readabilityDistanceHexes <= _runtimeProfile.Mob.Readability.BeaconRangeHexes);
        _healthLabel.Visible = visible && _selected;
    }

    private void PlayAnimation(string role)
    {
        if (_creatureAnimation is null || !_animationRoles.TryGetValue(role, out var animation))
            return;
        _creatureAnimation.Play(animation, customBlend: _runtimeProfile.Mob.Animation.BlendSeconds);
    }

    private void PrepareCreatureMaterials(Node3D creatureRoot)
    {
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(creatureRoot))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source)
                    continue;
                var material = source.Duplicate() as StandardMaterial3D
                    ?? throw new InvalidOperationException("Could not duplicate Fallout creature material.");
                material.EmissionEnabled = true;
                material.EmissionTexture = material.AlbedoTexture;
                material.Emission = _runtimeProfile.Mob.CreatureHighlight.NormalColor;
                material.EmissionEnergyMultiplier =
                    _runtimeProfile.Mob.CreatureHighlight.NormalEnergy;
                mesh.SetSurfaceOverrideMaterial(surface, material);
                _creatureMaterials.Add(material);
            }
        }
        if (_creatureMaterials.Count == 0)
            throw new InvalidOperationException("Fallout creature has no highlightable materials.");
    }

    private static int ConfigureIntactCreatureVisibility(
        Node3D creatureRoot,
        IReadOnlyList<string> hiddenNameFragments,
        IReadOnlyDictionary<string, string> sourceShapesByRuntimeNodeName)
    {
        var hidden = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(creatureRoot))
        {
            var runtimeName = mesh.Name.ToString();
            if (!sourceShapesByRuntimeNodeName.TryGetValue(runtimeName, out var name))
                throw new InvalidOperationException(
                    $"Fallout creature surface has no source-shape identity: {runtimeName}");
            var goreCap = hiddenNameFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            mesh.Visible = !goreCap;
            if (goreCap)
                hidden++;
        }
        return hidden;
    }

    private void SetCreatureHighlight(bool selected, bool defeated)
    {
        foreach (var material in _creatureMaterials)
        {
            material.Emission = defeated
                ? _runtimeProfile.Mob.CreatureHighlight.DefeatedColor
                : selected
                    ? _runtimeProfile.Mob.CreatureHighlight.SelectedColor
                    : _runtimeProfile.Mob.CreatureHighlight.NormalColor;
            material.EmissionEnergyMultiplier = defeated
                ? _runtimeProfile.Mob.CreatureHighlight.DefeatedEnergy
                : selected
                    ? _runtimeProfile.Mob.CreatureHighlight.SelectedEnergy
                    : _runtimeProfile.Mob.CreatureHighlight.NormalEnergy;
        }
    }

    private void SetCreatureScale(float multiplier)
    {
        if (_creatureRoot is null)
            return;
        _creatureRoot.Scale = _creatureBaseScale * multiplier;
        _creatureRoot.Position = Vector3.Up * (_creatureGroundOffset * multiplier);
    }

    private static Aabb WorldBounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var count = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            if (!mesh.Visible || mesh.Mesh is null)
                continue;
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException("Fallout creature has no visible corpse bounds.");
        return new Aabb(minimum, maximum - minimum);
    }

    private void GroundCorpse()
    {
        if (_creatureRoot is null)
            return;
        var bounds = WorldBounds(_creatureRoot);
        _creatureRoot.Position += Vector3.Up * (GlobalPosition.Y - bounds.Position.Y);
    }
}
