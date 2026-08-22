import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
  {
    // deploy/cluster.js is the App Service entry point, not app code: it is
    // copied next to the standalone `server.js` and started with plain
    // `node cluster.js`. The standalone package.json has no `"type":
    // "module"`, and `server.js` is CommonJS — so `require()` is the only
    // correct way to load it. Everything else stays linted.
    files: ["deploy/*.js"],
    rules: {
      "@typescript-eslint/no-require-imports": "off",
    },
  },
]);

export default eslintConfig;
