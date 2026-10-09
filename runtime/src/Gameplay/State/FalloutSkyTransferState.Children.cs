namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    internal void AttachChild(IFalloutSkyResetChild child)
    {
        RequireWriter(); ArgumentNullException.ThrowIfNull(child);
        if (child.Identity == Guid.Empty || child.Sky != Identity || child.Role is not ("Clouds" or "Precipitation"))
            throw new InvalidDataException("Sky child changed its selected real factory field/lifetime.");
        var source = child.Capture();
        if (source.Role != child.Role || source.SourceSha256.Length != 64 || !source.SourceSha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(source.Resource)) throw new InvalidDataException("Sky child omitted its actual resource declaration.");
        var retained = child.Role == "Clouds" ? _cloudBinding : _precipitationBinding;
        var current = child.Role == "Clouds" ? _clouds : _precipitation;
        if (current is not null && !ReferenceEquals(current, child))
            throw new InvalidOperationException("A different native Sky child still owns the source field.");
        if (retained.Disposition == FalloutSkyChildDisposition.Published && retained.Source is { } saved &&
            (saved.Role != source.Role || saved.Resource != source.Resource || saved.SourceSha256 != source.SourceSha256 ||
             !saved.CloudSlots.SequenceEqual(source.CloudSlots) || saved.Precipitation != source.Precipitation))
            throw new InvalidDataException("Cold Sky child did not reconstruct its actual retained sampler/property fields.");
        var bound = new FalloutSkyChildBinding(FalloutSkyChildDisposition.Published, null, source, retained.Factory);
        if (child.Role == "Clouds") { _clouds = child; _cloudBinding = bound; }
        else { _precipitation = child; _precipitationBinding = bound; }
        Next();
    }
    internal void LoseNativeChild(IFalloutSkyResetChild child, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (child.Role == "Clouds" && ReferenceEquals(_clouds, child))
        { _clouds = null; _cloudBinding = _cloudBinding with { Disposition = FalloutSkyChildDisposition.Unowned, Failure = reason }; }
        else if (child.Role == "Precipitation" && ReferenceEquals(_precipitation, child))
        { _precipitation = null; _precipitationBinding = _precipitationBinding with { Disposition = FalloutSkyChildDisposition.Unowned, Failure = reason }; }
        else return;
        Next();
    }
    internal void RetainNativeChildRetirementFailure(IFalloutSkyResetChild child, Exception error)
    {
        if (!ReferenceEquals(_clouds, child) && !ReferenceEquals(_precipitation, child) &&
            !_failedNativeFactories.Values.Any(owner => ReferenceEquals(owner, child)))
            throw new InvalidOperationException("A foreign native child reported Sky retirement.", error);
        _retirementFailure = error.ToString(); Next();
    }
    private void ResetChild(FalloutSkyChildBinding binding, IFalloutSkyResetChild? child,
        string role, FalloutSkyResetContext context)
    {
        if (binding.Disposition == FalloutSkyChildDisposition.ConstructorNull) return;
        if (binding.Disposition != FalloutSkyChildDisposition.Published || child is null)
            throw new NotSupportedException(binding.Failure ?? "source-Sky-" + role + "-actual-child-republication-unowned");
        if (child.Sky != Identity || child.Role != role) throw new InvalidDataException("Sky reset child is foreign to its source field.");
        var returned = child.Reset(context);
        if (returned.Context != context || returned.Child != child.Identity || returned.Role != role || returned.Completed is null)
            throw new InvalidDataException("Sky child did not return its actual operation for this invoking source call.");
        var source = child.Capture();
        if (role == "Clouds")
        {
            if (!returned.Completed.SequenceEqual(source.CloudSlots.SelectMany(slot => new[]
                { slot.Slot + "/texture-pointer-null", slot.Slot + "/property-texture-null", slot.Slot + "/property-Float32-positive-zero" })) ||
                source.CloudSlots.Any(slot => slot.Texture is not null || slot.PropertyTexture is not null || slot.BlendBits != 0))
                throw new InvalidDataException("Sky Clouds returned before every source sampler/property null/zero store completed.");
            _cloudBinding = binding with { Source = source };
        }
        else
        {
            if (binding.Factory?.Setting.Number != 1 || source.Precipitation is not { } nodes ||
                !nodes.FirstNodeConstructorNull || !nodes.SecondNodeConstructorNull || !nodes.ParentBound ||
                nodes.ScalarBits != 0 || returned.Completed.Count != 0)
                throw new NotSupportedException("source-Precipitation-nonnull-node-writer-and-release-owner-unowned");
            _precipitationBinding = binding with { Source = source };
        }
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_thread is { } bound && bound != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Sky retirement left its actual presentation-thread lease.");
        var failures = new List<Exception>();
        foreach (var child in new[] { _clouds, _precipitation }.Where(child => child is not null)
            .Concat(_failedNativeFactories.Values).Distinct())
            try { child!.Retire(); }
            catch (Exception failure) { failures.Add(failure); }
        foreach (var instance in OrderedInstances())
            try { _images.RetireSourceSkyInstance(this, instance.Identity); }
            catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0)
        {
            _retirementFailure = new AggregateException("Sky retains independent child and manager retirement failures.", failures).ToString();
            Next(); throw new AggregateException("Sky retirement did not release every still-owned source child.", failures);
        }
        _clouds = null; _precipitation = null; _failedNativeFactories.Clear(); _instances.Clear(); _retired = true; Next();
    }
}
