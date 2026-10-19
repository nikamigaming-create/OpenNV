using System.Buffers.Binary;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicScriptSoundsProbe
{
    internal static void Run()
    {
        var settings = ClassicSoundSettings.Read("[sound]\nINITIALIZE=1\nsounds=1\nmaster_volume=32767\nsndfx_volume=16384");
        if (settings.Decibels != -9.99f || (settings with { Master = 0 }).Decibels != -100)
            throw new Exception("Source script sound volume changed.");
        var source = new byte[14]; source[0] = 0x97; source[1] = 0x28; source[2] = 3; source[3] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(10), 11025);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(12), 3 << 4);
        var reads = new List<string>();
        byte[] Read(string path) { reads.Add(path); return path.EndsWith("bad.acm", StringComparison.Ordinal) ? throw new FileNotFoundException(path) : source; }
        var owner = new ClassicScriptSounds("fallout-1", Read, () => settings);
        try { owner.Prepare(["a", "bad"]); throw new Exception("Invalid batch accepted."); } catch (FileNotFoundException) { }
        if (owner.ActiveCount != 0 || owner.TakePending().Length != 0) throw new Exception("Failed event emitted partial audio.");
        var prepared = owner.Prepare(["a", "b", "c", "d", "bad"]);
        if (prepared.Sounds.Count != 4 || prepared.Sounds.Any(pcm => pcm.Bytes.Length != 6)) throw new Exception("Source admission or scalar PCM changed.");
        owner.Commit(prepared);
        var pending = owner.TakePending();
        if (pending.Length != 4 || owner.TakePending().Length != 0 || owner.ActiveCount != 4) throw new Exception("Pending voices lost or duplicated.");
        var before = reads.Count; owner.Commit(owner.Prepare(["bad"]));
        if (reads.Count != before) throw new Exception("Saturated source sound attempted a load.");
        if (!owner.Retire(pending[0].Id) || owner.Retire(pending[0].Id) || owner.ActiveCount != 3) throw new Exception("Voice retired twice.");
        var stale = owner.Prepare(["e"]); owner.Retire(pending[1].Id);
        try { owner.Commit(stale); throw new Exception("Stale sound batch committed."); } catch (InvalidOperationException) { }
        owner.Clear();
        if (owner.ActiveCount != 0 || owner.TakePending().Length != 0) throw new Exception("Shutdown retained sound requests.");
        settings = settings with { Enabled = false }; before = reads.Count;
        owner.Commit(owner.Prepare(["bad"]));
        if (owner.ActiveCount != 0 || reads.Count != before) throw new Exception("Disabled source sound attempted playback.");
        var fallback = new ClassicScriptSounds("fallout-1", path => path.Contains("MALIEU", StringComparison.Ordinal) ? throw new FileNotFoundException(path) : source,
            () => settings with { Enabled = true });
        if (fallback.Prepare(["MALIEUAA"]).Sounds.Single().Path != "sound/sfx/MAMTNTAA.acm") throw new Exception("Source sound fallback changed.");
        Console.WriteLine("OPENNV_CLASSIC_SCRIPT_SOUND_PASS atomic=true capacity=true disabled=true retirement=true scalarPcm=true volume=true fixture=true gameplay=false");
    }
}
