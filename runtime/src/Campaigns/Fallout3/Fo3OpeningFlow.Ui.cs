using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void AddSelector(GridContainer grid, string title, OptionButton selector)
    {
        selector.Name = $"FO3_RaceSexMenu_{title}";
        selector.CustomMinimumSize = new Vector2(
            0.0f,
            _profile.Appearance.Ui.ListItemHeight);
        selector.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        selector.AddThemeFontSizeOverride(
            "font_size",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        grid.AddChild(selector);
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceRace> options,
        string selectedFormId,
        string prefix)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem($"{prefix}  •  {options[index].Label}");
            selector.SetItemMetadata(index, options[index].FormId);
            if (options[index].FormId == selectedFormId)
                selector.Select(index);
        }
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceOption> options,
        string selectedFormId,
        string prefix)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem($"{prefix}  •  {options[index].Label}");
            selector.SetItemMetadata(index, options[index].FormId);
            if (options[index].FormId == selectedFormId)
                selector.Select(index);
        }
    }

    private void RenderAppearancePreview(
        HBoxContainer preview,
        Fo3AppearanceAsset head,
        Fo3AppearanceAsset hair,
        Fo3AppearanceAsset eyes,
        Fo3FaceGenDefaults faceGen)
    {
        foreach (var child in preview.GetChildren())
        {
            preview.RemoveChild(child);
            child.QueueFree();
        }
        preview.AddChild(AppearancePreviewTile("MENU", _profile.Appearance.Ui.BackgroundTexture));
        preview.AddChild(AppearancePreviewTile("HEAD", head));
        preview.AddChild(AppearancePreviewTile("HAIR", hair));
        preview.AddChild(AppearancePreviewTile("EYES", eyes));
        preview.TooltipText =
            $"FaceGen defaults: {faceGen.SymmetricGeometrySha256} / " +
            $"{faceGen.AsymmetricGeometrySha256} / {faceGen.SymmetricTextureSha256}";
    }

    private VBoxContainer AppearancePreviewTile(string title, Fo3AppearanceAsset asset)
    {
        var image = LoadAppearanceImage(asset);
        var tile = new VBoxContainer();
        tile.AddChild(Label(title, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        tile.AddChild(new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(image),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(
                Fo3OpeningFlowNumericContracts.AppearancePreviewTexturePixels,
                Fo3OpeningFlowNumericContracts.AppearancePreviewTexturePixels),
            TooltipText = $"source={asset.SourceSha256} preview={asset.PreviewSha256}",
        });
        return tile;
    }

    private static Image LoadAppearanceImage(Fo3AppearanceAsset asset)
    {
        var image = Image.LoadFromFile(asset.PreviewPath);
        if (image is null || image.IsEmpty())
            throw new InvalidOperationException(
                $"Fallout 3 owned appearance preview could not be loaded: {asset.PreviewPath}");
        return image;
    }

    private Control CreatorSurface(
        float left,
        float top,
        float width,
        float height,
        Fo3AppearanceAsset background,
        string name)
    {
        if (_creatorLayer is null)
        {
            _creatorLayer = new Control { Name = "FO3_OwnedCreatorCanvas_1600x1200" };
            _creatorLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_creatorLayer);
            _panel.Visible = false;
            _background.Visible = _vaultPreviewHost is null;
        }
        var surface = new Control { Name = name };
        surface.AnchorLeft = left;
        surface.AnchorTop = top;
        surface.AnchorRight = left + width;
        surface.AnchorBottom = top + height;
        _creatorLayer.AddChild(surface);
        var texture = new TextureRect
        {
            Name = $"{name}_OwnedBackground",
            Texture = ImageTexture.CreateFromImage(LoadAppearanceImage(background)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText =
                $"source={background.SourceSha256} preview={background.PreviewSha256}",
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        surface.AddChild(texture);
        return surface;
    }

    private static VBoxContainer CreatorColumn(Control surface, int marginPixels)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, marginPixels);
        surface.AddChild(margin);
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride(
            "separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        margin.AddChild(column);
        return column;
    }

    private void ClearContent()
    {
        if (_creatorLayer is not null)
        {
            _creatorLayer.Visible = false;
            _creatorLayer.QueueFree();
            _creatorLayer = null;
        }
        _activeNameInput = null;
        _activeAppearanceCategory = null;
        _activeFaceControlSlider = null;
        _activeAppearanceSelection = null;
        _activeFacePreview = null;
        _reflectron = null;
        _panel.Visible = true;
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Label Label(string text, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", _profile.InterfaceColor);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private Button Button(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(
                0.0f,
                Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels),
        };
        button.AddThemeColorOverride("font_color", _profile.InterfaceColor);
        button.AddThemeColorOverride("font_hover_color", Colors.Black);
        button.AddThemeColorOverride("font_pressed_color", Colors.Black);
        button.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        var highlight = new StyleBoxFlat
        {
            BgColor = _profile.InterfaceColor,
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        };
        button.AddThemeStyleboxOverride("hover", highlight);
        button.AddThemeStyleboxOverride("focus", highlight);
        button.AddThemeStyleboxOverride("pressed", highlight);
        return button;
    }
}
