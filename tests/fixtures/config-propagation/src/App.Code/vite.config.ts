import { defineConfig } from 'vite';

// Fixture: a project that owns its Vite config and honours the SDK contract.
export default defineConfig(({ mode }) => ({
  build: {
    minify: mode === 'production',
    sourcemap: mode === 'production' ? false : 'inline'
  }
}));
