using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class DestructionContracts
{
    internal static void Run()
    {
        var key = new FalloutFormKey("Synthetic.esm", 1);
        var source = new FalloutDestructible(key, new string('a', 64), 100, 1,
            [new(90, 0, 1, 0, 2, null, null, 0, null), new(60, 1, 2, 5, 1, null, null, 0, "meshes/wreck.nif"),
                new(0, 2, 0, 2, 0, null, null, 0, null)]);
        var state = FalloutDestructionState.Initial(source);
        var first = state.Damage(source, 80, key);
        Require(first.State is { Health: 60, Stage: 1, ModelStage: 2, Destroyed: true, Disabled: false } && first.Entered.Length == 2,
            "One shot must enter crossed stages in order and stop at the source cap.");
        Require(first.State.Damage(source, 20, null).State == first.State, "Destroyed objects must reject subsequent external hits.");
        var next = first.State.Damage(source, 10, null, self: true);
        Require(next.State.Health == 50 && next.Entered.Length == 0 && next.State.Attacker == key,
            "Self damage must continue after destruction without replaying stages or losing attribution.");
        var cold = JsonSerializer.Deserialize<FalloutDestructionState>(JsonSerializer.Serialize(next.State))!;
        cold.Validate(source);
        Require(cold == next.State, "Saved destruction fields did not survive serialization.");
        var last = cold.Damage(source, 500, null, self: true);
        Require(last.State is { Health: 0, Stage: 2, ModelStage: 2, Destroyed: true, Disabled: true } && last.Entered.Length == 1,
            "Terminal disable must retain the most recent nonzero model stage.");
        Reject(() => (cold with { SourceSha256 = new string('b', 64) }).Validate(source));
        Reject(() => (cold with { ModelStage = 7 }).Validate(source));
        Reject(() => (cold with { Destroyed = false }).Validate(source));
        Reject(() => cold.Damage(source, float.NaN, null));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException) { return; }
        throw new InvalidDataException("Invalid destruction state was admitted.");
    }
}
