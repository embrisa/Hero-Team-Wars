import * as z from "zod/v4";
import { sha256Schema, uuidSchema } from "./common.js";

const jsonObject = z.record(z.string(), z.unknown());
const identifier = z.string().regex(/^[A-Za-z_][A-Za-z0-9_]*$/);
const moduleIdentifier = z.string().regex(/^[A-Za-z_][A-Za-z0-9_.-]*$/);
const rawcode = z.string().regex(/^[\x20-\x7E]{4}$/);

const scriptSourceValue = z.object({ language: z.string().regex(/^jass$/i), source: z.string().min(1).max(16 * 1024 * 1024) }).strict();
const scriptExpectedValue = z.union([sha256Schema, z.object({ sha256: sha256Schema }).strict()]);

const eventSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("map_initialization") }).strict(),
  z.object({ type: z.literal("periodic_timer"), period: z.number().positive(), repeat: z.boolean().optional() }).strict(),
  z.object({ type: z.literal("elapsed_time"), seconds: z.number().nonnegative() }).strict(),
  z.object({ type: z.literal("player_chat"), player_id: z.number().int().min(1).max(24), message: z.string().min(1), exact: z.boolean().optional() }).strict(),
  z.object({ type: z.literal("unit_death"), player_id: z.number().int().min(1).max(24).optional(), unit_rawcode: rawcode.optional() }).strict(),
  z.object({ type: z.literal("region_entry"), region_id: identifier.optional(), region_name: z.string().min(1).optional() }).strict().refine(value => value.region_id !== undefined || value.region_name !== undefined, "region_entry requires region_id or region_name"),
  z.object({ type: z.literal("player_state_change"), player_id: z.number().int().min(1).max(24), state: identifier, operator: z.enum(["equal", "not_equal", "less", "less_equal", "greater", "greater_equal"]), value: z.number() }).strict(),
  z.object({ type: z.literal("custom_event"), name: identifier }).strict()
]);

const conditionSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("always") }).strict(),
  z.object({ type: z.literal("boolean_variable"), variable_id: identifier, value: z.boolean().optional() }).strict(),
  z.object({ type: z.literal("integer_compare"), variable_id: identifier, operator: z.enum(["equal", "not_equal", "less", "less_equal", "greater", "greater_equal"]), value: z.number().int() }).strict(),
  z.object({ type: z.literal("real_compare"), variable_id: identifier, operator: z.enum(["equal", "not_equal", "less", "less_equal", "greater", "greater_equal"]), value: z.number() }).strict(),
  z.object({ type: z.literal("function"), function: identifier }).strict()
]);

const actionSchema: z.ZodTypeAny = z.lazy(() => z.discriminatedUnion("type", [
  z.object({ type: z.literal("set_variable"), variable_id: identifier, value: z.unknown() }).strict(),
  z.object({ type: z.literal("branch"), condition: conditionSchema, then: z.array(actionSchema).min(1), else: z.array(actionSchema).optional() }).strict(),
  z.object({ type: z.literal("create_timer"), variable_id: identifier, period: z.number().positive(), repeating: z.boolean().optional(), callback: identifier.optional() }).strict(),
  z.object({ type: z.literal("unit_operation"), operation: z.enum(["kill_trigger_unit", "remove_trigger_unit"]) }).strict(),
  z.object({ type: z.literal("group_operation"), operation: z.enum(["add_trigger_unit", "remove_trigger_unit", "destroy"]), group_variable_id: identifier }).strict(),
  z.object({ type: z.literal("message"), text: z.string().min(1) }).strict(),
  z.object({ type: z.literal("phase_transition"), phase: z.number().int().optional() }).strict(),
  z.object({ type: z.literal("call_function"), function: identifier }).strict(),
  z.object({ type: z.literal("return") }).strict()
]));

const referenceSchema = z.object({
  variables: z.array(identifier).optional(), variable_ids: z.array(identifier).optional(), regions: z.array(z.string().min(1)).optional(),
  objects: z.array(z.string().min(1)).optional(), object_ids: z.array(z.string().min(1)).optional(), rawcodes: z.array(rawcode).optional(),
  players: z.array(z.string().min(1)).optional(), forces: z.array(z.string().min(1)).optional(), functions: z.array(identifier).optional()
}).strict();
const regionId = z.string().regex(/^region:[0-9]+$/);
const regionTargetSchema = z.object({ id: regionId.optional(), region_id: regionId.optional(), name: z.string().min(1).optional(), creation_number: z.number().int().min(0).optional() }).strict().refine(value => Object.keys(value).length > 0, "Region target requires id, region_id, name, or creation_number");
const regionCreateSchema = z.object({
  id: regionId.optional(), name: z.string().min(1), min_x: z.number().finite(), min_y: z.number().finite(), max_x: z.number().finite(), max_y: z.number().finite(),
  creation_number: z.number().int().min(0).optional(), weather: z.string().optional(), ambient_sound: z.string().optional(), color_argb: z.number().int().optional()
}).strict().superRefine((value, context) => {
  if (value.min_x > value.max_x || value.min_y > value.max_y) context.addIssue({ code: "custom", path: ["min_x"], message: "Region minimum bounds cannot exceed maximum bounds." });
  if (value.id !== undefined && value.creation_number !== undefined && value.id !== `region:${value.creation_number}`) context.addIssue({ code: "custom", path: ["id"], message: "Region id must be region:<creation_number>." });
});
const regionUpdateSchema = z.object({ min_x: z.number().finite().optional(), min_y: z.number().finite().optional(), max_x: z.number().finite().optional(), max_y: z.number().finite().optional(), weather: z.string().optional(), ambient_sound: z.string().optional(), color_argb: z.number().int().optional() }).strict().refine(value => Object.keys(value).length > 0, "Region update requires at least one field");
const regionReferenceRewritePlanSchema = z.object({
  mcp_owned: z.union([z.enum(["rewrite", "unchanged", "not_applicable"]), z.array(z.unknown())]),
  editor_trigger: z.union([z.enum(["rewrite", "unchanged", "not_applicable"]), z.array(z.unknown())]),
  custom_text: z.union([z.enum(["rewrite", "unchanged", "not_applicable"]), z.array(z.unknown())]),
  unresolved: z.array(z.unknown()).optional()
}).strict();
const regionRenameSchema = z.object({ name: z.string().min(1), reference_rewrite_plan: regionReferenceRewritePlanSchema }).strict();
const regionRoleSchema = z.object({ role: z.enum(["arena", "entrance", "backline", "camp", "spawn", "cleanup", "camera_boundary"]) }).strict();
const sourceLocationSchema = z.object({ path: z.string().min(1), line: z.number().int().positive().optional(), column: z.number().int().positive().optional() }).strict();
const editorEncodingSchema = z.object({ status: z.enum(["unsupported", "available", "not_applicable"]), version: z.string().optional(), reason: z.string().optional() }).strict();

export const gameplayModuleSchema = z.object({
  id: moduleIdentifier, path: z.string().min(1).optional(), source: z.string().min(1).max(2 * 1024 * 1024), source_sha256: sha256Schema.optional(),
  enabled: z.boolean().optional(), dependencies: z.array(moduleIdentifier).default([]), public_symbols: z.array(identifier).default([]), provenance: z.string().optional(), capability: z.string().optional()
}).strict();

export const gameplayVariableSchema = z.object({
  id: identifier, name: identifier, type: z.enum(["integer", "real", "boolean", "string", "handle", "timer", "trigger", "unit", "group", "region", "rect", "player", "force"]),
  initial: z.unknown().optional(), default_value: z.unknown().optional(), value: z.unknown().optional(), dependencies: z.array(identifier).default([]), provenance: z.string().optional(), capability: z.string().optional()
}).strict();

export const gameplayTriggerSchema = z.object({
  id: identifier, name: z.string().min(1), folder_path: z.string().min(1), enabled: z.boolean().optional(), initially_on: z.boolean().optional(),
  events: z.array(eventSchema).min(1), conditions: z.array(conditionSchema).default([]), actions: z.array(actionSchema).default([]), references: referenceSchema.optional(),
  source_location: sourceLocationSchema.optional(), handler_name: identifier.optional(), dependencies: z.array(identifier).default([]), editor_encoding: editorEncodingSchema.optional(), provenance: z.string().optional(), capability: z.string().optional()
}).strict();

const gameplayTriggerUpdateSchema = gameplayTriggerSchema.partial().omit({ id: true });
const gameplayVariableUpdateSchema = gameplayVariableSchema.partial().omit({ id: true });

export const operationSchema = z.object({
  operation_id: uuidSchema,
  type: z.enum([
    "set_map_metadata", "create_player_slot", "set_player_slot", "delete_player_slot", "create_force", "set_force", "delete_force", "create_team", "set_team", "delete_team",
    "set_team_arena", "set_team_members", "create_region", "update_region", "rename_region", "delete_region", "reorder_regions", "set_region_role", "create_object_definition", "update_object_definition",
    "delete_object_definition", "set_object_reference", "place_object", "move_object", "update_placed_object", "remove_placed_object", "place_unit", "move_unit", "remove_placed_unit",
    "set_object_data", "set_script_source", "upsert_script_module", "remove_script_module", "set_trigger_mode", "create_trigger", "update_trigger", "move_trigger", "delete_trigger",
    "create_variable", "update_variable", "delete_variable"
  ]),
  target: jsonObject, expected_revision: z.number().int().min(0).optional(), expected: z.unknown().optional(), value: z.unknown().optional(), rationale: z.string().min(1).max(2000), design_reference: z.string().regex(/^HTW-[0-9]{2}$/).optional()
}).strict().superRefine((operation, context) => {
  const targetId = operation.target.id ?? operation.target.trigger_id ?? operation.target.variable_id;
  const requireTargetId = (module = false) => { const schema = module ? moduleIdentifier : identifier; if (typeof targetId !== "string" || !schema.safeParse(targetId).success) context.addIssue({ code: "custom", path: ["target"], message: "Gameplay operations require a stable identifier target." }); };
  const requireExpected = () => { if (operation.expected === undefined) context.addIssue({ code: "custom", path: ["expected"], message: "This gameplay operation requires the expected prior value or source hash." }); };

  if (operation.type === "upsert_script_module") {
    requireTargetId(true);
    if (!gameplayModuleSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "upsert_script_module requires a complete typed module with source text." });
    if (operation.expected !== undefined && !sha256Schema.safeParse(operation.expected).success && !z.object({ source_sha256: sha256Schema }).strict().safeParse(operation.expected).success && !jsonObject.safeParse(operation.expected).success) context.addIssue({ code: "custom", path: ["expected"], message: "Module expected must be a source SHA-256 or prior module object." });
  }
  if (operation.type === "remove_script_module") { requireTargetId(true); requireExpected(); }
  if (["create_trigger", "update_trigger", "move_trigger", "delete_trigger"].includes(operation.type)) {
    requireTargetId();
    if (operation.type === "create_trigger") {
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_trigger requires an absent expected value." });
      if (!gameplayTriggerSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_trigger requires a complete typed trigger value." });
    } else {
      requireExpected();
      const schema = operation.type === "move_trigger" ? z.object({ folder_path: z.string().min(1) }).strict() : gameplayTriggerUpdateSchema;
      if (!schema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: `${operation.type} has an invalid typed trigger value.` });
    }
  }
  if (["create_variable", "update_variable", "delete_variable"].includes(operation.type)) {
    requireTargetId();
    if (operation.type === "create_variable") {
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_variable requires an absent expected value." });
      if (!gameplayVariableSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_variable requires a complete typed variable value." });
    } else {
      requireExpected();
      if (operation.type === "update_variable" && !gameplayVariableUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "update_variable has an invalid typed variable value." });
    }
  }
  if (["create_region", "update_region", "rename_region", "delete_region", "reorder_regions", "set_region_role"].includes(operation.type)) {
    if (operation.type === "create_region") {
      if (Object.keys(operation.target).length !== 0) context.addIssue({ code: "custom", path: ["target"], message: "create_region requires an empty target." });
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_region requires an absent expected value." });
      if (!regionCreateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_region has an invalid typed region value." });
    } else if (operation.type === "reorder_regions") {
      if (Object.keys(operation.target).length !== 0) context.addIssue({ code: "custom", path: ["target"], message: "reorder_regions requires an empty target." });
      if (!z.array(regionId).safeParse(operation.expected).success) context.addIssue({ code: "custom", path: ["expected"], message: "reorder_regions requires the complete prior region-id order." });
      if (!z.object({ region_ids: z.array(regionId).min(1) }).strict().safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "reorder_regions requires region_ids." });
    } else {
      if (!regionTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Region operations require an id, name, region_id, or creation_number target." });
      if (operation.type !== "set_region_role" && operation.expected === undefined) context.addIssue({ code: "custom", path: ["expected"], message: "This region operation requires the complete expected prior region record." });
      if (operation.type === "update_region" && !regionUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "update_region has an invalid typed region update." });
      if (operation.type === "rename_region" && !regionRenameSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "rename_region requires a name and complete reference_rewrite_plan." });
      if (operation.type === "set_region_role" && !regionRoleSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_region_role requires a supported role." });
    }
  }
  if (operation.type === "set_trigger_mode") {
    requireExpected();
    if (!z.object({ mode: z.enum(["mcp_native", "mcp_native_jass", "editor_compatible"]) }).strict().safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_trigger_mode requires mcp_native_jass or editor_compatible." });
  }
  if (operation.type !== "set_script_source") return;
  if (operation.target.archive_path !== "war3map.j") context.addIssue({ code: "custom", path: ["target", "archive_path"], message: "set_script_source targets only the existing war3map.j member." });
  if (!scriptExpectedValue.safeParse(operation.expected).success) context.addIssue({ code: "custom", path: ["expected"], message: "set_script_source requires the current war3map.j SHA-256 as expected or expected.sha256." });
  if (!scriptSourceValue.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_script_source requires { language: 'jass', source: '...' }." });
});

export type OperationInput = z.infer<typeof operationSchema>;
