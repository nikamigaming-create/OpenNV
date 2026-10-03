using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ActorFaceAnimationProbe
{
    internal static void Run()
    {
        var samples = 0;
        var blink = new FalloutFaceBlink(new(0.2f, 0.4f, 1, 3, 0.25f), () => { samples++; return 0.5f; });
        blink.Advance(2, 0);
        Require(blink.DelaySeconds == 2 && blink.Weight == 0 && blink.PendingTargets == 2, "Blink did not respect its randomized source delay.");
        blink.Advance(0.1, 0);
        Require(Math.Abs(blink.Weight - 0.5f) < 0.000001, "Blink close did not use its source duration.");
        blink.Advance(0.05, 0);
        Require(Math.Abs(blink.Weight - 0.875f) < 0.000001, "FaceGen queue replaced incremental blending with an absolute curve.");
        var saved = JsonSerializer.Deserialize<FalloutFaceBlinkSnapshot>(JsonSerializer.Serialize(blink.Capture()))!;
        var restored = new FalloutFaceBlink(blink.Settings, () => throw new InvalidOperationException("Cold blink redrew its active queue."));
        restored.Restore(saved);
        Require(JsonSerializer.Serialize(restored.Capture()) == JsonSerializer.Serialize(saved), "Cold blink lost its partial close queue.");
        foreach (var step in new[] { 0.0, 0.03, 0.02 })
        {
            blink.Advance(step, 0); restored.Advance(step, 0);
            Require(JsonSerializer.Serialize(restored.Capture()) == JsonSerializer.Serialize(blink.Capture()),
                "Cold blink changed incremental blending or consumed new randomness.");
        }
        var before = JsonSerializer.Serialize(restored.Capture());
        foreach (var invalidState in new[] { saved with { ElapsedSeconds = -1 }, saved with { Weight = 2 },
            saved with { Targets = [new(1, 99)] }, saved with { Settings = saved.Settings with { DownSeconds = 0.3f } } })
        {
            var failed = false;
            try { restored.Restore(invalidState); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { failed = true; }
            Require(failed && JsonSerializer.Serialize(restored.Capture()) == before, "Invalid cold blink partially changed its live queue.");
        }
        blink.Advance(0.45, 1);
        Require(blink.Weight == 0 && blink.PendingTargets == 0 && samples == 1, "An active blink lost elapsed time or restarted in the same publication.");
        blink.Advance(10, 0.25f);
        Require(blink.PendingTargets == 0 && samples == 1, "Look-down suppression created another blink.");
        blink.Advance(0, 0);
        Require(blink.PendingTargets == 3 && samples == 2, "Blink did not resume after look-down suppression.");
        var disabled = new FalloutFaceBlink(new(0, 1, 1, 2, 1), () => throw new InvalidOperationException("Disabled blink consumed RNG."));
        disabled.Advance(5, 0);
        Require(disabled.PendingTargets == 0 && disabled.Weight == 0, "Nonpositive source timing failed to disable new blinks.");
        var invalid = new FalloutFaceBlink(new(1, 1, 1, 2, 1), () => -0.1f);
        var rejected = false;
        try { invalid.Advance(0, 0); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "An invalid random sample silently changed blink timing.");
        var activity = new FalloutActorActivityState();
        Require(!activity.Alerted && !activity.Attacked && !activity.WeaponDrawn, "Fresh actor activity is not clear.");
        activity.SetAlerted(true); activity.RecordAttack(); activity.SetWeaponDrawn(true);
        Require(activity.Alerted && activity.Attacked && activity.WeaponDrawn && activity.Revision == 3, "Mutable actor activity did not publish changes.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
