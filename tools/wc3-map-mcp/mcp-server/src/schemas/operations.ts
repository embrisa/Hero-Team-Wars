import * as z from "zod/v4";
import { sha256Schema, uuidSchema } from "./common.js";

const jsonObject = z.record(z.string(), z.unknown());
const scriptSourceValue = z.object({
  language: z.string().regex(/^jass$/i),
  source: z.string().min(1).max(16 * 1024 * 1024)
}).strict();
const scriptExpectedValue = z.union([
  sha256Schema,
  z.object({ sha256: sha256Schema }).strict()
]);

export const operationSchema = z.object({
  operation_id: uuidSchema,
  type: z.enum([
    "set_map_metadata", "create_player_slot", "set_player_slot", "delete_player_slot",
    "create_force", "set_force", "delete_force", "create_team", "set_team", "delete_team",
    "set_team_arena", "set_team_members", "create_region", "update_region", "rename_region",
    "delete_region", "set_region_role", "create_object_definition", "update_object_definition",
    "delete_object_definition", "set_object_reference", "place_object", "move_object",
    "update_placed_object", "remove_placed_object", "place_unit", "move_unit",
    "remove_placed_unit", "set_object_data", "set_script_source", "upsert_script_module",
    "remove_script_module", "set_trigger_mode", "create_trigger", "update_trigger",
    "move_trigger", "delete_trigger", "create_variable", "update_variable", "delete_variable"
  ]),
  target: jsonObject,
  expected_revision: z.number().int().min(0).optional(),
  expected: z.unknown().optional(),
  value: z.unknown().optional(),
  rationale: z.string().min(1).max(2000),
  design_reference: z.string().regex(/^HTW-[0-9]{2}$/).optional()
}).strict().superRefine((operation, context) => {
  if (operation.type === "upsert_script_module" || operation.type === "remove_script_module") {
    if (typeof operation.target.id !== "string" || operation.target.id.length < 1) context.addIssue({ code: "custom", path: ["target", "id"], message: "Script module operations require a stable target.id." });
    if (operation.type === "upsert_script_module" && typeof operation.value !== "object") context.addIssue({ code: "custom", path: ["value"], message: "upsert_script_module requires a typed module value." });
  }
  if (operation.type === "set_trigger_mode") {
    const value = operation.value as { mode?: unknown } | undefined;
    if (value?.mode !== "mcp_native_jass" && value?.mode !== "editor_compatible") context.addIssue({ code: "custom", path: ["value", "mode"], message: "set_trigger_mode requires mcp_native_jass or editor_compatible." });
  }
  if (operation.type !== "set_script_source") return;

  if (!jsonObject.safeParse(operation.target).success || operation.target.archive_path !== "war3map.j") {
    context.addIssue({ code: "custom", path: ["target", "archive_path"], message: "set_script_source targets only the existing war3map.j member." });
  }
  if (!scriptExpectedValue.safeParse(operation.expected).success) {
    context.addIssue({ code: "custom", path: ["expected"], message: "set_script_source requires the current war3map.j SHA-256 as expected or expected.sha256." });
  }
  if (!scriptSourceValue.safeParse(operation.value).success) {
    context.addIssue({ code: "custom", path: ["value"], message: "set_script_source requires { language: 'jass', source: '...' }." });
  }
});

export type OperationInput = z.infer<typeof operationSchema>;
