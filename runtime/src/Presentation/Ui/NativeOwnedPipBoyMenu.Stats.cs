using System.Globalization;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private readonly Dictionary<FalloutDefaultObject, FalloutFormKey?> _statsAidForms = [];
    private readonly Dictionary<Key, (BaseButton Button, Action Action)> _statsAidKeys = [];

    private bool HandleStatsAidKey(InputEventKey input)
    {
        var key = input.PhysicalKeycode != Key.None ? input.PhysicalKeycode : input.Keycode;
        if (!_statsAidKeys.TryGetValue(key, out var binding)) return false;
        if (binding.Button.IsVisibleInTree() && !binding.Button.Disabled) RunAction(binding.Action);
        return true;
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
            var shortcut = _tiles.Root.Elements().SingleOrDefault(element =>
                element.Name.LocalName.StartsWith("_PCButton_", StringComparison.Ordinal) && element.Value.Trim() == name);
            if (shortcut is not null)
            {
                var sourceKey = shortcut.Name.LocalName["_PCButton_".Length..];
                if (!Enum.TryParse<Key>(sourceKey, out var key))
                    throw new NotSupportedException($"Stats source shortcut {sourceKey} has no desktop key owner.");
                // Device keys arrive through PushInput, the same path as page
                // navigation. Dispatch here rather than depending on a later
                // viewport shortcut phase, which may not receive that event.
                _statsAidKeys.Add(key, (button, use));
            }
        }
    }
}
