using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunLoadedPlayerProcessQueryAdmission(FalloutMainScriptCallerSource main, string stack,
        FalloutProcessSceneQuery empty)
    {
        // These receipts are authored caller fixtures. Production resolves the
        // pointer through the real loaded-context registry and bound manager.
        var query = empty with { AnimationContext = Guid.NewGuid() };
        var binding = new FalloutAnimationProcessQueryBinding(query.AnimationContext.Value, Guid.NewGuid(), Guid.NewGuid(),
            query.Process, Guid.NewGuid(), query.Epoch, Guid.NewGuid(), query.ExecutableSha256, query.Actor,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var mode in new[] { "owned", "unbound", "foreign-epoch", "changed-model", "weapon" })
        {
            var input = new FalloutDirectInputState(stack, 123, new(_ => null));
            var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid());
            var hud = new FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource.Reviewed(main), stack, scene.Process,
                AuthoredReferenceHudComponents());
            try
            {
                scene.BindInputManager(input);
                var reads = 0; var bindings = 0;
                var current = mode == "weapon" ? query with { WeaponEntry = new(Guid.NewGuid(), query.Actor.Reference, 1) } : query;
                scene.BindActorProcessQueries(() => { ++reads; return current; }, mode == "unbound" ? null : _ =>
                {
                    ++bindings;
                    return mode == "foreign-epoch" ? binding with { Epoch = binding.Epoch + 1 } :
                        mode == "changed-model" && bindings == 2 ? binding with { Root = Guid.NewGuid() } : binding;
                });
                var invocation = Guid.NewGuid();
                scene.StoreSceneClock(invocation, 1, () => AuthoredSceneCachedDelta(scene, 0x3c800000));
                scene.EnterSceneVirtual(invocation, 1);
                Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud));
                var saved = scene.Capture(); var call = saved.ActorProcessQuery!;
                Require(call.Result == current && call.EntryCalls == 1 && call.AnimationReturned == (mode == "owned") &&
                    reads == (mode is "owned" or "changed-model" ? 2 : 1),
                    "Current animation admission lost its first returned query or repeated a source getter during binding.");
                Require(mode == "owned" ? call.Failure is null && call.AnimationBinding == binding : call.Failure is not null,
                    "An unavailable or foreign animation context escaped the process query boundary.");
                var copy = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
                FalloutStandalonePlayerSceneState.Validate(copy);
                var cold = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(), copy);
                try
                {
                    Require(cold.Capture().ActorProcessQuery == copy.ActorProcessQuery,
                        "Cold query history replayed an animation getter or lost its failed prefix.");
                    Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(copy));
                }
                finally { cold.Retire(); }
                if (mode == "owned")
                    Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                    { ActorProcessQuery = call with { AnimationBinding = null } }));
            }
            finally { scene.Retire(); hud.Retire(); input.Retire(); }
        }
        Console.WriteLine("OPENNV_PLAYER_PROCESS_ANIMATION_QUERY_PASS authoredCaller=true twoIndependentReads=true " +
            "missingOwnerRefused=true foreignEpochRefused=true changedModelRefused=true weaponBoundaryPreserved=true coldNoReplay=true");
    }
}
