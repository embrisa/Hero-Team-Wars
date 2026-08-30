import * as z from "zod/v4";

const rootRelativePath = z.string().min(1);

export const projectConfigSchema = z.object({
  root: z.string().min(1),
  source_maps: z.array(rootRelativePath).min(1),
  baseline_sha256: z.string().regex(/^[0-9a-f]{64}$/i).optional(),
  read_roots: z.array(rootRelativePath).default([]),
  staging_root: rootRelativePath,
  artifact_root: rootRelativePath,
  build_root: rootRelativePath,
  log_root: rootRelativePath,
  test_output_root: rootRelativePath.default("tools/wc3-map-mcp/artifacts/tests"),
  gameplay_source_roots: z.array(rootRelativePath).default(["scripts/mcp"]),
  world_editor: z.string().min(1).optional(),
  warcraft: z.string().min(1).optional(),
  test_map_root: z.string().min(1).optional(),
  enabled_tools: z.array(z.string().min(1)).default([]),
  write_policy: z.enum(["read_only", "writes", "all"]).default("writes"),
  script_policy: z.enum(["disabled", "mcp_owned_jass"]).default("disabled"),
  profile: z.enum(["mvp_2arena", "full_6team", "gui_compatible"]).default("mvp_2arena"),
  max_map_bytes: z.number().int().positive().default(512 * 1024 * 1024),
  max_operation_count: z.number().int().positive().max(1000).default(100)
}).strict();

export const configSchema = z.object({
  schema_version: z.literal("1.0"),
  engine: z.object({
    executable: z.string().min(1),
    arguments: z.array(z.string()).default([]),
    request_timeout_ms: z.number().int().positive().max(600_000).default(120_000)
  }).strict(),
  projects: z.record(z.string().min(1), projectConfigSchema).refine(value => Object.keys(value).length > 0, "At least one project is required.")
}).strict();

export type Wc3Config = z.infer<typeof configSchema>;
export type ProjectConfig = z.infer<typeof projectConfigSchema>;
