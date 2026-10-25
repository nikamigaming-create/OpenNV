using System.Buffers.Binary;
using System.Globalization;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicSoundSettings(bool Enabled, int Master, int Effects)
{
    internal static ClassicSoundSettings Read(string configuration)
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sound = false;
        foreach (var raw in configuration.Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.StartsWith('[')) { sound = line.Equals("[sound]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!sound) continue;
            var pair = line.Split('=', 2);
            if (pair.Length != 2 || pair[0].Trim().ToLowerInvariant() is not ("initialize" or "sounds" or "master_volume" or "sndfx_volume")) continue;
            if (!int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("Classic sound setting is not an integer.");
            values[pair[0].Trim()] = value;
        }
        int Required(string key) => values.TryGetValue(key, out var value) ? value : throw new InvalidDataException("Missing classic sound setting: " + key);
        var master = Required("master_volume"); var effects = Required("sndfx_volume");
        if (master is < 0 or > 32767 || effects is < 0 or > 32767) throw new InvalidDataException("Classic sound volume is out of range.");
        return new(Required("initialize") != 0 && Required("sounds") != 0, master, effects);
    }

    internal float Decibels
    {
        get
        {
            var value = (long)Master * Effects / 32767;
            return (value == 0 ? -10000 : (int)Math.Clamp(-1000 * Math.Log2(32767.0 / value), -10000, 0)) / 100f;
        }
    }
}

internal sealed record ClassicSoundPcm(string Path, byte[] Bytes);
internal sealed record ClassicSoundVoice(long Id, ClassicSoundPcm Pcm, float Decibels);
internal sealed record ClassicSoundBatch(long Revision, IReadOnlyList<ClassicSoundPcm> Sounds, float Decibels);

/// <summary>Source script SFX admission and lifetime. Presentation acknowledges completion explicitly.</summary>
internal sealed class ClassicScriptSounds(string campaign, Func<string, byte[]> read, Func<ClassicSoundSettings> settings)
{
    private readonly Dictionary<long, ClassicSoundVoice> _voices = [];
    private readonly Queue<ClassicSoundVoice> _pending = [];
    private long _revision;
    private long _next;
    internal int ActiveCount => _voices.Count;
    internal ClassicSoundBatch Prepare(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return new(_revision, [], 0);
        if (campaign != "fallout-1") throw new NotSupportedException("This campaign's script sound voice contract is not bound.");
        var options = settings();
        if (!options.Enabled) return new(_revision, [], options.Decibels);
        var accepted = new List<ClassicSoundPcm>();
        foreach (var name in names.Take(Math.Max(0, 4 - ActiveCount)))
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || name.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Classic script sound name is not a relative source basename.");
            var path = "sound/sfx/" + name + ".acm";
            byte[] source;
            try { source = read(path); }
            catch (FileNotFoundException) when (name.StartsWith("MALIEU", StringComparison.Ordinal) || name.StartsWith("MAMTN2", StringComparison.Ordinal))
            { path = "sound/sfx/MAMTNT" + name[6..] + ".acm"; source = read(path); }
            var decoder = new ClassicAcmDecoder(source);
            var bytes = new byte[checked(decoder.Header.Samples * 2)];
            var buffer = new short[4096]; var position = 0;
            for (var count = decoder.Read(buffer); count != 0; count = decoder.Read(buffer))
                for (var index = 0; index < count; index++, position += 2)
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(position), buffer[index]);
            accepted.Add(new(path, bytes));
        }
        return new(_revision, accepted, options.Decibels);
    }

    internal void Commit(ClassicSoundBatch batch)
    {
        if (batch.Revision != _revision || ActiveCount + batch.Sounds.Count > 4)
            throw new InvalidOperationException("Classic sound admission changed before event commit.");
        foreach (var pcm in batch.Sounds)
        {
            var voice = new ClassicSoundVoice(++_next, pcm, batch.Decibels);
            _voices.Add(voice.Id, voice); _pending.Enqueue(voice);
        }
        _revision++;
    }

    internal ClassicSoundVoice[] TakePending()
    {
        var result = _pending.Where(voice => _voices.ContainsKey(voice.Id)).ToArray(); _pending.Clear(); return result;
    }
    internal bool Retire(long id)
    {
        if (!_voices.Remove(id)) return false;
        _revision++; return true;
    }
    internal void Clear() { _voices.Clear(); _pending.Clear(); _revision++; }
}
