using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1Mob : Node3D
{
    private Sprite3D _sprite = null!;

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
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        AddChild(_sprite);
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
        if (!Alive)
        {
            _sprite.Modulate = new Color(0.38f, 0.16f, 0.13f, 0.72f);
            _sprite.RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f);
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
        _sprite.Modulate = selected
            ? new Color(1.0f, 0.68f, 0.28f, 1.0f)
            : Colors.White;
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
