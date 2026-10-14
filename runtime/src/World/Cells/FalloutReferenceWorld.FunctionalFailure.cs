namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutFunctionalGameplayFailure(string Owner, string ExceptionType, string Error);

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutFunctionalGameplayFailure? FunctionalFailure { get; private set; }

    internal void RetainFunctionalFailure(Exception error, string owner)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        FunctionalFailure ??= new(owner, error.GetType().FullName ?? error.GetType().Name,
            string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message);
    }

    internal void RequireNoFunctionalFailure()
    {
        if (FunctionalFailure is { } failure)
            throw new InvalidOperationException("Authoritative gameplay stopped at " + failure.Owner + ": " + failure.Error);
    }
}
