import preprocess from "svelte-preprocess";
import adapter from "@sveltejs/adapter-auto";
import { vitePreprocess } from "@sveltejs/kit/vite";
import { resolve } from 'path';

/** @type {import('@sveltejs/kit').Config} */
const config = {
  // Consult https://kit.svelte.dev/docs/integrations#preprocessors
  // for more information about preprocessors
  preprocess: [
    vitePreprocess(),
    preprocess({
      postcss: true,
    }),
  ],

  kit: {
    // adapter-auto only supports some environments, see https://kit.svelte.dev/docs/adapter-auto for a list.
    // If your environment is not supported or you settled on a specific environment, switch out the adapter.
    // See https://kit.svelte.dev/docs/adapters for more information about adapters.
    adapter: adapter(),
    // csp: {
    // 	directives: {
    // 		'referrer': ['no-referrer-when-downgrade']
    // 	}
    // }
    alias: {
      // Add your custom aliases here
      '@assets': resolve('./src/assets')
    }
  },
};

export default config;
