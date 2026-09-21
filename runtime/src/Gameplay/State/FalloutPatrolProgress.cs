namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPatrolProgress(string SourceSha256, int Index, int Direction,
    bool Arrived, double RemainingSeconds, bool ReturningToStart, bool Complete)
{
    internal void Validate()
    {
        if (SourceSha256 is not { Length: 64 } || !SourceSha256.All(Uri.IsHexDigit) || Index < 0 ||
            Direction is not (-1 or 1) || !double.IsFinite(RemainingSeconds) || RemainingSeconds < 0 ||
            !Arrived && (RemainingSeconds != 0 || Complete))
            throw new InvalidDataException("Saved patrol progress is invalid.");
    }
}
