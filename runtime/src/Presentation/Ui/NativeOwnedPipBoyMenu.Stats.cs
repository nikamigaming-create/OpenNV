using System.Globalization;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private readonly Dictionary<FalloutDefaultObject, FalloutFormKey?> _statsAidForms = [];
    private readonly Dictionary<Key, (BaseButton Button, Action Action)> _sourceActionKeys = [];

    private bool HandleSourceActionKey(InputEventKey input)
    {
        var key = input.PhysicalKeycode != Key.None ? input.PhysicalKeycode : input.Keycode;
        if (!_sourceActionKeys.TryGetValue(key, out var binding)) return false;
        if (binding.Button.IsVisibleInTree() && !binding.Button.Disabled) RunAction(binding.Action);
        return true;
    }

    private void BindSourceActionShortcut(System.Xml.Linq.XElement tile, BaseButton button, Action action)
    {
        if (_state.Page is not (FalloutPipBoyPage.Stats or FalloutPipBoyPage.Items)) return;
        var name = (string?)tile.Attribute("name");
        if (name is null) return;
        foreach (var shortcut in _tiles.Root.Elements().Where(element =>
                     element.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal) && element.Value.Trim() == name))
        {
            var sourceKey = shortcut.Name.LocalName["_PCButton_".Length..];
            if (!Enum.TryParse<Key>(sourceKey, ignoreCase: true, out var key))
                throw new NotSupportedException($"Source action shortcut {sourceKey} has no desktop key owner.");
            _sourceActionKeys.Add(key, (button, action));
        }
    }

    private void BindStatsAid()
    {
        var vitals = _vitals();
        foreach (var (name, slot, useful) in new[]
        {
            ("stats_stimpak_button", FalloutDefaultObject.Stimpak, vitals.ExactHitPoints < vitals.MaximumHitPoints),
            ("stats_radx_button", FalloutDefaultObject.RadX, true),
            ("stats_radaway_button", FalloutDefaultObject.RadAway, vitals.RadiationRads > 0),
            ("stats_drbag_button", FalloutDefaultObject.DoctorsBag, vitals.LimbDamage?.Values.Any(damage => damage > 0) == true)
        })
        {
            var tile = Tile(name);
            if (!_statsAidForms.TryGetValue(slot, out var form))
                _statsAidForms.Add(slot, form = FalloutDefaultObjects.Read(_records, slot));
            if (form is null) { _tiles.Bind(tile, "visible", 0); continue; }
            if (_records.GetEffective(form.Value).Signature != "ALCH")
                throw new InvalidDataException("Stats aid default object is not an ingestible.");
            var count = _inventory.Item(form.Value)?.Count ?? 0;
            var label = $"({count.ToString(CultureInfo.InvariantCulture)}) {NameOf(form.Value)}";
            _tiles.Text[tile] = label;
            var enabled = count > 0 && useful && _useAid is not null;
            _tiles.Bind(tile, "target", enabled ? 1 : 0);
            var selected = form.Value;
            Action use = () =>
            {
                (_useAid ?? throw new NotSupportedException("Stats aid has no gameplay owner."))(selected);
                Refresh();
            };
            Target(tile, label, use);
            var button = _buttons[^1].Button;
            button.Disabled = !enabled;

        }
    }
}
