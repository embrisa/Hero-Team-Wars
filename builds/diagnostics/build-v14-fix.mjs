import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");
const v14Objects = JSON.parse(readFileSync(objectsPath, "utf8")).objects;

async function main() {
  console.log("=== Building Fix Build: v14 (Persisted Altar Vision via FogModifierStart + Camera Pan + Selection) ===");

  const v14 = await buildMapVariant({
    variantId: "v14-persisted-altar-vision",
    versionLabel: "v14",
    description: "Complete MVP Custom Hero Selection build: uses war3map.w3u v2 format, custom heroes H001..H004 and shared altar n0AL. At map start, creates and starts persistent FogModifiers (FogModifierStart) for all players over the altar area, pans camera to altar, and selects the altar.",
    heroObjects: v14Objects
  });

  console.log("\n=== v14 Build Successful ===");
  console.log("Output Path:", v14.output_path);
  console.log("SHA-256:", v14.output_sha256);

  const reportPath = join(root, "builds", "diagnostics", "v14-build-report.json");
  writeFileSync(reportPath, JSON.stringify(v14, null, 2));
  console.log("Report saved to:", reportPath);
}

main().catch((err) => {
  console.error("Build failed:", err);
  process.exit(1);
});
