# Safety, Recovery, and Audit

## Non-negotiable protections

- Source maps are opened read-only.
- The original `HeroTeamWars_M0_2Arena.w3m` is never a default output target.
- Before staging, record source size, modified time, and SHA-256.
- All builds use unique output names and temporary staging directories.
- Promotion requires an explicit destination and expected hash.
- Recursive deletion is restricted to a resolved transaction directory beneath the configured MCP staging root.
- Unknown archive members are preserved unless an operation explicitly removes one.
- A transaction spanning multiple serializers commits all changed members as
  one staged revision or commits none of them.
- Unknown fields inside a supported member are preserved and immutable unless a
  field-specific serializer and fixture explicitly enables them.
- Generated gameplay source is treated as a derived artifact with recorded
  module/trigger/variable inputs, not as an untracked side effect.

## Recovery artifacts

Each transaction contains:

- source manifest and hash;
- engine/dependency versions;
- requested operations;
- per-operation result;
- before/after semantic diff;
- validation report;
- build hash and path;
- launch/test evidence;
- failure log if applicable.

Feature transactions additionally retain:

- source/module/trigger/variable manifests and hashes;
- component serializer versions and capability profile;
- cross-component reference graph before and after;
- generated JASS source and hash, when gameplay source is involved;
- per-member replacement hashes and preserved-opaque-member hashes.

## Concurrency

Use a per-project lock for mutation/build operations. Read-only inspection can run concurrently against an immutable source hash. If the source changes, active staged transactions become stale and cannot promote.

## Logging

- Protocol output must contain MCP/engine messages only.
- Human diagnostics use structured log files with correlation IDs.
- Never log API tokens, environment secrets, or full arbitrary environment dumps.
- Log external commands as executable plus argument array, with sensitive values redacted.

## Audit questions every mutating tool must answer

1. What source hash was used?
2. What exact semantic values changed?
3. Where is the recovery snapshot?
4. Which validations ran and what failed or warned?
5. What build was produced?
6. Was it opened, loaded, smoke-tested, or playtested?

## Windows path-containment algorithm

For every path-bearing operation:

1. Start from a configured absolute root, never a tool-provided root.
2. Combine the root with a validated relative path.
3. Reject rooted input, `..` segments, empty terminal name, wildcard characters for destructive operations, and unsupported alternate data stream syntax.
4. Resolve the full path with platform APIs.
5. Resolve the intended parent/root and account for existing junctions/symlinks.
6. Compare path segments case-insensitively on Windows. A raw string prefix is insufficient because `C:\Safe` also prefixes `C:\Safe-Evil`.
7. Require the resolved target to be strictly beneath the root for deletion; the root itself is never valid.
8. Re-check immediately before the operation to reduce time-of-check/time-of-use drift.

Implement and unit-test this in one reusable policy module. Do not duplicate partial checks across tools.

## Atomic-file pattern

For manifests, revisions, and reports:

1. serialize to a unique temporary file in the same directory;
2. flush and close;
3. parse/validate the temporary file;
4. replace/rename atomically;
5. never leave a partially written file at the authoritative path.

Map build files follow a similar temporary-directory/final-rename pattern.

## Lock rules

- One project mutation/build lock.
- Optional narrower transaction lock only if lock ordering is documented.
- Lock record includes PID, process start identity if available, operation, correlation ID, and creation UTC.
- A stale lock is not deleted solely because it is old; verify owning process identity first.
- Read inspection may run concurrently only against a captured immutable source hash.
- A component serializer may not write directly to the accepted source or to a
  sibling transaction. It writes a transaction-local member set that the
  archive builder commits only after all enabled serializers succeed.

## Deletion checklist

Before `wc3_discard_transaction` deletes anything:

- resolved target is a direct child/descendant of configured transaction root;
- target is not root;
- target directory name equals requested UUID;
- manifest exists and declares that UUID/project;
- expected source hash matches manifest;
- target is not referenced as an accepted/promoted build source;
- audit tombstone is safely stored outside the deleted directory;
- deletion result is verified.

For a multi-component transaction, discard must retain the complete manifest,
source/module hashes, semantic diff, and failure/tombstone record outside the
deleted transaction directory.

## Recovery procedure

If a transaction/build crashes:

1. preserve logs and last valid manifest;
2. identify last valid revision by parsing manifests, not filename alone;
3. rehash staged source and generated artifacts;
4. mark transaction `failed` with correlation ID;
5. do not auto-resume mutations;
6. allow a read-only recovery report;
7. create a new revision/transaction for retry unless exact idempotent replay is proven.

If one member serializer succeeds and another fails, the transaction remains
failed with its staged snapshot intact; no partial archive is promotable. The
recovery report must identify the successful and failed member serializers and
the exact revision that was last valid.
