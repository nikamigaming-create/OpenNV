namespace OpenNV.Runtime.Content;

internal sealed class FalloutActorQueries
{
    private readonly List<Lazy<FalloutVampireQueryDeclaration>> _vampire = [];

    internal int GetVampire() => (_vampire.LastOrDefault() ??
        throw new NotSupportedException("GetVampire has no owned executable query declaration.")).Value.Value;

    internal IDisposable BindVampire(Func<FalloutVampireQueryDeclaration> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var declaration = new Lazy<FalloutVampireQueryDeclaration>(read);
        _vampire.Add(declaration);
        return new Binding(() => _vampire.Remove(declaration));
    }

    private sealed class Binding(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
