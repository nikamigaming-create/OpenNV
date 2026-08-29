using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2TempleInventoryScreenNumericContracts
{
    internal const int SourceWidth = 499;
    internal const int SourceHeight = 377;
    internal const float DimmerAlpha = 0.72f;
    internal const float CharacterX = 46.0f;
    internal const float CharacterY = 48.0f;
    internal const float CharacterWidth = 68.0f;
    internal const float CharacterHeight = 250.0f;
    internal const float ItemX = 286.0f;
    internal const float ItemY = 68.0f;
    internal const float ItemWidth = 157.0f;
    internal const float ItemHeight = 182.0f;
    internal const float DetailsX = 151.0f;
    internal const float DetailsY = 190.0f;
    internal const float DetailsWidth = 184.0f;
    internal const float DetailsHeight = 119.0f;
    internal const float CloseX = 354.0f;
    internal const float CloseY = 323.0f;
    internal const float CloseWidth = 102.0f;
    internal const float CloseHeight = 28.0f;
    internal const int MinimumDetailFontSize = 12;
}

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
            source.Width != Fo2TempleInventoryScreenNumericContracts.SourceWidth ||
            source.Height != Fo2TempleInventoryScreenNumericContracts.SourceHeight)
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
            Color = new Color(
                0.0f,
                0.0f,
                0.0f,
                Fo2TempleInventoryScreenNumericContracts.DimmerAlpha),
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
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.CharacterX,
                Fo2TempleInventoryScreenNumericContracts.CharacterY),
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.CharacterWidth,
                Fo2TempleInventoryScreenNumericContracts.CharacterHeight),
            fontSize);
        _itemLabel = AddLabel(
            frame,
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.ItemX,
                Fo2TempleInventoryScreenNumericContracts.ItemY),
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.ItemWidth,
                Fo2TempleInventoryScreenNumericContracts.ItemHeight),
            fontSize);
        _detailsLabel = AddLabel(
            frame,
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.DetailsX,
                Fo2TempleInventoryScreenNumericContracts.DetailsY),
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.DetailsWidth,
                Fo2TempleInventoryScreenNumericContracts.DetailsHeight),
            Math.Max(
                Fo2TempleInventoryScreenNumericContracts.MinimumDetailFontSize,
                fontSize - 3));
        var close = AddLabel(
            frame,
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.CloseX,
                Fo2TempleInventoryScreenNumericContracts.CloseY),
            new Vector2(
                Fo2TempleInventoryScreenNumericContracts.CloseWidth,
                Fo2TempleInventoryScreenNumericContracts.CloseHeight),
            Math.Max(
                Fo2TempleInventoryScreenNumericContracts.MinimumDetailFontSize,
                fontSize - 3));
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
