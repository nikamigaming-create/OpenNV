using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutControlBinding([property: JsonRequired] byte Keyboard, [property: JsonRequired] byte Mouse);

// Control settings belong to the selected user profile, rather than a campaign
// snapshot. The source INIs are read-only; a separate overlay survives sessions.
internal sealed class FalloutInputControls(Func<string, string> source, string? profilePath = null, bool newVegas = true)
{
    private FalloutControlBinding[]? _bindings;
    private bool _dirty;
    internal long Revision { get; private set; }
    internal event Action? Changed;
    internal const int Count = 28;
    private string[] Names => ["Forward", "Back", "Slide Left", "Slide Right", "Use", "Activate", "Block",
        "Ready Item", "Crouch/Sneak", "Run", "Always Run", "Auto Move", "Jump", "Toggle POV", "Menu Mode",
        "Rest", "Vats", "Hotkey1", newVegas ? "Ammo Swap" : "Hotkey2", "Hotkey3", "Hotkey4", "Hotkey5",
        "Hotkey6", "Hotkey7", "Hotkey8", "QuickSave", "QuickLoad", "Grab"];

    internal IReadOnlyList<FalloutControlBinding> Bindings => Array.AsReadOnly(Load());
    internal object State => new
    {
        initialized = _bindings is not null,
        Revision,
        dirty = _dirty,
        bindings = _bindings?.Select((value, control) => new { control, keyboard = Code(value.Keyboard, 0), mouse = Code(value.Mouse, 1) }).ToArray(),
        persistence = "user-profile-overlay",
        unbound = "joystick,gamepad,executable-default-bindings"
    };

    internal int Get(uint control, uint type = 0)
    {
        ValidateType(type);
        if (control >= Count) return -1;
        var value = Load()[control];
        return Code(type == 0 ? value.Keyboard : value.Mouse, type);
    }

    internal void Set(uint control, uint key, uint type = 0)
    {
        ValidateType(type);
        if (control >= Count) return;
        // xNVSE accepts both raw mouse bytes and the published 256-based codes.
        var scan = key >= 256 ? key - 256 : key;
        if (scan > byte.MaxValue) throw new InvalidDataException("Control binding is outside the byte-sized input domain.");
        var previous = Load(); var next = previous.ToArray();
        var old = type == 0 ? previous[control].Keyboard : previous[control].Mouse;
        for (var index = 0; index < Count; index++)
            if ((type == 0 ? next[index].Keyboard : next[index].Mouse) == scan)
            {
                next[index] = With(next[index], old, type); break;
            }
        next[control] = With(next[control], (byte)scan, type);
        if (next.SequenceEqual(previous)) return;
        _bindings = next; Revision++;
        try { Changed?.Invoke(); }
        catch { _bindings = previous; Revision--; throw; }
        _dirty = true;
    }

    internal void Flush()
    {
        if (!_dirty) return;
        if (profilePath is null) throw new NotSupportedException("Control changes have no profile persistence owner.");
        var path = Path.GetFullPath(profilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Persisted("opennv-input-controls/v1", newVegas, Load())));
            File.Move(temporary, path, true);
            _dirty = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private FalloutControlBinding[] Load()
    {
        if (_bindings is not null) return _bindings;
        if (profilePath is not null && File.Exists(profilePath))
        {
            var saved = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(profilePath),
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ??
                throw new InvalidDataException("Control profile is empty.");
            if (saved.Schema != "opennv-input-controls/v1" || saved.NewVegas != newVegas || saved.Bindings?.Length != Count)
                throw new InvalidDataException("Control profile identity or binding table is invalid.");
            return _bindings = saved.Bindings;
        }
        // The eight-hex-digit Controls value contains a keyboard byte followed
        // by its mouse byte. Other device fields are not interpreted here.
        var rows = Names.Select(name =>
        {
            var text = source(name).Trim();
            if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word))
                throw new InvalidDataException($"Owned control binding is malformed: {name}.");
            return new FalloutControlBinding((byte)((word >> 16) & 255), (byte)((word >> 8) & 255));
        }).ToArray();
        return _bindings = rows;
    }

    private static FalloutControlBinding With(FalloutControlBinding value, byte scan, uint type) =>
        type == 0 ? value with { Keyboard = scan } : value with { Mouse = scan };
    private static int Code(byte scan, uint type) => scan == byte.MaxValue ? -1 : scan + (type == 1 ? 256 : 0);
    private static void ValidateType(uint type)
    {
        if (type is 2 or 3) throw new NotSupportedException("Joystick/gamepad control bindings have no admitted input adapter.");
        if (type > 3) throw new InvalidDataException("Control device type is invalid.");
    }
    internal static uint Index(double value) => double.IsFinite(value) && value >= 0 && value <= uint.MaxValue && value == Math.Truncate(value)
        ? (uint)value : throw new InvalidDataException("Control arguments require unsigned integers.");
    private sealed record Persisted([property: JsonRequired] string Schema, [property: JsonRequired] bool NewVegas,
        [property: JsonRequired] FalloutControlBinding[] Bindings);
}
