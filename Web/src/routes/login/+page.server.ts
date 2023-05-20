import type { PageServerLoad, Actions } from './$types';
 
export const actions = {
  login: async ({ cookies, request }) => {
    const data = await request.formData();
    console.log(data);
    const formData = {
      Provider: 'google',
      IdToken: data.get('credential')
    };

    try{
      const response = await fetch('http://host.docker.internal:5400/auth/externallogin', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(formData)
      });
    
      if (response.ok) {
        const token = await response.json();
        console.log(token.token);
        cookies.set('accessToken', token.token);
        return { success: true, error: null };
      } else {
        console.error('Error:', response.statusText);
        return {success: false, error: response.statusText};
      }
    } catch(e) {
      console.log(e);
    }
  },
  register: async (event) => {
    // TODO register the user
  }
} satisfies Actions;
