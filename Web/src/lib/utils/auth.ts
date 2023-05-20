export function isAuthenticated() {
    // Check if the user is authenticated
    const authToken = localStorage.getItem('authToken');
    return !!authToken;
}