// Writes a different file name than ScriptLibraryName: the SDK must fail with a clear message.
import { mkdirSync, writeFileSync } from 'node:fs';
mkdirSync('build', { recursive: true });
writeFileSync('build/something_else.js', 'var x = 1;\n');
