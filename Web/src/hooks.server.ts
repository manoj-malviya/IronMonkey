import { parseJwt } from "$lib/utils/json";
import type { Handle } from "@sveltejs/kit";

export const handle: Handle = async ({event, resolve}) => {
    const session = event.cookies.get('session');
    if (!session) {
        // if there is no session load page as normal
        return await resolve(event)
    }
    const user = parseJwt(session);
    if (user) {
        event.locals.user = {
            name: "Manoj",
            role: "Admin",
          }
    }

    return await resolve(event)
}