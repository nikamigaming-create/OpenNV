using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativeLandscapeParameterBindings : IDisposable
{
    private readonly Dictionary<string, Variant> _values = [];
    internal IEnumerable<KeyValuePair<string, Variant>> Values => _values;
    internal Exception? OriginalFailure { get; set; }
    internal void Add(string name, Variant value)
    {
        try { _values.Add(name, value); }
        catch (Exception original)
        {
            try { value.Dispose(); }
            catch (Exception release)
            { throw new AggregateException("LAND parameter binding retains its original insertion and variant-release failure.", original, release); }
            throw;
        }
    }
    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var value in _values.Values)
            try { value.Dispose(); } catch (Exception error) { errors.Add(error); }
        _values.Clear();
        if (errors.Count != 0)
            throw new AggregateException("LAND parameter binding retains native variant retirement failures.",
                OriginalFailure is { } original ? new[] { original }.Concat(errors) : errors);
    }
}
