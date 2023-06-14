import { goto } from '$app/navigation';
import { isAuthenticated } from '$lib/utils/auth';

export async function beforeEach(to: any, from: any) {
  if (to.meta && to.meta.auth && !await isAuthenticated()) {
    goto('/login'); // Redirect to login page if user is not authenticated
    return false; // Prevent access to the protected page
  }
}