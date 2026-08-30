import * as z from "zod/v4";
import { sha256Schema, uuidSchema } from "./common.js";

const jsonObject = z.record(z.string(), z.unknown());
const identifier = z.string().regex(/^[A-Za-z_][A-Za-z0-9_]*$/);
const moduleIdentifier = z.string().regex(/^[A-Za-z_][A-Za-z0-9_.-]*$/);
const rawcode = z.string().regex(/^[\x20-\x7E]{4}$/);
const objectCategory = z.enum(["unit", "ability", "item", "destructable", "doodad", "buff", "upgrade"]);
const objectModificationSchema = z.object({
  id: rawcode, type: z.enum(["Int", "Real", "Unreal", "String", "Bool", "Char"]), value: z.union([z.string(), z.number(), z.boolean()]),
  level: z.number().int().nonnegative().optional(), pointer: z.number().int().nonnegative().optional(), variation: z.number().int().nonnegative().optional()
}).strict().superRefine((value, context) => {
  if (value.type === "Char" && (typeof value.value !== "string" || value.value.length !== 1)) context.addIssue({ code: "custom", path: ["value"], message: "Char modifications require exactly one character." });
  if (["Int"].includes(value.type) && (typeof value.value !== "number" || !Number.isInteger(value.value))) context.addIssue({ code: "custom", path: ["value"], message: "Int modifications require an integer." });
  if (["Real", "Unreal"].includes(value.type) && typeof value.value !== "number") context.addIssue({ code: "custom", path: ["value"], message: "Real modifications require a number." });
  if (value.type === "String" && typeof value.value !== "string") context.addIssue({ code: "custom", path: ["value"], message: "String modifications require text." });
  if (value.type === "Bool" && typeof value.value !== "boolean") context.addIssue({ code: "custom", path: ["value"], message: "Bool modifications require a boolean." });
});
const objectDefinitionSchema = z.object({
  id: z.string().min(1).optional(), archive_path: z.string().min(1).optional(), category: objectCategory, object_kind: z.enum(["base", "custom"]),
  base_rawcode: rawcode, custom_rawcode: rawcode, rawcode, display_name: z.string().min(1).nullable().optional(),
  dependencies: z.array(rawcode).default([]), references: z.record(z.string(), z.unknown()).default({}), unknown_ids: z.array(rawcode).default([]),
  modifications: z.array(objectModificationSchema).default([]), codec_version: z.string().optional(), provenance: z.string().optional(), capability: z.string().optional()
}).strict().superRefine((value, context) => {
  const active = value.object_kind === "custom" ? value.custom_rawcode : value.base_rawcode;
  if (value.rawcode !== active) context.addIssue({ code: "custom", path: ["rawcode"], message: "rawcode must match the active base/custom rawcode." });
  if (value.object_kind === "custom" && value.base_rawcode === value.custom_rawcode) context.addIssue({ code: "custom", path: ["custom_rawcode"], message: "Custom objects require distinct base and custom rawcodes." });
  if (value.category === "ability" || value.category === "upgrade") {
    for (const modification of value.modifications) if (modification.level === undefined || modification.pointer === undefined) context.addIssue({ code: "custom", path: ["modifications"], message: "Ability and upgrade modifications require level and pointer." });
  }
  if (value.category === "doodad") {
    for (const modification of value.modifications) if (modification.variation === undefined || modification.pointer === undefined) context.addIssue({ code: "custom", path: ["modifications"], message: "Doodad modifications require variation and pointer." });
  }
});
const objectDefinitionUpdateSchema = z.object({ display_name: z.string().min(1).nullable().optional(), modifications: z.array(objectModificationSchema).optional() }).strict().refine(value => Object.keys(value).length > 0, "Object definition update requires display_name or modifications.");
const positionSchema = z.object({ x: z.number().finite(), y: z.number().finite(), z: z.number().finite() }).strict();
const placementInventorySchema = z.object({ slot: z.number().int().min(0).max(5), rawcode }).strict();
const placementAbilitySchema = z.object({ rawcode, autocast_active: z.boolean().optional(), hero_ability_level: z.number().int().nonnegative().optional() }).strict();
const placementSchema = z.object({
  id: z.string().regex(/^(unit|doodad):[0-9]+$/).optional(), member: z.enum(["war3mapUnits.doo", "war3map.doo"]).optional(),
  kind: z.enum(["unit", "building", "item", "doodad", "destructable", "special_doodad"]), rawcode, skin_rawcode: rawcode.optional(), owner_id: z.number().int().min(1).max(24).optional(),
  flags: z.number().int().optional(), inventory: z.array(placementInventorySchema).optional(), abilities: z.array(placementAbilitySchema).optional(), position: positionSchema,
  facing: z.number().finite().optional(), scale: positionSchema.optional(), variation: z.number().int().nonnegative().optional(), creation_number: z.number().int().nonnegative().optional(),
  waygate_destination_region_id: z.number().int().min(-1).optional(), map_region_role: z.unknown().optional(), provenance: z.string().optional(), capability: z.string().optional()
}).strict();
const placementUpdateSchema = placementSchema.partial().omit({ id: true, member: true, kind: true, creation_number: true }).refine(value => Object.keys(value).length > 0, "Placed-object update requires at least one typed field.");
const objectTargetSchema = z.object({ id: z.string().min(1).optional(), category: objectCategory.optional(), rawcode: rawcode.optional() }).strict();
const placementTargetSchema = z.object({ id: z.string().regex(/^(unit|doodad):[0-9]+$/).optional(), creation_number: z.number().int().nonnegative().optional() }).strict().refine(value => Object.keys(value).length > 0, "Placement target requires id or creation_number.");
const objectReferenceValueSchema = z.union([rawcode, z.object({ rawcode }).strict(), z.number().int().nonnegative(), z.object({ player_id: z.number().int().min(1).max(24) }).strict(), z.object({ region_id: z.union([z.number().int().nonnegative(), z.string().regex(/^region:[0-9]+$/)]) }).strict()]);

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
const playerId = z.number().int().min(1).max(24);
const playerStartSchema = z.object({ x: z.number().finite(), y: z.number().finite() }).strict();
const playerSlotSchema = z.object({
  id: playerId.optional(), name: z.string().min(1), controller: z.enum(["None", "User", "Computer", "Neutral", "Rescuable"]),
  race: z.enum(["Human", "Orc", "NightElf", "Undead", "Random", "Selectable"]), flags: z.number().int().nonnegative(), start: playerStartSchema,
  ally_low_priority_mask: z.number().int().nonnegative(), ally_high_priority_mask: z.number().int().nonnegative(),
  enemy_low_priority_mask: z.number().int().nonnegative(), enemy_high_priority_mask: z.number().int().nonnegative(),
  observer: z.boolean().nullable().optional(), locked: z.boolean().optional(), slot_status: z.string().optional()
}).strict();
const playerSlotUpdateSchema = playerSlotSchema.partial().omit({ id: true }).refine(value => Object.keys(value).length > 0, "Player slot update requires at least one field.");
const forceIndex = z.number().int().min(0).max(23);
const forceSchema = z.object({
  index: forceIndex.optional(), name: z.string().min(1), flags: z.number().int().nonnegative(), player_ids: z.array(playerId).min(1), player_mask: z.number().int(),
  alliance: z.boolean().optional(), shared_vision: z.boolean().optional(), shared_unit_control: z.boolean().optional()
}).strict();
const forceUpdateSchema = forceSchema.partial().omit({ index: true }).refine(value => Object.keys(value).length > 0, "Force update requires at least one field.");
const teamId = z.string().regex(/^team_[0-9]+$/);
const teamSchema = z.object({
  id: teamId.optional(), name: z.string().min(1), member_player_ids: z.array(playerId).min(1), force_index: forceIndex,
  arena_id: z.string().min(1), hero_ids: z.array(z.string().min(1)), life_state: z.string().min(1), routing_state: z.string().min(1)
}).strict();
const teamUpdateSchema = teamSchema.partial().omit({ id: true }).refine(value => Object.keys(value).length > 0, "Team update requires at least one field.");
const teamTargetSchema = z.object({ id: teamId.optional(), team_id: teamId.optional() }).strict().refine(value => value.id !== undefined || value.team_id !== undefined, "Team target requires id or team_id.");

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
  if (["create_player_slot", "set_player_slot", "delete_player_slot"].includes(operation.type)) {
    if (!z.object({ id: playerId }).strict().safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Player operations require an explicit numeric slot id." });
    if (operation.type === "create_player_slot") {
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_player_slot requires an absent expected value." });
      if (!playerSlotSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_player_slot requires a complete typed player slot." });
    } else {
      requireExpected();
      if (operation.type === "set_player_slot" && !playerSlotUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_player_slot requires a typed player-slot update." });
    }
  }
  if (["create_force", "set_force", "delete_force"].includes(operation.type)) {
    if (!z.object({ index: forceIndex }).strict().safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Force operations require an explicit numeric force index." });
    if (operation.type === "create_force") {
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_force requires an absent expected value." });
      if (!forceSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_force requires a complete typed force record." });
    } else {
      requireExpected();
      if (operation.type === "set_force" && !forceUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_force requires a typed force update." });
    }
  }
  if (["create_team", "set_team", "delete_team", "set_team_arena", "set_team_members"].includes(operation.type)) {
    if (operation.type === "create_team") {
      if (!z.object({ id: teamId }).strict().safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "create_team requires an explicit stable team id." });
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_team requires an absent expected value." });
      if (!teamSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_team requires a complete typed team record." });
    } else {
      if (!teamTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Team operations require stable team id or team_id." });
      requireExpected();
      const valueSchema = operation.type === "set_team_arena" ? z.object({ arena_id: z.string().min(1) }).strict() : operation.type === "set_team_members" ? z.object({ member_player_ids: z.array(playerId).min(1) }).strict() : teamUpdateSchema;
      if (operation.type !== "delete_team" && !valueSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: `${operation.type} has an invalid typed team value.` });
    }
  }
  if (["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"].includes(operation.type)) {
    if (!objectTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Object-definition operations require id, category, or rawcode targets only." });
    if (operation.type === "create_object_definition") {
      if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "create_object_definition requires an absent expected value." });
      if (!objectDefinitionSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "create_object_definition requires a complete typed object definition." });
    } else {
      requireExpected();
      if (operation.type !== "delete_object_definition" && !objectDefinitionUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "Object-definition updates allow display_name and/or typed modifications only." });
    }
  }
  if (operation.type === "set_object_reference") {
    const target = z.object({ id: z.string().min(1).optional(), category: objectCategory.optional(), rawcode: rawcode.optional(), creation_number: z.number().int().nonnegative().optional(), relation: z.enum(["ability", "item", "upgrade", "owner", "region"]) }).strict();
    if (!target.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "set_object_reference requires a typed relation and object or placement target." });
    requireExpected();
    if (!objectReferenceValueSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_object_reference requires a typed rawcode, player, or region reference." });
  }
  if (["place_object", "place_unit"].includes(operation.type)) {
    if (!z.object({ id: z.string().regex(/^(unit|doodad):[0-9]+$/).optional() }).strict().safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Placement creation targets may contain only an optional stable id." });
    if (operation.expected !== undefined) context.addIssue({ code: "custom", path: ["expected"], message: "Placement creation requires an absent expected value." });
    if (!placementSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "Placement creation requires a typed kind, rawcode, and position." });
  }
  if (["move_object", "move_unit"].includes(operation.type)) {
    if (!placementTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Placement movement requires id or creation_number." });
    requireExpected();
    if (!z.object({ position: positionSchema }).strict().safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "Placement movement requires a complete position." });
  }
  if (operation.type === "update_placed_object") {
    if (!placementTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Placement updates require id or creation_number." });
    requireExpected();
    if (!placementUpdateSchema.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "Placement updates require at least one typed placement field." });
  }
  if (["remove_placed_object", "remove_placed_unit"].includes(operation.type)) {
    if (!placementTargetSchema.safeParse(operation.target).success) context.addIssue({ code: "custom", path: ["target"], message: "Placement removal requires id or creation_number." });
    requireExpected();
  }
  if (operation.type !== "set_script_source") return;
  if (operation.target.archive_path !== "war3map.j") context.addIssue({ code: "custom", path: ["target", "archive_path"], message: "set_script_source targets only the existing war3map.j member." });
  if (!scriptExpectedValue.safeParse(operation.expected).success) context.addIssue({ code: "custom", path: ["expected"], message: "set_script_source requires the current war3map.j SHA-256 as expected or expected.sha256." });
  if (!scriptSourceValue.safeParse(operation.value).success) context.addIssue({ code: "custom", path: ["value"], message: "set_script_source requires { language: 'jass', source: '...' }." });
});

export type OperationInput = z.infer<typeof operationSchema>;
