import { redirect, fail } from '@sveltejs/kit';
import type { PageServerLoad, Actions, Action } from './$types';

export const load: PageServerLoad = async ({ locals }) => {
  // redirect user if logged in
  console.log(locals);
  if (locals.user) {
    throw redirect(302, '/')
  }
}
 
const login: Action = async ({ cookies, request }) => {
    const data = await request.formData();
    const formData = {
      Provider: 'google',
      IdToken: data.get('credential')
    };

    try{
      const response = await fetch('http://host.docker.internal:5002/auth/externallogin', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(formData)
      });
    
      if (response.ok) {
        const token = await response.json();
        cookies.set('session', token.token, {
          path: '/',
          httpOnly: true,
          sameSite: 'strict',
          maxAge: 24*60*60
        });

        throw redirect(302, '/');
      } else {
        return fail(400, { credentials: true });
      }
    } catch(e) {
      console.log(e);
    }
  }

  export const actions: Actions = { login }