using System.Diagnostics;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventory
{
    private sealed record ResourceResult(long Candidates, long Bytes, bool DiscoveryComplete, object Details);

    private static ResourceResult ReadResources(RuntimeLiveContentSource content, string directory, Failures failures,
        CorpusByteEvidenceCache reuse, Stopwatch watch)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveReports = new List<object>();
        var looseReports = new List<object>();
        var storedTypes = new Dictionary<string, Layout>(StringComparer.OrdinalIgnoreCase);
        var discoveryComplete = true;
        long archiveMembers = 0;
        using (var storedRows = new StreamWriter(Path.Combine(directory, "archive-stored-extents.jsonl"), append: false))
        {
            foreach (var path in content.ArchivePaths)
            {
                string? containerSha256 = null;
                try { containerSha256 = reuse.File(path).Sha256; }
                catch (Exception error) when (SourceFailure(error))
                { failures.Add("archive-container-bytes", path, error); }
                long count = 0, storedBytes = 0;
                var archiveComplete = true;
                try
                {
                    using var archive = new FalloutBsaArchive(path);
                    foreach (var member in archive.MemberPaths.Order(StringComparer.OrdinalIgnoreCase))
                    {
                        candidates.Add(member);
                        var extent = archive.StoredExtent(member);
                        var extension = Path.GetExtension(member);
                        if (!storedTypes.TryGetValue(extension, out var layout)) storedTypes.Add(extension, layout = new());
                        layout.Add(extent.Bytes); ++count; storedBytes += extent.Bytes;
                        WriteRow(storedRows, new { archive = path, member, containerSha256,
                            extent.Offset, storedBytes = extent.Bytes, extent.Compressed,
                            lane = "stored-extent", winningByteRead = "separate-resource-row", decoding = "uninspected" });
                    }
                }
                catch (Exception error) when (SourceFailure(error))
                {
                    discoveryComplete = false; archiveComplete = false;
                    failures.Add("archive-member-discovery", path, error);
                }
                archiveMembers += count;
                archiveReports.Add(new { archive = path, containerSha256, discoveredMembers = count, storedMemberBytes = storedBytes,
                    discoveryComplete = archiveComplete, remainingMemberDenominator = archiveComplete ? "known" : "unknown" });
            }
        }
        // Discover the entire loose namespace using the existing layered reader.
        // No directory whitelist can silently exclude UI, media, extensions or new formats.
        foreach (var root in content.ContentRoots)
        {
            var layer = new FalloutContentLayers([root]);
            var count = 0;
            var rootComplete = true;
            try
            {
                foreach (var file in layer.TopLevelFiles().Keys)
                { candidates.Add(FalloutBsaArchive.CanonicalPath(file)); ++count; }
            }
            catch (Exception error) when (SourceFailure(error))
            { discoveryComplete = false; rootComplete = false; failures.Add("loose-top-level-discovery", root, error); }
            string[] directories;
            try { directories = Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception error) when (SourceFailure(error))
            {
                discoveryComplete = false; rootComplete = false; directories = [];
                failures.Add("loose-directory-discovery", root, error);
            }
            foreach (var path in directories)
            {
                try
                {
                    foreach (var logical in layer.ResourcePathsUnder(Path.GetFileName(path)))
                    { candidates.Add(logical); ++count; }
                }
                catch (Exception error) when (SourceFailure(error))
                { discoveryComplete = false; rootComplete = false; failures.Add("loose-subtree-discovery", path, error); }
            }
            looseReports.Add(new { root, discoveredPaths = count, discoveryComplete = rootComplete,
                remainingPathDenominator = rootComplete ? "known" : "unknown" });
        }
        // Warmup errors remain an independent source refusal even when a loose winner
        // or the ordinary fallback can still read some discovered resources.
        try { content.ArchiveWarmup.GetAwaiter().GetResult(); }
        catch (Exception error) when (SourceFailure(error))
        { failures.Add("ordinary-archive-index", content.SaveCompatibilityId, error); }
        long completed = 0, visited = 0, byteCount = 0;
        using var rows = new StreamWriter(Path.Combine(directory, "winning-resources.jsonl"), append: false);
        foreach (var logical in candidates.Order(StringComparer.OrdinalIgnoreCase))
        {
            string? identity = null;
            object? extentRow = null;
            long? bytes = null;
            string? sha256 = null, originalFileSha256 = null, readKind = null, archiveEncoding = null;
            var byteOutcome = "failed";
            try
            {
                if (!content.TryResolve(logical, null, out var resolved)) throw new FileNotFoundException("Discovered logical resource has no ordinary winning source: " + logical);
                identity = resolved;
                if (resolved.Contains("::", StringComparison.Ordinal))
                {
                    var extent = content.ResourceExtent(resolved);
                    extentRow = new { extent.File, extent.Offset, extent.StoredBytes, extent.Compressed };
                    var evidence = reuse.Archive(extent.File, logical, extent.Offset, extent.StoredBytes, extent.Compressed, () =>
                    {
                        if (!content.TryRead(logical, null, out var payload, out var readSource, out var readEncoding) || !readSource.Equals(resolved, FileComparison))
                            throw new InvalidDataException("Ordinary resource winner changed between resolution and byte read: " + logical);
                        if (readEncoding is null)
                            throw new InvalidDataException("Ordinary archive read has no retained successful encoding: " + logical);
                        return (payload, (string?)readEncoding.Value.ToString());
                    });
                    bytes = evidence.Bytes; sha256 = evidence.Sha256; originalFileSha256 = evidence.FileSha256; readKind = evidence.ReadKind;
                    archiveEncoding = evidence.ArchiveEncoding;
                }
                else
                {
                    // A selected loose path may be a multi-gigabyte container. Stream
                    // every original byte without creating an artificial Int32 buffer limit.
                    var evidence = reuse.File(resolved);
                    extentRow = new { file = resolved, offset = 0L, storedBytes = evidence.Bytes, compressed = false };
                    bytes = evidence.Bytes; sha256 = evidence.Sha256; originalFileSha256 = evidence.Sha256; readKind = evidence.ReadKind;
                }
                byteOutcome = "read"; ++completed; byteCount += bytes.Value;
            }
            catch (Exception error) when (SourceFailure(error))
            { failures.Add("winning-resource-bytes", identity ?? logical, error, new { logical, winningSource = identity, storedExtent = extentRow }); }
            WriteRow(rows, new { logical, winningSource = identity, storedExtent = extentRow, byteOutcome, bytes, sha256, originalFileSha256, readKind, archiveEncoding,
                decoding = "uninspected", semantics = "uninspected", runtimeAcceptance = "uninspected" });
            if (++visited % 10000 == 0) Console.Error.WriteLine($"CORPUS resources={visited}/{candidates.Count} seconds={watch.Elapsed.TotalSeconds:F1}");
        }
        return new(candidates.Count, completed, discoveryComplete, new
        {
            archives = archiveReports, archiveMembers, looseRoots = looseReports, discoveredLogicalPaths = candidates.Count,
            rows = visited, completeWinningByteEvidence = completed, completeWinningBytes = byteCount,
            storedAssetTypes = storedTypes.OrderByDescending(pair => pair.Value.Count).Select(pair => new { extension = pair.Key, layout = pair.Value.Report() }).ToArray(),
            remainingDenominator = discoveryComplete ? "known" : "unknown", discoveryComplete,
            internalInactiveContainerDiscovery = "uninspected", formatDecoding = "uninspected", nativeConsumers = "uninspected"
        });
    }
}
