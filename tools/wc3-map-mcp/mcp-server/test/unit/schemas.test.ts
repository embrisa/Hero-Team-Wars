import { describe, expect, it } from "vitest";
import { operationSchema } from "../../src/schemas/operations.js";
import { encodeNdjson, parseNdjsonLine } from "../../src/transport/ndjson.js";

describe("operation schema", () => {
  it("requires a rationale and a UUID operation id", () => {
    expect(operationSchema.safeParse({ type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After" }).success).toBe(false);
    expect(operationSchema.safeParse({ operation_id: "c0a80101-0000-4000-8000-000000000001", type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After", rationale: "Change title" }).success).toBe(true);
  });

  it("requires a pinned JASS source shape and current hash", () => {
    const operation = {
      operation_id: "c0a80101-0000-4000-8000-000000000010",
      type: "set_script_source",
      target: { archive_path: "war3map.j" },
      expected: "A".repeat(64),
      value: { language: "jass", source: "function main takes nothing returns nothing\nendfunction\n" },
      rationale: "Change gameplay logic."
    };
    expect(operationSchema.safeParse(operation).success).toBe(true);
    expect(operationSchema.safeParse({ ...operation, expected: undefined }).success).toBe(false);
    expect(operationSchema.safeParse({ ...operation, target: { archive_path: "war3map.lua" } }).success).toBe(false);
  });

  it("validates typed object and placement operation shapes", () => {
    const object = {
      operation_id: "c0a80101-0000-4000-8000-000000000011", type: "create_object_definition", target: { category: "unit", rawcode: "Z001" },
      value: { category: "unit", object_kind: "custom", base_rawcode: "hfoo", custom_rawcode: "Z001", rawcode: "Z001", modifications: [], unknown_ids: [] }, rationale: "Create a typed object."
    };
    expect(operationSchema.safeParse(object).success).toBe(true);
    expect(operationSchema.safeParse({ ...object, value: { ...object.value, rawcode: "hfoo" } }).success).toBe(false);

    const placement = {
      operation_id: "c0a80101-0000-4000-8000-000000000012", type: "place_object", target: {},
      value: { kind: "building", rawcode: "hkee", position: { x: 512, y: 512, z: 0 } }, rationale: "Place a typed building."
    };
    expect(operationSchema.safeParse(placement).success).toBe(true);
    expect(operationSchema.safeParse({ ...placement, value: { ...placement.value, position: { x: 512, y: 512 } } }).success).toBe(false);
    expect(operationSchema.safeParse({ ...placement, expected: {} }).success).toBe(false);
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
