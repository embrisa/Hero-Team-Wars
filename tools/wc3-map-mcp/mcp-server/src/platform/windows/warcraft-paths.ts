import { execFileSync } from "node:child_process";
import { existsSync, lstatSync, mkdirSync, realpathSync } from "node:fs";
import { dirname, extname, isAbsolute, parse, resolve } from "node:path";
import { AppError } from "../../errors/app-error.js";
import { isWithin } from "../../config/resolve-project.js";

export type WarcraftTarget = "editor" | "game";

export interface ExecutableInfo {
  target: WarcraftTarget;
  path: string;
  version: string;
  version_verified: boolean;
}

export interface WarcraftPaths {
  executable(configuredPath: string | undefined, target: WarcraftTarget): ExecutableInfo;
  testRoot(configuredPath: string | undefined): string;
  testCopyPath(testRoot: string, buildId: string, sessionId: string, extension: string): string;
}

/** Resolve and verify the external Warcraft III tools used by a launch. */
export class WindowsWarcraftPaths implements WarcraftPaths {
  public executable(configuredPath: string | undefined, target: WarcraftTarget): ExecutableInfo {
    if (!configuredPath || !isAbsolute(configuredPath)) {
      throw new AppError("LAUNCH_FAILED", `${target === "editor" ? "World Editor" : "Warcraft III"} must be configured with an absolute executable path.`);
    }

    const path = resolve(configuredPath);
    if (!existsSync(path)) throw new AppError("LAUNCH_FAILED", `Configured ${target} executable does not exist: ${path}`);
    const stat = lstatSync(path);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new AppError("LAUNCH_FAILED", `Configured ${target} executable must be a regular file: ${path}`);
    if (extname(path).toLowerCase() !== ".exe") throw new AppError("LAUNCH_FAILED", `Configured ${target} executable must be an .exe file: ${path}`);

    const version = readWindowsFileVersion(path);
    return { target, path, version: version ?? "unknown", version_verified: version !== undefined };
  }

  public testRoot(configuredPath: string | undefined): string {
    if (!configuredPath || !isAbsolute(configuredPath)) {
      throw new AppError("LAUNCH_FAILED", "test_map_root must be configured with an absolute directory path.");
    }

    const root = resolve(configuredPath);
    if (parse(root).root.toLowerCase() === root.toLowerCase()) {
      throw new AppError("PATH_OUTSIDE_ROOT", "The configured test_map_root is too broad; a filesystem root is not a valid test destination.");
    }
    if (/[?*\0]/.test(root)) throw new AppError("PATH_OUTSIDE_ROOT", "The configured test_map_root contains unsafe wildcard or NUL syntax.");

    mkdirSync(root, { recursive: true });
    const stat = lstatSync(root);
    if (!stat.isDirectory() || stat.isSymbolicLink()) throw new AppError("PATH_OUTSIDE_ROOT", `The configured test_map_root must be a real directory: ${root}`);
    return realpathSync(root);
  }

  public testCopyPath(testRoot: string, buildId: string, sessionId: string, extension: string): string {
    if (!isAbsolute(testRoot)) {
      throw new AppError("PATH_OUTSIDE_ROOT", "The test map root must be absolute.");
    }
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(buildId) || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(sessionId)) {
      throw new AppError("INVALID_ARGUMENT", "Build and session IDs must be UUIDs before creating a test copy.");
    }
    const normalizedExtension = extension.toLowerCase();
    if (normalizedExtension !== ".w3m" && normalizedExtension !== ".w3x") throw new AppError("INVALID_ARGUMENT", "Test map extension must be .w3m or .w3x.");

    const root = resolve(testRoot);
    const fileName = `MCP_${buildId}_${sessionId}${normalizedExtension}`;
    const candidate = resolve(root, fileName);
    if (!isWithin(root, candidate) || candidate === root || dirname(candidate).toLowerCase() !== root.toLowerCase()) {
      throw new AppError("PATH_OUTSIDE_ROOT", "The generated test map destination escaped the configured test root.");
    }
    if (existsSync(candidate)) throw new AppError("OUTPUT_EXISTS", `The generated test map already exists: ${candidate}`);

    // Resolve the parent through junctions/symlinks immediately before the
    // copy. The destination is a direct child of this exact configured root.
    const realRoot = realpathSync(root);
    const realParent = realpathSync(dirname(candidate));
    if (!isWithin(realRoot, realParent) || realParent.toLowerCase() !== realRoot.toLowerCase()) {
      throw new AppError("PATH_OUTSIDE_ROOT", "The generated test map destination resolves outside the configured test root.");
    }
    return candidate;
  }
}

function readWindowsFileVersion(path: string): string | undefined {
  if (process.platform !== "win32") return undefined;
  try {
    // The fixed PowerShell program receives the path over stdin, so an
    // executable path cannot become PowerShell syntax.
    const output = execFileSync(
      "powershell.exe",
      ["-NoProfile", "-NonInteractive", "-Command", "$input | ForEach-Object { (Get-Item -LiteralPath $_ -ErrorAction Stop).VersionInfo.FileVersion }"],
      { input: `${path}\n`, encoding: "utf8", windowsHide: true }
    ).trim();
    return output.length > 0 ? output : undefined;
  } catch {
    return undefined;
  }
}
