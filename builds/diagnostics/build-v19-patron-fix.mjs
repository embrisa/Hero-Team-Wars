import { main } from "./build-v18-fix.mjs";

main(19).catch((error) => {
  console.error("v19 build failed:", error);
  process.exit(1);
});
