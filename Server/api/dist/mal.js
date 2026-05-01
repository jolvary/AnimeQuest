"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.fetchTopAnimePage = fetchTopAnimePage;
exports.fetchCurrentMalUser = fetchCurrentMalUser;
exports.fetchUserAnimeList = fetchUserAnimeList;
exports.buildMalAuthorizationUrl = buildMalAuthorizationUrl;
exports.exchangeMalCodeForToken = exchangeMalCodeForToken;
exports.refreshMalAccessToken = refreshMalAccessToken;
const MAL_API_BASE = "https://api.myanimelist.net/v2";
const MAL_AUTH_URL = "https://myanimelist.net/v1/oauth2/authorize";
const MAL_TOKEN_URL = "https://myanimelist.net/v1/oauth2/token";
function authHeaders(clientId, accessToken) {
    const headers = { "X-MAL-CLIENT-ID": clientId };
    if (accessToken)
        headers.Authorization = `Bearer ${accessToken}`;
    return headers;
}
function encodeMalUsernamePath(username) {
    return username === "@me" ? "@me" : encodeURIComponent(username);
}
async function fetchTopAnimePage(params) {
    const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type"].join(",");
    const url = `${MAL_API_BASE}/anime/ranking?ranking_type=all&limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
    const response = await fetch(url, { headers: authHeaders(params.clientId) });
    if (!response.ok)
        throw new Error(`MAL ranking request failed: ${response.status}`);
    return (await response.json());
}
async function fetchCurrentMalUser(params) {
    const fields = ["id", "name"].join(",");
    const url = `${MAL_API_BASE}/users/@me?fields=${encodeURIComponent(fields)}`;
    const response = await fetch(url, { headers: authHeaders(params.clientId, params.accessToken) });
    if (!response.ok)
        throw new Error(`MAL current user request failed: ${response.status}`);
    return (await response.json());
}
async function fetchUserAnimeList(params) {
    const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type", "my_list_status"].join(",");
    const usernamePath = encodeMalUsernamePath(params.username);
    const url = `${MAL_API_BASE}/users/${usernamePath}/animelist?limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
    const response = await fetch(url, { headers: authHeaders(params.clientId, params.accessToken) });
    if (!response.ok)
        throw new Error(`MAL user list request failed: ${response.status}`);
    return (await response.json());
}
function buildMalAuthorizationUrl(params) {
    const query = new URLSearchParams({
        response_type: "code",
        client_id: params.clientId,
        state: params.state,
        redirect_uri: params.redirectUri,
        code_challenge: params.codeVerifier,
        code_challenge_method: "plain",
    });
    return `${MAL_AUTH_URL}?${query.toString()}`;
}
async function exchangeMalCodeForToken(params) {
    const body = new URLSearchParams({
        client_id: params.clientId,
        client_secret: params.clientSecret,
        grant_type: "authorization_code",
        code: params.code,
        redirect_uri: params.redirectUri,
        code_verifier: params.codeVerifier,
    });
    const response = await fetch(MAL_TOKEN_URL, { method: "POST", body });
    if (!response.ok)
        throw new Error(`MAL token exchange failed: ${response.status}`);
    return response.json();
}
async function refreshMalAccessToken(params) {
    const body = new URLSearchParams({
        client_id: params.clientId,
        client_secret: params.clientSecret,
        grant_type: "refresh_token",
        refresh_token: params.refreshToken,
    });
    const response = await fetch(MAL_TOKEN_URL, { method: "POST", body });
    if (!response.ok)
        throw new Error(`MAL token refresh failed: ${response.status}`);
    return response.json();
}
