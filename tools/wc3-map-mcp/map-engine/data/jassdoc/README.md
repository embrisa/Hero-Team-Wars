# Canonical JASS API dataset

`jass-api.json` is generated locally from the pinned [lep/jassdoc](https://github.com/lep/jassdoc) checkout at commit `deddec452ec16ea355ca0aa47046b88d416dbc65`.

Run `scripts/sync-jassdoc.ps1` from any working directory. The bootstrap helper fetches the exact commit into a temporary checkout, imports `common.j`, `Blizzard.j`, and `builtin-types.j` with `scripts/import-jassdoc.mjs`, and removes the temporary checkout after generation:

```powershell
& "C:\Users\hp\Documents\Warcraft III\Hero Team Wars\tools\wc3-map-mcp\scripts\sync-jassdoc.ps1"
```

For an already checked-out copy of jassdoc, pass `-SourceRoot`; the checkout must resolve to the pinned commit:

```powershell
& "...\scripts\sync-jassdoc.ps1" -SourceRoot "C:\src\jassdoc"
```

The generated JSON and any optional local source checkout are intentionally ignored by Git. This avoids redistributing upstream source or generated data before the jassdoc license and redistribution terms have been reviewed. Runtime consumers should treat the generated file as a local build prerequisite.

The importer emits a deterministic `schema_version`, source repository/commit, and sorted `symbols` array. Each symbol retains its source declaration, kind (`native`, `function`, `type`, or `global`), parameters, return type, documentation, annotations, source line, and type inheritance where applicable. No timestamps or host-specific metadata are written.
