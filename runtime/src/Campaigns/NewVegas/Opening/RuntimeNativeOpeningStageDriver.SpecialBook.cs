using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeSpecialAllocationEntry? _specialBookEntry;
    internal object? SpecialBookState => _specialBookEntry?.State;

    private void OpenSpecialBookMenu(int budget)
    {
        if (BlockingExecutionError is not null || _specialBookEntry is not null || _nameEntry is not null ||
            _raceSexEntry is not null || _vigorEntry is not null || _tagSkillEntry is not null ||
            _traitEntry is not null || _recipeMenu is not null || _barterMenu is not null || _levelUpEntry is not null)
            throw new InvalidOperationException("SPECIAL book cannot open while another menu owns input or its driver has failed.");

        var entry = new RuntimeNativeSpecialAllocationEntry();
        _specialBookEntry = entry;
        AddChild(entry);
        entry.Accepted += () =>
        {
            // The source activation already ran its stage prefix. Done only
            // closes the book; its writes are already in the shared player.
            GD.Print("OPENNV_NATIVE_SPECIAL_BOOK_ACCEPTED owner=engine-player-special-pools stageCompletion=source-script");
            RetireSpecialBook(entry);
        };
        entry.Failed += error =>
        {
            ExecutionError = BlockingExecutionError ?? error.Message;
            GD.PushError($"OPENNV_NATIVE_SPECIAL_BOOK_DIVERGENCE: {error.Message}");
        };
        entry.TreeExiting += () =>
        {
            if (ReferenceEquals(_specialBookEntry, entry)) _specialBookEntry = null;
        };
        entry.Configure((accept, fail) =>
        {
            var menu = new NativeOwnedSpecialBookMenu(_pluginStack, SpecialAllocationBinding, budget, accept, fail);
            return new(menu, () => menu.State);
        }, () => _player.ModalInput, _player.SetModalInput);
        if (entry.Error is not null) throw new NotSupportedException(entry.Error);
        GD.Print($"OPENNV_NATIVE_SPECIAL_BOOK_OPEN menu={FalloutSpecialBookPresentation.MenuId} budget={budget} " +
            $"source={NativeOwnedSpecialBookMenu.MenuPath} owner=engine-player-special-pools parity=unverified");
    }

    private void RetireSpecialBook(RuntimeNativeSpecialAllocationEntry entry)
    {
        if (!ReferenceEquals(_specialBookEntry, entry)) return;
        _specialBookEntry = null;
        entry.ReleasePause();
        entry.QueueFree();
    }
}
