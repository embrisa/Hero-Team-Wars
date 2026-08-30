import { describe, expect, it } from "vitest";
import { assertSafeRelative, isWithin } from "../../src/config/resolve-project.js";

describe("path policy", () => {
  it("rejects traversal and rooted paths", () => {
    expect(() => assertSafeRelative("../outside.w3m")).toThrow();
    expect(() => assertSafeRelative("C:/outside.w3m")).toThrow();
    expect(() => assertSafeRelative("safe/*.w3m", true)).toThrow();
  });

  it("uses segment-aware containment", () => {
    expect(isWithin("C:/safe/root", "C:/safe/root/file.w3m")).toBe(true);
    expect(isWithin("C:/safe/root", "C:/safe/root-evil/file.w3m")).toBe(false);
  });
});
