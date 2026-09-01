using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed class Fo3SpecialBookMenuRuntime
{
    private readonly Fo3Cg01Stage20Interaction _source;
    private readonly Fo3Cg01ToddlerWorldContract _input;
    private readonly Action<IReadOnlyList<int>> _accepted;
    private readonly Action<IReadOnlyList<int>> _changed;
    private readonly int[] _values;
    private Node3D? _book;
    private Fo3Cg01ToddlerPlayer? _player;
    private int _selectedIndex;

    internal Fo3SpecialBookMenuRuntime(
        Fo3Cg01Stage20Interaction source,
        Fo3Cg01ToddlerWorldContract input,
        IReadOnlyList<int> values,
        Action<IReadOnlyList<int>> changed,
        Action<IReadOnlyList<int>> accepted)
    {
        if (values.Count != source.ActorValues.Count)
            throw new InvalidOperationException("Fallout 3 SPECIAL menu value coverage differs.");
        _source = source;
        _input = input;
        _changed = changed;
        _accepted = accepted;
        _values = values.ToArray();
        ValidateAllocation(requireComplete: false);
    }

    internal void Open(Node3D book, Fo3Cg01ToddlerPlayer player)
    {
        if (_book is not null)
            throw new InvalidOperationException("Fallout 3 SPECIAL menu is already open.");
        _book = book;
        _player = player;
        player.SetMenuInputHandler(HandleInput);
        PublishSourcePage();
    }

    private bool HandleInput(InputEvent inputEvent)
    {
        if (!inputEvent.IsPressed() || inputEvent.IsEcho())
            return false;
        if (inputEvent.IsAction(_input.MoveForwardAction))
            return Execute("index_up");
        if (inputEvent.IsAction(_input.MoveBackwardAction))
            return Execute("index_down");
        if (inputEvent.IsAction(_input.MoveRightAction))
            return Execute("increase_value");
        if (inputEvent.IsAction(_input.MoveLeftAction))
            return Execute("decrease_value");
        if (inputEvent.IsAction(_input.ActivateAction))
            return Execute("exit_menu");
        return false;
    }

    private bool Execute(string tile)
    {
        if (_source.Tiles.Bindings.Count(binding => binding.Tile == tile) != 1 ||
            _source.Tiles.Controls.Count(control => control.Tile == tile) != 1)
            throw new InvalidOperationException(
                $"Fallout 3 SPECIAL source control is unavailable: {tile}");
        switch (tile)
        {
            case "index_up":
                _selectedIndex = (_selectedIndex + _values.Length - 1) % _values.Length;
                break;
            case "index_down":
                _selectedIndex = (_selectedIndex + 1) % _values.Length;
                break;
            case "increase_value":
                if (_values.Sum() < _source.MenuPoints &&
                    _values[_selectedIndex] < _source.ActorValues[_selectedIndex].MaximumValue)
                    _values[_selectedIndex]++;
                break;
            case "decrease_value":
                if (_values[_selectedIndex] > _source.ActorValues[_selectedIndex].MinimumValue)
                    _values[_selectedIndex]--;
                break;
            case "exit_menu":
                ValidateAllocation(requireComplete: true);
                Close();
                _accepted(_values.ToArray());
                return true;
            default:
                throw new InvalidOperationException(
                    $"Fallout 3 SPECIAL source control is unsupported: {tile}");
        }
        ValidateAllocation(requireComplete: false);
        _changed(_values.ToArray());
        PublishSourcePage();
        return true;
    }

    private void PublishSourcePage()
    {
        var book = _book ?? throw new InvalidOperationException(
            "Fallout 3 SPECIAL source book is absent.");
        var value = _source.ActorValues[_selectedIndex];
        book.SetMeta("opennv_special_menu_document", _source.Tiles.Document);
        book.SetMeta("opennv_special_menu_document_sha256", _source.Tiles.DocumentSha256);
        book.SetMeta("opennv_special_selected_form_id", value.FormId);
        book.SetMeta("opennv_special_selected_label", value.Label);
        book.SetMeta("opennv_special_selected_description", value.Description);
        book.SetMeta("opennv_special_selected_value", _values[_selectedIndex]);
        book.SetMeta("opennv_special_remaining_points", _source.MenuPoints - _values.Sum());
    }

    private void ValidateAllocation(bool requireComplete)
    {
        if (_values.Select((value, index) =>
                value < _source.ActorValues[index].MinimumValue ||
                value > _source.ActorValues[index].MaximumValue).Any(invalid => invalid) ||
            _values.Sum() > _source.MenuPoints ||
            requireComplete && _values.Sum() != _source.MenuPoints)
            throw new InvalidOperationException("Fallout 3 SPECIAL allocation is invalid.");
    }

    private void Close()
    {
        (_player ?? throw new InvalidOperationException(
            "Fallout 3 SPECIAL menu player is absent.")).SetMenuInputHandler(null);
        _player = null;
        _book = null;
    }
}
