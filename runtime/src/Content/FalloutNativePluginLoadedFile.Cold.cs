namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFile
{
    internal FalloutNativeLoadedFileSnapshot Capture()
    {
        RequireIdle(); return new(_parser.Capture(), _binary.Capture(), _metadata.Capture(), _revision);
    }

    internal void Restore(FalloutNativeLoadedFileSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved); RequireIdle();
        if (saved.Parser is null || saved.Binary is null || saved.Metadata is null || saved.Revision < 0)
            throw new InvalidDataException("Cold loaded contributor lacks its real parser/buffer/metadata owner.");

        // Both existing owners validate independently against original bytes.
        // Preflight uses only new read-only leases and in-memory state, never
        // a decoded asset cache, original write or persistent transformed input.
        FalloutNativePluginSourceFile? parser = null;
        FalloutNativePluginBinaryFile? binary = null;
        Exception? first = null;
        try
        {
            parser = new(_records, _context); parser.Restore(saved.Parser);
            binary = new(_records, _context, _binary.Construction, _binary.Capacity); binary.Restore(saved.Binary);
        }
        catch (Exception error) { first = error; throw; }
        finally
        {
            var errors = new List<Exception>();
            try { parser?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { binary?.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0)
            {
                if (first is not null) errors.Insert(0, first);
                throw new AggregateException("Cold contributor validation/temporary retirement failed.", errors);
            }
        }
        var metadata = _metadata.ValidateRestore(saved.Metadata);
        var beforeParser = _parser.Capture(); var beforeBinary = _binary.Capture();
        var beforeMetadata = _metadata.Capture(); var beforeRecord = _record; var beforeRevision = _revision;
        try
        {
            _parser.Restore(saved.Parser); _binary.Restore(saved.Binary); _metadata.RestoreValidated(metadata);
            _record = saved.Parser.RecordHeaderOffset is { } at ?
                _context.Plugin.Records.Single(record => record.HeaderOffset == at) : null;
            _revision = saved.Revision;
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try { _parser.Restore(beforeParser); } catch (Exception rollback) { errors.Add(rollback); }
            try { _binary.Restore(beforeBinary); } catch (Exception rollback) { errors.Add(rollback); }
            try { _metadata.RestoreValidated(beforeMetadata); } catch (Exception rollback) { errors.Add(rollback); }
            _record = beforeRecord; _revision = beforeRevision;
            if (errors.Count != 1)
            {
                _fault = new AggregateException("Cold contributor retains restore/rollback failures.", errors);
                throw _fault;
            }
            throw;
        }
    }
}
