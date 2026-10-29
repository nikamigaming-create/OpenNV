using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;

// This discovery pass does not parse tile grammar or assert retail semantics.
// It inventories literal include edges independently of the current XML parser.
internal static class MenuIncludeGraph
{
    internal static int Run(RuntimeLiveContentSource content)
    {
        var paths = content.ResourcePathsUnder("menus")
            .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var known = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<object>(); var edgeCount = 0;
        foreach (var path in paths)
        {
            graph.Add(path, []);
            if (!content.TryRead(path, null, out var bytes, out _))
            { errors.Add(new { path, reason = "winning resource could not be read" }); continue; }
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            { errors.Add(new { path, reason = "menu bytes are not UTF-8" }); continue; }
            text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
            if (text.Contains("<!--", StringComparison.Ordinal) || text.Contains("-->", StringComparison.Ordinal))
                errors.Add(new { path, reason = "unbalanced comment delimiter" });
            var starts = Regex.Matches(text, @"<\s*include\b", RegexOptions.IgnoreCase);
            var declarations = Regex.Matches(text, @"<\s*include\b[^<>]*>", RegexOptions.IgnoreCase);
            if (starts.Count != declarations.Count)
                errors.Add(new { path, reason = "incomplete include declaration", starts = starts.Count, parsed = declarations.Count });
            foreach (Match include in declarations)
            {
                var match = Regex.Match(include.Value, "^<\\s*include\\s+src\\s*=\\s*([\"'])([^\"']+)\\1\\s*/?\\s*>$", RegexOptions.IgnoreCase);
                if (!match.Success)
                { errors.Add(new { path, reason = "unrecognized include declaration", offset = include.Index }); continue; }
                var target = "menus/prefabs/" + match.Groups[2].Value.Replace('\\', '/');
                graph[path].Add(target); ++edgeCount;
                if (!known.Contains(target)) errors.Add(new { path, reason = "missing included resource", target });
            }
        }
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Visit(string path)
        {
            state[path] = 1;
            foreach (var target in graph[path])
            {
                if (!graph.ContainsKey(target)) continue;
                var next = state.GetValueOrDefault(target);
                if (next == 1) errors.Add(new { path, reason = "include cycle", target });
                else if (next == 0) Visit(target);
            }
            state[path] = 2;
        }
        foreach (var path in paths) if (state.GetValueOrDefault(path) == 0) Visit(path);
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-menu-include-graph/v1",
            resources = paths.Length, includeEdges = edgeCount, errors,
            nodes = paths.Select(path => new { path, includes = graph[path] }),
            boundary = "Literal include discovery only; tile parsing, loading order, evaluation and retail behavior are unverified." }));
        return errors.Count == 0 ? 0 : 1;
    }
}
