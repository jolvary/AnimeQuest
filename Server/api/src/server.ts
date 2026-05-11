import Fastify, { type FastifyReply, type FastifyRequest } from "fastify";
import cors from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import swagger from "@fastify/swagger";
import swaggerUI from "@fastify/swagger-ui";
import Redis from "ioredis";
import { Prisma, PrismaClient } from "@prisma/client";
import { createCipheriv, createDecipheriv, createHash, randomBytes, randomUUID } from "crypto";
import { fetchNakamaAccount } from "./nakama";
import {
  buildMalAuthorizationUrl,
  exchangeMalCodeForToken,
  fetchCurrentMalUser,
  fetchTopAnimePage,
  fetchUserAnimeList,
  refreshMalAccessToken,
  type MalAnimeNode,
  type MalCurrentUser,
  type MalTokenResponse,
} from "./mal";

export type AppContext = {
  prisma: PrismaClient;
  redis: Redis;
  env: {
    PORT: number;
    DATABASE_URL: string;
    REDIS_URL: string;
    NAKAMA_HTTP: string;
    NAKAMA_SERVER_KEY: string;
    MAL_CLIENT_ID?: string;
    MAL_CLIENT_SECRET?: string;
    MAL_REDIRECT_URI?: string;
    MAL_TOKEN_ENCRYPTION_KEY?: string;
    MAL_SYNC_INTERVAL_MINUTES: number;
    MAL_CATALOG_SYNC_MAX_PAGES?: number;
  };
};

type TableRow = Record<string, unknown>;
type AnimeDeckSourceRow = {
  animeId: string;
  title: string;
  imageUrl: string | null;
  synopsis: string | null;
  year: number | null;
  episodes: number | null;
  genres: string[];
  trailerYoutubeId: string | null;
  provider: string;
  providerId: string;
};
type AnimeWatchState = {
  status?: string | null;
  score?: number | null;
  episodesWatched?: number | null;
};
type AnimeListRow = Prisma.AnimeGetPayload<{
  select: {
    animeId: true;
    title: true;
    imageUrl: true;
    synopsis: true;
    year: true;
    episodes: true;
    genres: true;
    trailerYoutubeId: true;
    provider: true;
    providerId: true;
    watchEntries: {
      select: {
        status: true;
        score: true;
        episodesWatched: true;
      };
    };
  };
}>;
type QuestListRow = Prisma.QuestGetPayload<{
  select: {
    questId: true;
    code: true;
    title: true;
    description: true;
    requirements: true;
    rewards: true;
  };
}>;

type MalOauthState = {
  userId: string;
  codeVerifier: string;
};

const WATCH_STATUSES = ["watching", "completed", "planned", "dropped", "on_hold"] as const;
type WatchStatus = (typeof WATCH_STATUSES)[number];

const MAL_OAUTH_STATE_TTL_SECONDS = 10 * 60;
const MAL_TOKEN_REFRESH_BUFFER_MS = 5 * 60 * 1000;
const DEFAULT_MAL_EXPIRES_IN_SECONDS = 60 * 60;
const DEFAULT_ANIME_LIMIT = 100;
const MAX_ANIME_LIMIT = 500;

type AnimeDeckItem = {
  id: string;
  title: string;
  briefDescription: string;
  description: string;
  imageUrl: string;
  episodes: number | null;
  releaseDate: string;
  isWatching: boolean;
  watchStatus: WatchStatus | null;
  score: number | null;
  episodesWatched: number;
  lists: string[];
  genres: string[];
  trailerYoutubeId: string | null;
  provider: string;
  providerId: string;
  matchCount?: number;
  matchingUsers?: {
    userId: string;
    displayName: string;
    status: WatchStatus | null;
    score: number | null;
    episodesWatched: number;
  }[];
};

declare module "fastify" {
  interface FastifyRequest {
    userId?: string;
    username?: string;
  }
}

function toReleaseDate(year: number | null): string {
  return year ? `${year}-01-01` : "unknown";
}

function truncate(value: string, maxLength: number) {
  const trimmed = value.trim();
  if (trimmed.length <= maxLength) return trimmed;
  return `${trimmed.slice(0, maxLength - 3).trim()}...`;
}

function cleanMalSynopsis(value?: string | null) {
  if (!value) return null;
  const cleaned = value.replace(/\s*\[Written by MAL Rewrite\]\s*$/i, "").trim();
  return cleaned.length > 0 ? cleaned : null;
}

function malImageUrl(node: MalAnimeNode) {
  return node.main_picture?.large ?? node.main_picture?.medium ?? null;
}

function buildBriefDescription(row: AnimeDeckSourceRow): string {
  if (row.synopsis) {
    return truncate(row.synopsis, 150);
  }

  if (row.genres.length === 0) {
    return "Anime catalog entry";
  }

  return `${row.genres.slice(0, 3).join(" | ")} anime`;
}

function buildDescription(row: AnimeDeckSourceRow): string {
  if (row.synopsis) {
    return row.synopsis;
  }

  const genreText = row.genres.length > 0 ? row.genres.join(", ") : "varied genres";
  const episodesText = row.episodes != null ? `${row.episodes} episodes` : "episode count TBD";
  const yearText = row.year != null ? `${row.year}` : "unknown release year";

  return `${row.title} is listed as ${genreText}, with ${episodesText}, released around ${yearText}.`;
}

function posterUrl(row: AnimeDeckSourceRow): string {
  if (row.imageUrl) {
    return row.imageUrl;
  }

  if (row.trailerYoutubeId) {
    return `https://img.youtube.com/vi/${row.trailerYoutubeId}/hqdefault.jpg`;
  }

  return `https://placehold.co/72x108?text=${encodeURIComponent(row.title.slice(0, 2).toUpperCase())}`;
}

function normalizeWatchStatus(value: string): WatchStatus | null {
  return WATCH_STATUSES.includes(value as WatchStatus) ? (value as WatchStatus) : null;
}

function normalizeMalWatchStatus(value?: string): WatchStatus {
  if (value === "watching") return "watching";
  if (value === "completed") return "completed";
  if (value === "dropped") return "dropped";
  if (value === "on_hold") return "on_hold";
  if (value === "plan_to_watch" || value === "planned") return "planned";
  return "planned";
}

function animeUpsertData(node: MalAnimeNode) {
  return {
    title: node.title,
    imageUrl: malImageUrl(node),
    synopsis: cleanMalSynopsis(node.synopsis),
    genres: (node.genres ?? []).map((g) => g.name),
    episodes: node.num_episodes ?? null,
    year: node.start_season?.year ?? null,
  };
}

function buildAnimeDeckItem(row: AnimeDeckSourceRow, watch?: AnimeWatchState | null): AnimeDeckItem {
  const watchStatus = normalizeWatchStatus(watch?.status ?? "");

  return {
    id: row.animeId.toString(),
    title: row.title,
    briefDescription: buildBriefDescription(row),
    description: buildDescription(row),
    imageUrl: posterUrl(row),
    episodes: row.episodes,
    releaseDate: toReleaseDate(row.year),
    isWatching: watchStatus === "watching",
    watchStatus,
    score: watch?.score ?? null,
    episodesWatched: watch?.episodesWatched ?? 0,
    lists: watchStatus ? [watchStatus] : [],
    genres: row.genres,
    trailerYoutubeId: row.trailerYoutubeId,
    provider: row.provider,
    providerId: row.providerId,
  };
}

function parseAnimeListQuery(query: { q?: string; limit?: string; offset?: string }) {
  const q = query.q?.trim();
  const requestedLimit = Number.parseInt(query.limit ?? `${DEFAULT_ANIME_LIMIT}`, 10) || DEFAULT_ANIME_LIMIT;
  const requestedOffset = Number.parseInt(query.offset ?? "0", 10) || 0;
  const limit = Math.min(Math.max(requestedLimit, 1), MAX_ANIME_LIMIT);
  const offset = Math.max(requestedOffset, 0);
  return { q, limit, offset };
}

function incrementCount(counts: Record<string, number>, key: string) {
  counts[key] = (counts[key] ?? 0) + 1;
}

function isMalOAuthConfigured(ctx: AppContext) {
  return Boolean(ctx.env.MAL_CLIENT_ID && ctx.env.MAL_CLIENT_SECRET && ctx.env.MAL_REDIRECT_URI);
}

function accessTokenExpiresAt(expiresIn?: number) {
  const seconds = Math.max(expiresIn ?? DEFAULT_MAL_EXPIRES_IN_SECONDS, 60);
  return new Date(Date.now() + seconds * 1000);
}

function malTokenSecret(ctx: AppContext) {
  const secret = ctx.env.MAL_TOKEN_ENCRYPTION_KEY ?? ctx.env.MAL_CLIENT_SECRET;
  if (!secret) {
    throw new Error("MAL token encryption not configured");
  }
  return createHash("sha256").update(secret).digest();
}

function encryptMalToken(ctx: AppContext, value: string) {
  const iv = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", malTokenSecret(ctx), iv);
  const encrypted = Buffer.concat([cipher.update(value, "utf8"), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `v1:${iv.toString("base64")}:${tag.toString("base64")}:${encrypted.toString("base64")}`;
}

function decryptMalToken(ctx: AppContext, value: string) {
  const [version, iv, tag, encrypted] = value.split(":");
  if (version !== "v1" || !iv || !tag || !encrypted) {
    throw new Error("Unsupported MAL token format");
  }

  const decipher = createDecipheriv("aes-256-gcm", malTokenSecret(ctx), Buffer.from(iv, "base64"));
  decipher.setAuthTag(Buffer.from(tag, "base64"));
  return Buffer.concat([decipher.update(Buffer.from(encrypted, "base64")), decipher.final()]).toString("utf8");
}

function parseMalOauthState(value: string): MalOauthState | null {
  try {
    const parsed = JSON.parse(value) as Partial<MalOauthState>;
    if (typeof parsed.userId === "string" && typeof parsed.codeVerifier === "string") {
      return { userId: parsed.userId, codeVerifier: parsed.codeVerifier };
    }
  } catch {
    return null;
  }

  return null;
}

function escapeHtml(value: string) {
  const replacements: Record<string, string> = {
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  };
  return value.replace(/[&<>"']/g, (char) => replacements[char]);
}

function buildMalConnectedPage(profile: MalCurrentUser | null) {
  const linkedName = profile?.name ? ` as ${escapeHtml(profile.name)}` : "";
  return `<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>MyAnimeList Linked</title></head>
<body style="font-family: sans-serif; margin: 3rem; line-height: 1.5;">
  <h1>MyAnimeList linked${linkedName}</h1>
  <p>You can return to AnimeQuest and import your anime list.</p>
</body>
</html>`;
}

async function completeMalOAuthCallback(
  ctx: AppContext,
  req: FastifyRequest,
  reply: FastifyReply,
  query: { code?: string; state?: string }
) {
  if (!isMalOAuthConfigured(ctx)) return reply.code(500).send({ error: "MAL OAuth not configured" });
  if (!query.code || !query.state) return reply.code(400).send({ error: "Missing code/state" });

  const stateKey = `mal:oauth:state:${query.state}`;
  const rawState = await ctx.redis.get(stateKey);
  const oauthState = rawState ? parseMalOauthState(rawState) : null;
  if (!oauthState) return reply.code(400).send({ error: "Invalid or expired oauth state" });

  const token = await exchangeMalCodeForToken({
    clientId: ctx.env.MAL_CLIENT_ID!,
    clientSecret: ctx.env.MAL_CLIENT_SECRET!,
    code: query.code,
    codeVerifier: oauthState.codeVerifier,
    redirectUri: ctx.env.MAL_REDIRECT_URI!,
  });

  let profile: MalCurrentUser | null = null;
  try {
    profile = await fetchCurrentMalUser({ clientId: ctx.env.MAL_CLIENT_ID!, accessToken: token.access_token });
  } catch (error) {
    req.log.warn({ userId: oauthState.userId, error }, "[Dozzle][MAL] linked token but profile lookup failed");
  }

  await ensureAppUser(ctx, oauthState.userId);
  await storeMalTokens(ctx, oauthState.userId, token, profile);
  await ctx.redis.del(stateKey);

  req.log.info({ userId: oauthState.userId, malUsername: profile?.name ?? null }, "[Dozzle][MAL] oauth callback linked account");
  return reply.type("text/html").send(buildMalConnectedPage(profile));
}

async function ensureAppUser(ctx: AppContext, userId: string, username?: string) {
  const displayName = username ?? `player_${userId.slice(0, 6)}`;
  return ctx.prisma.user.upsert({
    where: { userId },
    update: { displayName },
    create: { userId, displayName },
  });
}

async function storeMalTokens(ctx: AppContext, userId: string, token: MalTokenResponse, profile: MalCurrentUser | null) {
  const updateData: Prisma.UserUpdateInput = {
    malUserId: profile ? String(profile.id) : null,
    malUsername: profile?.name ?? null,
    malAccessToken: encryptMalToken(ctx, token.access_token),
    malAccessTokenExpiresAt: accessTokenExpiresAt(token.expires_in),
  };

  if (token.refresh_token) {
    updateData.malRefreshToken = encryptMalToken(ctx, token.refresh_token);
  }

  await ctx.prisma.user.update({ where: { userId }, data: updateData });
}

async function getValidMalAccessToken(ctx: AppContext, userId: string) {
  if (!ctx.env.MAL_CLIENT_ID || !ctx.env.MAL_CLIENT_SECRET) {
    return { ok: false as const, statusCode: 500, error: "MAL OAuth not configured" };
  }

  const user = await ctx.prisma.user.findUnique({
    where: { userId },
    select: {
      malAccessToken: true,
      malRefreshToken: true,
      malAccessTokenExpiresAt: true,
    },
  });
  if (!user?.malAccessToken || !user.malAccessTokenExpiresAt) {
    return { ok: false as const, statusCode: 401, error: "MyAnimeList account not linked; link MyAnimeList first" };
  }

  const shouldRefresh = user.malAccessTokenExpiresAt.getTime() <= Date.now() + MAL_TOKEN_REFRESH_BUFFER_MS;
  if (!shouldRefresh) {
    return { ok: true as const, accessToken: decryptMalToken(ctx, user.malAccessToken) };
  }

  if (!user.malRefreshToken) {
    return { ok: false as const, statusCode: 401, error: "MyAnimeList authorization expired; reconnect MyAnimeList" };
  }

  try {
    const refreshToken = decryptMalToken(ctx, user.malRefreshToken);
    const token = await refreshMalAccessToken({
      clientId: ctx.env.MAL_CLIENT_ID,
      clientSecret: ctx.env.MAL_CLIENT_SECRET,
      refreshToken,
    });

    const updateData: Prisma.UserUpdateInput = {
      malAccessToken: encryptMalToken(ctx, token.access_token),
      malAccessTokenExpiresAt: accessTokenExpiresAt(token.expires_in),
    };
    if (token.refresh_token) {
      updateData.malRefreshToken = encryptMalToken(ctx, token.refresh_token);
    }

    await ctx.prisma.user.update({ where: { userId }, data: updateData });
    return { ok: true as const, accessToken: token.access_token };
  } catch {
    return { ok: false as const, statusCode: 401, error: "MyAnimeList authorization expired; reconnect MyAnimeList" };
  }
}

async function syncTopAnimeCatalog(ctx: AppContext, maxPages?: number) {
  if (!ctx.env.MAL_CLIENT_ID) return { pages: 0, upserted: 0 };

  const pageSize = 100;
  let upserted = 0;
  let pages = 0;

  while (maxPages == null || pages < maxPages) {
    const payload = await fetchTopAnimePage({
      clientId: ctx.env.MAL_CLIENT_ID,
      limit: pageSize,
      offset: pages * pageSize,
    });

    pages += 1;

    for (const item of payload.data ?? []) {
      const node = item.node;
      const data = animeUpsertData(node);
      await ctx.prisma.anime.upsert({
        where: { provider_providerId: { provider: "myanimelist", providerId: String(node.id) } },
        update: data,
        create: {
          provider: "myanimelist",
          providerId: String(node.id),
          ...data,
        },
      });
      upserted += 1;
    }

    if (!payload.paging?.next) break;
  }

  return { pages, upserted };
}
export function buildServer(ctx: AppContext) {
  const app = Fastify({
    logger: true,
    serializerOpts: {
      replacer: (_key: string, value: unknown) =>
        typeof value === "bigint" ? value.toString() : value,
    },
  });

  app.register(cors, { origin: true });

  app.register(rateLimit, {
    max: 120,
    timeWindow: "1 minute",
    redis: ctx.redis as any,
  });

  app.register(swagger, {
    openapi: {
      info: {
        title: "AnimeQuest API",
        version: "0.1.0",
      },
    },
  });

  app.register(swaggerUI, {
    routePrefix: "/docs",
  });

  app.log.info("[Dozzle][DB] Fastify server initialized with Redis-backed rate limiting");

  app.get("/health", async () => {
    return { ok: true };
  });

  app.get("/", async (req, reply) => {
    const query = req.query as { code?: string; state?: string };
    if (query.code || query.state) {
      return completeMalOAuthCallback(ctx, req, reply, query);
    }

    return { ok: true };
  });

  app.addHook("preHandler", async (req, reply) => {
    const path = req.url.split("?")[0];
    if (!path.startsWith("/api/")) {
      return;
    }

    if (path === "/api/mal/oauth/callback") {
      return;
    }

    const auth = req.headers.authorization;
    if (!auth?.startsWith("Bearer ")) {
      return reply.code(401).send({ error: "Missing Bearer token" });
    }

    const token = auth.slice("Bearer ".length).trim();

    try {
      const acct = await fetchNakamaAccount({
        nakamaHttp: ctx.env.NAKAMA_HTTP,
        serverKey: ctx.env.NAKAMA_SERVER_KEY,
        sessionToken: token,
      });

      req.userId = acct.userId;
      req.username = acct.username;
    } catch (error) {
      req.log.warn({ error }, "Auth failed");
      return reply.code(401).send({ error: "Invalid session token" });
    }
  });

  app.post("/api/me/ensure", async (req) => {
    const userId = req.userId!;
    const user = await ensureAppUser(ctx, userId, req.username);
    req.log.info({ userId }, "[Dozzle][DB] upsert user profile");

    return {
      id: user.userId,
      displayName: user.displayName,
    };
  });

  app.get("/api/anime", async (req) => {
    const userId = req.userId!;
    const { q, limit, offset } = parseAnimeListQuery(req.query as { q?: string; limit?: string; offset?: string });

    const rows: AnimeListRow[] = await ctx.prisma.anime.findMany({
      where: q
        ? {
            title: {
              contains: q,
              mode: "insensitive",
            },
          }
        : undefined,
      orderBy: { title: "asc" },
      skip: offset,
      take: limit + 1,
      select: {
        animeId: true,
        title: true,
        imageUrl: true,
        synopsis: true,
        year: true,
        episodes: true,
        genres: true,
        trailerYoutubeId: true,
        provider: true,
        providerId: true,
        watchEntries: {
          where: { userId },
          select: { status: true, score: true, episodesWatched: true },
          take: 1,
        },
      },
    });
    const pageRows = rows.slice(0, limit);
    const hasMore = rows.length > limit;
    req.log.info({ userId, count: pageRows.length, search: q ?? "", limit, offset, hasMore }, "[Dozzle][DB] global anime catalog query");

    return {
      items: pageRows.map((row) => buildAnimeDeckItem(row, row.watchEntries[0])),
      limit,
      offset,
      hasMore,
    };
  });

  app.get("/api/anime/user", async (req) => {
    const userId = req.userId!;
    const { q, limit, offset } = parseAnimeListQuery(req.query as { q?: string; limit?: string; offset?: string });

    const rows = await ctx.prisma.watchEntry.findMany({
      where: {
        userId,
        anime: q
          ? {
              title: {
                contains: q,
                mode: "insensitive",
              },
            }
          : undefined,
      },
      orderBy: { updatedAt: "desc" },
      skip: offset,
      take: limit + 1,
      select: {
        status: true,
        score: true,
        episodesWatched: true,
        anime: {
          select: {
            animeId: true,
            title: true,
            imageUrl: true,
            synopsis: true,
            year: true,
            episodes: true,
            genres: true,
            trailerYoutubeId: true,
            provider: true,
            providerId: true,
          },
        },
      },
    });
    const pageRows = rows.slice(0, limit);
    const hasMore = rows.length > limit;
    req.log.info({ userId, count: pageRows.length, search: q ?? "", limit, offset, hasMore }, "[Dozzle][DB] user anime catalog query");

    return {
      items: pageRows.map((row) => buildAnimeDeckItem(row.anime, row)),
      limit,
      offset,
      hasMore,
    };
  });

  app.get("/api/anime/matches", async (req) => {
    const userId = req.userId!;
    const { q, limit } = parseAnimeListQuery(req.query as { q?: string; limit?: string; offset?: string });

    const currentEntries = await ctx.prisma.watchEntry.findMany({
      where: {
        userId,
        anime: q
          ? {
              title: {
                contains: q,
                mode: "insensitive",
              },
            }
          : undefined,
      },
      select: {
        animeId: true,
        status: true,
        score: true,
        episodesWatched: true,
        anime: {
          select: {
            animeId: true,
            title: true,
            imageUrl: true,
            synopsis: true,
            year: true,
            episodes: true,
            genres: true,
            trailerYoutubeId: true,
            provider: true,
            providerId: true,
          },
        },
      },
    });

    const animeIds = currentEntries.map((entry) => entry.animeId);
    if (animeIds.length === 0) {
      req.log.info({ userId, count: 0 }, "[Dozzle][DB] anime matching query");
      return { items: [] };
    }

    const currentByAnime = new Map(currentEntries.map((entry) => [entry.animeId, entry]));
    const otherEntries = await ctx.prisma.watchEntry.findMany({
      where: {
        animeId: { in: animeIds },
        userId: { not: userId },
      },
      select: {
        animeId: true,
        status: true,
        score: true,
        episodesWatched: true,
        user: {
          select: {
            userId: true,
            displayName: true,
          },
        },
      },
    });

    const matchesByAnime = new Map<string, typeof otherEntries>();
    for (const entry of otherEntries) {
      const existing = matchesByAnime.get(entry.animeId) ?? [];
      existing.push(entry);
      matchesByAnime.set(entry.animeId, existing);
    }

    const items = [...matchesByAnime.entries()]
      .map(([animeId, matches]) => {
        const current = currentByAnime.get(animeId)!;
        const item = buildAnimeDeckItem(current.anime, current);
        item.matchCount = matches.length;
        item.matchingUsers = matches.slice(0, 5).map((match) => ({
          userId: match.user.userId,
          displayName: match.user.displayName,
          status: normalizeWatchStatus(match.status),
          score: match.score,
          episodesWatched: match.episodesWatched,
        }));
        return item;
      })
      .sort((a, b) => (b.matchCount ?? 0) - (a.matchCount ?? 0) || a.title.localeCompare(b.title))
      .slice(0, limit);

    req.log.info({ userId, count: items.length, searchedAnime: animeIds.length, matchedEntries: otherEntries.length }, "[Dozzle][DB] anime matching query");
    return { items };
  });

  app.patch("/api/anime/:id/watching", async (req, reply) => {
    const userId = req.userId!;
    const params = req.params as { id: string };
    const body = req.body as { isWatching?: boolean };

    if (typeof body?.isWatching !== "boolean") {
      return reply.code(400).send({ error: "isWatching must be a boolean" });
    }

    const anime = await ctx.prisma.anime.findUnique({ where: { animeId: params.id } });
    if (!anime) {
      return reply.code(404).send({ error: "Anime not found" });
    }

    const status: WatchStatus = body.isWatching ? "watching" : "planned";
    const watchEntry = await ctx.prisma.watchEntry.upsert({
      where: {
        userId_animeId: {
          userId,
          animeId: params.id,
        },
      },
      update: {
        status,
        updatedAt: new Date(),
      },
      create: {
        userId,
        animeId: params.id,
        status,
      },
    });

    req.log.info({ userId, animeId: params.id, status }, "[Dozzle][DB] watch status upsert");
    return {
      id: watchEntry.animeId,
      isWatching: watchEntry.status === "watching",
      watchStatus: watchEntry.status,
      score: watchEntry.score,
      episodesWatched: watchEntry.episodesWatched,
      lists: [watchEntry.status],
    };
  });

  app.patch("/api/anime/:id/lists", async (req, reply) => {
    const userId = req.userId!;
    const params = req.params as { id: string };
    const body = req.body as { add?: string[]; remove?: string[] };

    const add = Array.isArray(body?.add)
      ? body.add.filter((item): item is string => typeof item === "string" && item.trim().length > 0)
      : [];
    const remove = Array.isArray(body?.remove)
      ? body.remove.filter((item): item is string => typeof item === "string" && item.trim().length > 0)
      : [];

    if (add.length === 0 && remove.length === 0) {
      return reply.code(400).send({ error: "Request must include add and/or remove arrays" });
    }

    const anime = await ctx.prisma.anime.findUnique({ where: { animeId: params.id } });
    if (!anime) {
      return reply.code(404).send({ error: "Anime not found" });
    }

    const addStatus = add.map((item) => normalizeWatchStatus(item)).find((item) => item != null) ?? null;
    const currentEntry = await ctx.prisma.watchEntry.findUnique({
      where: {
        userId_animeId: {
          userId,
          animeId: params.id,
        },
      },
    });
    const currentStatus = normalizeWatchStatus(currentEntry?.status ?? "");
    const shouldRemoveCurrent =
      currentStatus != null && remove.some((item) => item.trim().toLowerCase() === currentStatus);

    if (addStatus) {
      await ctx.prisma.watchEntry.upsert({
        where: {
          userId_animeId: {
            userId,
            animeId: params.id,
          },
        },
        update: {
          status: addStatus,
          updatedAt: new Date(),
        },
        create: {
          userId,
          animeId: params.id,
          status: addStatus,
        },
      });
      req.log.info({ userId, animeId: params.id, status: addStatus }, "[Dozzle][DB] watch status upsert");
    } else if (shouldRemoveCurrent) {
      await ctx.prisma.watchEntry.delete({
        where: {
          userId_animeId: {
            userId,
            animeId: params.id,
          },
        },
      });
      req.log.info({ userId, animeId: params.id, status: currentStatus }, "[Dozzle][DB] watch status deleted");
    }

    const nextEntry = await ctx.prisma.watchEntry.findUnique({
      where: {
        userId_animeId: {
          userId,
          animeId: params.id,
        },
      },
    });
    const nextStatus = normalizeWatchStatus(nextEntry?.status ?? "");

    return {
      id: params.id,
      isWatching: nextStatus === "watching",
      watchStatus: nextStatus,
      score: nextEntry?.score ?? null,
      episodesWatched: nextEntry?.episodesWatched ?? 0,
      lists: nextStatus ? [nextStatus] : [],
    };
  });

  app.get("/api/mal/oauth/start", async (req, reply) => {
    const userId = req.userId!;
    if (!isMalOAuthConfigured(ctx)) return reply.code(500).send({ error: "MAL OAuth not configured" });

    await ensureAppUser(ctx, userId, req.username);

    const state = randomUUID();
    const codeVerifier = randomUUID().replace(/-/g, "") + randomUUID().replace(/-/g, "");
    await ctx.redis.setex(
      `mal:oauth:state:${state}`,
      MAL_OAUTH_STATE_TTL_SECONDS,
      JSON.stringify({ userId, codeVerifier } satisfies MalOauthState)
    );

    req.log.info({ userId }, "[Dozzle][MAL] oauth start URL generated");
    return {
      url: buildMalAuthorizationUrl({
        clientId: ctx.env.MAL_CLIENT_ID!,
        redirectUri: ctx.env.MAL_REDIRECT_URI!,
        state,
        codeVerifier,
      }),
    };
  });

  app.get("/api/mal/oauth/callback", async (req, reply) => {
    return completeMalOAuthCallback(ctx, req, reply, req.query as { code?: string; state?: string });
  });

  app.get("/api/mal/oauth/status", async (req) => {
    const configured = isMalOAuthConfigured(ctx);
    if (!configured) {
      return {
        configured,
        linked: false,
        malUsername: null,
        reconnectRequired: false,
      };
    }

    const userId = req.userId!;
    const user = await ctx.prisma.user.findUnique({
      where: { userId },
      select: {
        malAccessToken: true,
        malRefreshToken: true,
        malAccessTokenExpiresAt: true,
        malUsername: true,
      },
    });
    const linked = Boolean(user?.malAccessToken && user.malAccessTokenExpiresAt);
    const reconnectRequired = Boolean(
      linked && !user?.malRefreshToken && user?.malAccessTokenExpiresAt && user.malAccessTokenExpiresAt.getTime() <= Date.now() + MAL_TOKEN_REFRESH_BUFFER_MS
    );

    return {
      configured,
      linked,
      malUsername: user?.malUsername ?? null,
      reconnectRequired,
    };
  });

  app.post("/api/mal/oauth/refresh", async (req, reply) => {
    const userId = req.userId!;
    const result = await getValidMalAccessToken(ctx, userId);
    if (!result.ok) return reply.code(result.statusCode).send({ error: result.error });
    req.log.info({ userId }, "[Dozzle][MAL] access token refreshed or still valid");
    return { ok: true };
  });

  app.post("/api/mal/import", async (req, reply) => {
    const userId = req.userId!;
    const tokenResult = await getValidMalAccessToken(ctx, userId);
    if (!tokenResult.ok) {
      req.log.warn({ userId, error: tokenResult.error }, "[Dozzle][MAL] import blocked");
      return reply.code(tokenResult.statusCode).send({ error: tokenResult.error });
    }

    req.log.info({ userId }, "[Dozzle][MAL] import started");

    try {
      let imported = 0;
      let scoredEntries = 0;
      let episodeProgressEntries = 0;
      const statusCounts: Record<string, number> = {};
      const rawStatusCounts: Record<string, number> = {};
      for (let offset = 0; ; offset += 100) {
        const payload = await fetchUserAnimeList({
          clientId: ctx.env.MAL_CLIENT_ID!,
          accessToken: tokenResult.accessToken,
          username: "@me",
          limit: 100,
          offset,
        });

        const pageRawStatusCounts: Record<string, number> = {};
        for (const item of payload.data ?? []) {
          const node = item.node;
          const data = animeUpsertData(node);
          const row = await ctx.prisma.anime.upsert({
            where: { provider_providerId: { provider: "myanimelist", providerId: String(node.id) } },
            update: data,
            create: {
              provider: "myanimelist",
              providerId: String(node.id),
              ...data,
            },
          });

          const rawStatus = item.list_status?.status ?? "missing";
          const status = normalizeMalWatchStatus(rawStatus);
          const score = item.list_status?.score ?? null;
          const episodesWatched = item.list_status?.num_episodes_watched ?? 0;
          incrementCount(rawStatusCounts, rawStatus);
          incrementCount(pageRawStatusCounts, rawStatus);
          incrementCount(statusCounts, status);
          if (score != null && score > 0) scoredEntries += 1;
          if (episodesWatched > 0) episodeProgressEntries += 1;

          await ctx.prisma.watchEntry.upsert({
            where: { userId_animeId: { userId, animeId: row.animeId } },
            update: {
              status,
              score,
              episodesWatched,
              updatedAt: new Date(),
            },
            create: {
              userId,
              animeId: row.animeId,
              status,
              score,
              episodesWatched,
            },
          });

          imported += 1;
        }

        req.log.info({ userId, offset, pageCount: payload.data?.length ?? 0, pageRawStatusCounts }, "[Dozzle][MAL] import page parsed");
        if (!payload.paging?.next) break;
      }

      req.log.info(
        { userId, imported, statusCounts, rawStatusCounts, scoredEntries, episodeProgressEntries },
        "[Dozzle][MAL] import completed and watch statuses replicated"
      );
      return { ok: true, imported, statusCounts, rawStatusCounts, scoredEntries, episodeProgressEntries };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      req.log.error({ userId, error }, "[Dozzle][MAL] import failed");
      if (message.includes("401")) {
        return reply.code(401).send({ error: "MyAnimeList authorization expired; reconnect MyAnimeList" });
      }
      return reply.code(502).send({ error: "MAL import request failed" });
    }
  });

  app.get("/api/quests", async () => {
    const quests: QuestListRow[] = await ctx.prisma.quest.findMany({
      orderBy: { questId: "asc" },
      select: {
        questId: true,
        code: true,
        title: true,
        description: true,
        requirements: true,
        rewards: true,
      },
    });
    app.log.info({ count: quests.length }, "[Dozzle][DB] quest catalog query");

    return {
      items: quests.map((quest: QuestListRow) => ({
        id: quest.questId.toString(),
        code: quest.code,
        title: quest.title,
        description: quest.description,
        requirements: quest.requirements,
        rewards: quest.rewards,
      })),
    };
  });

  const ALLOWED_TABLES = new Set([
    "anime",
    "quests",
    "users",
    "watch_entries",
    "user_quests",
    "achievements",
    "user_achievements",
  ]);

  app.get("/api/table/:name", async (req, reply) => {
    const params = req.params as { name: string };
    const query = req.query as { limit?: string; offset?: string };

    const name = params.name;

    if (!ALLOWED_TABLES.has(name)) {
      return reply.code(403).send({ error: "Table not allowed" });
    }

    const limit = Math.min(Number.parseInt(query.limit ?? "50", 10) || 50, 200);
    const offset = Math.max(Number.parseInt(query.offset ?? "0", 10) || 0, 0);

    const rowsResult = await ctx.prisma.$queryRawUnsafe(
      `SELECT * FROM ${name} ORDER BY 1 LIMIT $1 OFFSET $2`,
      limit,
      offset
    );
    req.log.info({ table: name, limit, offset }, "[Dozzle][DB] table viewer query");

    const rows = rowsResult as TableRow[];
    const columns = rows.length > 0 ? Object.keys(rows[0]) : [];

    return {
      table: name,
      columns,
      rows,
      limit,
      offset,
    };
  });

  const runCatalogSync = async () => {
    try {
      const result = await syncTopAnimeCatalog(ctx, ctx.env.MAL_CATALOG_SYNC_MAX_PAGES);
      app.log.info(result, "MAL catalog sync completed");
    } catch (error) {
      app.log.error({ error }, "MAL catalog sync failed");
    }
  };

  if (ctx.env.MAL_CLIENT_ID) {
    runCatalogSync();
    setInterval(runCatalogSync, Math.max(ctx.env.MAL_SYNC_INTERVAL_MINUTES, 5) * 60 * 1000);
  } else {
    app.log.warn("MAL_CLIENT_ID not configured; MAL catalog sync disabled");
  }

  return app;
}
