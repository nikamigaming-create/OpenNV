using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2TempleInventoryScreen : Control
{
    private static readonly Color ClassicText = Colors.PaleGoldenrod;
    private readonly Fo2CharacterStartAsset _source;
    private readonly Fo2CharacterSelection _character;
    private readonly Fo2TempleConfrontationLoot _loot;
    private readonly Label _characterLabel;
    private readonly Label _itemLabel;
    private readonly Label _detailsLabel;
    private bool _spearLooted;
    private bool _spearEquipped;

    internal bool IsOpen => Visible;
    internal bool SpearSelected => Visible && _spearLooted;
    internal bool InspectionVisible { get; private set; }
    internal string SourceLogicalPath => _source.LogicalPath;
    internal string SourceSha256 => _source.SourceSha256;
    internal string CharacterText => _characterLabel.Text;
    internal string ItemText => _itemLabel.Text;
    internal string InspectionText => _detailsLabel.Text;

    internal Fo2TempleInventoryScreen(
        Fo2CharacterStartAsset source,
        Fo2CharacterSelection character,
        Fo2TempleConfrontationLoot loot,
        int fontSize)
    {
        if (source.Id != "inventory" ||
            source.LogicalPath != "art\\intrface\\invbox.frm" ||
            source.Width != 499 || source.Height != 377)
            throw new InvalidOperationException(
                "Fallout 2 inventory screen is not bound to the owned INVBOX FRM.");
        character.Profile.Validate(character.Mode == Fo2CharacterSelection.CreateMode);
        _source = source;
        _character = character;
        _loot = loot;
        Name = "FO2_TEMPLE_CLASSIC_INVENTORY";
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var dimmer = new ColorRect
        {
            Color = new Color(0.0f, 0.0f, 0.0f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dimmer);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var frame = new Control
        {
            CustomMinimumSize = new Vector2(source.Width, source.Height),
            MouseFilter = MouseFilterEnum.Stop,
        };
        center.AddChild(frame);
        var background = new TextureRect
        {
            Texture = source.Load(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(background);

        _characterLabel = AddLabel(
            frame,
            new Vector2(46.0f, 48.0f),
            new Vector2(68.0f, 250.0f),
            fontSize);
        _itemLabel = AddLabel(
            frame,
            new Vector2(286.0f, 68.0f),
            new Vector2(157.0f, 182.0f),
            fontSize);
        _detailsLabel = AddLabel(
            frame,
            new Vector2(151.0f, 190.0f),
            new Vector2(184.0f, 119.0f),
            Math.Max(12, fontSize - 3));
        var close = AddLabel(
            frame,
            new Vector2(354.0f, 323.0f),
            new Vector2(102.0f, 28.0f),
            Math.Max(12, fontSize - 3));
        close.Text = "ESC: CLOSE";
        Refresh(false, false);
        Visible = false;
    }

    internal void Refresh(bool spearLooted, bool spearEquipped)
    {
        _spearLooted = spearLooted;
        _spearEquipped = spearEquipped;
        _characterLabel.Text =
            $"{_character.Profile.Name.ToUpperInvariant()}\n\n" +
            $"{_character.Role.ToUpperInvariant()}\n" +
            $"{_character.Profile.Sex.ToUpperInvariant()}\n" +
            $"AGE {_character.Profile.Age}";
        _itemLabel.Text = spearLooted
            ? $"> {_loot.Quantity} × {_loot.DisplayName.ToUpperInvariant()}\n" +
              $"[{(spearEquipped ? "EQUIPPED" : "UNEQUIPPED")}]\n\n" +
              "SPACE/A: EQUIP\nE/B: INSPECT"
            : "NO ITEMS IN THIS\nBOUNDED TEMPLE SLICE";
        if (InspectionVisible)
            SetInspectionText();
        else
            _detailsLabel.Text = spearLooted ? "SPEAR SELECTED" : "NO ITEM SELECTED";
    }

    internal void Open(bool spearLooted, bool spearEquipped)
    {
        InspectionVisible = false;
        Refresh(spearLooted, spearEquipped);
        Visible = true;
    }

    internal void ShowInspection(bool spearEquipped)
    {
        if (!SpearSelected)
            throw new InvalidOperationException(
                "Fallout 2 inventory cannot inspect an unavailable Spear.");
        _spearEquipped = spearEquipped;
        InspectionVisible = true;
        SetInspectionText();
    }

    internal bool Close()
    {
        if (!Visible)
            return false;
        Visible = false;
        InspectionVisible = false;
        return true;
    }

    private void SetInspectionText()
    {
        _detailsLabel.Text =
            $"PID {_loot.Pid}\n" +
            $"DMG {_loot.Weapon.MinimumDamage}–{_loot.Weapon.MaximumDamage}\n" +
            $"AP {_loot.Weapon.ActionPointCostPrimary}/{_loot.Weapon.ActionPointCostSecondary}\n" +
            $"RNG {_loot.Weapon.MaximumRangePrimary}/{_loot.Weapon.MaximumRangeSecondary}\n" +
            $"STR {_loot.Weapon.MinimumStrength}\n" +
            (_spearEquipped ? "EQUIPPED" : "UNEQUIPPED");
    }

    private static Label AddLabel(
        Control parent,
        Vector2 position,
        Vector2 size,
        int fontSize)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", ClassicText);
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(label);
        return label;
    }
}
