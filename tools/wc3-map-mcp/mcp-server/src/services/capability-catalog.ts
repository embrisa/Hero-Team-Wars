import type { ProjectConfig } from "../config/schema.js";

export const CAPABILITY_PROFILES = ["mvp_2arena", "full_6team", "gui_compatible"] as const;
export type CapabilityProfile = (typeof CAPABILITY_PROFILES)[number];

const NATIVE_PROFILES: CapabilityProfile[] = ["mvp_2arena", "full_6team"];
const ALL_PROFILES: CapabilityProfile[] = [...CAPABILITY_PROFILES];

const PROFILE_TOOLS: Record<CapabilityProfile, string[]> = {
  mvp_2arena: [
    "wc3_compose_gameplay_source", "wc3_validate_gameplay_source", "wc3_prepare_gameplay_chunk",
    "wc3_run_scenario_build", "wc3_record_chunk_result"
  ],
  full_6team: [
    "wc3_compose_gameplay_source", "wc3_validate_gameplay_source", "wc3_prepare_gameplay_chunk",
    "wc3_run_scenario_build", "wc3_record_chunk_result"
  ],
  gui_compatible: []
};

const READ_ONLY_TOOLS = new Set([
  "wc3_project_status", "wc3_inspect_map", "wc3_list_archive_files", "wc3_get_component",
  "wc3_get_script_source", "wc3_validate_map", "wc3_compare_maps", "wc3_compose_gameplay_source",
  "wc3_validate_gameplay_source", "jass_lookup", "jass_search", "jass_validate_call", "jass_validate_source"
]);

/** Global canonical knowledge tools are not project-scoped allow-list entries. */
export const GLOBAL_KNOWLEDGE_TOOLS = new Set([
  "jass_lookup", "jass_search", "jass_validate_call", "jass_validate_source"
]);

const MUTATING_TOOLS = new Set([
  "wc3_begin_transaction", "wc3_apply_operations", "wc3_transaction_diff", "wc3_validate_transaction",
  "wc3_discard_transaction", "wc3_build_map", "wc3_build_report", "wc3_promote_build",
  "wc3_launch_editor", "wc3_launch_test_map", "wc3_record_test_result", "wc3_get_test_session",
  "wc3_prepare_gameplay_chunk", "wc3_run_scenario_build", "wc3_record_chunk_result"
]);

export interface CapabilityMember {
  member: string;
  component: string;
  capability: string;
  supported_operations: string[];
  profiles: CapabilityProfile[];
  evidence: string;
}

export interface CapabilityOperation {
  operation: string;
  component: string;
  profiles: CapabilityProfile[];
  capability: string;
  enabled: boolean;
  reason: string;
}

const MEMBERS: CapabilityMember[] = [
  member("war3map.w3i", "players_forces", "typed_write_enabled", ["set_map_metadata", "create_player_slot", "set_player_slot", "delete_player_slot", "create_force", "set_force", "delete_force", "create_team", "set_team", "delete_team", "set_team_arena", "set_team_members"]),
  member("war3map.w3r", "regions", "typed_write_enabled", ["create_region", "update_region", "rename_region", "delete_region", "reorder_regions", "set_region_role"]),
  member("war3map.w3u", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3a", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3t", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3b", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3d", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3h", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3map.w3q", "object_data", "roundtrip_verified", ["create_object_definition", "update_object_definition", "delete_object_definition", "set_object_data"]),
  member("war3mapUnits.doo", "placements", "roundtrip_verified", ["place_object", "place_unit", "move_object", "move_unit", "update_placed_object", "remove_placed_object", "remove_placed_unit", "set_object_reference"]),
  member("war3map.doo", "placements", "roundtrip_verified", ["place_object", "move_object", "update_placed_object", "remove_placed_object", "set_object_reference"]),
  member("war3map.j", "gameplay_source", "staged_typed_write", ["set_script_source", "upsert_script_module", "remove_script_module", "create_trigger", "update_trigger", "move_trigger", "delete_trigger", "create_variable", "update_variable", "delete_variable", "set_trigger_mode"], NATIVE_PROFILES, "static source/composition only; runtime remains separately observed"),
  member("war3map.wtg", "gui_triggers", "preserved_opaque", [], [], "exact GUI serializer and editor evidence are pending"),
  member("war3map.wct", "gui_triggers", "preserved_opaque", [], [], "exact GUI serializer and editor evidence are pending"),
  member("war3map.wts", "trigger_strings", "parsed_read_only", [], ALL_PROFILES, "parsed/read-only references are preserved"),
  member("war3map.w3e", "terrain", "preserved_opaque", [], ALL_PROFILES, "terrain writer is not enabled"),
  member("war3map.wpm", "pathing", "preserved_opaque", [], ALL_PROFILES, "pathing writer is not enabled")
];

const OPERATION_COMPONENTS: Array<[string, string, CapabilityProfile[]]> = [
  ["set_map_metadata", "map_identity", ALL_PROFILES],
  ["create_player_slot", "players_forces", NATIVE_PROFILES], ["set_player_slot", "players_forces", NATIVE_PROFILES], ["delete_player_slot", "players_forces", NATIVE_PROFILES],
  ["create_force", "players_forces", NATIVE_PROFILES], ["set_force", "players_forces", NATIVE_PROFILES], ["delete_force", "players_forces", NATIVE_PROFILES],
  ["create_team", "players_forces", NATIVE_PROFILES], ["set_team", "players_forces", NATIVE_PROFILES], ["delete_team", "players_forces", NATIVE_PROFILES], ["set_team_arena", "players_forces", NATIVE_PROFILES], ["set_team_members", "players_forces", NATIVE_PROFILES],
  ["create_region", "regions", NATIVE_PROFILES], ["update_region", "regions", NATIVE_PROFILES], ["rename_region", "regions", NATIVE_PROFILES], ["delete_region", "regions", NATIVE_PROFILES], ["reorder_regions", "regions", NATIVE_PROFILES], ["set_region_role", "regions", NATIVE_PROFILES],
  ["create_object_definition", "object_data", NATIVE_PROFILES], ["update_object_definition", "object_data", NATIVE_PROFILES], ["delete_object_definition", "object_data", NATIVE_PROFILES], ["set_object_data", "object_data", NATIVE_PROFILES], ["set_object_reference", "references", NATIVE_PROFILES],
  ["place_object", "placements", NATIVE_PROFILES], ["place_unit", "placements", NATIVE_PROFILES], ["move_object", "placements", NATIVE_PROFILES], ["move_unit", "placements", NATIVE_PROFILES], ["update_placed_object", "placements", NATIVE_PROFILES], ["remove_placed_object", "placements", NATIVE_PROFILES], ["remove_placed_unit", "placements", NATIVE_PROFILES],
  ["set_script_source", "gameplay_source", NATIVE_PROFILES], ["upsert_script_module", "gameplay_source", NATIVE_PROFILES], ["remove_script_module", "gameplay_source", NATIVE_PROFILES], ["create_trigger", "gameplay_source", NATIVE_PROFILES], ["update_trigger", "gameplay_source", NATIVE_PROFILES], ["move_trigger", "gameplay_source", NATIVE_PROFILES], ["delete_trigger", "gameplay_source", NATIVE_PROFILES], ["create_variable", "gameplay_source", NATIVE_PROFILES], ["update_variable", "gameplay_source", NATIVE_PROFILES], ["delete_variable", "gameplay_source", NATIVE_PROFILES], ["set_trigger_mode", "gameplay_source", NATIVE_PROFILES]
];

export function isToolSupportedByProfile(profile: CapabilityProfile, name: string): boolean {
  const profileSpecificTools = new Set([...PROFILE_TOOLS.mvp_2arena, ...PROFILE_TOOLS.full_6team]);
  return !profileSpecificTools.has(name) || PROFILE_TOOLS[profile].includes(name);
}

export function isToolEnabledForProject(project: ProjectConfig, name: string): boolean {
  if (!isToolSupportedByProfile(project.profile, name)) return false;
  if (project.write_policy === "read_only" && !READ_ONLY_TOOLS.has(name)) return false;
  if (GLOBAL_KNOWLEDGE_TOOLS.has(name)) return true;
  return project.enabled_tools.length === 0 || project.enabled_tools.includes(name);
}

export function capabilityMatrix(project: ProjectConfig): Record<string, unknown> {
  const profiles = Object.fromEntries(CAPABILITY_PROFILES.map(profile => {
    const active = project.profile === profile;
    const profileMembers = MEMBERS.filter(item => item.profiles.includes(profile));
    const profileOperations = OPERATION_COMPONENTS.map(([operation, component, supportedProfiles]) => ({
      operation,
      component,
      profiles: supportedProfiles,
      capability: supportedProfiles.includes(profile) ? "typed_write_enabled" : "gated",
      enabled: active && supportedProfiles.includes(profile) && (operation !== "set_script_source" || project.script_policy === "mcp_owned_jass"),
      reason: supportedProfiles.includes(profile)
        ? operation === "set_script_source" && project.script_policy !== "mcp_owned_jass" ? "script_policy is not mcp_owned_jass" : "supported by active profile"
        : `not enabled for ${profile}`
    }));
    return [profile, {
      active,
      status: active ? profile === "gui_compatible" ? "gated" : "enabled" : "available",
      supported_tools: [...new Set([...READ_ONLY_TOOLS, ...MUTATING_TOOLS])].filter(name => isToolSupportedByProfile(profile, name)),
      enabled_tools: active ? [...new Set([...READ_ONLY_TOOLS, ...MUTATING_TOOLS])].filter(name => isToolEnabledForProject(project, name)) : [],
      members: profileMembers.map(item => item.member),
      operations: profileOperations.filter(item => item.profiles.includes(profile)).map(item => item.operation),
      evidence: profile === "gui_compatible" ? "gated_pending_exact_wtg_wct_wts_fixture_and_editor_evidence" : "static_only_until_exact_editor_and_game_observation"
    }];
  }));

  return {
    schema_version: "1.0",
    active_profile: project.profile,
    profiles,
    members: MEMBERS.map(item => ({
      ...item,
      profile_status: Object.fromEntries(CAPABILITY_PROFILES.map(profile => [profile, {
        supported: item.profiles.includes(profile),
        enabled: project.profile === profile && item.profiles.includes(profile) && (item.member !== "war3map.j" || project.script_policy === "mcp_owned_jass"),
        reason: item.profiles.includes(profile) ? item.evidence : `member is not enabled for ${profile}`
      }]))
    })),
    operations: OPERATION_COMPONENTS.map(([operation, component, profiles]) => ({
      operation,
      component,
      profiles,
      capability: profiles.includes(project.profile) ? "typed_write_enabled" : "gated",
      enabled: profiles.includes(project.profile) && (operation !== "set_script_source" || project.script_policy === "mcp_owned_jass"),
      reason: profiles.includes(project.profile)
        ? operation === "set_script_source" && project.script_policy !== "mcp_owned_jass" ? "script_policy is not mcp_owned_jass" : "supported by active profile"
        : `active profile ${project.profile} does not support this operation`
    })),
    gui_trigger_compatibility: {
      enabled: false,
      capability: "gated",
      reason: "Exact WTG/WCT/WTS serializer, fixture, and World Editor evidence are not available."
    }
  };
}

function member(memberName: string, component: string, capability: string, operations: string[], profiles: CapabilityProfile[] = NATIVE_PROFILES, evidence = "typed member round-trip evidence") : CapabilityMember {
  return { member: memberName, component, capability, supported_operations: operations, profiles, evidence };
}
