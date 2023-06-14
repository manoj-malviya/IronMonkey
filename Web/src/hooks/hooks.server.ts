import type { Handle } from "@sveltejs/kit";

export const handle: Handle = async ({event, resolve}) => {
    const session = event.cookies.get('accessToken');
    if (!session) {
        // if there is no session load page as normal
        return await resolve(event)
    }

    const user = JSON.parse(session);
    console.log(user);
    if (user) {
        event.locals.user = {
            name: "Manoj",
            role: "Admin",
          }
    }

    return await resolve(event)
}