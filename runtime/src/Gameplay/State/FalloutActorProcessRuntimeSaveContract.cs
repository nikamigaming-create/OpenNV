using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutActorProcessRuntimeSaveContract
{
    internal static void ValidateShape(FalloutActorProcessRuntimeSnapshot? runtime, FalloutProcessCommonSnapshot? common,
        FalloutActorProcessesSnapshot? processes)
    {
        if (runtime is null || common is null || processes is null)
            throw new InvalidDataException("Current save omitted its actual Main/Player/life/common process ownership.");
        FalloutActorProcessRuntimeState.Validate(runtime); FalloutActorProcessCommonState.Validate(common);
        FalloutActorProcessManager.ValidateShape(processes);
        if (runtime.Contract != common.Contract || runtime.Stack != common.Stack || runtime.Stack != processes.Stack ||
            runtime.Player != processes.Player || runtime.MainFrame.Boundary is not null ||
            runtime.MainFrame.Windows.Any(window => window.Next != FalloutMainQueueFrameStep.Complete || window.Failure is not null) ||
            runtime.MainOperations.Any(item => item.Phase != FalloutMainProcessPhase.Complete) ||
            runtime.Travel is { Phase: not FalloutPlayerTravelPhase.Complete } || runtime.Actors.Any(actor => actor.Failure is not null) ||
            common.Current.Any(actor => actor.Boundary is not null) || common.Transfer is { Failure: not null } or { Initialized: false })
            throw new NotSupportedException("Current runtime/common process retains its real source invocation or unowned writer.");
        var actors = processes.Actors.ToDictionary(actor => actor.Source.Reference, FalloutFormKeyComparer.Instance);
        if (!runtime.Actors.Select(actor => actor.Source.Reference).ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(actors.Keys) ||
            !common.Current.Select(actor => actor.Source.Reference).ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(actors.Keys))
            throw new InvalidDataException("Runtime/common state omitted an actual constructed source process.");
        foreach (var actor in runtime.Actors)
            if (actor.Source != actors[actor.Source.Reference].Source || actor.Retired != actors[actor.Source.Reference].Retired)
                throw new InvalidDataException("Actor neutral life belongs to a different source or retired process lifetime.");
        foreach (var actor in common.Current)
        {
            var process = actors[actor.Source.Reference];
            if (actor.Source != process.Source || actor.Epoch != process.Epoch || !process.Retired && actor.Level != process.Level ||
                (actor.Phase == FalloutProcessCommonPhase.Retired) != process.Retired)
                throw new InvalidDataException("Common state differs from its actual retained process class/epoch/lifetime.");
        }
    }

    internal static void ValidateSource(FalloutPluginStack records, FalloutNativeCampaignState saved)
    {
        ValidateShape(saved.ActorProcessRuntime, saved.ActorProcessCommon, saved.ActorProcesses);
        var source = records.OwnedSource ?? throw new InvalidDataException("Runtime/common save has no exact selected source owner.");
        var selected = FalloutActorProcessRuntimeDeclaration.ForExecutable(
            FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath).ExecutableSha256);
        var groups = FalloutCombatGroupDeclaration.ForExecutable(selected.ExecutableSha256);
        var runtime = saved.ActorProcessRuntime!; var common = saved.ActorProcessCommon!;
        if (runtime.Contract != selected.Contract || runtime.Stack != source.StackId || runtime.Player != records.RuntimeFormKey(0x14))
            throw new InvalidDataException("Runtime/common cold state changed its selected executable/stack/player declaration.");
        var references = saved.References ?? throw new InvalidDataException("Process cold state has no real reference graph.");
        FalloutReferenceSnapshot.Validate(references);
        var placed = references.Where(reference => records.GetEffective(reference.Reference).Signature is "ACHR" or "ACRE")
            .ToDictionary(reference => reference.Reference, FalloutFormKeyComparer.Instance);
        if (!runtime.Actors.Select(actor => actor.Source.Reference).ToHashSet(FalloutFormKeyComparer.Instance)
                .SetEquals(placed.Keys.Append(runtime.Player)))
            throw new InvalidDataException("Runtime constructor history omitted a current actor reference.");
        foreach (var entry in runtime.Actors)
        {
            if (FalloutCombatActorSource.Read(records, groups, entry.Source.Reference) != entry.Source ||
                !entry.Source.EnginePlayer && entry.Retired != placed[entry.Source.Reference].Deleted)
                throw new InvalidDataException("Cold runtime changed a winning reference/master/base or deletion.");
        }
        foreach (var entry in common.Current.Concat(common.Retired))
        {
            if (FalloutCombatActorSource.Read(records, groups, entry.Source.Reference) != entry.Source)
                throw new InvalidDataException("Common process history changed its exact actor source winner.");
            if (entry.Body is not { } body) continue;
            var bodyPart = entry.Source.EnginePlayer || entry.Source.BaseSignature == "NPC_" ? records.RuntimeFormKey(0x1d) :
                FalloutActorHealthSource.Read(records, placed[entry.Source.Reference].Base,
                    placed[entry.Source.Reference].Templates is { } templates ? new FalloutActorTemplateSelection(templates) : null).BodyParts;
            if (bodyPart != body.BodyPartSource)
                throw new InvalidDataException("Common High body lost its actual actor body-part source.");
            var record = records.GetEffective(bodyPart); var parts = FalloutBodyPartData.Read(record);
            if (!source.TryRead(body.SkeletonPath, null, out var bytes, out _))
                throw new InvalidDataException("Cold common High body lost its winning source NIF.");
            var nif = FalloutNifFile.Read(bytes);
            var actual = FalloutActorProcessBodySource.Read(selected, entry.Source.Reference, body.SkeletonPath, nif, parts,
                Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant(), body.Owner);
            if (!FalloutActorProcessCommonState.BodyEquivalent(actual, body))
                throw new InvalidDataException("Cold High node/controller lookups changed their source resource/record declarations.");
        }
        // This validates declarations. A new native actor must still publish a
        // fresh living body lease before the saved High process is executable.
    }
}
