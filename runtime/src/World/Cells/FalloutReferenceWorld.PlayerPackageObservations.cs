using System.Runtime.CompilerServices;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed record PlayerPackageEffectObservation(Guid Observation);
    private readonly ConditionalWeakTable<FalloutReferenceScriptEffect, PlayerPackageEffectObservation> _playerPackageEffects = new();

    // The identity belongs to this executed C# observation, not a retail script
    // invocation. A retained bootstrap effect may later attach to the driver;
    // that attachment reuses its receipt and never replays source mutations.
    internal Guid ObserveStandalonePlayerPackageCommand(FalloutReferenceScriptEffect effect, string producer)
    {
        ArgumentNullException.ThrowIfNull(effect); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (!HasStandalonePlayerScene) return Guid.Empty;
        if (effect.Kind != FalloutReferenceEffectKind.ScriptPackage || effect.Target != _enginePlayer)
            throw new InvalidDataException("Player package ingress requires its actual typed source command/target.");
        if (_playerPackageEffects.TryGetValue(effect, out var previous)) return previous.Observation;
        var observation = Guid.NewGuid();
        ObserveStandalonePlayerPackageMutation(observation, producer, effect.Source, effect.Argument, effect.Argument is null);
        CampaignStandalonePlayerScene.PlayerInhibition.ObserveUnownedWriter(observation, producer, FalloutPlayerInhibitionLane.Fourth);
        _playerPackageEffects.Add(effect, new(observation));
        return observation;
    }

    internal void ObserveStandalonePlayerPackageMutation(Guid observation, string producer,
        OpenNV.Runtime.Content.FalloutFormKey? commandSource, OpenNV.Runtime.Content.FalloutFormKey? requested, bool remove)
    {
        if (!HasStandalonePlayerScene) return;
        RequireStandaloneControlPublication();
        var scene = CampaignStandalonePlayerScene;
        var actor = ActorProcesses.Read(_enginePlayer);
        if (actor.Retired || !actor.Source.EnginePlayer || _actorProcessStack != scene.Stack)
            throw new InvalidOperationException("Actual package ingress lost the current Player/process source lease.");
        // The reached command's package election and mandatory effects are not
        // yet owned. Both independent slots may be affected; neither semantic
        // current package nor missing presentation establishes its branch.
        ProcessCommon.ObserveSourcePackageAssignment(_enginePlayer, actor.Epoch, observation, producer,
            commandSource, requested, remove, FalloutProcessPackageWriterTarget.Both);
    }

    private void ObserveActualActorPackageMutation(OpenNV.Runtime.Content.FalloutFormKey actor,
        OpenNV.Runtime.Content.FalloutFormKey? requested, string producer)
    {
        if (!HasStandalonePlayerScene) return;
        if (actor == _enginePlayer)
        {
            ObserveStandalonePlayerPackageMutation(Guid.NewGuid(), producer, null, requested, requested is null);
            return;
        }
        if (_processCommon?.HasActor(actor) != true) return;
        if (!ActorProcessesConfigured)
            throw new NotSupportedException("source-package-assignment-actual-process-manager-unbound");
        var current = ActorProcesses.Read(actor);
        if (current.Retired) throw new InvalidOperationException("A retired source actor cannot change package ownership.");
        if (current.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
        {
            ProcessCommon.RetainUnownedMutation(actor, "source-package-assignment-lower-tier-election-unbound");
            throw new NotSupportedException("source-package-assignment-lower-tier-election-unbound");
        }
        ProcessCommon.ObserveSourcePackageAssignment(actor, current.Epoch, Guid.NewGuid(), producer, null, requested,
            requested is null, FalloutProcessPackageWriterTarget.Both);
    }
}
