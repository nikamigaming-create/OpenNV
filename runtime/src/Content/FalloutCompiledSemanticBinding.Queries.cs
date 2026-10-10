namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledQueryBinding(FalloutCompiledSemanticBinding Semantic,
    FalloutScriptFunction Function);

internal sealed partial record FalloutCompiledSemanticBinding
{
    // Declaration transport alone grants no behavior. The same invocation must
    // retain the actual shared function bound to its caller and campaign state.
    internal static FalloutCompiledQueryBinding? ResolveQuery(FalloutCompiledCommandDeclaration declaration,
        Func<string, FalloutScriptFunction?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        var declared = Read(declaration);
        if (declared.Kind is FalloutCompiledSemanticKind.Effect or FalloutCompiledSemanticKind.Message)
            return null;
        var primary = resolve(declaration.Name);
        var alias = declaration.Alias.Length == 0 ? null :
            declaration.Name.Equals(declaration.Alias, StringComparison.OrdinalIgnoreCase) ? primary :
            resolve(declaration.Alias);
        if (primary is not null && alias is not null && !primary.SharesInvocation(alias))
            throw new NotSupportedException("Selected command names resolve to conflicting actual query owners: " +
                declaration.Name + "/" + declaration.Alias);
        var function = primary ?? alias;
        if (function is null) return null;
        var name = primary is not null ? declaration.Name : declaration.Alias;
        return new(new(declaration.Name, declaration.Alias, name, FalloutCompiledSemanticKind.Query), function);
    }
}
