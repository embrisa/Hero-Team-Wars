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
  type: z.enum(["set_map_metadata", "set_player_slot", "set_force", "create_region", "update_region", "delete_region", "place_unit", "move_unit", "remove_placed_unit", "set_object_data", "set_script_source"]),
  target: jsonObject,
  expected_revision: z.number().int().min(0).optional(),
  expected: z.unknown().optional(),
  value: z.unknown().optional(),
  rationale: z.string().min(1).max(2000),
  design_reference: z.string().regex(/^HTW-[0-9]{2}$/).optional()
}).strict().superRefine((operation, context) => {
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
