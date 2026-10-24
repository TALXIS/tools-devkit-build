// Fixture: a project that owns its rollup config and honours the SDK contract
// (NODE_ENV decides minification and source maps).
import typescript from '@rollup/plugin-typescript';
import terser from '@rollup/plugin-terser';

const production = process.env.NODE_ENV === 'production';

export default {
  input: 'src/index.ts',
  output: {
    file: 'build/fx_rollup.js',
    format: 'umd',
    name: 'FxRollup',
    sourcemap: production ? false : 'inline'
  },
  plugins: [
    typescript({ tsconfig: './tsconfig.json' }),
    ...(production ? [terser()] : [])
  ]
};
