using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSandboxAvailability(bool Food, bool Furniture, bool SleepingFurniture);

// Calendar-window ownership and the independent actor-process scalar are
// required inputs. Neither menu visibility nor IsInCombat supplies either one.
internal sealed record FalloutSandboxActionContext(int ScheduledMode, bool WindowActive, float DialogueScalar)
{
    internal void Validate()
    {
        if (ScheduledMode is < -1 or > 3 || !float.IsFinite(DialogueScalar))
            throw new InvalidDataException("Sandbox source calendar/process context is invalid.");
    }

    internal int[] Weights(byte[] history, FalloutSandboxAvailability availability)
    {
        Validate();
        if (history.Length != 6) throw new InvalidDataException("Sandbox byte history has no six source action classes.");
        var result = history.Select(value => (int)value).ToArray();
        var scheduledAvailable = ScheduledMode switch
        {
            0 => availability.SleepingFurniture,
            1 or 2 or 3 => availability.Food,
            _ => false,
        };
        if (WindowActive && scheduledAvailable)
        {
            Array.Clear(result);
            result[ScheduledMode == 0 ? (int)FalloutSandboxAction.Sleeping : (int)FalloutSandboxAction.Eating] = 10;
        }
        else
        {
            result[(int)FalloutSandboxAction.Sleeping] = 0;
            if (availability.Food) result[(int)FalloutSandboxAction.Eating] += 20;
            if (!availability.Furniture) result[(int)FalloutSandboxAction.Eating] -= 15;
        }
        if (DialogueScalar > 0) result[(int)FalloutSandboxAction.Dialogue] = 0;
        return result;
    }
}

internal sealed record FalloutSandboxDiscovery(IReadOnlyList<FalloutSandboxCandidate> Candidates,
    FalloutSandboxAvailability Availability)
{
    internal void Validate()
    {
        if (Candidates is null || Availability is null) throw new InvalidDataException("Sandbox discovery lost its actual source registry.");
        foreach (var candidate in Candidates)
        {
            candidate.Validate();
            if (candidate.Reference is not null) candidate.RequireSourceIdentity();
        }
        if (Availability.Food != Candidates.Any(candidate => candidate.Action == (int)FalloutSandboxAction.Eating) ||
            Availability.Furniture != Candidates.Any(candidate => candidate.Action == (int)FalloutSandboxAction.Furniture) ||
            Availability.SleepingFurniture != Candidates.Any(candidate => candidate.Action == (int)FalloutSandboxAction.Sleeping))
            throw new InvalidDataException("Sandbox availability bytes differ from the eligible source registry rebuild.");
    }
    internal FalloutSandboxDiscovery Copy() => this with { Candidates = Candidates.ToArray() };
}

internal sealed record FalloutSandboxTimerSample(string SourceSha256, long Epoch, uint Milliseconds)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(SourceSha256) || Epoch <= 0)
            throw new InvalidDataException("Sandbox timer observation has no genuine source-clock lifetime.");
    }
}

internal sealed record FalloutSandboxRegistrySnapshot(string ClockSourceSha256, long Epoch,
    uint LastMilliseconds, uint RescanAt, uint RepeatUntil, FalloutFormKey? RepeatedReference,
    bool ScanEntered, FalloutSandboxDiscovery? Registry, string? Failure, FalloutFormKey? PreviousReference = null)
{
    internal void Validate()
    {
        new FalloutSandboxTimerSample(ClockSourceSha256, Epoch, LastMilliseconds).Validate();
        Registry?.Validate();
        if (RepeatedReference is { } form && (string.IsNullOrWhiteSpace(form.OwnerPlugin) || form.ObjectId == 0) ||
            PreviousReference is { } previous && (string.IsNullOrWhiteSpace(previous.OwnerPlugin) || previous.ObjectId == 0) ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Sandbox registry continuation is invalid.");
    }
    internal FalloutSandboxRegistrySnapshot Copy() => this with { Registry = Registry?.Copy() };
}
