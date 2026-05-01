"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.fetchTopAnimePage = fetchTopAnimePage;
exports.fetchUserAnimeList = fetchUserAnimeList;
const MAL_API_BASE = "https://api.myanimelist.net/v2";
function authHeaders(clientId, accessToken) {
    const headers = { "X-MAL-CLIENT-ID": clientId };
    if (accessToken)
        headers.Authorization = `Bearer ${accessToken}`;
    return headers;
}
async function fetchTopAnimePage(params) {
    const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type"].join(",");
    const url = `${MAL_API_BASE}/anime/ranking?ranking_type=all&limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
    const response = await fetch(url, { headers: authHeaders(params.clientId) });
    if (!response.ok)
        throw new Error(`MAL ranking request failed: ${response.status}`);
    return (await response.json());
}
async function fetchUserAnimeList(params) {
    const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type", "my_list_status"].join(",");
    const url = `${MAL_API_BASE}/users/${encodeURIComponent(params.username)}/animelist?limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
    const response = await fetch(url, { headers: authHeaders(params.clientId, params.accessToken) });
    if (!response.ok)
        throw new Error(`MAL user list request failed: ${response.status}`);
    return (await response.json());
}
