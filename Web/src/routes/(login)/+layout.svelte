<script lang="ts">
    import Sidebar from '$lib/components/Sidebar.svelte';
    import { beforeUpdate } from 'svelte';
	import '@assets/app.postcss';
</script>
<svelte:head>
  <title>Login</title>
</svelte:head>
<div class="flex bg-white">
		<main>
			<div class="px-4 pt-6">
			<slot />
			</div>	
		</main>
</div>
