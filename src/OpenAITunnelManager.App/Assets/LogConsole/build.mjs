import { build } from 'esbuild';
import { copyFile, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = new URL('.', import.meta.url).pathname;
const dist = resolve(root, 'dist');
await mkdir(dist, { recursive: true });

await build({
  entryPoints: [resolve(root, 'src/console.js')],
  bundle: true,
  minify: true,
  format: 'iife',
  platform: 'browser',
  target: ['chrome120'],
  outfile: resolve(dist, 'console.js'),
  legalComments: 'none'
});

await Promise.all([
  copyFile(resolve(root, 'src/index.html'), resolve(dist, 'index.html')),
  copyFile(resolve(root, 'src/console.css'), resolve(dist, 'console.css')),
  copyFile(resolve(root, 'node_modules/@xterm/xterm/css/xterm.css'), resolve(dist, 'xterm.css')),
  copyFile(resolve(root, 'THIRD-PARTY-NOTICES.txt'), resolve(dist, 'THIRD-PARTY-NOTICES.txt'))
]);
