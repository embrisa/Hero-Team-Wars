import { existsSync, readFileSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { configSchema, type Wc3Config } from "./schema.js";

export function defaultConfigPath(): string {
  const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
  const localPath = join(serverRoot, "config", "wc3-map-mcp.local.json");
  return existsSync(localPath) ? localPath : join(serverRoot, "config", "wc3-map-mcp.example.json");
}

export function loadConfig(configPath = process.env.WC3_MAP_MCP_CONFIG ?? defaultConfigPath()): Wc3Config {
  const resolvedPath = isAbsolute(configPath) ? configPath : resolve(configPath);
  let parsed: unknown;
  try {
    parsed = JSON.parse(readFileSync(resolvedPath, "utf8")) as unknown;
  } catch (error) {
    throw new Error(`Unable to read WC3 MCP configuration '${resolvedPath}': ${error instanceof Error ? error.message : String(error)}`);
  }

  const result = configSchema.safeParse(parsed);
  if (!result.success) {
    throw new Error(`Invalid WC3 MCP configuration '${resolvedPath}': ${result.error.message}`);
  }

  const resolvedExecutable = isAbsolute(result.data.engine.executable) ? result.data.engine.executable : resolve(dirname(resolvedPath), result.data.engine.executable);
  if (!existsSync(resolvedExecutable)) {
    throw new Error(`Configured map engine executable does not exist: ${resolvedExecutable}`);
  }

  return {
    ...result.data,
    engine: {
      ...result.data.engine,
      executable: resolvedExecutable
    }
  };
}
