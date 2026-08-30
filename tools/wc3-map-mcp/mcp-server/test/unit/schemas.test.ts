import { describe, expect, it } from "vitest";
import { operationSchema } from "../../src/schemas/operations.js";

describe("operation schema", () => {
  it("requires a rationale and a UUID operation id", () => {
    expect(operationSchema.safeParse({ type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After" }).success).toBe(false);
    expect(operationSchema.safeParse({ operation_id: "c0a80101-0000-4000-8000-000000000001", type: "set_map_metadata", target: { field: "title" }, expected: "Before", value: "After", rationale: "Change title" }).success).toBe(true);
  });
});
