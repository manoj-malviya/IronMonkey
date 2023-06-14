export function getUser(cookies) {
    // Check if the user is authenticated
    const authToken = cookies.get('session');

    const user = JSON.parse(authToken);

    return user;
}