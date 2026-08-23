using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1Mob : Node3D
{
    private Sprite3D _sprite = null!;
    private ShaderMaterial _spriteMaterial = null!;
    private MeshInstance3D _hostileMarker = null!;
    private MeshInstance3D _hostileBeacon = null!;
    private Label3D _healthLabel = null!;

    internal int Serial { get; private set; }
    internal string DisplayName { get; private set; } = "";
    internal string Pid { get; private set; } = "";
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

    internal void Configure(
        int serial,
        string displayName,
        string pid,
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
        Texture2D texture,
        float pixelSize,
        Vector2 offset)
    {
        Serial = serial;
        DisplayName = displayName;
        Pid = pid;
        Tile = tile;
        HitPoints = Math.Clamp(hitPoints, 1, maximumHitPoints);
        MaximumHitPoints = maximumHitPoints;
        ActionPoints = Math.Max(0, actionPoints);
        MaximumActionPoints = Math.Max(maximumActionPoints, ActionPoints);
        ArmorClass = armorClass;
        MeleeDamage = Math.Max(1, meleeDamage);
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
            Scale = Vector3.One * 1.8f,
        };
        _spriteMaterial = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_prepass_alpha;
                    uniform sampler2D source_texture : source_color, filter_nearest;
                    uniform vec3 highlight_color : source_color = vec3(1.0, 0.42, 0.08);
                    uniform float highlight_mix : hint_range(0.0, 1.0) = 0.34;
                    void fragment() {
                        vec4 source = texture(source_texture, UV);
                        ALBEDO = mix(source.rgb, highlight_color, highlight_mix);
                        ALPHA = source.a;
                    }
                    """,
            },
        };
        _spriteMaterial.SetShaderParameter("source_texture", texture);
        _sprite.MaterialOverride = _spriteMaterial;
        AddChild(_sprite);
        var hostileMaterial = Fo1HexVisuals.Material(new Color(1.0f, 0.12f, 0.08f, 0.96f), true);
        hostileMaterial.NoDepthTest = true;
        hostileMaterial.EmissionEnabled = true;
        hostileMaterial.Emission = new Color(1.0f, 0.04f, 0.01f);
        hostileMaterial.EmissionEnergyMultiplier = 2.4f;
        _hostileMarker = new MeshInstance3D
        {
            Name = "HostileHexMarker",
            Mesh = Fo1HexVisuals.BuildRingMesh(0.62f, 1.18f),
            MaterialOverride = hostileMaterial,
            Position = Vector3.Up * 0.035f,
        };
        AddChild(_hostileMarker);
        var beaconMaterial = Fo1HexVisuals.Material(new Color(1.0f, 0.08f, 0.03f, 0.95f), true);
        beaconMaterial.NoDepthTest = true;
        beaconMaterial.EmissionEnabled = true;
        beaconMaterial.Emission = new Color(1.0f, 0.03f, 0.01f);
        beaconMaterial.EmissionEnergyMultiplier = 3.0f;
        _hostileBeacon = new MeshInstance3D
        {
            Name = "HostileBeacon",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.035f,
                BottomRadius = 0.10f,
                Height = 1.35f,
                RadialSegments = 8,
            },
            MaterialOverride = beaconMaterial,
            Position = Vector3.Up * 0.70f,
        };
        AddChild(_hostileBeacon);
        _healthLabel = new Label3D
        {
            Name = "HostileHealthLabel",
            Text = "RAT",
            Position = Vector3.Up * 1.50f,
            FontSize = 36,
            PixelSize = 0.010f,
            Modulate = new Color(1.0f, 0.34f, 0.20f),
            OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        AddChild(_healthLabel);
        SetTile(tile);
    }

    internal void SetTile(int tile)
    {
        Tile = tile;
        Position = Fo1HexMath.Center(tile) + Vector3.Up * 0.015f;
    }

    internal int TakeDamage(int damage)
    {
        var applied = Math.Min(HitPoints, Math.Max(0, damage));
        HitPoints -= applied;
        _healthLabel.Text = Alive
            ? $"RAT {HitPoints}/{MaximumHitPoints}"
            : "RAT DEAD";
        if (!Alive)
        {
            _spriteMaterial.SetShaderParameter("highlight_color", new Vector3(0.30f, 0.08f, 0.05f));
            _spriteMaterial.SetShaderParameter("highlight_mix", 0.78f);
            _sprite.RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f);
            _hostileMarker.Visible = false;
            _hostileBeacon.Visible = false;
            _healthLabel.Modulate = new Color(0.48f, 0.24f, 0.20f);
            _healthLabel.Visible = false;
        }
        return applied;
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
        _spriteMaterial.SetShaderParameter("highlight_color", selected
            ? new Vector3(1.0f, 0.68f, 0.08f)
            : new Vector3(1.0f, 0.28f, 0.05f));
        _spriteMaterial.SetShaderParameter("highlight_mix", selected ? 0.82f : 0.34f);
        _sprite.Scale = Vector3.One * (selected ? 2.25f : 1.8f);
        var markerMaterial = _hostileMarker.MaterialOverride as StandardMaterial3D;
        if (markerMaterial is not null)
        {
            markerMaterial.AlbedoColor = selected
                ? new Color(1.0f, 0.78f, 0.08f, 1.0f)
                : new Color(1.0f, 0.12f, 0.08f, 0.92f);
            markerMaterial.Emission = selected
                ? new Color(1.0f, 0.52f, 0.01f)
                : new Color(1.0f, 0.04f, 0.01f);
        }
        _hostileMarker.Scale = selected ? new Vector3(1.45f, 1.0f, 1.45f) : Vector3.One;
        _hostileBeacon.Scale = selected ? new Vector3(1.35f, 1.35f, 1.35f) : Vector3.One;
        _healthLabel.Modulate = selected
            ? new Color(1.0f, 0.88f, 0.25f)
            : new Color(1.0f, 0.34f, 0.20f);
        _healthLabel.Text = selected
            ? "▼"
            : "RAT";
        _healthLabel.Scale = selected ? Vector3.One * 1.6f : Vector3.One * 0.72f;
    }

    internal object Report() => new
    {
        serial = Serial,
        name = DisplayName,
        pid = Pid,
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
        alive = Alive,
    };
}
