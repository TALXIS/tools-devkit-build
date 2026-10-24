// Mimics Vite: writes index.html plus a content-hashed asset into dist/ (cleared each build).
import { mkdirSync, writeFileSync, rmSync, readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
const src = readFileSync('src.js', 'utf8');
const hash = createHash('sha256').update(src).digest('hex').slice(0, 8);
rmSync('dist', { recursive: true, force: true });
mkdirSync('dist/assets', { recursive: true });
writeFileSync(`dist/assets/index-${hash}.js`, src);
writeFileSync('dist/index.html', `<script src="assets/index-${hash}.js"></script>\n`);
