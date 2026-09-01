import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");
const v13Objects = JSON.parse(readFileSync(objectsPath, "utf8")).objects;

async function main() {
  console.log("=== Building Fix Build: v13 (Object Data Format v2 + Fog of War Altar Vision Fix) ===");

  const v13 = await buildMapVariant({
    variantId: "v13-custom-heroes-w3u-v2-format",
    versionLabel: "v13",
    description: "Complete MVP Custom Hero Selection build: uses war3map.w3u encoded with ObjectDataFormatVersion.v2 (fixing the 4-byte unk header alignment crash), includes custom heroes H001..H004 and shared altar n0AL, and grants vision modifier over the altar during selection.",
    heroObjects: v13Objects
  });

  console.log("\n=== v13 Build Successful ===");
  console.log("Output Path:", v13.output_path);
  console.log("SHA-256:", v13.output_sha256);
  console.log("Size (bytes):", v13.size_bytes);

  const reportPath = join(root, "builds", "diagnostics", "v13-build-report.json");
  writeFileSync(reportPath, JSON.stringify(v13, null, 2));
  console.log("Report saved to:", reportPath);
}

main().catch((err) => {
  console.error("Build failed:", err);
  process.exit(1);
});
