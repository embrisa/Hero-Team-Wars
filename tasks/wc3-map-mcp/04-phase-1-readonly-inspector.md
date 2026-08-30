# Phase 1 - Read-Only Inspector

## Objective

Expose reliable map inspection through MCP before any mutation tools exist.

## Implement

- `wc3_project_status`
- `wc3_inspect_map`
- `wc3_list_archive_files`
- `wc3_get_component`
- `wc3_validate_map` in read-only mode
- `wc3_compare_maps`
- pagination/filtering for large collections
- canonical JSON serialization and stable ordering

## Inventory coverage

- map metadata and format version;
- player slots, starts, forces, alliances, and shared controls;
- regions and cameras;
- variables and trigger inventory where parsable;
- custom object data and rawcodes;
- placed units, items, destructibles, doodads, and starting locations;
- terrain dimensions, tileset metadata, pathing/shadow presence;
- imported files and script language;
- unsupported or opaque data.

## Hero Team Wars mapping

Generate an `HTW-00` candidate report that matches the sections in `design/08-implementation-chunks.md`. Values unavailable from the archive must remain `unknown` and be listed for manual World Editor verification.

## Acceptance criteria

- All tools are formally marked read-only.
- Repeated inspection of the same map returns the same canonical result.
- Filters never change the underlying inventory.
- The agent can distinguish observed values, derived values, and unknown values.
- The original map hash remains unchanged.
