export type MalAnimeNode = {
  id: number;
  title: string;
  genres?: { name: string }[];
  num_episodes?: number;
  start_season?: { year?: number };
  media_type?: string;
};

const MAL_API_BASE = "https://api.myanimelist.net/v2";

function authHeaders(clientId: string, accessToken?: string): Record<string, string> {
  const headers: Record<string, string> = { "X-MAL-CLIENT-ID": clientId };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  return headers;
}

export async function fetchTopAnimePage(params: { clientId: string; limit: number; offset: number }) {
  const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type"].join(",");
  const url = `${MAL_API_BASE}/anime/ranking?ranking_type=all&limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
  const response = await fetch(url, { headers: authHeaders(params.clientId) });
  if (!response.ok) throw new Error(`MAL ranking request failed: ${response.status}`);
  return (await response.json()) as { data: { node: MalAnimeNode }[]; paging?: { next?: string } };
}

export async function fetchUserAnimeList(params: { clientId: string; accessToken?: string; username: string; limit: number; offset: number }) {
  const fields = ["id", "title", "genres", "num_episodes", "start_season", "media_type", "my_list_status"].join(",");
  const url = `${MAL_API_BASE}/users/${encodeURIComponent(params.username)}/animelist?limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
  const response = await fetch(url, { headers: authHeaders(params.clientId, params.accessToken) });
  if (!response.ok) throw new Error(`MAL user list request failed: ${response.status}`);
  return (await response.json()) as {
    data: { node: MalAnimeNode; list_status?: { status?: string; score?: number; num_episodes_watched?: number } }[];
    paging?: { next?: string };
  };
}
