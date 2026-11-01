using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    // Outcome receipts retain the firing owner across cancellation. Script and
    // perk GetVATSValue queries independently read the actual current action.
    private sealed record VatsShotOwner(FalloutVatsSession Session, FalloutVatsSession.Attack Attack);

    private VatsShotOwner? CaptureVatsShotOwner() => VatsBurstActive && _vats?.CurrentAttack is { } attack
        ? new(_vats, attack) : null;

    private static void RecordVatsImpact(VatsShotOwner? owner, FalloutActorHit? hit)
    {
        if (owner is not null && hit is not null)
            owner.Session.RecordImpact(owner.Attack.Ordinal, hit.Reference, hit.Part, hit.HealthBefore, hit.HealthAfter);
    }

    private void RetainVatsPublicationFailure(VatsShotOwner? owner, Exception error)
    {
        if (owner is null) return;
        owner.Session.Fail(error);
        if (!_vatsGameplayFault) _vatsError = owner.Session.Error;
        _vatsGameplayFault = true;
        // The fired projectile still owns its damage/event continuation. Retire
        // the UI/weapon request, without discarding or reassigning that outcome.
        if (ReferenceEquals(owner.Session, _vats) || VatsExecuting || VatsMenuActive) RetireVats("publication-failed");
        GD.PushError("OPENNV_NATIVE_VATS_PUBLICATION_DIVERGENCE: " + error.Message);
    }

    private void LaunchVatsProjectile(RuntimeNativeProjectileFlight flight)
    {
        var owner = CaptureVatsShotOwner();
        var publication = owner is null ? null : _vatsPublications.Begin(owner.Session);
        try
        {
            _shotEffects!.LaunchProjectile(flight);
            if (publication is not null)
            {
                var observeFinished = flight.OnFinished;
                flight.OnFinished = completed =>
                {
                    try
                    {
                        observeFinished?.Invoke(completed);
                    }
                    catch (Exception error) { RetainVatsPublicationFailure(owner, error); throw; }
                    finally
                    {
                        try
                        {
                            _vatsPublications.Finish(publication);
                            if (completed.Error is not null || completed.Status is not ("hit" or "range-ended" or "stopped" or "detonated"))
                                throw new InvalidOperationException($"VATS projectile {completed.Status}: {completed.Error}");
                        }
                        catch (Exception error) { RetainVatsPublicationFailure(owner, error); }
                    }
                };
            }
        }
        catch (Exception error)
        {
            if (publication is not null) _vatsPublications.Finish(publication);
            RetainVatsPublicationFailure(owner, error);
            throw;
        }
    }
}
