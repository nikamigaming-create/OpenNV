using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// This indexes literal guards directly from the current source; it does not
// execute them or claim that their guards, effects or host dependencies work.
internal static class ScriptDispatchIndex
{
    internal sealed record Site(string Owner, string File, int Line, string Guard, string Kind);
    internal sealed record Input(string File, string Sha256);
    internal sealed record Index(IReadOnlyDictionary<string, List<Site>> Operations, IReadOnlyList<Input> Sources,
        IReadOnlyList<Site> DynamicGuards);

    internal static Index Read(string repository)
    {
        var specifications = new[]
        {
            ("reference-result", "runtime/src/World/Cells/FalloutReferenceScripts.cs", "Call", true),
            ("quest-fallback", "runtime/src/Content/FalloutQuestScripts.cs", "Call", true),
            ("native-host", "runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.Commands.cs", "ApplyNativeSourceCommand", false)
        };
        var operations = new Dictionary<string, List<Site>>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<Input>();
        var dynamic = new List<Site>();
        foreach (var (owner, relative, methodName, local) in specifications)
        {
            var path = Path.Combine(repository, relative.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            var tree = CSharpSyntaxTree.ParseText(System.Text.Encoding.UTF8.GetString(bytes), path: relative);
            if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidDataException("Dispatch source does not parse: " + relative);
            var root = tree.GetRoot();
            BlockSyntax body;
            if (local)
            {
                var matches = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>()
                    .Where(function => function.Identifier.ValueText == methodName && function.Body is not null).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Expected one script dispatch local in " + relative);
                body = matches[0].Body!;
            }
            else
            {
                var matches = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Identifier.ValueText == methodName && method.Body is not null).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Expected one script dispatch method in " + relative);
                body = matches[0].Body!;
            }
            sources.Add(new(relative, Convert.ToHexString(SHA256.HashData(bytes))));
            Site SiteFor(SyntaxNode node, string guard, string kind) =>
                new(owner, relative, tree.GetLineSpan(node.Span).StartLinePosition.Line + 1, guard, kind);
            void Add(string literal, SyntaxNode node, string guard, string kind)
            {
                var value = literal.ToLowerInvariant();
                // Receiver-qualified fallback cases remain explicit evidence;
                // the generic map joins the operation while retaining its guard.
                var operation = value[(value.LastIndexOf('.') + 1)..];
                if (operation.Length == 0) return;
                if (!operations.TryGetValue(operation, out var sites)) operations.Add(operation, sites = []);
                sites.Add(SiteFor(node, guard, kind));
            }
            foreach (var statement in body.Statements)
            {
                if (statement is IfStatementSyntax first)
                {
                    for (IfStatementSyntax? conditional = first; conditional is not null;
                         conditional = conditional.Else?.Statement as IfStatementSyntax)
                    {
                        var found = false;
                        foreach (var equality in conditional.Condition.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>()
                                     .Where(value => value.IsKind(SyntaxKind.EqualsExpression)))
                        {
                            if (equality.Left is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "operation" &&
                                equality.Right is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                            { Add(literal.Token.ValueText, conditional, conditional.Condition.ToString(), "if-equality"); found = true; }
                        }
                        foreach (var pattern in conditional.Condition.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>()
                                     .Where(value => value.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "operation"))
                            foreach (var literal in pattern.Pattern.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                                         .Where(value => value.IsKind(SyntaxKind.StringLiteralExpression)))
                            { Add(literal.Token.ValueText, conditional, conditional.Condition.ToString(), "if-pattern"); found = true; }
                        if (!found && conditional.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                                .Any(id => id.Identifier.ValueText == "operation"))
                            dynamic.Add(SiteFor(conditional, conditional.Condition.ToString(), "nonliteral-guard"));
                    }
                }
                else if (statement is SwitchStatementSyntax dispatch &&
                         (dispatch.Expression.ToString() == "operation" || dispatch.Expression.ToString() == "command.ToLowerInvariant()"))
                {
                    foreach (var label in dispatch.Sections.SelectMany(section => section.Labels))
                    {
                        if (label is CaseSwitchLabelSyntax simple && simple.Value is LiteralExpressionSyntax literal &&
                            literal.IsKind(SyntaxKind.StringLiteralExpression))
                            Add(literal.Token.ValueText, label, label.ToString(), "switch");
                        else if (label is CasePatternSwitchLabelSyntax pattern)
                            foreach (var item in pattern.Pattern.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                                         .Where(value => value.IsKind(SyntaxKind.StringLiteralExpression)))
                                Add(item.Token.ValueText, label, label.ToString(), "switch-pattern");
                    }
                }
            }
        }
        return new(operations, sources, dynamic);
    }
}
