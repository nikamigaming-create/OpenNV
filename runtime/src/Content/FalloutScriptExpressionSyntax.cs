namespace OpenNV.Runtime.Content;

internal enum FalloutScriptExpressionSyntaxKind
{
    Number, String, Identifier, Group, Unary, Binary, Assignment,
    Call, ReferenceCall, Index, Pair,
}

internal enum FalloutScriptExpressionCategory { Dynamic, Number, String, Form, Array, Pair, Identifier }

// Syntax retains the actual parser's grouping and token extent. It carries no
// value reader, execution callback, array handle or persistent storage slot.
internal sealed record FalloutScriptExpressionSyntax(
    FalloutScriptExpressionSyntaxKind Kind, string Operation, int TokenStart, int TokenCount,
    FalloutScriptExpressionCategory Category, IReadOnlyList<FalloutScriptExpressionSyntax> Children)
{
    internal static FalloutScriptExpressionSyntax Create(FalloutScriptExpressionSyntaxKind kind,
        string operation, int start, int end, FalloutScriptExpressionCategory category,
        params FalloutScriptExpressionSyntax[] children) =>
        new(kind, operation, start, checked(end - start), category, Array.AsReadOnly(children));

    internal IEnumerable<FalloutScriptExpressionSyntax> Descendants()
    {
        var pending = new Stack<FalloutScriptExpressionSyntax>();
        pending.Push(this);
        while (pending.TryPop(out var node))
        {
            yield return node;
            for (var index = node.Children.Count - 1; index >= 0; --index) pending.Push(node.Children[index]);
        }
    }

    internal void RequireBasicUses()
    {
        foreach (var node in Descendants())
        {
            if (node.Kind == FalloutScriptExpressionSyntaxKind.Pair)
            {
                if (node.Children.Count != 2 || node.Children[0].Category is not
                    (FalloutScriptExpressionCategory.Dynamic or FalloutScriptExpressionCategory.Number or FalloutScriptExpressionCategory.String) ||
                    node.Children[1].Category == FalloutScriptExpressionCategory.Pair)
                    throw new InvalidDataException("Pair syntax requires a numeric/string key and a basic value.");
            }
            else if (node.Kind is FalloutScriptExpressionSyntaxKind.Unary or FalloutScriptExpressionSyntaxKind.Binary or
                FalloutScriptExpressionSyntaxKind.Assignment or FalloutScriptExpressionSyntaxKind.Index &&
                node.Children.Any(child => child.Category == FalloutScriptExpressionCategory.Pair))
                throw new InvalidDataException("A transient pair cannot be used as a scalar, index or assignment value.");
        }
    }
}
