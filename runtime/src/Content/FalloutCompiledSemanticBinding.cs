namespace OpenNV.Runtime.Content;

internal enum FalloutCompiledSemanticKind { Unowned, Query, Effect, Message }
internal sealed record FalloutCompiledSemanticBinding(string SourceName, string SourceAlias,
    string ExecutionName, FalloutCompiledSemanticKind Kind)
{
    // Name and alias come from the same selected CommandInfo, with the same
    // Execute/Parse/ParamInfo association. An alias is not recovered from an
    // opcode, campaign, diagnostic script or approximate spelling.
    internal static FalloutCompiledSemanticBinding Read(FalloutCompiledCommandDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!PublicName(declaration.Name) || declaration.Alias is null || declaration.Alias.Length != 0 && !PublicName(declaration.Alias))
            throw new InvalidDataException("Compiled command omitted its actual public source name/alias declaration.");
        var primary = KindOf(declaration.Name);
        var alias = KindOf(declaration.Alias);
        if (primary != FalloutCompiledSemanticKind.Unowned && alias != FalloutCompiledSemanticKind.Unowned &&
            !declaration.Name.Equals(declaration.Alias, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Selected command names identify conflicting shared semantic owners: " +
                declaration.Name + "/" + declaration.Alias);
        return primary != FalloutCompiledSemanticKind.Unowned || alias == FalloutCompiledSemanticKind.Unowned
            ? new(declaration.Name, declaration.Alias, declaration.Name, primary)
            : new(declaration.Name, declaration.Alias, declaration.Alias, alias);
    }
    private static FalloutCompiledSemanticKind KindOf(string name)
    {
        var query = FalloutCompiledSemanticOwners.IsQuery(name);
        var effect = FalloutCompiledSemanticOwners.IsEffect(name);
        if (query && effect) throw new InvalidDataException("One shared command name declares both query and effect semantics.");
        return query ? FalloutCompiledSemanticKind.Query : !effect ? FalloutCompiledSemanticKind.Unowned :
            FalloutCompiledSemanticOwners.IsMessage(name) ? FalloutCompiledSemanticKind.Message : FalloutCompiledSemanticKind.Effect;
    }
    private static bool PublicName(string? name) => !string.IsNullOrEmpty(name) &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
