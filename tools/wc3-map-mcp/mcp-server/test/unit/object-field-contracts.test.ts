import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { toJSONSchema } from "zod/v4";
import { operationSchema } from "../../src/schemas/operations.js";
import { objectFieldLookupSchema, objectFieldSearchSchema } from "../../src/schemas/tools.js";
import { workerOperationSchema } from "../../src/schemas/worker.js";

const root = fileURLToPath(new URL("../../", import.meta.url));
const json = (path: string) => JSON.parse(readFileSync(resolve(root, path), "utf8"));

describe("object-field contracts", () => {
  it("keeps worker operations and generated public schemas synchronized", () => {
    expect(json("../contracts/schemas/engine-request.schema.json").properties.operation.enum.sort()).toEqual([...workerOperationSchema.options].sort());
    for (const [name, schema] of [["lookup", objectFieldLookupSchema], ["search", objectFieldSearchSchema]] as const) {
      expect(json(`../contracts/schemas/object-field-${name}.schema.json`)).toEqual(toJSONSchema(schema));
    }
    expect(objectFieldLookupSchema.safeParse({ field: "Ncl", bogus: true }).success).toBe(false);
    expect(objectFieldSearchSchema.safeParse({ query: " ", limit: 51 }).success).toBe(false);
    expect(objectFieldLookupSchema.safeParse({ field: "Ncl2", base_rawcode: "Ncl" }).success).toBe(false);
  });

  it("accepts named authoring without weakening raw or closed input contracts", () => {
    const input = { operation_id: "00000000-0000-4000-8000-000000000001", type: "create_object_definition", target: {}, rationale: "Contract test", value: {
      category: "ability", object_kind: "custom", base_rawcode: "ANcl", custom_rawcode: "Z001", rawcode: "Z001",
      modifications: [{ field: "channelTargetType", value: "unit", level: 1 }]
    } };
    expect(operationSchema.safeParse(input).success).toBe(true);
    for (const modification of [
      { field: "channelTargetType", id: "Ncl2", value: "unit", level: 1 },
      { field: "channelTargetType", type: "Int", value: 1, level: 1 },
      { id: "Ncl2", value: 1, level: 1, pointer: 2 },
      { field: "channelTargetType", value: "unit" },
      { field: "channelTargetType", value: "unit", level: 1, arbitrary: true }
    ]) expect(operationSchema.safeParse({ ...input, value: { ...input.value, modifications: [modification] } }).success).toBe(false);
  });
});
