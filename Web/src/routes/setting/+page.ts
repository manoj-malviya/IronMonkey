import { isAuthenticated } from '$lib/utils/auth.js';

export function load() {
  if (!isAuthenticated()) {
    return {
      status: 401,
      redirect: '/login'
    };
  }
}