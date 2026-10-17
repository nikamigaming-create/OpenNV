using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunScenePointLightContracts()
    {
        var reference = new FalloutPlacedReference(new("Lights.esm", 0x101), "OwnedPoint", new("Lights.esm", 1),
            new("Lights.esm", 2), 0, [0, 0, 0], [0, 0, 0], 1, null, null, null, false);
        var parameters = new FalloutPlacedLight(reference.FormKey, reference.Base, 10, [255, 128, 64], 2, [.25f, .5f, .75f], null, 0);
        var light = new FalloutSourcePointLight(reference, parameters);
        var scene = Task.Run(() => new FalloutSceneLightCollection()).GetAwaiter().GetResult(); var enabled = true;
        RunLightBoundNameLookup();
        var publication = new object();
        Reject(() => scene.Sum([0, 0, 0]));
        scene.PublishThread(publication); scene.PublishThread(publication);
        Reject(() => scene.PublishThread(new object()));
        var placement = new FalloutReferencePlacement(reference.Cell, [0, 0, 0], [0, 0, 0]);
        var membership = scene.Attach(light, () => placement, () => enabled);
        try
        {
            Reject(() => scene.Attach(light, () => placement, () => enabled));
            var first = scene.Sum([0, 0, 0]);
            Require(first.Returned && first.Lights is [{ Wrapper: var wrapper, Contribution: 0x3fc00000, Stores: 1 }] &&
                wrapper == light.Identity && first.SumBits == 0x3fc00000 && light.LastContribution == 1.5f,
                "Scene light did not consume its current C# fields or store the same real wrapper's contribution.");
            placement = placement with { Position = [10, 0, 0] };
            Require(scene.Sum([0, 0, 0]).SumBits == 0 && light.LastContribution == 0,
                "Scene light ignored its current source placement/radius.");
            var stores = light.ContributionStores; enabled = false;
            light.ConfigureColor(() => throw new InvalidOperationException("Disabled light sampled its color before the source early return."));
            Require(scene.Sum([10, 0, 0]).SumBits == 0 && light.ContributionStores == stores,
                "Disabled light changed its cached contribution instead of returning before the store.");
            enabled = true; var factor = .5f;
            light.ConfigureDiffuse([1, .5f, .25f], () => [factor, factor, factor]);
            Require(scene.Sum([10, 0, 0]).SumBits == 0x3f800000, "Light lost current external emittance or its independent dimmer.");
            light.StoreDiffuse(new(.25f, .5f, .75f)); factor = .25f;
            Require(BitConverter.UInt32BitsToSingle(scene.Sum([10, 0, 0]).SumBits) == .375f,
                "Diffuse animation did not publish the absolute C# color before current emittance sampling.");
            var borrowed = light.ReadDiffuse(); borrowed[0] = 99;
            Require(light.ReadDiffuse()[0] == .0625f, "A color observation changed the actual mutable source fields.");
            var failing = new FalloutSourcePointLight(reference with { FormKey = new("Lights.esm", 0x102) },
                parameters with { Reference = new("Lights.esm", 0x102) });
            failing.ConfigureColor(() => throw new IOException("authored-light-color-read-failed"));
            using (scene.Attach(failing, () => placement, () => true))
            {
                Reject(() => scene.Sum([10, 0, 0]));
                Require(scene.LastSum is { Returned: false, Failure: "authored-light-color-read-failed", Lights.Count: 1 },
                    "Later light failure lost the earlier real cached-contribution store.");
            }
        }
        finally { membership.Dispose(); membership.Dispose(); }
        Require(scene.Count == 0 && scene.Sum([0, 0, 0]) is { Returned: true, SumBits: 0 } && scene.SaveBlocker is null,
            "Retired scene light kept a live membership or replayed release.");
        Console.WriteLine("OPENNV_SOURCE_SCENE_POINT_LIGHT_PASS actualMutableFields=true currentPlacement=true sourceEnable=true " +
            "diffuseAndEmittance=true independentDimmer=true disabledCachePreserved=true failurePrefix=true retirement=true nativeRendering=UNEXECUTED");
    }
}
