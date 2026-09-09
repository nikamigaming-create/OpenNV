using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Owned idle/walk frame clock and source horizontal displacement, without renderer state.</summary>
internal sealed class ClassicPlayerAnimation
{
    private readonly Fallout1NativeFrmFrame[][] _idle, _walk;
    private double _seconds;
    private bool _moving;
    internal string IdlePath { get; }
    internal string WalkPath { get; }
    internal int FrameIndex { get; private set; }
    internal int Rotation { get; private set; }
    internal bool Moving => _moving;
    internal Fallout1NativeFrmFrame Frame => (_moving ? _walk : _idle)[Rotation][FrameIndex];
    internal double Phase => (FrameIndex + _seconds * Frame.StoredFps) / Frame.FramesPerDirection;

    internal ClassicPlayerAnimation(Func<string, byte[]> read, bool female, string campaign = "fallout-1", ClassicInventory? inventory = null)
    {
        var prefix = campaign switch
        {
            "fallout-1" => female ? "hfjmps" : "hmjmps",
            "fallout-2" => female ? "hfprim" : "hmwarr",
            _ => throw new InvalidDataException("Unknown classic character animation campaign."),
        };
        if (inventory?.Worn?.Definition.Armor is { } armor)
        {
            var entries = System.Text.Encoding.ASCII.GetString(read("art/critters/critters.lst")).Replace("\r", "", StringComparison.Ordinal).Split('\n');
            var index = female ? armor.FemaleArt : armor.MaleArt;
            if (index < 0 || index >= entries.Length) throw new NotSupportedException("Armor has no source art for this character.");
            prefix = entries[index].Split(',')[0].Trim();
        }
        const string weapons = "adefghijklm";
        var weapon = inventory?.Held?.Definition.Weapon?.Animation ?? 0;
        if (weapon < 0 || weapon >= weapons.Length) throw new NotSupportedException("Held weapon has an unsupported source animation code.");
        IdlePath = $"art/critters/{prefix}{weapons[weapon]}a.frm"; WalkPath = $"art/critters/{prefix}{weapons[weapon]}b.frm";
        Fallout1NativeFrmFrame[][] Load(string path)
        {
            var bytes = read(path); var header = Fallout1NativeFrmReader.ReadFirstFrame(bytes);
            if (header.StoredFps == 0) throw new InvalidDataException("Player animation requires an authored frame rate.");
            return Enumerable.Range(0, 6).Select(rotation => Enumerable.Range(0, header.FramesPerDirection)
                .Select(index => Fallout1NativeFrmReader.ReadFrame(bytes, rotation, index)).ToArray()).ToArray();
        }
        _idle = Load(IdlePath); _walk = Load(WalkPath);
        foreach (var frames in _walk)
            if (frames.Sum(frame => Math.Abs((int)frame.FrameX)) == 0) throw new InvalidDataException("Walk art has no source displacement.");
    }

    internal void Tick(ClassicPlayerSession player, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_moving != player.Moving) { _moving = player.Moving; FrameIndex = 0; _seconds = 0; }
        Rotation = player.Rotation; _seconds += seconds;
        while (_seconds >= 1.0 / Frame.StoredFps)
        {
            _seconds -= 1.0 / Frame.StoredFps;
            if (_moving)
            {
                // Classic screen-space hex offsets are (+/-16,+/-12) or
                // (+/-32,0). Vertical offsets also contain the authored gait.
                // Preserve the horizontal source clock; retail edge timing and
                // the camera-relative 2D gait still require matched observation.
                var pixelsPerHex = Rotation is 1 or 4 ? 32.0 : 16.0;
                player.Advance(Math.Abs((int)Frame.FrameX) / pixelsPerHex);
                Rotation = player.Rotation;
            }
            if (_moving != player.Moving) { _moving = player.Moving; FrameIndex = 0; _seconds = 0; }
            else FrameIndex = (FrameIndex + 1) % Frame.FramesPerDirection;
        }
    }
}
