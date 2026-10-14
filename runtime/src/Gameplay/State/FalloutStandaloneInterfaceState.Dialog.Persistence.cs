using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    private FalloutStandaloneMenusSnapshot CaptureDialogMenus() => new(_dialogMenus.ToArray(), _menuWrites.ToArray(),
        _menuTransitions.ToArray(), _selectedMenu, _selectedTile);
    private static void ValidateDialogMenus(FalloutStandaloneInterfaceSnapshot saved)
    {
        var menus = saved.Menus;
        if (menus is null || menus.Dialogs is null || menus.StackWrites is null || menus.Transitions is null ||
            menus.Dialogs.Any(row => row is null) || menus.StackWrites.Any(row => row is null) || menus.Dialogs.Select(row => row.Object).Distinct().Count() != menus.Dialogs.Count ||
            menus.SelectedMenu is not null || menus.SelectedTile is not null)
            throw new InvalidDataException("Source menu constructor/selection ownership is missing or unowned.");
        foreach (var row in menus.Dialogs)
        {
            FalloutStandaloneDialogSource.Validate(saved.Source, saved.Stack, row.Declaration);
            var owner = saved.Objects.SingleOrDefault(item => item.Identity == row.Object);
            if (owner is null || owner.Process != row.Process || owner.Role != FalloutStandaloneInterfaceObjectRole.DialogMenu ||
                row.Constructed <= owner.Constructed || row.Changed < row.Constructed || row.Changed > saved.Sequence ||
                row.Step is not (FalloutStandaloneDialogStep.Constructed or FalloutStandaloneDialogStep.RootStored or
                    FalloutStandaloneDialogStep.MenuIdentityStored or FalloutStandaloneDialogStep.PushCommitted or FalloutStandaloneDialogStep.SceneByteEntered or
                    FalloutStandaloneDialogStep.SelectionCleared or FalloutStandaloneDialogStep.ControlsRequired or FalloutStandaloneDialogStep.SpeakerStored or
                    FalloutStandaloneDialogStep.ActorPreparationEntered or FalloutStandaloneDialogStep.Retired) ||
                (row.Step == FalloutStandaloneDialogStep.Retired) != (row.Retired is not null) || row.Flags != 0x100 || row.Lifecycle != 4 ||
                row.Controls is not { Count: 4 } || row.ReturnedTiles is null ||
                row.ReturnedTiles.Any(tile => tile is null || tile.Identity == Guid.Empty) ||
                row.ReturnedTiles.Select(tile => tile.Identity).Distinct().Count() != row.ReturnedTiles.Count ||
                row.ReturnedTiles.Count != row.Declaration.Controls.Count ||
                !row.ReturnedTiles.Select(tile => (tile.CallbackOrdinal, tile.Id, tile.Path, tile.Kind))
                    .SequenceEqual(row.Declaration.Controls.Select(tile => (tile.Ordinal, tile.Id, tile.Path, tile.Kind))) ||
                row.Declaration.Resources[0].Sha256 != owner.Native.ResourceSha256 ||
                row.Root is { } root && root == Guid.Empty || row.RootVisibleBits is not null ||
                row.Boundary is not null && string.IsNullOrWhiteSpace(row.Boundary) ||
                row.Failure is not null && string.IsNullOrWhiteSpace(row.Failure) ||
                row.Retired is { } end && (end < row.Constructed || end > saved.Sequence || owner.Retired is null || end > owner.Retired))
                throw new InvalidDataException("Dialog current state changed its actual class/control/native/source prefix.");
            for (var control = 0; control < 4; ++control)
                if (row.Controls[control] != row.ReturnedTiles.LastOrDefault(tile => tile.Id == (uint)control)?.Identity)
                    throw new InvalidDataException("Original Dialog control callback overwriting order changed.");
            if (row.Step != FalloutStandaloneDialogStep.Constructed && row.Root is null && row.Retired is null ||
                row.Root is null && row.MenuIdentity != 0 ||
                row.MenuIdentity != 0 && (!FalloutStandaloneDialogSource.Registers(row.Declaration.StackingBits) || row.MenuIdentity != FalloutStandaloneDialogSource.MenuId) ||
                row.Speaker is { } speaker && (speaker.ObjectId == 0 || string.IsNullOrWhiteSpace(speaker.OwnerPlugin) ||
                    row.Step is not (FalloutStandaloneDialogStep.SpeakerStored or FalloutStandaloneDialogStep.ActorPreparationEntered or FalloutStandaloneDialogStep.Retired)) ||
                row.Retired is null && row.Step != FalloutStandaloneDialogStep.Constructed && row.Failure is null ||
                row.Failure is not null && saved.Failure is null || row.Boundary is not null && saved.Boundary is null)
                throw new InvalidDataException("Source Dialog declared a returned tail without its genuine consumers.");
        }
        ValidateCombinedMenuStack(saved);
        // Source HUD transitions share this actual queue. Their independent
        // allocator/TLS and current clock are validated by the HUD owner and
        // the retained source menu-factory graph.
    }
    private void RestoreDialogMenus(FalloutStandaloneMenusSnapshot menus)
    {
        _dialogMenus.AddRange(menus.Dialogs); _menuWrites.AddRange(menus.StackWrites);
        foreach (var transition in menus.Transitions) _menuTransitions.AddLast(transition);
        // Historical source control identities do not become current native
        // roots. A new menu requires its actual new returned factory/closure.
    }
    private string? DialogSaveBlocker => _dialogInvocation is not null ? "source-Dialog-original-child-entered" :
        _dialogMenus.Any(row => row.Process == _process && row.Retired is null) ? "source-Dialog-native-lifetime-active" :
        HasCurrentSourcePauseMenu ? null :
        _slots.Any(value => value != 0) || _mode != 1 || _menuTransitions.Count != 0 ? "source-menu-original-stack-transition-unreturned" : null;
}
