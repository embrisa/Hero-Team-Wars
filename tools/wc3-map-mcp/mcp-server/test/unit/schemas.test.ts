import { describe, expect, it } from "vitest";
import { operationSchema } from "../../src/schemas/operations.js";
import { encodeNdjson, parseNdjsonLine } from "../../src/transport/ndjson.js";

describe("operation schema", () => {
  it("requires a rationale and a UUID operation id", () => {
    expect(operationSchema.safeParse({ type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After" }).success).toBe(false);
    expect(operationSchema.safeParse({ operation_id: "c0a80101-0000-4000-8000-000000000001", type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After", rationale: "Change title" }).success).toBe(true);
  });
});

describe("worker NDJSON contract", () => {
  it("round-trips one request as one line", () => {
    const request = { protocol_version: "1.0", request_id: "request", operation: "environment_status", payload: { configured_files: {} } };
    const encoded = encodeNdjson(request);

    expect(encoded.endsWith("\n")).toBe(true);
    expect(encoded.slice(0, -1)).not.toContain("\n");
    expect(parseNdjsonLine(encoded.trim())).toEqual(request);
  });
});
