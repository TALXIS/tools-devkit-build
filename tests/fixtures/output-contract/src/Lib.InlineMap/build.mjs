// Ignores NODE_ENV: always embeds an inline source map and pads to a known size.
import { mkdirSync, writeFileSync } from 'node:fs';
mkdirSync('dist', { recursive: true });
const padding = '/*' + 'x'.repeat(2000) + '*/\n';
writeFileSync('dist/fx_inline.js', 'var fx_inline = 1;\n' + padding + '//# sourceMappingURL=data:application/json;base64,e30=\n');
