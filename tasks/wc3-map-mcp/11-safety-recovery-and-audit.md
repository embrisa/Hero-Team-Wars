# Safety, Recovery, and Audit

## Non-negotiable protections

- Source maps are opened read-only.
- The original `HeroTeamWars_M0_2Arena.w3m` is never a default output target.
- Before staging, record source size, modified time, and SHA-256.
- All builds use unique output names and temporary staging directories.
- Promotion requires an explicit destination and expected hash.
- Recursive deletion is restricted to a resolved transaction directory beneath the configured MCP staging root.
- Unknown archive members are preserved unless an operation explicitly removes one.

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
