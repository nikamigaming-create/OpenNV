using System.Numerics;

namespace OpenNV.AnimationCompare;

internal sealed record PoseComparisonOptions(double MotionDegrees = .05, double MotionMeters = .0001,
    double PhaseToleranceSeconds = .025, double DifferenceDegrees = .5, double DifferenceMeters = .001,
    double OutlierMultiplier = 4, double MinimumJumpDegrees = 2);
internal sealed record MotionPeak(double Seconds, long BeforeFrame, long Frame, double RotationDegrees,
    double PositionMeters, double? DegreesPerSecond, bool LayerChanged, string BeforeLayers, string Layers);
internal sealed record MotionSummary(int Intervals, bool Moving, double TravelMeters, double RotationTravelDegrees,
    double MaximumStepDegrees, double? MaximumDegreesPerSecond, double? P95DegreesPerSecond,
    double MaximumTransitionDegrees, double MaximumWithinLayerDegrees, int LayerChanges, MotionPeak? Peak,
    string RateTimeBasis, bool SimulationRateQualified, int RateIntervals);
internal sealed record AnimationSlotState(int Slot, int State, int Samples);
internal sealed record AnimationOwnerState(string Owner, int? State, int Samples);
internal sealed record AnimationResourceObservation(int Samples, int TotalSamples, int QualifiedLayerSamples,
    IReadOnlyList<AnimationSlotState> SlotStates, IReadOnlyList<AnimationOwnerState> UnqualifiedOwnerStates);
internal sealed record AnimationResourceComparison(string Resource, string Status,
    AnimationResourceObservation Retail, AnimationResourceObservation OpenNv);
internal sealed record TravelSummary(int MovingIntervals, int BackwardIntervals, double TravelMeters, double? MinimumFacingDot);
internal sealed record BoneComparison(string Bone, string? NativeBone, string Status, bool HierarchyJoined,
    MotionSummary OpenNvMotion, MotionSummary? RetailMotion, MotionSummary? OpenNvWorldMotion,
    MotionSummary? RetailWorldMotion, int PhaseJoinedSamples, double? MaximumRotationDifferenceDegrees,
    double? MaximumPositionDifferenceMeters, double? MaximumScaleDifference, double? AngularSpeedRatio,
    IReadOnlyList<string> Findings);
internal sealed record ActorComparison(string Reference, string? Base, int OpenNvSamples, int RetailSamples,
    TravelSummary OpenNvTravel, TravelSummary? RetailTravel, int PhaseJoinedSamples,
    IReadOnlyDictionary<string, int> UnjoinedPhaseReasons, IReadOnlyList<string> NativeNodesOutsideSkeleton,
    IReadOnlyList<BoneComparison> Bones, IReadOnlyList<string> Findings,
    IReadOnlyList<AnimationResourceComparison> AnimationResources);
internal sealed record PoseComparisonReport(string Schema, DateTimeOffset CreatedUtc, PoseComparisonOptions Options,
    string RetailInput, string OpenNvInput, string OpenNvStage, IReadOnlyList<ActorComparison> Actors,
    IReadOnlyList<string> InputProblems, bool ParityCertified = false,
    string Boundary = "Diagnostic thresholds, not acceptance specifications. Local pose residuals require matching active resources and phases. Bone identities, source bytes, controller contributions, GPU skin, audio and final pixels remain independent lanes. Unmatched data never passes.");

internal static class PoseComparison
{
    private sealed record PhaseJoin(int Index, string Reason);
    internal static PoseComparisonReport Compare(ObservedCapture retail, ObservedCapture openNv, PoseComparisonOptions options)
    {
        double[] thresholds = [options.MotionDegrees, options.MotionMeters, options.PhaseToleranceSeconds,
            options.DifferenceDegrees, options.DifferenceMeters, options.OutlierMultiplier, options.MinimumJumpDegrees];
        if (thresholds.Any(value => !double.IsFinite(value)) || options.MinimumJumpDegrees < 0 ||
            options.MotionDegrees < 0 || options.MotionMeters < 0 || options.PhaseToleranceSeconds <= 0 ||
            options.DifferenceDegrees < 0 || options.DifferenceMeters < 0 || options.OutlierMultiplier <= 1)
            throw new ArgumentException("Comparison thresholds are invalid.");
        var actors = new List<ActorComparison>();
        foreach (var identity in openNv.Actors.Keys.Union(retail.Actors.Keys, StringComparer.OrdinalIgnoreCase))
        {
            openNv.Actors.TryGetValue(identity, out var candidate);
            retail.Actors.TryGetValue(identity, out var reference);
            var findings = new List<string>();
            if (candidate is null) findings.Add("actor-not-observed-in-candidate-capture");
            candidate ??= new(identity);
            if (reference is null) findings.Add("actor-not-observed-in-reference-capture");
            else if (reference.Samples.Count == 0) findings.Add("reference-actor-has-no-observed-pose-owner");
            if (reference?.PosePublicationUnqualifiedReason is { } publication) findings.Add(publication);
            if (candidate.Base is not null && reference?.Base is not null &&
                !candidate.Base.Equals(reference.Base, StringComparison.OrdinalIgnoreCase)) findings.Add("base-identity-mismatch");
            var joins = candidate.Samples.Select(sample => reference is null || reference.Samples.Count == 0
                ? new PhaseJoin(-1, "actor-pose-unobserved") : reference.PosePublicationUnqualifiedReason is not null
                    ? new PhaseJoin(-1, "native-pose-publication-unqualified") : JoinPhase(sample, reference.Samples, options)).ToArray();
            var reasons = joins.Where(join => join.Index < 0).GroupBy(join => join.Reason)
                .ToDictionary(group => group.Key, group => group.Count());
            var nativeKeys = reference?.Samples.SelectMany(sample => sample.Bones.Keys).Distinct(StringComparer.Ordinal).ToArray() ?? [];
            var pairedNative = new HashSet<string>(StringComparer.Ordinal);
            var bones = new List<BoneComparison>();
            foreach (var key in candidate.Samples.SelectMany(sample => sample.Bones.Keys).Distinct(StringComparer.Ordinal))
            {
                var matching = nativeKeys.Where(native => native == key || native.EndsWith('\u001f' + key, StringComparison.Ordinal)).ToArray();
                var nativeKey = matching.Length == 1 ? matching[0] : null;
                var issues = new List<string>();
                if (nativeKey is null) issues.Add(matching.Length == 0 ? "bone-not-observed" : "ambiguous-bone-hierarchy");
                else pairedNative.Add(nativeKey);
                var ours = Motion(candidate.Samples, key, options, false);
                var theirs = nativeKey is not null ? Motion(reference!.Samples, nativeKey, options, false) : null;
                MotionSummary? ourWorld = candidate.Samples.Any(sample => sample.WorldBones?.ContainsKey(key) == true)
                    ? Motion(candidate.Samples, key, options, true) : null;
                MotionSummary? theirWorld = nativeKey is not null && reference!.Samples.Any(sample => sample.WorldBones?.ContainsKey(nativeKey) == true)
                    ? Motion(reference.Samples, nativeKey, options, true) : null;
                double? rotationError = null, positionError = null, scaleError = null;
                var paired = 0;
                if (nativeKey is not null)
                    for (var index = 0; index < candidate.Samples.Count; index++)
                    {
                        if (joins[index].Index < 0 || !candidate.Samples[index].Bones.TryGetValue(key, out var a) ||
                            !reference!.Samples[joins[index].Index].Bones.TryGetValue(nativeKey, out var b)) continue;
                        ++paired;
                        rotationError = Math.Max(rotationError ?? 0, Angle(a.Rotation, b.Rotation));
                        positionError = Math.Max(positionError ?? 0, Vector3.Distance(a.Position, b.Position));
                        scaleError = Math.Max(scaleError ?? 0, Vector3.Distance(a.Scale, b.Scale));
                    }
                var sameRateBasis = theirs is not null && (ours.SimulationRateQualified && theirs.SimulationRateQualified ||
                    ours.RateTimeBasis == "wall-clock" && theirs.RateTimeBasis == "wall-clock");
                double? ratio = sameRateBasis && ours.MaximumDegreesPerSecond is { } ourRate &&
                    theirs!.MaximumDegreesPerSecond is { } theirRate ? ourRate / Math.Max(theirRate, options.MotionDegrees) : null;
                if (!sameRateBasis) issues.Add("angular-rate-bases-unjoined; callback-bursts-are-not-game-time");
                if (ours.MaximumTransitionDegrees > options.MinimumJumpDegrees &&
                    ours.MaximumTransitionDegrees > Math.Max(ours.MaximumWithinLayerDegrees, options.MotionDegrees) * options.OutlierMultiplier)
                    issues.Add("abrupt-pose-change-at-layer-transition");
                if (ratio > options.OutlierMultiplier && ours.MaximumStepDegrees > options.MinimumJumpDegrees)
                    issues.Add("angular-speed-outside-recorded-reference-envelope; phase-coverage-required");
                if (rotationError > options.DifferenceDegrees) issues.Add("phase-joined-local-rotation-residual");
                if (positionError > options.DifferenceMeters) issues.Add("phase-joined-local-position-residual");
                if (paired == 0) issues.Add("no-phase-joined-pose-comparison");
                if (!candidate.HierarchyQualified) issues.Add("legacy-name-only-identity-join");
                var status = issues.Any(issue => issue.StartsWith("abrupt-", StringComparison.Ordinal) ||
                    issue.StartsWith("phase-joined-", StringComparison.Ordinal)) ? "investigate" : paired == 0 ? "unjoined" : "measured";
                bones.Add(new(Display(key), nativeKey is null ? null : Display(nativeKey), status,
                    candidate.HierarchyQualified && reference?.HierarchyQualified == true, ours, theirs, ourWorld, theirWorld,
                    paired, rotationError, positionError, scaleError, ratio, issues));
            }
            var travel = Travel(candidate.Samples, options);
            var nativeTravel = reference is null ? null : Travel(reference.Samples, options);
            if (travel.BackwardIntervals != 0) findings.Add("body-travel-opposes-facing; inspect-selected-directional-animation");
            if (reasons.ContainsKey("active-layer-set-differs")) findings.Add("active-animation-layer-set-differs");
            if (bones.Any(bone => bone.Findings.Contains("abrupt-pose-change-at-layer-transition"))) findings.Add("pose-discontinuities-at-layer-transitions");
            var resources = Resources(reference?.Samples ?? [], candidate.Samples);
            if (resources.Any(resource => resource.Status == "unobserved-in-candidate-take"))
                findings.Add("reference-animation-resources-unobserved-in-candidate-take; implementation-status-not-determined");
            actors.Add(new(candidate.Reference, candidate.Base ?? reference?.Base, candidate.Samples.Count, reference?.Samples.Count ?? 0,
                travel, nativeTravel, joins.Count(join => join.Index >= 0), reasons,
                nativeKeys.Where(key => !pairedNative.Contains(key)).Select(Display).ToArray(),
                bones.OrderByDescending(bone => bone.OpenNvMotion.MaximumTransitionDegrees).ToArray(), findings, resources));
        }
        return new("opennv-animation-comparison/v2", DateTimeOffset.UtcNow, options, retail.Input, openNv.Input,
            openNv.Stage, actors, retail.Problems.Concat(openNv.Problems).Distinct().ToArray());
    }

    private static IReadOnlyList<AnimationResourceComparison> Resources(IReadOnlyList<ObservedPose> retail,
        IReadOnlyList<ObservedPose> candidate)
    {
        static IEnumerable<string> Identities(ObservedPose sample) => sample.Layers.Where(layer => layer.State != 0)
            .Select(layer => layer.Resource).Concat((sample.AdditionalResources ?? []).Select(resource => resource.Resource));
        static AnimationResourceObservation Observe(IReadOnlyList<ObservedPose> samples, string resource)
        {
            var matching = samples.Where(sample => Identities(sample).Contains(resource, StringComparer.Ordinal)).ToArray();
            var slots = matching.SelectMany(sample => sample.Layers.Where(layer => layer.State != 0 && layer.Resource == resource)
                .Select(layer => (layer.Slot, layer.State)).Distinct()).GroupBy(pair => pair)
                .OrderBy(group => group.Key.Slot).ThenBy(group => group.Key.State)
                .Select(group => new AnimationSlotState(group.Key.Slot, group.Key.State, group.Count())).ToArray();
            var owners = matching.SelectMany(sample => (sample.AdditionalResources ?? []).Where(item => item.Resource == resource)
                .Select(item => (item.Owner, item.State)).Distinct()).GroupBy(pair => pair)
                .OrderBy(group => group.Key.Owner, StringComparer.Ordinal).ThenBy(group => group.Key.State)
                .Select(group => new AnimationOwnerState(group.Key.Owner, group.Key.State, group.Count())).ToArray();
            return new(matching.Length, samples.Count, matching.Count(sample => sample.LayerCoverageQualified &&
                sample.Layers.Any(layer => layer.State != 0 && layer.Resource == resource)), slots, owners);
        }
        return retail.Concat(candidate).SelectMany(Identities).Distinct(StringComparer.Ordinal).Select(resource =>
            {
                var native = Observe(retail, resource); var ours = Observe(candidate, resource);
                return new AnimationResourceComparison(resource, resource.Length == 0 ? "resource-identity-unobserved" :
                    ours.Samples == 0 ? "unobserved-in-candidate-take" : native.Samples == 0 ? "unobserved-in-reference-take" :
                    "observed-in-both; contribution-and-phase-unjoined", native, ours);
            }).OrderBy(resource => resource.Status == "unobserved-in-candidate-take" ? 0 : 1)
            .ThenBy(resource => resource.Resource, StringComparer.Ordinal).ToArray();
    }

    private static PhaseJoin JoinPhase(ObservedPose sample, IReadOnlyList<ObservedPose> reference, PoseComparisonOptions options)
    {
        if (!sample.LayerCoverageQualified || sample.Layers.Count == 0) return new(-1, "candidate-layer-coverage-unqualified");
        if (sample.Layers.Any(layer => layer.State != 1 || layer.Ease != 1)) return new(-1, "transition-contributions-unjoined");
        var key = Signature(sample);
        var best = -1;
        var bestError = double.PositiveInfinity;
        var signatureSeen = false;
        for (var index = 0; index < reference.Count; index++)
        {
            var other = reference[index];
            if (!other.LayerCoverageQualified || Signature(other) != key) continue;
            signatureSeen = true;
            if (other.Layers.Any(layer => layer.State != 1 || layer.Ease != 1)) continue;
            var error = 0d;
            foreach (var layer in sample.Layers)
            {
                var original = other.Layers.Single(value => value.Slot == layer.Slot && value.Resource == layer.Resource);
                if (original.Begin is not { } begin || original.End is not { } end || end <= begin ||
                    original.Frequency <= 0 || Math.Abs(layer.Weight - original.Weight) > 1e-6)
                { error = double.PositiveInfinity; break; }
                // Legacy elapsed clocks lack the source cycle mode. Only join
                // their first interval; wrapping could mistake a clamped clip
                // for a looping one. New observations expose source time.
                var seconds = layer.Elapsed ? begin + layer.Seconds * original.Frequency : layer.Seconds;
                if (seconds < begin || seconds > end) { error = double.PositiveInfinity; break; }
                var gap = Math.Abs(seconds - original.Seconds);
                error = Math.Max(error, gap / original.Frequency);
            }
            if (error < bestError) { best = index; bestError = error; }
        }
        return best >= 0 && bestError <= options.PhaseToleranceSeconds ? new(best, "source-phase-joined") :
            new(-1, signatureSeen ? "matching-phase-not-recorded" : "active-layer-set-differs");
    }

    private static MotionSummary Motion(IReadOnlyList<ObservedPose> samples, string key, PoseComparisonOptions options, bool world)
    {
        var speed = new List<double>();
        double travel = 0, angleTravel = 0, maxAngle = 0, maxTransition = 0, maxWithin = 0;
        var changes = 0; var intervals = 0;
        var bases = samples.Select(sample => sample.RateTimeBasis).Distinct(StringComparer.Ordinal).ToArray();
        var timeBasis = bases.Length == 1 ? bases[0] : bases.Length == 0 ? "unobserved" : "mixed-unqualified";
        var simulationQualified = samples.Count > 1 && samples.All(sample => sample.SimulationRateQualified);
        MotionPeak? peak = null;
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1]; var current = samples[index];
            var before = world ? previous.WorldBones : previous.Bones;
            var after = world ? current.WorldBones : current.Bones;
            if (before is null || after is null || !before.TryGetValue(key, out var a) || !after.TryGetValue(key, out var b)) continue;
            // Physics callbacks can execute in a QPC burst after a stalled draw.
            // Their nominal fixed-step clock is distinct from wall-clock and
            // remains unqualified as game time without Engine.TimeScale.
            double? delta = current.RateTimeBasis != previous.RateTimeBasis ? null :
                current.SimulationSeconds is { } now && previous.SimulationSeconds is { } then ? now - then :
                current.RateTimeBasis == "wall-clock" ? current.Seconds - previous.Seconds : null;
            if (delta is { } elapsed && (!double.IsFinite(elapsed) || elapsed <= 0))
                throw new InvalidDataException("Qualified pose interval must increase within an observation phase.");
            var angle = Angle(a.Rotation, b.Rotation);
            var distance = Vector3.Distance(a.Position, b.Position);
            var changed = Signature(previous) != Signature(current);
            var rate = delta is { } duration ? angle / duration : (double?)null;
            if (rate is { } value) speed.Add(value);
            ++intervals; travel += distance; angleTravel += angle;
            maxAngle = Math.Max(maxAngle, angle);
            if (changed) { ++changes; maxTransition = Math.Max(maxTransition, angle); }
            else maxWithin = Math.Max(maxWithin, angle);
            // Select the largest actual pose step independently of clock basis.
            if (peak is null || angle > peak.RotationDegrees)
                peak = new(current.Seconds, previous.Frame, current.Frame, angle, distance, rate,
                    changed, Signature(previous), Signature(current));
        }
        speed.Sort();
        return new(intervals, maxAngle > options.MotionDegrees || travel > options.MotionMeters,
            travel, angleTravel, maxAngle, speed.Count == 0 ? null : speed[^1],
            speed.Count == 0 ? null : speed[(int)Math.Floor((speed.Count - 1) * .95)], maxTransition, maxWithin, changes, peak,
            timeBasis, simulationQualified, speed.Count);
    }

    private static TravelSummary Travel(IReadOnlyList<ObservedPose> samples, PoseComparisonOptions options)
    {
        var moving = 0; var backwards = 0; double distance = 0; double? minimum = null;
        for (var index = 1; index < samples.Count; index++)
        {
            var displacement = samples[index].Body.Position - samples[index - 1].Body.Position;
            displacement.Y = 0;
            var length = displacement.Length(); distance += length;
            if (length <= options.MotionMeters) continue;
            var forward = Vector3.Transform(-Vector3.UnitZ, samples[index].Body.Rotation);
            forward.Y = 0;
            if (forward.LengthSquared() <= 0) throw new InvalidDataException("Actor facing has no horizontal direction.");
            var dot = Vector3.Dot(displacement / length, Vector3.Normalize(forward));
            ++moving; if (dot < 0) ++backwards;
            minimum = Math.Min(minimum ?? 1, dot);
        }
        return new(moving, backwards, distance, minimum);
    }

    private static double Angle(Quaternion a, Quaternion b)
    {
        // Compute in doubles: float dot products create false motion around
        // identical small-angle poses, precisely where this detector operates.
        var dot = (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W;
        var aa = (double)a.X * a.X + (double)a.Y * a.Y + (double)a.Z * a.Z + (double)a.W * a.W;
        var bb = (double)b.X * b.X + (double)b.Y * b.Y + (double)b.Z * b.Z + (double)b.W * b.W;
        return 2 * Math.Acos(Math.Clamp(Math.Abs(dot) / Math.Sqrt(aa * bb), 0, 1)) * 180 / Math.PI;
    }
    private static string Signature(ObservedPose sample) => string.Join("|", sample.Layers.Where(layer => layer.State != 0)
        .OrderBy(layer => layer.Slot).ThenBy(layer => layer.Resource, StringComparer.Ordinal)
        .Select(layer => $"{layer.Slot}:{layer.Resource}"));
    private static string Display(string key) => key.Replace("\u001f", " > ", StringComparison.Ordinal);
}
