using System.Security.Cryptography;
using OpenNV.Runtime.Content;

if (args.Length == 3 && args[0] == "--compare-directory-readers")
{
    try
    {
        var sequential = new FalloutBsaArchive(args[1]);
        var offset = new FalloutBsaArchive(args[1], useOffsetDirectoryForAudit: true);
        var sequentialData = sequential.Read(args[2]);
        var offsetData = offset.Read(args[2]);
        if (!sequentialData.AsSpan().SequenceEqual(offsetData))
            throw new InvalidDataException("Sequential and offset BSA readers returned different member bytes.");
        Console.WriteLine(
            $"OPENNV_BSA_DIRECTORY_EQUIVALENCE_OK bytes={sequentialData.Length} " +
            $"sequentialReads={sequential.DirectoryTableReadOperations} " +
            $"offsetReads={offset.DirectoryTableReadOperations}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_RESOURCE_ERROR {error.Message}");
        return 1;
    }
}

if (args.Length >= 1 && args[0] == "--resolve")
{
    if (args.Length is < 5 or > 8 || args[2] != "-" && args.Length != 8)
    {
        Console.Error.WriteLine(
            "usage: OpenNV.ResourceProbe --resolve <data-root> <mod-stack|-> <logical-path> <preferred-archive> [expected-resource-sha256] [manifest-sha256 stack-id]");
        return 2;
    }
    try
    {
        RuntimeOwnedContentSource.Configure(
            args[1],
            args[2] == "-" ? null : args[2],
            args.Length == 8 ? args[6] : null,
            args.Length == 8 ? args[7] : null);
        if (!RuntimeOwnedContentSource.Current!.TryRead(args[3], args[4], out var resolved, out var source))
        {
            Console.Error.WriteLine("OPENNV_RESOURCE_MISSING");
            return 1;
        }
        var resolvedSha256 = Convert.ToHexString(SHA256.HashData(resolved)).ToLowerInvariant();
        if (args.Length >= 6 && !string.Equals(resolvedSha256, args[5], StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"resource hash mismatch expected={args[5]} actual={resolvedSha256}");
            return 1;
        }
        Console.WriteLine(
            $"OPENNV_RESOURCE_OK bytes={resolved.Length} sha256={resolvedSha256} source={source}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_RESOURCE_ERROR {error.Message}");
        return 1;
    }
}

if (args.Length >= 1 && args[0] == "--resolve-stack")
{
    if (args.Length != 8)
    {
        Console.Error.WriteLine(
            "usage: OpenNV.ResourceProbe --resolve-stack <source-stack> <logical-path> " +
            "<preferred-archive> <expected-resource-sha256> <manifest-sha256> <stack-id> " +
            "<campaign>");
        return 2;
    }
    try
    {
        RuntimeOwnedContentSource.ConfigureSourceStack(
            args[1], args[5], args[6], args[7]);
        if (!RuntimeOwnedContentSource.Current!.TryRead(args[2], args[3], out var resolved, out var source))
        {
            Console.Error.WriteLine("OPENNV_RESOURCE_MISSING");
            return 1;
        }
        var resolvedSha256 = Convert.ToHexString(SHA256.HashData(resolved)).ToLowerInvariant();
        if (!string.Equals(resolvedSha256, args[4], StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"resource hash mismatch expected={args[4]} actual={resolvedSha256}");
            return 1;
        }
        Console.WriteLine(
            $"OPENNV_RESOURCE_OK bytes={resolved.Length} sha256={resolvedSha256} source={source} " +
            $"stackId={RuntimeOwnedContentSource.Current.StackId} " +
            $"saveIdentity={RuntimeOwnedContentSource.Current.SaveCompatibilityId}");
        return 0;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"OPENNV_RESOURCE_ERROR {error.Message}");
        return 1;
    }
}

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: OpenNV.ResourceProbe <archive.bsa> <logical-path> [expected-sha256]");
    return 2;
}

try
{
    var archive = new FalloutBsaArchive(args[0]);
    var data = archive.Read(args[1]);
    var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    if (args.Length == 3 && !string.Equals(sha256, args[2], StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"resource hash mismatch expected={args[2]} actual={sha256}");
        return 1;
    }
    Console.WriteLine($"OPENNV_RESOURCE_OK bytes={data.Length} sha256={sha256}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"OPENNV_RESOURCE_ERROR {error.Message}");
    return 1;
}
