import { describe, expect, it, vi } from "vitest";
import { registerLaunchTools } from "../../src/tools/launches.js";
import type { LaunchService } from "../../src/services/launch-service.js";
import type { RegisterTool } from "../../src/tools/builds.js";

describe("Phase 4 launch tool registration", () => {
  it("registers the four launch/evidence tools with the current schemas", async () => {
    const registrations: Array<{ name: string; config: Record<string, any>; handler: (input: any) => Promise<Record<string, unknown>> }> = [];
    const launches = {
      launchEditor: vi.fn(() => ({ session: { evidence_level: "process_started" } })),
      launchGame: vi.fn(() => ({ session: { evidence_level: "process_started" } })),
      record: vi.fn(() => ({ session: { evidence_level: "editor_opened" } })),
      get: vi.fn(() => ({ verified: true }))
    } as unknown as LaunchService;

    registerLaunchTools(((name, config, handler) => registrations.push({ name, config, handler })) as RegisterTool, launches);

    expect(registrations.map(item => item.name)).toEqual([
      "wc3_launch_editor",
      "wc3_launch_test_map",
      "wc3_record_test_result",
      "wc3_get_test_session"
    ]);
    expect(registrations.map(item => item.config.annotations)).toEqual([
      { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
      { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
      { readOnlyHint: false, destructiveHint: false, idempotentHint: false },
      { readOnlyHint: true, destructiveHint: false, idempotentHint: true }
    ]);

    await registrations[0]!.handler({ project_id: "hero-team-wars", build_id: "11111111-1111-4111-8111-111111111111", expected_build_hash: "A".repeat(64) });
    expect(launches.launchEditor).toHaveBeenCalledWith("hero-team-wars", "11111111-1111-4111-8111-111111111111", "A".repeat(64), expect.any(String));

    const recordSchema = registrations[2]!.config.inputSchema as { safeParse: (value: unknown) => { success: boolean; data?: any } };
    const parsed = recordSchema.safeParse({
      project_id: "hero-team-wars",
      session_id: "33333333-3333-4333-8333-333333333333",
      expected_build_hash: "A".repeat(64),
      milestone: "editor_opened",
      result: "pass",
      recorder: "user_observation",
      notes: "Observed"
    });
    expect(parsed.success).toBe(true);
    await registrations[2]!.handler(parsed.data);
    expect(launches.record).toHaveBeenCalledWith("hero-team-wars", "33333333-3333-4333-8333-333333333333", "A".repeat(64), "editor_opened", "pass", "user_observation", "Observed", [], expect.any(String));
  });
});
