import { describe, expect, it } from "vitest";
import { WorkerClient } from "../../src/transport/worker-client.js";
import type { Wc3Config } from "../../src/config/schema.js";

function configFor(script: string, requestTimeoutMs = 500): Wc3Config {
  return {
    schema_version: "1.0",
    engine: {
      executable: process.execPath,
      // The real engine accepts the client's trailing --stdio flag. Node's
      // eval mode needs an argument separator so the same invocation remains
      // valid for these child-process fixtures.
      arguments: ["-e", script, "--"],
      request_timeout_ms: requestTimeoutMs
    },
    projects: {
      test: {
        root: process.cwd(),
        source_maps: ["fixture.w3m"],
        read_roots: [],
        staging_root: "staging",
        artifact_root: "artifacts",
        build_root: "builds",
        log_root: "logs",
        test_output_root: "test-output",
        enabled_tools: [],
        write_policy: "read_only",
        max_map_bytes: 1024,
        max_operation_count: 1
      }
    }
  };
}

function clientWith(script: string, requestTimeoutMs?: number): WorkerClient {
  return new WorkerClient(configFor(script, requestTimeoutMs));
}

describe("WorkerClient process and protocol failures", () => {
  it("maps a request timeout to retryable engine unavailability", async () => {
    const client = clientWith("process.stdin.resume(); setInterval(() => undefined, 1000);", 50);

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_UNAVAILABLE",
      retryable: true,
      message: "Map engine timed out during 'inspect_map'."
    });
  });

  it("maps a non-zero worker exit to retryable engine unavailability", async () => {
    const client = clientWith([
      "process.stdin.resume();",
      "process.stdin.on('end', () => { process.stderr.write('worker crashed\\n'); process.exit(17); });"
    ].join(" "));

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_UNAVAILABLE",
      retryable: true,
      details: { code: 17, stderr: "worker crashed\n" }
    });
  });

  it("maps a clean worker exit without stdout to a protocol error", async () => {
    const client = clientWith([
      "process.stdin.resume();",
      "process.stdin.on('end', () => process.exit(0));"
    ].join(" "));

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_PROTOCOL_ERROR",
      retryable: true,
      message: "Map engine returned no response for 'inspect_map'."
    });
  });

  it("maps malformed JSON to a non-retryable protocol error", async () => {
    const client = clientWith([
      "process.stdin.resume();",
      "process.stdin.on('end', () => { process.stdout.write('not-json\\n'); process.exit(0); });"
    ].join(" "));

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_PROTOCOL_ERROR",
      retryable: false,
      message: "Map engine returned malformed JSON for 'inspect_map'."
    });
  });

  it("maps multiple non-empty stdout lines to a protocol error", async () => {
    const client = clientWith([
      "const fs = require('node:fs');",
      "const request = JSON.parse(fs.readFileSync(0, 'utf8'));",
      "const response = JSON.stringify({ protocol_version: '1.0', request_id: request.request_id, ok: true, result: {} });",
      "process.stdout.write(response + '\\n' + response + '\\n');"
    ].join(" "));

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_PROTOCOL_ERROR",
      retryable: false,
      message: "Map engine returned multiple stdout responses for 'inspect_map'.",
      details: { response_count: 2 }
    });
  });

  it("schema-validates a response before returning its result", async () => {
    const client = clientWith([
      "const fs = require('node:fs');",
      "const request = JSON.parse(fs.readFileSync(0, 'utf8'));",
      "process.stdout.write(JSON.stringify({ protocol_version: '1.0', request_id: request.request_id, ok: true }) + '\\n');"
    ].join(" "));

    await expect(client.request("inspect_map", {})).rejects.toMatchObject({
      code: "ENGINE_PROTOCOL_ERROR",
      retryable: false,
      message: "Map engine returned an invalid response for 'inspect_map'."
    });
  });
});
