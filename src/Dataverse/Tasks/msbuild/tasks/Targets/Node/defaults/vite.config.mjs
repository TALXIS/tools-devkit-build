// Default build for CodeApp projects without a "build" script in package.json.
// The SDK runs it as: <package manager exec> vite build --config <this file>, from the Node root.
//
// Contract with the SDK (environment variables):
//   NODE_ENV           development | production
//   BUILD_OUTPUT_DIR   folder the app is written to
//
// Plugins are resolved from the project's node_modules: React, Tailwind and the Power Apps
// plugin are used when installed. "@" is aliased to ./src as in the CodeApp template.
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = process.cwd();
const requireFromProject = createRequire(join(root, 'package.json'));
const production = process.env.NODE_ENV === 'production';

async function optionalPlugin(packageName, exportName) {
  let resolved;
  try {
    resolved = requireFromProject.resolve(packageName);
  } catch {
    return undefined;
  }
  const module = await import(pathToFileURL(resolved).href);
  const factory = exportName ? module[exportName] : (module.default ?? module);
  return typeof factory === 'function' ? factory() : undefined;
}

const plugins = (await Promise.all([
  optionalPlugin('@vitejs/plugin-react'),
  optionalPlugin('@tailwindcss/vite'),
  optionalPlugin('@microsoft/power-apps-vite', 'powerApps')
])).filter(Boolean);

export default {
  root,
  mode: production ? 'production' : 'development',
  plugins,
  resolve: { alias: { '@': join(root, 'src') } },
  build: {
    outDir: process.env.BUILD_OUTPUT_DIR || join(root, 'dist'),
    emptyOutDir: true,
    minify: production,
    sourcemap: production ? false : 'inline'
  }
};
