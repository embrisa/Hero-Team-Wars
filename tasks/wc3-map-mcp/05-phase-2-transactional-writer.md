# Phase 2 - Transactional Writer

## Objective

Allow structured changes only inside isolated transactions, with no direct source-map overwrite.

## Implement

- `wc3_begin_transaction`
- `wc3_apply_operations`
- `wc3_transaction_diff`
- `wc3_discard_transaction`
- `wc3_promote_build`
- transaction manifest, state machine, locks, and crash recovery
- typed operation schemas and optimistic source-hash checking

## Initial operations

- set map metadata;
- configure player slots and forces;
- create/update/delete regions with explicit coordinates;
- place/move/remove units and starting locations;
- create/update supported object-data records;
- replace or add an explicitly connected Lua/JASS source component;
- import/remove an asset within declared size/type rules.

Do not expose a generic `write_file` or `patch_archive` tool. New capabilities should be added as typed operations with validators.

## Transaction states

`created`, `staged`, `modified`, `validated`, `built`, `tested`, `promoted`, `discarded`, `failed`.

Only `validated` transactions can build. Only built transactions may launch. Promotion copies a selected built artifact to an explicit destination; it never replaces the original by default.

## Acceptance criteria

- A failed operation leaves the staging tree at its previous valid revision.
- Every operation records before/after values and the requesting tool call.
- Source-hash drift stops the transaction and requests a fresh inspection.
- Promotion outside configured output roots is rejected.
- No-op and rollback tests pass.
