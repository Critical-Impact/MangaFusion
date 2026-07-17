import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'

// The SPA builds directly into the ASP.NET host's wwwroot so it is served same-origin,
// and in dev proxies API/auth/hub traffic to the running ASP.NET host.
const API_TARGET = process.env.API_TARGET ?? 'http://localhost:5253'

// https://vite.dev/config/
export default defineConfig({
  plugins: [tailwindcss(), svelte()],
  resolve: {
    // Not a SvelteKit project, so $lib isn't aliased automatically — shadcn-svelte's generated
    // components (src/lib/components/ui/**) assume it. tsconfig.app.json already declares this
    // path for the TS/svelte-check side; Vite's own bundler needs its own alias to match.
    alias: {
      $lib: fileURLToPath(new URL('./src/lib', import.meta.url)),
    },
  },
  build: {
    outDir: '../src/MangaFusion.Web/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: API_TARGET, changeOrigin: true },
      '/health': { target: API_TARGET, changeOrigin: true },
      '/hubs': { target: API_TARGET, changeOrigin: true, ws: true },
    },
  },
})
