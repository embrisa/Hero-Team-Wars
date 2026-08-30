import type { McpServer } from "@modelcontextprotocol/server";
import { correlationId } from "../schemas/common.js";
import * as schemas from "../schemas/tools.js";
import type { TransactionService } from "../services/transaction-service.js";
import { safeCall } from "./response.js";

export function registerTransactionTools(server: McpServer, transactions: TransactionService, enabled: (name: string) => boolean = () => true): void {
  const registerTool = server.registerTool.bind(server) as (...args: any[]) => void;

  if (enabled("wc3_begin_transaction")) registerTool("wc3_begin_transaction", {
    description: "Stage an isolated transaction from an exact inspected source hash. The original map is never modified; call wc3_transaction_diff and wc3_validate_transaction before building.",
    inputSchema: schemas.beginTransactionSchema as never,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => transactions.begin(input.project_id, input.map, input.expected_source_hash, input.label, id));
  });

  if (enabled("wc3_apply_operations")) registerTool("wc3_apply_operations", {
    description: "Apply one bounded batch of typed semantic operations atomically to a transaction revision. A stale expected revision or any failed operation leaves the previous state intact; set_script_source requires project script_policy=mcp_owned_jass, a current war3map.j hash, and statically valid JASS. Use dry_run for review.",
    inputSchema: schemas.applyOperationsSchema as never,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => transactions.apply(input.project_id, input.transaction_id, input.expected_revision, input.operations, input.dry_run, id));
  });

  if (enabled("wc3_transaction_diff")) registerTool("wc3_transaction_diff", {
    description: "Read the attributable semantic diff for a staged transaction, grouped by component and target. Use it to review a revision before validation.",
    inputSchema: schemas.transactionDiffSchema as never,
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => transactions.diff(input.project_id, input.transaction_id, input.from_revision, input.to_revision));
  });

  if (enabled("wc3_validate_transaction")) registerTool("wc3_validate_transaction", {
    description: "Validate an exact staged transaction revision and persist its report. Error-free validation moves the transaction to validated; validation errors keep it modified and prevent building.",
    inputSchema: schemas.validateTransactionSchema as never,
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: true }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => transactions.validate(input.project_id, input.transaction_id, input.revision, id));
  });

  if (enabled("wc3_discard_transaction")) registerTool("wc3_discard_transaction", {
    description: "After explicit confirmation, delete only one MCP-owned transaction directory whose source hash and manifest identity match; an audit tombstone is retained outside the deleted directory.",
    inputSchema: schemas.discardSchema as never,
    annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: false }
  }, async (input: any) => {
    const id = correlationId();
    return safeCall(id, () => transactions.discard(input.project_id, input.transaction_id, input.expected_source_hash, input.confirmation, id));
  });
}
