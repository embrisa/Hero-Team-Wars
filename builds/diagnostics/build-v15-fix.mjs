import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildMapVariant } from "./build-matrix-runner.mjs";

const root = "C:\\Users\\hp\\Documents\\Warcraft III\\Hero Team Wars";
const objectsPath = join(root, "tools", "wc3-map-mcp", "scripts", "mcp", "object-data", "v8-hero-objects.json");
const v15Objects = JSON.parse(readFileSync(objectsPath, "utf8")).objects;

async function main() {
  console.log("=== Building MVP v15 (Instant Custom Hero Stock) ===");

  const v15 = await buildMapVariant({
    variantId: "v15-instant-hero-stock",
    versionLabel: "v15",
    description: "MVP custom hero selection build with ObjectDataFormatVersion.v2, custom heroes H001..H004, shared altar n0AL, persistent altar vision/camera focus, and uhst=0 / usst=1 stock overrides for instant hero availability.",
    heroObjects: v15Objects
  });

  console.log("\n=== v15 Build Successful ===");
  console.log("Output Path:", v15.output_path);
  console.log("SHA-256:", v15.output_sha256);
  console.log("Size (bytes):", v15.output_size_bytes);

  const reportPath = join(root, "builds", "diagnostics", "v15-build-report.json");
  writeFileSync(reportPath, JSON.stringify(v15, null, 2));
  console.log("Report saved to:", reportPath);
}

main().catch((err) => {
  console.error("Build failed:", err);
  process.exit(1);
});
