using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ObjectPhysicsContracts
{
    internal static void Run()
    {
        var body = new FalloutObjectPhysicsSnapshot(3, new string('a', 64),
            [1, 0, 0, 0, 1, 0, 0, 0, 1, 10, 20, 30], [1, 2, 3], [4, 5, 6], false, 42);
        var reference = new FalloutReferenceSnapshot(new("Physics.esm", 1), new("Physics.esm", 2), new("Physics.esm", 3),
            null, null, new Dictionary<uint, double>(), null, ObjectPhysics: [body]);
        var cold = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(reference))!;
        FalloutReferenceSnapshot.Validate([cold]);
        Require(JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(reference), "Physical snapshot roundtrip changed state.");
        var copied = body.Copy();
        Require(!ReferenceEquals(copied.Transform, body.Transform) && !ReferenceEquals(copied.LinearVelocity, body.LinearVelocity) &&
            !ReferenceEquals(copied.AngularVelocity, body.AngularVelocity), "Physical snapshot aliases mutable vectors.");
        foreach (var invalid in new FalloutObjectPhysicsSnapshot[]
        {
            body with { Body = -1 }, body with { Sha256 = "invalid" }, body with { Transform = [] },
            body with { LinearVelocity = [0, float.NaN, 0] }, body with { AngularVelocity = [0, 0] },
            body with { Transform = new float[12] }, body with { Transform = [-1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0] }
        }) Reject(() => FalloutReferenceSnapshot.Validate([reference with { ObjectPhysics = [invalid] }]));
        Reject(() => FalloutReferenceSnapshot.Validate([reference with { ObjectPhysics = [body, body with { Sha256 = body.Sha256.ToUpperInvariant() }] }]));
        var previous = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(new
        {
            Schema = FalloutNativeCampaignSave.BeforeObjectPhysicsSchema,
            References = new[] { reference with { ObjectPhysics = null } }
        }))!;
        var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous);
        Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema && ReferenceEquals(upgraded.References, previous.References),
            "Previous physical-free schema admission changed reference state.");
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(previous with { References = [reference] }));
        var current = previous with { Schema = FalloutNativeCampaignSave.ExpectedSchema, References = [reference] };
        Require(ReferenceEquals(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(current), current), "Current physical state was replaced.");
        Console.WriteLine("Object physics contracts: transform/velocity JSON, independent copies, malformed state, duplicates, v54 admission and future-owner refusal PASS.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid object physics state was admitted.");
    }
}
