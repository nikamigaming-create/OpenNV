namespace OpenNV.Runtime.World.Physics;

internal sealed partial class FalloutLivingPhysicalWorld
{
    internal FalloutLivingWorldSnapshot Capture()
    {
        RequireCurrent(); RequireCaptureBoundary();
        var islands = new Dictionary<FalloutLivingPhysicalIsland, int>(ReferenceEqualityComparer.Instance)
        { [_fixedIsland] = FalloutLivingWorldSnapshot.FixedIsland };
        var bodies = new List<FalloutLivingWorldBodySnapshot>();
        foreach (var body in _bodyAdmission)
        {
            body.RequireCurrent();
            if (body.CurrentWorld != this || body.EntityId is null || body.Island is null || !body.Backend.IsWorldAttached)
                throw new InvalidDataException("Living world capture lost actual physical body residency/identity.");
            var current = body.Island.Find();
            if (!islands.TryGetValue(current, out var island)) { island = islands.Count; islands.Add(current, island); }
            bodies.Add(new(body.Key, body.Model.Source.Skeleton.Source.Sha256, body.EntityId.Value, island, body.Motion,
                body.IncomingConstraints.Select(value => value.Key).ToArray(), body.OutgoingConstraints.Select(value => value.Key).ToArray(),
                body.LinkedConstraints.Select(value => value?.Key).ToArray()));
        }
        var constraints = _constraintAdmission.Select(value =>
        {
            value.RequireCurrent();
            if (value.CurrentWorld != this || value.Master is null || value.Slave is null || value.MasterIndex is null ||
                value.SlaveIndex is null || !value.Backend.IsWorldAttached)
                throw new InvalidDataException("Living world capture lost an actual physical constraint membership.");
            return new FalloutLivingWorldConstraintSnapshot(value.Key, value.FirstBody.Key, value.SecondBody.Key, value.Master.Key,
                value.Slave.Key, value.MasterIndex.Value, value.SlaveIndex.Value, value.Source.Header.Priority, value.DataType);
        }).ToArray();
        var saved = new FalloutLivingWorldSnapshot(FalloutLivingWorldSnapshot.CurrentSchema, Key, _nextEntityId, bodies, constraints);
        saved.Validate(); return saved;
    }

    // Construction of fresh backend resources is independent of restoring this
    // registry. Restore does not invoke Actor registration, allocate new source
    // IDs, rerun ordering, advance clocks or replay impulses.
    internal void Restore(FalloutLivingWorldSnapshot saved, IReadOnlyList<FalloutLivingActorModel> models)
    {
        RequireCurrent(); RequireCaptureBoundary(); ArgumentNullException.ThrowIfNull(saved); ArgumentNullException.ThrowIfNull(models);
        saved.Validate();
        if (saved.Key != Key || _bodies.Count != 0 || _constraints.Count != 0 || _bodyAdmission.Count != 0 || _constraintAdmission.Count != 0)
            throw new InvalidDataException("Cold living world changed identity or was not detached/empty.");
        foreach (var model in models) model.RequireCurrent();
        var bodies = models.SelectMany(model => model.Bodies).ToDictionary(body => body.Key, StringComparer.Ordinal);
        var constraints = models.SelectMany(model => model.Constraints).ToDictionary(value => value.Key, StringComparer.Ordinal);
        foreach (var captured in saved.Bodies)
        {
            if (!bodies.TryGetValue(captured.Key, out var body) || captured.SourceSha256 != body.Model.Source.Skeleton.Source.Sha256 ||
                body.Motion != captured.Motion || body.CurrentWorld is not null || body.PendingWorld is not null || body.Island is not null || body.EntityId is not null ||
                body.Backend.IsWorldAttached || body.IncomingConstraints.Count != 0 || body.OutgoingConstraints.Count != 0 ||
                !body.LinkedConstraints.Select(value => value?.Key).SequenceEqual(captured.Links))
                throw new InvalidDataException("Cold living world body source/current fields, links or detached boundary changed.");
        }
        foreach (var captured in saved.Constraints)
        {
            if (!constraints.TryGetValue(captured.Key, out var constraint) || constraint.FirstBody.Key != captured.First ||
                constraint.SecondBody.Key != captured.Second || constraint.Source.Header.Priority != captured.Priority || constraint.DataType != captured.DataType ||
                constraint.CurrentWorld is not null || constraint.PendingWorld is not null || constraint.Backend.IsWorldAttached ||
                constraint.Master is not null || constraint.Slave is not null || constraint.MasterIndex is not null || constraint.SlaveIndex is not null)
                throw new InvalidDataException("Cold living world constraint source or detached boundary changed.");
        }
        var islands = new Dictionary<int, FalloutLivingPhysicalIsland> { [FalloutLivingWorldSnapshot.FixedIsland] = _fixedIsland };
        var attachedBodies = new List<FalloutLivingPhysicalBody>();
        var attachedConstraints = new List<FalloutLivingPhysicalConstraint>();
        try
        {
            foreach (var captured in saved.Bodies)
            {
                var body = bodies[captured.Key]; _backend.AddBody(body.Backend); attachedBodies.Add(body);
                if (!body.Backend.IsWorldAttached) throw new InvalidDataException("Cold body did not enter the actual physical world.");
            }
            foreach (var captured in saved.Constraints)
            {
                var constraint = constraints[captured.Key]; _backend.AddConstraint(constraint.Backend); attachedConstraints.Add(constraint);
                if (!constraint.Backend.IsWorldAttached) throw new InvalidDataException("Cold constraint did not construct its actual current joint.");
            }
        }
        catch
        {
            // Release only the partial backend publication. No semantic world
            // state has been committed and no Source admission was reported.
            foreach (var constraint in attachedConstraints.AsEnumerable().Reverse()) _backend.RemoveConstraint(constraint.Backend);
            foreach (var body in attachedBodies.AsEnumerable().Reverse()) _backend.RemoveBody(body.Backend);
            throw;
        }
        foreach (var captured in saved.Bodies)
        {
            var body = bodies[captured.Key];
            if (!islands.TryGetValue(captured.Island, out var island)) { island = new(false); islands.Add(captured.Island, island); }
            body.CurrentWorld = this; body.Island = island; body.EntityId = captured.EntityId;
            _bodies.Add(body); _bodyAdmission.Add(body);
            body.MasterMemberships.AddRange(captured.Masters.Select(key => constraints[key]));
            body.SlaveMemberships.AddRange(captured.Slaves.Select(key => constraints[key]));
        }
        foreach (var captured in saved.Constraints)
        {
            var constraint = constraints[captured.Key]; constraint.CurrentWorld = this;
            constraint.Master = bodies[captured.Master]; constraint.Slave = bodies[captured.Slave];
            constraint.MasterIndex = captured.MasterIndex; constraint.SlaveIndex = captured.SlaveIndex;
            _constraints.Add(constraint); _constraintAdmission.Add(constraint);
        }
        _nextEntityId = saved.NextEntityId;
    }

    private void RequireCaptureBoundary()
    {
        if (_unknownState is not null) throw new NotSupportedException("Living structural world cannot capture after an unowned operation: " + _unknownState);
        if (_criticalDepth != 0 || _pendingBodies.Count != 0 || _pendingConstraints.Count != 0)
            throw new NotSupportedException("Living structural world cannot capture/restore pending or critical physical operations.");
    }
}
