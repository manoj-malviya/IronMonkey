<script lang="ts">
    import NavbarDashboard from '$lib/components/NavbarDashboard.svelte';
    import Sidebar from '$lib/components/Sidebar.svelte';
    import { beforeUpdate } from 'svelte';
	import '@assets/app.postcss';
</script>
<svelte:head>
  <title>SvelteKit Auth</title>
</svelte:head>
<NavbarDashboard />
<div class="flex overflow-hidden bg-white pt-16">
	<Sidebar />
	<div class="hidden fixed inset-0 z-10 bg-gray-900 opacity-50" id="sidebarBackdrop"></div>
	<div class="h-full w-full bg-gray-50 relative overflow-y-auto lg:ml-64">
		<main>
			<div class="px-4 pt-6">
			<slot />
			</div>	
		</main>
	</div>
</div>
