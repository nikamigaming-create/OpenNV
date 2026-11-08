using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static partial class RuntimeKnowledgeGraph
{
    private static object OpenNV(Graph graph, string root, string[] files, string revision)
    {
        long declarations = 0, guards = 0, references = 0;
        var sourceFiles = files.Where(path => path.EndsWith(".cs", StringComparison.Ordinal) &&
            !path.Split('/').Any(part => part is "obj" or "bin" or ".godot"))
            .Order(StringComparer.Ordinal).ToArray();
        foreach (var relative in sourceFiles)
        {
            var path = InputPath(root, relative);
            var test = relative.StartsWith("contract-tests/", StringComparison.Ordinal) || relative.StartsWith("runtime/tools/", StringComparison.Ordinal);
            var file = graph.Node("opennv-file:" + relative, test ? "test-source" : "opennv-source", relative,
                new { path = relative, sha256 = Hash(path), execution = "not-run-by-indexer" });
            graph.Edge(revision, "contains", file, "git-working-file-inventory"); Classify(graph, file, relative);
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: relative);
            if (tree.GetDiagnostics().Any(value => value.Severity == DiagnosticSeverity.Error))
                throw new InvalidDataException("Knowledge graph source does not parse: " + relative);
            var syntax = tree.GetRoot();
            foreach (var declaration in syntax.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                var name = declaration switch
                {
                    BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
                    MethodDeclarationSyntax method => method.Identifier.ValueText,
                    ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                    PropertyDeclarationSyntax property => property.Identifier.ValueText,
                    DelegateDeclarationSyntax value => value.Identifier.ValueText,
                    _ => null,
                };
                if (name is null) continue;
                var id = graph.Node(file + "/declaration:" + declaration.SpanStart, "opennv-declaration", name,
                    new
                    {
                        file = relative,
                        line = tree.GetLineSpan(declaration.Span).StartLinePosition.Line + 1,
                        syntax = declaration.Kind().ToString(),
                        test,
                        owner = string.Join(".", declaration.Ancestors()
                            .OfType<TypeDeclarationSyntax>().Reverse().Select(value => value.Identifier.ValueText))
                    });
                graph.Edge(file, "declares", id, "syntax");
                graph.Edge(id, "declared-name", graph.Name(name), "spelling-not-behavior"); ++declarations;
            }
            // Preserve overload/receiver ambiguity; mentions are not a semantic
            // call graph, and test source is not a successful test receipt.
            foreach (var name in syntax.DescendantNodes().OfType<SimpleNameSyntax>()
                         .Select(value => value.Identifier.ValueText).Distinct(StringComparer.Ordinal))
            {
                graph.Edge(file, test ? "test-mentions-name" : "mentions-name", graph.Name(name), "syntax-unresolved-symbol");
                ++references;
            }
            foreach (var creation in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                         .Where(value => value.Type.ToString() is "NotSupportedException" or "System.NotSupportedException"))
            {
                var gap = graph.Node(file + "/guard:" + creation.SpanStart, "unsupported-guard", creation.ArgumentList?.ToString() ?? "unsupported",
                    new
                    {
                        file = relative,
                        line = tree.GetLineSpan(creation.Span).StartLinePosition.Line + 1,
                        reachability = "unverified",
                        test,
                        disposition = "guard-site-not-observed-failure"
                    });
                graph.Edge(file, "contains-unsupported-guard", gap, "syntax"); ++guards;
            }
        }
        var plan = "docs/implementation-plan.md";
        if (files.Contains(plan, StringComparer.Ordinal))
        {
            var planFile = graph.Node("opennv-file:" + plan, "acceptance-plan", plan,
                new { path = plan, sha256 = Hash(InputPath(root, plan)) });
            graph.Edge(revision, "contains", planFile, "git-working-file-inventory");
            var rows = File.ReadAllLines(InputPath(root, plan));
            for (var index = 0; index < rows.Length; ++index)
            {
                var line = rows[index].Trim();
                if (line.StartsWith("### M", StringComparison.Ordinal) && line.Length > 5 && char.IsAsciiDigit(line[5]))
                {
                    var obligation = graph.Node("obligation:milestone:" + index, "acceptance-obligation", line[4..].Trim(),
                        new
                        {
                            file = plan,
                            line = index + 1,
                            kind = "milestone-section",
                            acceptance = "unverified"
                        });
                    graph.Edge(planFile, "requires-acceptance", obligation, "authored-plan-not-execution-evidence");
                    continue;
                }
                var columns = rows[index].Split('|');
                if (columns.Length != 5 || columns[1].Trim() is "System" or "---") continue;
                var tableObligation = graph.Node("obligation:" + index, "acceptance-obligation", columns[1].Trim(),
                    new
                    {
                        file = plan,
                        line = index + 1,
                        ownersAndChecks = columns[2].Trim(),
                        remains = columns[3].Trim(),
                        acceptance = "unverified"
                    });
                graph.Edge(planFile, "requires-acceptance", tableObligation, "authored-plan-not-execution-evidence");
            }
        }
        return new
        {
            files = sourceFiles.Length,
            declarations,
            nameReferences = references,
            unsupportedGuards = guards,
            actualTestResults = "not-inferred",
            semanticCallResolution = "not-performed"
        };
    }
}
