using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedGameplayHud
{
    private FalloutPlayerReferenceHudState? _referenceHud;
    private XElement? _enemyHealth;
    internal void BindStandaloneReferenceHud(FalloutStandalonePlayerSceneState scene)
    {
        if (_referenceHud is not null || !IsInsideTree() || IsQueuedForDeletion() || Error is not null ||
            _scriptUi is null || RuntimeLiveContentSource.Current is not { } content || StandaloneHudStack != scene.Stack ||
            content.StackId != scene.Stack)
            throw new InvalidOperationException("Player HUD binding changed its actual source/native menu lifetime.");
        var source = FalloutExecutableStringTable.ReadPlayerReferenceHud(content.FalloutExecutablePath, scene.Source.Main);
        _referenceHud = new(source, scene.Stack, scene.Process, _scriptUi);
        _enemyHealth = _scriptUi.CopyComponent(_referenceHud.Branch);
        _tiles.Root.Add(_enemyHealth);
        // The same C# tree supplies both the original writer and the native
        // drawing traits. The templates are real owned art/text/meter tiles.
        _tiles.ValidateDrawing(); QueueRedraw();
    }
    internal FalloutPlayerReferenceHudState ReferenceHud(FalloutStandalonePlayerSceneState scene)
    {
        if (!IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() || Error is not null ||
            _referenceHud is not { } hud || hud.Process != scene.Process || hud.Stack != scene.Stack ||
            hud.Source.Main != scene.Source.Main || !ReferenceEquals(hud.Components, _scriptUi))
            throw new NotSupportedException("source-Player-HUD-actual-native-menu-binding-unavailable");
        hud.RequireCurrent(); return hud;
    }
    internal (uint Color, float Bracket, float Meter, float Name) ReferenceFrameProjection(FalloutStandalonePlayerSceneState scene)
    {
        var hud = ReferenceHud(scene);
        var branch = _enemyHealth ?? throw new InvalidOperationException("Actual HUD branch was not published.");
        var paths = new[] { hud.Bracket, hud.Meter, hud.Name };
        var tiles = paths.Select(path => branch.Descendants().Single(tile => NativeOwnedMenuTree.SourcePath(_tiles.Root, tile) == path)).ToArray();
        _tiles.ValidateDrawing();
        return (BitConverter.SingleToUInt32Bits(_tiles.Number(branch, "user0")),
            _tiles.Number(tiles[0], "visible"), _tiles.Number(tiles[1], "visible"), _tiles.Number(tiles[2], "visible"));
    }
}
