using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class ClassicScriptSoundAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            using var owned = arguments.Length == 2 ? Fallout1OwnedContentSource.LoadInstall(arguments[0]) : null;
            var fixture = new byte[14]; fixture[0] = 0x97; fixture[1] = 0x28; fixture[2] = 3; fixture[3] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(fixture.AsSpan(4), 2205);
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.AsSpan(10), 22050);
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.AsSpan(12), 2205 << 4);
            var settings = owned is null ? new ClassicSoundSettings(true, 32767, 32767)
                : ClassicSoundSettings.Read(File.ReadAllText(Path.Combine(owned.InstallRoot, "fallout.cfg")));
            if (!settings.Enabled) throw new InvalidDataException("Owned sound is disabled; this audit cannot claim playback.");
            var owner = new ClassicScriptSounds("fallout-1", path => owned is null ? fixture : owned.Read(path, out _), () => settings);
            var name = owned is null ? "synthetic" : arguments[1];
            var adapter = new ClassicScriptSoundPlayback(); adapter.Configure(owner); AddChild(adapter);
            string? failure = null; adapter.Failed += message => failure = message;
            var batch = owner.Prepare([name, name, name, name, name]);
            owner.Commit(batch); adapter._Process(0);
            if (owner.ActiveCount != 4 || adapter.PlayingCount != 4 || failure is not null)
                throw new InvalidDataException("Script sound voices did not reach the actual mixer: " + failure);
            var seconds = batch.Sounds.Max(row => row.Bytes.Length / 2.0 / 22050);
            var until = Time.GetTicksMsec() + (ulong)((seconds + 3) * 1000);
            while (owner.ActiveCount != 0 && Time.GetTicksMsec() < until)
                await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            if (owner.ActiveCount != 0 || adapter.PlayingCount != 0 || failure is not null)
                throw new InvalidDataException("Finite script sound did not retire after mixer completion: " + failure);
            owner.Commit(owner.Prepare([name])); adapter._Process(0);
            RemoveChild(adapter); adapter.Free();
            if (owner.ActiveCount != 0 || owner.TakePending().Length != 0)
                throw new InvalidDataException("Script sound survived presentation shutdown.");
            // Stop releases its playback on the audio thread, after the fade buffer.
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"OPENNV_CLASSIC_SCRIPT_SOUND_MIXER_PASS owned={owned is not null} capacity=true finite=true shutdown=true fixture=true gameplay=false");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
