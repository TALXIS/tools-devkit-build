// Default build for ScriptLibrary projects without a "build" script in package.json.
// The SDK runs it as: <package manager exec> rollup -c <this file>, from the project's Node root.
//
// Contract with the SDK (environment variables):
//   NODE_ENV               development | production
//   BUILD_OUTPUT_DIR       folder the bundle is written to
//   TALXIS_LIBRARY_NAME    ScriptLibraryName: output file name and UMD global name
//
// Plugins are resolved from the project's node_modules, not from this file's location, so the
// project's own package versions are used. Only rollup itself is required; TypeScript, module
// resolution, CommonJS and minification are used when their plugin is installed.
import { createRequire } from 'node:module';
import { existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = process.cwd();
const requireFromProject = createRequire(join(root, 'package.json'));
const production = process.env.NODE_ENV === 'production';
const outputDir = process.env.BUILD_OUTPUT_DIR || join(root, 'build');
const name = process.env.TALXIS_LIBRARY_NAME || 'main';

async function optionalPlugin(packageName) {
  let resolved;
  try {
    resolved = requireFromProject.resolve(packageName);
  } catch {
    return undefined;
  }
  const module = await import(pathToFileURL(resolved).href);
  return module.default ?? module;
}

const input = ['src/index.ts', 'src/index.js'].map((file) => resolve(root, file)).find(existsSync);
if (!input) {
  throw new Error(`No entry point: expected src/index.ts or src/index.js under ${root}.`);
}

const typescript = input.endsWith('.ts') ? await optionalPlugin('@rollup/plugin-typescript') : undefined;
if (input.endsWith('.ts') && !typescript) {
  throw new Error('src/index.ts needs @rollup/plugin-typescript (and typescript, tslib) in devDependencies.');
}
const nodeResolve = await optionalPlugin('@rollup/plugin-node-resolve');
const commonjs = await optionalPlugin('@rollup/plugin-commonjs');
const terser = production ? await optionalPlugin('@rollup/plugin-terser') : undefined;
if (production && !terser) {
  console.warn('[TALXIS] @rollup/plugin-terser is not installed; the production bundle is not minified.');
}

const tsconfig = existsSync(join(root, 'tsconfig.json')) ? join(root, 'tsconfig.json') : false;

export default {
  input,
  output: {
    file: join(outputDir, `${name}.js`),
    format: 'umd',
    name,
    sourcemap: production ? false : 'inline'
  },
  plugins: [
    nodeResolve?.({ browser: true }),
    commonjs?.(),
    typescript?.({ tsconfig, sourceMap: !production, inlineSources: !production, outDir: undefined, declaration: false }),
    terser?.()
  ].filter(Boolean)
};
