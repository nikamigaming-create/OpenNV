using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

/// <summary>
/// The smallest arrival movement contract which can be proved without a renderer.
/// Every tile in the proof is selected from the verified Map 3 walk mask; this is
/// deliberately not an authored acceptance-track route.
/// </summary>
internal sealed record Fo2ArroyoArrivalFirstBeat(
    string MapSha256,
    string WalkMaskSha256,
    int ArrivalTile,
    IReadOnlyList<int> LegalPathTiles,
    int BoundaryTile,
    int RejectedCandidateTile,
    IReadOnlySet<int> ArrivalComponent)
{
    internal static Fo2ArroyoArrivalFirstBeat Build(
        Fo2ArroyoCavesPresentationCatalog catalog)
    {
        var component = RequireArrivalComponent(catalog);

        // The exact exit-grid hex may sit inside a legal component rather than
        // beside a source-blocked hex. Breadth-first traversal selects the
        // shortest source-legal path to its nearest boundary; sorted edges make
        // equal-length paths deterministic without embedding a Map 3 tile.
        var predecessors = new Dictionary<int, int>
        {
            [catalog.ArrivalTile] = catalog.ArrivalTile,
        };
        var pending = new Queue<int>();
        pending.Enqueue(catalog.ArrivalTile);
        var boundary = -1;
        while (pending.Count > 0)
        {
            var tile = pending.Dequeue();
            if (tile != catalog.ArrivalTile &&
                Fo1HexMath.Neighbors(tile).Any(neighbor => !component.Contains(neighbor)))
            {
                boundary = tile;
                break;
            }
            foreach (var neighbor in Fo1HexMath.Neighbors(tile)
                         .Where(component.Contains)
                         .Order())
            {
                if (!predecessors.TryAdd(neighbor, tile))
                    continue;
                pending.Enqueue(neighbor);
            }
        }
        if (boundary < 0)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 arrival has no source-bound reachable boundary tile.");
        var legalPath = PathFromArrival(
            catalog.ArrivalTile,
            boundary,
            predecessors);
        if (legalPath.Count == 0)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 arrival boundary path has no source-legal movement.");
        var rejectedCandidate = Fo1HexMath.Neighbors(boundary)
            .Where(tile => !component.Contains(tile))
            .Order()
            .FirstOrDefault(-1);
        if (rejectedCandidate < 0)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 first legal step has no source-blocked neighbor.");

        return new Fo2ArroyoArrivalFirstBeat(
            catalog.MapSha256,
            catalog.WalkMaskSha256,
            catalog.ArrivalTile,
            legalPath,
            boundary,
            rejectedCandidate,
            component);
    }

    internal static IReadOnlySet<int> RequireArrivalComponent(
        Fo2ArroyoCavesPresentationCatalog catalog)
    {
        if (catalog.ArrivalTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            catalog.Walkable.Count != Fo1HexMath.Width * Fo1HexMath.Height ||
            !catalog.Walkable[catalog.ArrivalTile] ||
            Fo2TempleMovementConsumer.MaskSha256(catalog.Walkable) !=
                catalog.WalkMaskSha256)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 arrival has no verified source walk mask.");

        var component = EntryComponent(catalog.ArrivalTile, catalog.Walkable);
        if (component.Count != catalog.ArrivalComponentHexes)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 arrival component differs from its source contract.");
        return component;
    }

    private static IReadOnlyList<int> PathFromArrival(
        int arrivalTile,
        int boundaryTile,
        IReadOnlyDictionary<int, int> predecessors)
    {
        var reversed = new List<int>();
        for (var cursor = boundaryTile; cursor != arrivalTile; cursor = predecessors[cursor])
            reversed.Add(cursor);
        reversed.Reverse();
        return reversed;
    }

    internal Fo2ArroyoArrivalFirstBeatCursor CreateCursor() => new(this);

    private static HashSet<int> EntryComponent(int arrivalTile, IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { arrivalTile };
        var queue = new Queue<int>();
        queue.Enqueue(arrivalTile);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited;
    }
}

/// <summary>Discrete source-mask movement used solely by the headless proof.</summary>
internal sealed class Fo2ArroyoArrivalFirstBeatCursor
{
    private readonly Fo2ArroyoArrivalFirstBeat _contract;

    internal Fo2ArroyoArrivalFirstBeatCursor(Fo2ArroyoArrivalFirstBeat contract)
    {
        _contract = contract;
        CurrentTile = contract.ArrivalTile;
    }

    internal int CurrentTile { get; private set; }
    internal int CompletedLegalMoves { get; private set; }

    internal bool TryStep(int destinationTile)
    {
        if (!_contract.ArrivalComponent.Contains(destinationTile) ||
            !Fo1HexMath.Neighbors(CurrentTile).Contains(destinationTile))
            return false;
        CurrentTile = destinationTile;
        CompletedLegalMoves++;
        return true;
    }
}

internal static class Fo2ArroyoArrivalFirstBeatProof
{
    internal static void Run(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoPlayerPresentationCatalog playerPresentation,
        Fo2HumanoidDonorContract humanoidDonor,
        string reportPath)
    {
        if (playerPresentation.SourceProfileId != catalog.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 player identity and MAP source profiles differ.");
        var output = Path.GetFullPath(reportPath);
        if (File.Exists(output) || Directory.Exists(output))
            throw new InvalidOperationException(
                $"Refusing to overwrite Fallout 2 Map 3 first-beat proof: {output}");
        var parent = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException(
                "Fallout 2 Map 3 first-beat report must have a parent directory.");

        var firstBeat = Fo2ArroyoArrivalFirstBeat.Build(catalog);
        var cursor = firstBeat.CreateCursor();
        var legalPathAccepted = firstBeat.LegalPathTiles
            .Select(cursor.TryStep)
            .ToArray();
        var legalMoveAccepted = legalPathAccepted.All(value => value);
        var tileAfterLegalPath = cursor.CurrentTile;
        var invalidMoveRejected = !cursor.TryStep(firstBeat.RejectedCandidateTile);
        var passed = legalMoveAccepted && invalidMoveRejected &&
            firstBeat.ArrivalTile == catalog.ArrivalTile &&
            tileAfterLegalPath == firstBeat.BoundaryTile &&
            cursor.CurrentTile == firstBeat.BoundaryTile &&
            cursor.CompletedLegalMoves == firstBeat.LegalPathTiles.Count &&
            !firstBeat.ArrivalComponent.Contains(firstBeat.RejectedCandidateTile);
        if (!passed)
            throw new InvalidOperationException(
                "Fallout 2 Map 3 source-bound first movement beat failed closed.");

        Directory.CreateDirectory(parent);
        var report = new
        {
            schema = "opennv-fo2-arroyo-arrival-first-beat-proof/v2",
            status = "pass-source-bound-discrete-arrival-movement-headless-not-rendered",
            campaign = "Fallout2",
            slice = "ArroyoCaves",
            source = new
            {
                mapIndex = Fo2ArroyoCavesPresentationCatalog.MapIndex,
                elevation = Fo2ArroyoCavesPresentationCatalog.Elevation,
                cacheManifestSha256 = catalog.ManifestSha256,
                sourceManifestSha256 = catalog.SourceManifestSha256,
                sourceProfileId = catalog.SourceProfileId,
                mapSha256 = firstBeat.MapSha256,
                walkMaskSha256 = firstBeat.WalkMaskSha256,
                arrivalComponentHexes = firstBeat.ArrivalComponent.Count,
            },
            playerPresentation = new
            {
                fid = playerPresentation.Source.Fid,
                logicalPath = playerPresentation.Source.LogicalPath,
                sourceSha256 = playerPresentation.Source.SourceSha256,
                identityAuthority = "owned-fallout2-gcd-pro-fid-frm",
            },
            ownedHumanoidDonor = new
            {
                manifestSha256 = humanoidDonor.ManifestSha256,
                sourceActorFormId = humanoidDonor.SourceActorFormId,
                sexes = humanoidDonor.Variants.Keys.Order().ToArray(),
                fullBodyPresentationOnly = true,
            },
            arrival = new
            {
                tile = firstBeat.ArrivalTile,
                exactExitGridPlacement = true,
            },
            movement = new
            {
                legalPathTiles = firstBeat.LegalPathTiles,
                legalPathAccepted,
                expectedLegalMoves = firstBeat.LegalPathTiles.Count,
                legalDestinationTile = firstBeat.BoundaryTile,
                legalMoveAccepted,
                tileAfterLegalPath,
                rejectedCandidateTile = firstBeat.RejectedCandidateTile,
                invalidMoveRejected,
                finalTile = cursor.CurrentTile,
                completedLegalMoves = cursor.CompletedLegalMoves,
                sourceComponentOnly = true,
            },
            promotion = new
            {
                sharedOwnedHumanoidDonorAdmitted = true,
                exactArrivalSpawnContract = true,
                deterministicLegalPath = true,
                invalidSourceMoveBlocked = true,
                rendered = false,
                continuousPhysicsProved = false,
                interactive = false,
                playableCampaign = false,
                retailParity = false,
            },
            unsupported = new[]
            {
                "continuous physics, camera, and rendered-player acceptance",
                "multihex footprint expansion beyond the source central hex",
                "outbound exit execution, combat, scripts, saves, and retail parity",
            },
            windowsAppControlUsed = false,
            foregroundInputInjected = false,
        };
        File.WriteAllText(
            output,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);
    }
}
