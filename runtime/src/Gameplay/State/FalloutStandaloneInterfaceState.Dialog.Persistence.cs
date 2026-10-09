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
        var slots = new uint[10]; uint mode = 1; long previous = 0;
        foreach (var write in menus.StackWrites)
        {
            var row = menus.Dialogs.SingleOrDefault(item => item.Object == write.Dialog);
            if (row is null || write.Before is not { Count: 10 } || write.After is not { Count: 10 } ||
                write.Changed <= previous || write.Changed > saved.Sequence || !write.Before.SequenceEqual(slots) || write.BeforeMode != mode ||
                write.Slot is < -1 or > 9 || write.Failure is not null && string.IsNullOrWhiteSpace(write.Failure))
                throw new InvalidDataException("Source menu stack omitted or reordered a committed operation.");
            var slot = Array.FindIndex(slots, value => value == 0);
            if (slot != write.Slot || row.MenuIdentity != FalloutStandaloneDialogSource.MenuId)
                throw new InvalidDataException("Source Dialog stack write changed the first-empty/exhausted operand.");
            if (slot >= 0) { slots[slot] = row.MenuIdentity; if (slot == 0) mode = 3; }
            var scene = slot == 0 && row.MenuIdentity != 1001 || slot == 1 && slots[0] == 1001;
            if (!write.After.SequenceEqual(slots) || write.AfterMode != mode || write.RequiresSceneByte != scene ||
                !scene && (!write.Returned || write.Failure is not null) || write.Failure is not null && write.Returned ||
                !write.Returned && write.Failure is null)
                throw new InvalidDataException("Menu source native child return changed the real stack/mode prefix.");
            previous = write.Changed;
        }
        if (!saved.MenuSlots.SequenceEqual(slots) || saved.Mode != mode || saved.ActiveMenus.Any(value => value != 0))
            throw new InvalidDataException("Interface mode/slots/active bytes have no matching original writer.");
        // Allocation/TLS and the original interface clock have not returned in
        // this packet. An empty constructor queue does not claim inspection of
        // those independently implemented value-transport algorithms.
        if (menus.Transitions.Count != 0)
            throw new NotSupportedException("Original menu transition allocation/TLS/current-frame producer is unowned.");
    }
    private void RestoreDialogMenus(FalloutStandaloneMenusSnapshot menus)
    {
        _dialogMenus.AddRange(menus.Dialogs); _menuWrites.AddRange(menus.StackWrites);
        _menuTransitions.AddRange(menus.Transitions);
        // Historical source control identities do not become current native
        // roots. A new menu requires its actual new returned factory/closure.
    }
    private string? DialogSaveBlocker => _dialogInvocation is not null ? "source-Dialog-original-child-entered" :
        _dialogMenus.Any(row => row.Process == _process && row.Retired is null) ? "source-Dialog-native-lifetime-active" :
        _slots.Any(value => value != 0) || _mode != 1 || _menuTransitions.Count != 0 ? "source-menu-original-stack-transition-unreturned" : null;
}
