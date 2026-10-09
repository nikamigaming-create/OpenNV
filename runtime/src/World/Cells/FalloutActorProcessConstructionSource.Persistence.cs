using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessConstructionSource
{
    internal void ValidateRetained(FalloutActorProcessesSnapshot snapshot)
    {
        if (snapshot.Stack != _stack || snapshot.Contract != _declaration.Contract)
            throw new InvalidDataException("Actor constructor continuation belongs to another selected source.");
        var currentIni = ReadHardwareThreadSetting();
        foreach (var actor in snapshot.Actors)
        {
            if (actor.Source.EnginePlayer)
            {
                if (actor.Construction is not null)
                    throw new InvalidDataException("Actual engine Player cannot acquire a manager constructor receipt.");
                continue;
            }
            var construction = actor.Construction ??
                throw new InvalidDataException("Current actor constructor continuation is absent.");
            var receipt = construction.Source ??
                throw new InvalidDataException("Current actor constructor source receipt is absent.");
            var actual = _identity(actor.Source.Reference); actual.Validate();
            var reference = _records.GetEffective(actual.Reference);
            ValidateSourceIdentity(reference, actual);
            var expected = actual.BaseSignature switch
            {
                "NPC_" => "ACHR",
                "CREA" => "ACRE",
                _ => throw new InvalidDataException("Retained actor has no original source constructor."),
            };
            if (receipt.Stack != _stack || receipt.ExecutableSha256 != _declaration.ExecutableSha256 ||
                receipt.ProcessContract != _declaration.Contract || receipt.ConstructedProcess == Guid.Empty || receipt.Actor != actual ||
                actual != actor.Source || construction.Actor != actual.Reference || actual.EnginePlayer ||
                reference.Signature != expected || receipt.ReferenceSignature != expected ||
                receipt.ReferenceSha256 != Hash(reference) || FalloutDialogueTopic.RequiredForm(reference, "NAME") != actual.Base ||
                receipt.HardwareThreads != currentIni || construction.RegistrationRequested.Value != true ||
                string.IsNullOrWhiteSpace(construction.RegistrationRequested.Owner) || construction.RegistrationRequested.Failure is not null ||
                string.IsNullOrWhiteSpace(construction.AlternateRegistrationMode.Owner) ||
                receipt.ModeFailure != construction.AlternateRegistrationMode.Failure)
                throw new InvalidDataException("Retained actor constructor winner/master/setting/source identity drifted.");
            if (currentIni.CurrentValue <= 1)
            {
                if (receipt.ThreadObservation is not null || receipt.AlternateMode != false || receipt.ModeFailure is not null ||
                    construction.AlternateRegistrationMode.Value != false || construction.AlternateRegistrationMode.Failure is not null)
                    throw new InvalidDataException("Short-circuit constructor mode acquired an unrelated thread history.");
            }
            else if (receipt.ThreadObservation is { } observation)
            {
                // This is a historical decision, not a new-process thread
                // readiness fact. Do not execute native producers during cold.
                bool? decision;
                string? failure = null;
                try { decision = EvaluateThreadMode(_declaration, observation); }
                catch (NotSupportedException error)
                {
                    decision = null;
                    failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
                }
                if (observation.Process != receipt.ConstructedProcess || receipt.AlternateMode != decision ||
                    construction.AlternateRegistrationMode.Value != decision || construction.AlternateRegistrationMode.Failure != failure)
                    throw new InvalidDataException("Retained constructor thread decision differs from its actual captured process.");
            }
            else if (receipt.AlternateMode is not null || construction.AlternateRegistrationMode.Value is not null ||
                string.IsNullOrWhiteSpace(construction.AlternateRegistrationMode.Failure))
                throw new InvalidDataException("Missing original worker owner was admitted as a constructor mode.");
        }
    }
}
