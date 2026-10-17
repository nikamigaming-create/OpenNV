using System.Reflection;
using System.Security.Cryptography;
using System.Globalization;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseImageSpaceClock(FalloutImageSpaceModifier? owned = null)
    {
        var modifier = owned ?? new FalloutImageSpaceModifier(new("Clock.esm", 1), "SyntheticFiniteFade",
            new string('a', 64), true, 10,
            Enumerable.Range(0, 21).Select(_ => Array.Empty<FalloutImageFloatKey>()).ToArray(),
            Enumerable.Range(0, 21).Select(_ => Array.Empty<FalloutImageFloatKey>()).ToArray(),
            new Dictionary<string, FalloutImageFloatKey[]>(), [],
            [new(0, System.Numerics.Vector4.One), new(1, System.Numerics.Vector4.Zero)],
            0, default, false, null, null);
        Require(modifier.Animated && modifier.Duration > 0, "Selected image effect has no finite authored duration.");
        var state = new FalloutImageSpaceState(); state.Apply(modifier);
        var clock = new RuntimeNativeImageSpaceClock(state);
        var failedDriver = new RuntimeNativeOpeningStageDriver();
        const string fault = "synthetic independent appearance failure";
        var paused = GetTree().Paused;
        try
        {
            (typeof(RuntimeNativeOpeningStageDriver).GetField("_executionError",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException("The driver failure fixture has no actual fault owner."))
                .SetValue(failedDriver, fault);
            AddChild(clock); clock.SetProcess(false);
            GetTree().Paused = false;
            failedDriver._Process(modifier.Duration / 2);
            clock._Process(modifier.Duration / 2);
            Require(state.Active.Single().ElapsedSeconds == modifier.Duration / 2 &&
                failedDriver.ExecutionError == fault,
                "An unrelated driver failure froze or erased the shared effect lifetime.");
            GetTree().Paused = true; clock._Process(modifier.Duration * 10);
            Require(state.Active.Single().ElapsedSeconds == modifier.Duration / 2,
                "Paused presentation advanced the authoritative finite effect.");
            GetTree().Paused = false; clock._Process(modifier.Duration / 2);
            Require(state.Active.Count == 0 && failedDriver.ExecutionError == fault,
                "The finite source effect did not expire at its authored threshold or erased the real driver fault.");
            state.Apply(modifier with { Animated = false });
            clock._Process(modifier.Duration * 10);
            Require(state.Active.Count == 1, "The independent clock invented a static effect lifetime.");
            GD.Print($"OPENNV_NATIVE_IMAGE_SPACE_CLOCK_PASS source={modifier.Form} duration={modifier.Duration:R} " +
                "independentDriverFault=true exactDuration=true pauseRetained=true staticRetained=true retailPixels=unverified");
        }
        finally { GetTree().Paused = paused; clock.Free(); failedDriver.Free(); }
    }

    private void ExerciseOwnedImageSpaceClock(string game, string mod, string root, string form, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var parts = form.Split(':');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !uint.TryParse(parts[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) ||
            id is 0 or > FalloutFormKey.ObjectIdMask)
            throw new ArgumentException("Owned image-space clock requires plugin:hex source identity.", nameof(form));
        var record = records.GetEffective(new(parts[0], id));
        var before = SHA256.HashData(record.ReadData());
        ExerciseImageSpaceClock(FalloutImageSpaceModifierReader.Read(record));
        Require(before.SequenceEqual(SHA256.HashData(record.ReadData())), "Owned image-space clock proof changed source bytes.");
    }
}
