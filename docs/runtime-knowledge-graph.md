# Runtime knowledge graph

The development tool joins a private reference checkout to OpenNV working
source and complete selected-stack quest audits. It writes a searchable graph,
not another gameplay engine or a completion percentage. Reference code is not
required to build, test or run OpenNV.

Run from the repository root after building OpenNV.DevelopmentLab:

~~~powershell
dotnet run --project tools/OpenNV.DevelopmentLab -c Release --no-build -- runtime-knowledge-graph local/fnv-decomp-reference local/runtime-knowledge-current local/quest-audit-fnv local/quest-audit-fo3
dotnet run --project tools/OpenNV.DevelopmentLab -c Release --no-build -- runtime-knowledge-query local/runtime-knowledge-current SetStage
~~~

Output must be a fresh directory under ignored local storage and outside the
inputs. The tool reads the reference checkout but never builds it. Its revision,
every inventoried file hash, OpenNV working-file hashes and audit identities are
recorded. The graph contains private reference metadata and owned record
identities; do not publish its output.

## Nodes and relationships

- Every tracked reference file, compilation unit, function entry, fold alias
  and exception-handler tail remains in the graph. Repeated function addresses
  across build variants remain distinct entries. Unique addresses/bytes and
  handler tails reconcile separately to the reference manifest.
- C# types, methods, properties and constructors have exact source locations.
  Name references and behavior-family membership are candidate search links.
  They do not resolve overloads or prove semantic equivalence. Unclassified
  files and unnamed reference functions remain visible.
- Reference opcode declarations join the owned compiled-opcode inventory by
  numeric identity. Normalized command names link to existing literal dispatch
  sites. Neither join implements operands, proves a command's guards or grants
  FNV opcode semantics to FO3.
- Reviewed neutral contracts link reference entry points to C# owners and
  synthetic/owned test source. Their scope is explicit; test-source mentions
  never count as passing test receipts.
- Unsupported guards, retained audit failures, aggregated statement inspection
  results and the implementation plan's acceptance obligations remain separate
  evidence. A guard's existence does not show it was reached, and absence of a
  matching name does not prove a missing implementation.

The files are nodes.jsonl, edges.jsonl and summary.json. Summary contains the
ranked audit gap list, denominator checks and hashes of both graph files.
Queries refuse incomplete or modified graphs. A command returns success when
graph construction succeeded; that exit code says nothing about game readiness.

## Reproducible source selection

The quest audit also accepts an explicit JSON array of installed ESM/ESP
filenames:

~~~powershell
dotnet run --project tools/OpenNV.DevelopmentLab -c Release --no-build -- quest-graph '<installation>' local/quest-audit-fnv --plugins local/fnv-plugins.json
~~~

Selection uses the existing winning-record and dependency-order owners, with
implicit activation disabled. Missing files refuse; there is no automatic
substitution of another stack. This keeps a sandbox's activation profile from
silently reducing the audit to the base master. The output retains exact plugin
hashes, record counts and the source compatibility identity.

## First mapped repairs

The shared SCDA reader now accounts for instruction headers, reference prefixes
and payload extents independently of SCTX. Full selected FO3/FNV audits retain
instruction counts and opaque opcodes, including malformed fields. Operand
decoding and compiled execution remain open.

The effect-local storage owner now truncates finite, in-range integer assignments
toward zero, retains floats and untouched cells, and preserves exact cold-state
bits. See [the behavioral contracts](compiled-script-contracts.md) for proof
boundaries and remaining overflow behavior.

The next work follows the compiled-operand and execution-owner graph, then
observed source/dispatch failures. Physical XR, whole campaigns, audio and final
pixels still require their independent acceptance lanes.
