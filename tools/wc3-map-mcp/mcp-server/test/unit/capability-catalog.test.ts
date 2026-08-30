import { describe, expect, it } from "vitest";
import { capabilityMatrix, isToolEnabledForProject, isToolSupportedByProfile } from "../../src/services/capability-catalog.js";

const baseProject = {
  root: ".", source_maps: ["map/test.w3m"], read_roots: [], staging_root: "snapshots", artifact_root: "artifacts",
  build_root: "builds", log_root: "logs", test_output_root: "artifacts/tests", gameplay_source_roots: ["scripts/mcp"],
  gameplay_manifest: "scripts/mcp/manifest.json", enabled_tools: [], write_policy: "writes" as const,
  script_policy: "mcp_owned_jass" as const, profile: "mvp_2arena" as const, max_map_bytes: 512, max_operation_count: 100
};

describe("Phase 5F capability catalog", () => {
  it("keeps profile-specific gameplay tools out of the GUI profile", () => {
    expect(isToolSupportedByProfile("mvp_2arena", "wc3_run_scenario_build")).toBe(true);
    expect(isToolSupportedByProfile("gui_compatible", "wc3_run_scenario_build")).toBe(false);
    expect(isToolEnabledForProject({ ...baseProject, profile: "gui_compatible" }, "wc3_run_scenario_build")).toBe(false);
  });

  it("reports members, operations, and profile status together", () => {
    const report = capabilityMatrix(baseProject) as any;
    expect(report.active_profile).toBe("mvp_2arena");
    expect(report.members.find((item: any) => item.member === "war3map.w3i").profile_status.mvp_2arena.enabled).toBe(true);
    expect(report.members.find((item: any) => item.member === "war3map.wtg").profile_status.mvp_2arena.supported).toBe(false);
    expect(report.operations.find((item: any) => item.operation === "set_script_source").enabled).toBe(true);
    expect(report.profiles.full_6team.operations).toContain("create_region");
    expect(report.gui_trigger_compatibility.enabled).toBe(false);
  });
});
