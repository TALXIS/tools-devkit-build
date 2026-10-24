// Honours the SDK contract: writes the deliverable to BUILD_OUTPUT_DIR.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
const out = process.env.BUILD_OUTPUT_DIR;
if (!out) { console.error('BUILD_OUTPUT_DIR not set'); process.exit(1); }
mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'fx_outdir.js'), `var fx_outdir = "${process.env.NODE_ENV}";\n`);
