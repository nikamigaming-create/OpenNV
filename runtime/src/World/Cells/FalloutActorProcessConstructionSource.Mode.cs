using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorRegistrationThreadObservation(Guid Process,
    FalloutActorProcessFact<bool> ThreadedProcessingEnabled,
    FalloutActorProcessFact<bool> MainOwnerGate,
    FalloutActorProcessFact<bool> CurrentThreadIsMain,
    FalloutActorProcessFact<bool> AlternateWorkerPhase,
    FalloutActorProcessFact<uint> PrimaryWorkTotal,
    FalloutActorProcessFact<uint> SecondaryWorkTotal,
    FalloutActorProcessFact<bool> SpecialWorkerPresent,
    FalloutActorProcessFact<bool> CurrentThreadIsSpecialWorker, string Owner);

internal sealed partial class FalloutActorProcessConstructionSource
{
    // Relationships come from the actual living thread owner. They do not
    // serialize obsolete OS IDs or infer roles from actor/cell metadata.
    internal static bool EvaluateThreadMode(FalloutActorProcessDeclaration source,
        FalloutActorRegistrationThreadObservation input)
    {
        source.Validate();
        if (input.Process == Guid.Empty || string.IsNullOrWhiteSpace(input.Owner) ||
            input.ThreadedProcessingEnabled is null || input.MainOwnerGate is null || input.CurrentThreadIsMain is null ||
            input.AlternateWorkerPhase is null || input.PrimaryWorkTotal is null || input.SecondaryWorkTotal is null ||
            input.SpecialWorkerPresent is null || input.CurrentThreadIsSpecialWorker is null)
            throw new InvalidDataException("Actor registration mode lacks a living thread owner.");
        if (!input.ThreadedProcessingEnabled.Require()) return false;
        var mainGate = input.MainOwnerGate.Require();
        var isMain = input.CurrentThreadIsMain.Require();
        var alternate = input.AlternateWorkerPhase.Require();
        if (!isMain)
        {
            if (alternate || input.SecondaryWorkTotal.Require() < 6) return true;
        }
        else if (!alternate)
        {
            // Independent original source families differ at this comparison.
            var limit = source.Arithmetic == FalloutDetectionScalarKind.NewVegas ? 11u : 10u;
            if (input.PrimaryWorkTotal.Require() < limit || input.SecondaryWorkTotal.Require() < 7) return true;
        }
        if (input.SpecialWorkerPresent.Require() && !input.CurrentThreadIsSpecialWorker.Require()) return true;
        return mainGate;
    }
}
