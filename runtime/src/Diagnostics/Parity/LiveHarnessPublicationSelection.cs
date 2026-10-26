namespace OpenNV.Runtime.Diagnostics.Parity;

/// <summary>Explicit diagnostic omissions; never changes simulation or input.</summary>
internal sealed class LiveHarnessPublicationSelection
{
    internal string Scope { get; private set; } = "full";

    internal void SetMode(string? mode)
    {
        if (mode is not ("full" or "performance"))
            throw new ArgumentException("Publication mode must be full or performance.");
        Scope = mode;
    }

    internal LiveHarnessStateDetails? Capture(Func<(string StateKey, ulong EventOrdinal)> identity,
        Func<object> gameplay, Func<object> controls)
    {
        if (Scope == "performance") return null;
        var currentIdentity = identity();
        var currentControls = controls();
        return new(currentIdentity, gameplay(), currentControls);
    }
}

internal sealed record LiveHarnessStateDetails((string StateKey, ulong EventOrdinal) Identity, object Gameplay, object Controls);
