<script context="module" lang="ts">
  export interface AuthResponse {
      readonly clientId: string
      readonly credential: string
   }
  
  const googleScript = 'https://accounts.google.com/gsi/client';
</script>
<script lang="ts">
  import { onMount } from "svelte";
  import { loadScript, removeScript } from '$lib/utils/script';

  // let idToken:string = "";
  // const handleSignIn = async (credential: string) => {
  //   idToken = credential;
  //   const form = document.getElementById('loginForm') as HTMLFormElement;
  //   form['credential'].value = credential;
  //   form.submit();
  // }

  onMount(() => {
    // loadScript(googleScript, 'google-login').then(() => {
    //   google.accounts.id.initialize({
    //     client_id: '401449407466-m1p50q3a67dkjfmn4qcraob5gjo7k4an.apps.googleusercontent.com',
    //     callback: (res: AuthResponse) => {
    //       handleSignIn(res.credential);
    //     }
    //   });
    //   const parent = document.getElementById('google_btn');
    //   google.accounts.id.renderButton(parent, {theme: "filled_blue"});
    // })

    loadScript(googleScript, 'google-login');
    
  })
</script>
  
<!-- <svelte:head>
  <script src="https://accounts.google.com/gsi/client" async defer></script>
</svelte:head> -->
<!-- <div id="google_btn" />
<form id="loginForm" method="POST" action="/login?/login">
  <input name="credential" type="hidden" value="{idToken}">
</form> -->

<div id="g_id_onload"
     data-client_id="401449407466-m1p50q3a67dkjfmn4qcraob5gjo7k4an.apps.googleusercontent.com"
     data-context="signin"
     data-ux_mode="popup"
     data-login_uri="/login?/login"
     data-auto_prompt="false">
</div>
<div class="g_id_signin"
     data-type="standard"
     data-shape="rectangular"
     data-theme="outline"
     data-text="signin_with"
     data-size="large"
     data-logo_alignment="left">
</div>