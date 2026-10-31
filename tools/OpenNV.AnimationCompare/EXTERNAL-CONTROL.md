# External engine control transport

`ExternalRigEngineControl` is the public boundary to an explicitly configured
private engine helper. `run <configuration.json> <fresh-report.json>` supplies
the `retail` configuration with `engine`, absolute `executable`, an `arguments`
array and optional absolute `workingDirectory`. Arguments use
`ProcessStartInfo.ArgumentList`; no shell command string is constructed.

For each operation the helper receives one UTF-8 `ParityRigRequest` JSON object
on stdin followed by EOF. It must write exactly one `ParityRigReceipt` object
to stdout. Logging belongs on stderr. All required receipt properties must be
present, including a null `error` on success. Unknown/duplicate properties,
multiple JSON objects, malformed UTF-8, stale identities and oversized pipes
are failures. Default bounds are 1 MiB request, 4 MiB receipt and 1 MiB stderr.

The receipt must match `run`, `request`, `engine`, `operation` and the canonical
plugin `sourceStackHash`. Completion requires exit code zero and no error.
Failed/unbound/cancelled receipts need an explicit reason and can retain
evidence. Evidence paths must be absolute existing files; transport verifies
their SHA-256 values before returning them. Pose/pixel agreement remains the
comparator's job.

Cancellation terminates only the helper process, never the game or an entire
process tree. Unconfirmed helper termination prevents further commands. Killing
a helper does not undo commands already delivered to the engine: the later
`rig.quiesce` operation must settle any entered work and release input before
the runner restores a checkpoint. Requests are never automatically retried.
The private helper must persist enough request/run state to make that guarantee
across its separate invocations. Merely accepting a command is not completion.

This adapter contains no native addresses, binary patches, hooks, game-launch
policy or process discovery. Constructing it performs no game action.
