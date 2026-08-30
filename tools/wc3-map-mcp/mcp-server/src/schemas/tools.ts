import * as z from "zod/v4";
import { operationSchema } from "./operations.js";
import { projectMapSchema, sha256Schema, uuidSchema } from "./common.js";

export const projectStatusSchema = z.object({ project_id: z.string().min(1).max(100) }).strict();
export const inspectMapSchema = projectMapSchema.extend({ section: z.enum(["metadata", "players", "forces", "teams", "team_registry", "regions", "region_roles", "cameras", "triggers", "variables", "gameplay_triggers", "gameplay_variables", "gameplay_modules", "trigger_mode", "scripts", "object_data", "placed_objects", "terrain_summary", "imports", "archive_members", "capabilities", "component_status", "profiles", "profile", "opaque_members"]).optional(), include_provenance: z.boolean().default(true), max_items_per_section: z.number().int().min(1).max(1000).default(100) }).strict();
export const listArchiveFilesSchema = projectMapSchema.extend({ prefix: z.string().max(200).optional(), cursor: z.string().max(1000).optional(), max_items: z.number().int().min(1).max(1000).default(200) }).strict();
export const getComponentSchema = projectMapSchema.extend({ component: z.enum(["metadata", "players", "forces", "teams", "team_registry", "regions", "region_roles", "cameras", "triggers", "variables", "gameplay_triggers", "gameplay_variables", "gameplay_modules", "trigger_mode", "scripts", "object_data", "placed_objects", "terrain_summary", "imports", "archive_members", "capabilities", "component_status", "profiles", "profile", "opaque_members"]), filter: z.string().max(200).optional(), cursor: z.string().max(1000).optional(), max_items: z.number().int().min(1).max(1000).default(100) }).strict();
export const getScriptSourceSchema = projectMapSchema.extend({ archive_path: z.string().regex(/^war3map\.j$/i).default("war3map.j"), expected_script_hash: sha256Schema.optional() }).strict();
export const validateMapSchema = projectMapSchema;
export const compareMapsSchema = z.object({ project_id: z.string().min(1).max(100), left: z.string().min(1).max(400), right: z.string().min(1).max(400) }).strict();
export const beginTransactionSchema = projectMapSchema.extend({ expected_source_hash: sha256Schema, label: z.string().max(100).optional() }).strict();
export const applyOperationsSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, expected_revision: z.number().int().min(0), operations: z.array(operationSchema).min(1).max(100), dry_run: z.boolean().default(false) }).strict();
export const transactionDiffSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, from_revision: z.number().int().min(0).optional(), to_revision: z.number().int().min(0).optional() }).strict();
export const validateTransactionSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, revision: z.number().int().min(0) }).strict();
export const buildMapSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, revision: z.number().int().min(0), expected_source_hash: sha256Schema, profile: z.enum(["debug", "release", "noop"]).default("debug"), label: z.string().max(60).optional() }).strict();
export const buildReportSchema = z.object({ project_id: z.string().min(1).max(100), build_id: uuidSchema }).strict();
export const launchSchema = z.object({ project_id: z.string().min(1).max(100), build_id: uuidSchema, expected_build_hash: sha256Schema }).strict();
export const recordTestResultSchema = z.object({ project_id: z.string().min(1).max(100), session_id: uuidSchema, expected_build_hash: sha256Schema, milestone: z.enum(["editor_opened", "game_loaded", "smoke_test", "playtest"]), result: z.enum(["pass", "fail"]), recorder: z.enum(["user_observation", "agent_log_observation"]), notes: z.string().max(10000), artifacts: z.array(z.string().min(1).max(400)).max(50).default([]) }).strict();
export const getTestSessionSchema = z.object({ project_id: z.string().min(1).max(100), session_id: uuidSchema }).strict();
export const promoteSchema = z.object({ project_id: z.string().min(1).max(100), build_id: uuidSchema, expected_build_hash: sha256Schema, destination_id: z.literal("test_map_root"), destination_name: z.string().min(1).max(200) }).strict();
export const discardSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, expected_source_hash: sha256Schema, confirmation: z.literal(true) }).strict();
export const gameplayManifestSchema = z.object({
  project_id: z.string().min(1).max(100),
  manifest_path: z.string().min(1).max(400),
  profile: z.enum(["mvp_2arena", "full_6team", "gui_compatible"]).optional(),
  expected_manifest_sha256: sha256Schema.optional(),
  expected_module_hashes: z.record(z.string().min(1), sha256Schema).optional()
}).strict();
export const prepareGameplayChunkSchema = gameplayManifestSchema.extend({ transaction_id: uuidSchema, expected_revision: z.number().int().min(0), chunk_id: z.string().regex(/^HTW-[0-9]{2}$/) }).strict();
export const runScenarioBuildSchema = z.object({ project_id: z.string().min(1).max(100), transaction_id: uuidSchema, revision: z.number().int().min(0), expected_source_hash: sha256Schema, chunk_id: z.string().regex(/^HTW-[0-9]{2}$/), scenario_ids: z.array(z.string().min(1).max(100)).max(50).optional(), profile: z.enum(["mvp_2arena", "full_6team"]).default("mvp_2arena") }).strict();
export const recordChunkResultSchema = z.object({ project_id: z.string().min(1).max(100), chunk_id: z.string().regex(/^HTW-[0-9]{2}$/), scenario_id: z.string().min(1).max(100), transaction_id: uuidSchema, revision: z.number().int().min(0), build_id: uuidSchema, expected_build_hash: sha256Schema, result: z.enum(["pass", "fail"]), evidence_level: z.enum(["static_only", "user_observed"]).default("static_only"), test_session_id: uuidSchema.optional(), notes: z.string().max(10000) }).strict().superRefine((value, context) => {
  if (value.evidence_level === "user_observed" && !value.test_session_id) context.addIssue({ code: "custom", path: ["test_session_id"], message: "user_observed chunk evidence requires an exact test_session_id." });
});
