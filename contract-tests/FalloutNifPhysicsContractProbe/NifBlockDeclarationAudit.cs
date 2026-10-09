using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class NifBlockDeclarationAudit
{
    internal static int Run(string archivePath, string[] logicalPaths)
    {
        var failures = 0;
        try
        {
            var path = Path.GetFullPath(archivePath);
            var before = HashArchive(path);
            using (var archive = new FalloutBsaArchive(path))
            {
                foreach (var logicalPath in logicalPaths)
                {
                    try
                    {
                        var payload = archive.Read(logicalPath);
                        var file = FalloutNifFile.Read(payload);
                        var decoded = 0;
                        foreach (var block in file.Blocks)
                        {
                            try
                            {
                                var declaration = file.ReadObject(block.Index);
                                decoded++;
                                Console.WriteLine(JsonSerializer.Serialize(new
                                {
                                    kind = "nif-block-declaration",
                                    logicalPath,
                                    block.Index,
                                    block.TypeName,
                                    outcome = "decoded",
                                    declaration = declaration.GetType().Name,
                                }));
                            }
                            catch (Exception error)
                            {
                                failures++;
                                Console.WriteLine(JsonSerializer.Serialize(new
                                {
                                    kind = "nif-block-declaration",
                                    logicalPath,
                                    block.Index,
                                    block.TypeName,
                                    outcome = "failed",
                                    error = error.GetType().Name,
                                    detail = error.Message,
                                }));
                            }
                        }
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            kind = "nif-resource-declarations",
                            source = path,
                            archiveSha256 = before,
                            logicalPath,
                            file.Sha256,
                            file.FileVersion,
                            file.UserVersion2,
                            blocks = file.Blocks.Count,
                            decoded,
                            failed = file.Blocks.Count - decoded,
                            runtime = "unverified",
                            finalPixels = "unverified",
                        }));
                    }
                    catch (Exception error)
                    {
                        failures++;
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            kind = "nif-resource-declarations",
                            source = path,
                            archiveSha256 = before,
                            logicalPath,
                            outcome = "failed",
                            error = error.GetType().Name,
                            detail = error.Message,
                        }));
                    }
                }
            }
            var unchanged = before == HashArchive(path);
            if (!unchanged) failures++;
            Console.WriteLine($"OPENNV_NIF_BLOCK_DECLARATIONS {(failures == 0 ? "PASS" : "FAIL")} " +
                $"resources={logicalPaths.Length} failures={failures} archiveUnchanged={unchanged} " +
                "runtime=unverified finalPixels=unverified");
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine($"OPENNV_NIF_BLOCK_DECLARATIONS FAIL {error.GetType().Name}: {error.Message}");
        }
        return failures == 0 ? 0 : 1;
    }

    private static string HashArchive(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
