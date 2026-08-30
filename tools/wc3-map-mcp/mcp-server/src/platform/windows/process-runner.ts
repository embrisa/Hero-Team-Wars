import { execFileSync, spawn } from "node:child_process";
import { basename } from "node:path";
import { AppError } from "../../errors/app-error.js";

export interface ProcessStartRequest {
  executable: string;
  arguments: readonly string[];
  working_directory?: string;
}

export interface StartedProcess {
  pid: number;
  started_utc: string;
  exit_state: "unknown";
}

/**
 * The launch service depends on this small interface so tests can prove the
 * exact executable/argument array without starting an installed game.
 */
export interface ProcessRunner {
  isRunning(executable: string): boolean;
  start(request: ProcessStartRequest): StartedProcess;
}

/**
 * Windows process adapter. It deliberately has no terminate method: launch
 * operations are allowed to report an existing-process conflict, never to
 * kill a process selected by basename or by a broad task list.
 */
export class NativeProcessRunner implements ProcessRunner {
  public isRunning(executable: string): boolean {
    if (process.platform !== "win32") return false;

    const imageName = basename(executable);
    try {
      const output = execFileSync(
        "tasklist.exe",
        ["/FO", "CSV", "/NH", "/FI", `IMAGENAME eq ${imageName}`],
        { encoding: "utf8", windowsHide: true }
      );
      return output
        .split(/\r?\n/)
        .some(line => parseTaskListImageName(line)?.toLowerCase() === imageName.toLowerCase());
    } catch (error) {
      throw new AppError(
        "LAUNCH_FAILED",
        `Unable to determine whether '${imageName}' is already running. Launch was refused for safety.`,
        true,
        { executable, image_name: imageName },
        { cause: error }
      );
    }
  }

  public start(request: ProcessStartRequest): StartedProcess {
    const startedUtc = new Date().toISOString();
    let child;
    try {
      child = spawn(request.executable, [...request.arguments], {
        cwd: request.working_directory,
        detached: true,
        shell: false,
        stdio: "ignore",
        // The editor/game window is intentionally visible to the user.
        windowsHide: false
      });
    } catch (error) {
      throw new AppError(
        "LAUNCH_FAILED",
        `Unable to start '${request.executable}'.`,
        false,
        { executable: request.executable, arguments: [...request.arguments] },
        { cause: error }
      );
    }

    if (child.pid === undefined) {
      throw new AppError(
        "LAUNCH_FAILED",
        `The operating system did not return a PID for '${request.executable}'.`,
        false,
        { executable: request.executable, arguments: [...request.arguments] }
      );
    }

    // An asynchronously emitted spawn error must not become an unhandled
    // process error. The session remains "unknown" because this adapter does
    // not keep a server-owned wait loop for detached applications.
    child.once("error", () => undefined);
    child.unref();
    return { pid: child.pid, started_utc: startedUtc, exit_state: "unknown" };
  }
}

function parseTaskListImageName(line: string): string | undefined {
  const trimmed = line.trim();
  if (!trimmed.startsWith('"')) return undefined;
  const end = trimmed.indexOf('"', 1);
  return end > 1 ? trimmed.slice(1, end) : undefined;
}
