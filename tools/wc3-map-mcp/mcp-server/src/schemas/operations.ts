import * as z from "zod/v4";
import { uuidSchema } from "./common.js";

const jsonObject = z.record(z.string(), z.unknown());

export const operationSchema = z.object({
  operation_id: uuidSchema,
  type: z.enum(["set_map_metadata", "set_player_slot", "set_force", "create_region", "update_region", "delete_region", "place_unit", "move_unit", "remove_placed_unit", "set_object_data", "set_script_source"]),
  target: jsonObject,
  expected_revision: z.number().int().min(0).optional(),
  expected: z.unknown().optional(),
  value: z.unknown().optional(),
  rationale: z.string().min(1).max(2000),
  design_reference: z.string().regex(/^HTW-[0-9]{2}$/).optional()
}).strict();

export type OperationInput = z.infer<typeof operationSchema>;
