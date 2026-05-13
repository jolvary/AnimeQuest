export type MalAnimeNode = {
  id: number;
  title: string;
  genres?: { name: string }[];
  num_episodes?: number;
  start_season?: { year?: number };
  media_type?: string;
  main_picture?: { medium?: string; large?: string };
  synopsis?: string;
  mean?: number;
  rank?: number;
  popularity?: number;
  num_list_users?: number;
};

type JikanTrailer = {
  youtube_id?: string | null;
  url?: string | null;
  embed_url?: string | null;
};

type JikanPromo = {
  trailer?: JikanTrailer | null;
};

type JikanAnimeVideosResponse = {
  data?: {
    promo?: JikanPromo[];
    promos?: JikanPromo[];
  };
};

type JikanAnimeFullResponse = {
  data?: {
    trailer?: JikanTrailer | null;
  };
};

export type MalTokenResponse = {
  access_token: string;
  refresh_token?: string;
  expires_in?: number;
};

export type MalCurrentUser = {
  id: number;
  name: string;
};

const MAL_API_BASE = "https://api.myanimelist.net/v2";
const MAL_AUTH_URL = "https://myanimelist.net/v1/oauth2/authorize";
const MAL_TOKEN_URL = "https://myanimelist.net/v1/oauth2/token";
const JIKAN_API_BASE = "https://api.jikan.moe/v4";
const EXTERNAL_FETCH_TIMEOUT_MS = 20000;
const EXTERNAL_FETCH_MAX_ATTEMPTS = 4;
const ANIME_FIELDS = [
  "id",
  "title",
  "main_picture",
  "synopsis",
  "mean",
  "rank",
  "popularity",
  "num_list_users",
  "genres",
  "num_episodes",
  "start_season",
  "media_type",
].join(",");

function normalizeYoutubeId(value?: string | null) {
  if (!value) return null;
  const trimmed = value.trim();
  return /^[A-Za-z0-9_-]{6,32}$/.test(trimmed) ? trimmed : null;
}

function youtubeIdFromUrl(value?: string | null) {
  if (!value) return null;

  try {
    const url = new URL(value);
    const directId = normalizeYoutubeId(url.searchParams.get("v"));
    if (directId) return directId;

    const pathMatch = url.pathname.match(/\/(?:embed|shorts|watch)\/([A-Za-z0-9_-]{6,32})/);
    if (pathMatch) return normalizeYoutubeId(pathMatch[1]);
  } catch {
    const fallbackMatch = value.match(/(?:embed\/|watch\?v=|youtu\.be\/)([A-Za-z0-9_-]{6,32})/);
    if (fallbackMatch) return normalizeYoutubeId(fallbackMatch[1]);
  }

  return null;
}

function trailerYoutubeId(trailer?: JikanTrailer | null) {
  return normalizeYoutubeId(trailer?.youtube_id) ??
    youtubeIdFromUrl(trailer?.url) ??
    youtubeIdFromUrl(trailer?.embed_url);
}

function authHeaders(clientId: string, accessToken?: string): Record<string, string> {
  const headers: Record<string, string> = { "X-MAL-CLIENT-ID": clientId };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  return headers;
}

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function shouldRetryResponse(response: Response) {
  return response.status === 429 || response.status >= 500;
}

function retryDelayMs(attempt: number, response?: Response) {
  const retryAfter = response?.headers.get("retry-after");
  if (retryAfter) {
    const seconds = Number.parseFloat(retryAfter);
    if (Number.isFinite(seconds) && seconds > 0) return Math.min(seconds * 1000, 30000);
  }

  return Math.min(1000 * attempt, 5000);
}

async function fetchWithRetry(url: string, init?: RequestInit) {
  let lastError: unknown;

  for (let attempt = 1; attempt <= EXTERNAL_FETCH_MAX_ATTEMPTS; attempt += 1) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), EXTERNAL_FETCH_TIMEOUT_MS);

    try {
      const response = await fetch(url, { ...init, signal: controller.signal });
      if (!shouldRetryResponse(response) || attempt === EXTERNAL_FETCH_MAX_ATTEMPTS) {
        return response;
      }

      lastError = new Error(`External request failed with retryable status ${response.status}`);
      console.warn(`External request retry ${attempt}/${EXTERNAL_FETCH_MAX_ATTEMPTS}: status=${response.status}; url=${url}`);
      await sleep(retryDelayMs(attempt, response));
    } catch (error) {
      lastError = error;
      if (attempt === EXTERNAL_FETCH_MAX_ATTEMPTS) break;

      console.warn(`External request retry ${attempt}/${EXTERNAL_FETCH_MAX_ATTEMPTS}: error=${error instanceof Error ? error.message : String(error)}; url=${url}`);
      await sleep(retryDelayMs(attempt));
    } finally {
      clearTimeout(timeout);
    }
  }

  throw lastError instanceof Error ? lastError : new Error(String(lastError));
}

function encodeMalUsernamePath(username: string) {
  return username === "@me" ? "@me" : encodeURIComponent(username);
}

function hasMalImage(node: MalAnimeNode) {
  return Boolean(node.main_picture?.large || node.main_picture?.medium);
}

function mergeAnimeDetails(base: MalAnimeNode, details: MalAnimeNode): MalAnimeNode {
  return {
    ...base,
    ...details,
    main_picture: details.main_picture ?? base.main_picture,
    synopsis: details.synopsis ?? base.synopsis,
    genres: details.genres ?? base.genres,
    num_episodes: details.num_episodes ?? base.num_episodes,
    start_season: details.start_season ?? base.start_season,
    media_type: details.media_type ?? base.media_type,
  };
}

async function enrichAnimeNodeImage(params: { clientId: string; accessToken?: string; node: MalAnimeNode }) {
  if (hasMalImage(params.node)) return params.node;

  try {
    const details = await fetchAnimeDetails({
      clientId: params.clientId,
      accessToken: params.accessToken,
      animeId: params.node.id,
    });
    return mergeAnimeDetails(params.node, details);
  } catch {
    return params.node;
  }
}

async function enrichAnimeEntries<T extends { node: MalAnimeNode }>(params: {
  clientId: string;
  accessToken?: string;
  entries: T[];
}) {
  const enriched: T[] = [];
  for (const entry of params.entries) {
    const node = await enrichAnimeNodeImage({
      clientId: params.clientId,
      accessToken: params.accessToken,
      node: entry.node,
    });
    enriched.push({ ...entry, node } as T);
  }
  return enriched;
}

export async function fetchTopAnimePage(params: { clientId: string; limit: number; offset: number }) {
  const url = `${MAL_API_BASE}/anime/ranking?ranking_type=all&limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(ANIME_FIELDS)}`;
  const response = await fetchWithRetry(url, { headers: authHeaders(params.clientId) });
  if (!response.ok) throw new Error(`MAL ranking request failed: ${response.status}`);
  const payload = (await response.json()) as { data: { node: MalAnimeNode }[]; paging?: { next?: string } };
  return {
    ...payload,
    data: await enrichAnimeEntries({ clientId: params.clientId, entries: payload.data ?? [] }),
  };
}

export async function fetchAnimeDetails(params: { clientId: string; animeId: number; accessToken?: string }) {
  const url = `${MAL_API_BASE}/anime/${params.animeId}?fields=${encodeURIComponent(ANIME_FIELDS)}`;
  const response = await fetchWithRetry(url, { headers: authHeaders(params.clientId, params.accessToken) });
  if (!response.ok) throw new Error(`MAL anime detail request failed: ${response.status}`);
  return (await response.json()) as MalAnimeNode;
}

export async function fetchAnimeTrailerYoutubeId(params: { animeId: number }) {
  const videosUrl = `${JIKAN_API_BASE}/anime/${params.animeId}/videos`;
  const videosResponse = await fetchWithRetry(videosUrl, {
    headers: { "user-agent": "AnimeQuest/0.1 trailer lookup" },
  });

  if (videosResponse.ok) {
    const payload = (await videosResponse.json()) as JikanAnimeVideosResponse;
    const promos = payload.data?.promo ?? payload.data?.promos ?? [];
    for (const promo of promos) {
      const youtubeId = trailerYoutubeId(promo.trailer);
      if (youtubeId) return youtubeId;
    }
  }

  const fullUrl = `${JIKAN_API_BASE}/anime/${params.animeId}/full`;
  const fullResponse = await fetchWithRetry(fullUrl, {
    headers: { "user-agent": "AnimeQuest/0.1 trailer lookup" },
  });

  if (!fullResponse.ok) return null;

  const payload = (await fullResponse.json()) as JikanAnimeFullResponse;
  return trailerYoutubeId(payload.data?.trailer);
}

export async function fetchCurrentMalUser(params: { clientId: string; accessToken: string }) {
  const fields = ["id", "name"].join(",");
  const url = `${MAL_API_BASE}/users/@me?fields=${encodeURIComponent(fields)}`;
  const response = await fetchWithRetry(url, { headers: authHeaders(params.clientId, params.accessToken) });
  if (!response.ok) throw new Error(`MAL current user request failed: ${response.status}`);
  return (await response.json()) as MalCurrentUser;
}

export async function fetchUserAnimeList(params: { clientId: string; accessToken?: string; username: string; limit: number; offset: number }) {
  const fields = `${ANIME_FIELDS},list_status`;
  const usernamePath = encodeMalUsernamePath(params.username);
  const url = `${MAL_API_BASE}/users/${usernamePath}/animelist?limit=${params.limit}&offset=${params.offset}&fields=${encodeURIComponent(fields)}`;
  const response = await fetchWithRetry(url, { headers: authHeaders(params.clientId, params.accessToken) });
  if (!response.ok) throw new Error(`MAL user list request failed: ${response.status}`);
  const payload = (await response.json()) as {
    data: { node: MalAnimeNode; list_status?: { status?: string; score?: number; num_episodes_watched?: number } }[];
    paging?: { next?: string };
  };
  return {
    ...payload,
    data: await enrichAnimeEntries({ clientId: params.clientId, accessToken: params.accessToken, entries: payload.data ?? [] }),
  };
}

export function buildMalAuthorizationUrl(params: {
  clientId: string;
  redirectUri: string;
  state: string;
  codeVerifier: string;
}) {
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

export async function exchangeMalCodeForToken(params: {
  clientId: string;
  clientSecret: string;
  code: string;
  codeVerifier: string;
  redirectUri: string;
}) {
  const body = new URLSearchParams({
    client_id: params.clientId,
    client_secret: params.clientSecret,
    grant_type: "authorization_code",
    code: params.code,
    redirect_uri: params.redirectUri,
    code_verifier: params.codeVerifier,
  });

  const response = await fetchWithRetry(MAL_TOKEN_URL, { method: "POST", body });
  if (!response.ok) throw new Error(`MAL token exchange failed: ${response.status}`);
  return response.json() as Promise<MalTokenResponse>;
}

export async function refreshMalAccessToken(params: { clientId: string; clientSecret: string; refreshToken: string }) {
  const body = new URLSearchParams({
    client_id: params.clientId,
    client_secret: params.clientSecret,
    grant_type: "refresh_token",
    refresh_token: params.refreshToken,
  });
  const response = await fetchWithRetry(MAL_TOKEN_URL, { method: "POST", body });
  if (!response.ok) throw new Error(`MAL token refresh failed: ${response.status}`);
  return response.json() as Promise<MalTokenResponse>;
}
