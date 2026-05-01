"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.buildServer = buildServer;
const fastify_1 = __importDefault(require("fastify"));
const cors_1 = __importDefault(require("@fastify/cors"));
const rate_limit_1 = __importDefault(require("@fastify/rate-limit"));
const swagger_1 = __importDefault(require("@fastify/swagger"));
const swagger_ui_1 = __importDefault(require("@fastify/swagger-ui"));
const crypto_1 = require("crypto");
const nakama_1 = require("./nakama");
const mal_1 = require("./mal");
const WATCH_STATUSES = ["watching", "completed", "planned", "dropped", "on_hold"];
const MAL_OAUTH_STATE_TTL_SECONDS = 10 * 60;
const MAL_TOKEN_REFRESH_BUFFER_MS = 5 * 60 * 1000;
const DEFAULT_MAL_EXPIRES_IN_SECONDS = 60 * 60;
function toReleaseDate(year) {
    return year ? `${year}-01-01` : "unknown";
}
function buildBriefDescription(row) {
    if (row.genres.length === 0) {
        return "Anime catalog entry";
    }
    return `${row.genres.slice(0, 3).join(" • ")} anime`;
}
function buildDescription(row) {
    const genreText = row.genres.length > 0 ? row.genres.join(", ") : "varied genres";
    const episodesText = row.episodes != null ? `${row.episodes} episodes` : "episode count TBD";
    const yearText = row.year != null ? `${row.year}` : "unknown release year";
    return `${row.title} is listed as ${genreText}, with ${episodesText}, released around ${yearText}.`;
}
function posterUrl(row) {
    if (row.trailerYoutubeId) {
        return `https://img.youtube.com/vi/${row.trailerYoutubeId}/hqdefault.jpg`;
    }
    return `https://placehold.co/72x108?text=${encodeURIComponent(row.title.slice(0, 2).toUpperCase())}`;
}
function normalizeWatchStatus(value) {
    return WATCH_STATUSES.includes(value) ? value : null;
}
function normalizeMalWatchStatus(value) {
    if (value === "watching")
        return "watching";
    if (value === "completed")
        return "completed";
    if (value === "dropped")
        return "dropped";
    if (value === "on_hold")
        return "on_hold";
    return "planned";
}
function isMalOAuthConfigured(ctx) {
    return Boolean(ctx.env.MAL_CLIENT_ID && ctx.env.MAL_CLIENT_SECRET && ctx.env.MAL_REDIRECT_URI);
}
function accessTokenExpiresAt(expiresIn) {
    const seconds = Math.max(expiresIn ?? DEFAULT_MAL_EXPIRES_IN_SECONDS, 60);
    return new Date(Date.now() + seconds * 1000);
}
function malTokenSecret(ctx) {
    const secret = ctx.env.MAL_TOKEN_ENCRYPTION_KEY ?? ctx.env.MAL_CLIENT_SECRET;
    if (!secret) {
        throw new Error("MAL token encryption not configured");
    }
    return (0, crypto_1.createHash)("sha256").update(secret).digest();
}
function encryptMalToken(ctx, value) {
    const iv = (0, crypto_1.randomBytes)(12);
    const cipher = (0, crypto_1.createCipheriv)("aes-256-gcm", malTokenSecret(ctx), iv);
    const encrypted = Buffer.concat([cipher.update(value, "utf8"), cipher.final()]);
    const tag = cipher.getAuthTag();
    return `v1:${iv.toString("base64")}:${tag.toString("base64")}:${encrypted.toString("base64")}`;
}
function decryptMalToken(ctx, value) {
    const [version, iv, tag, encrypted] = value.split(":");
    if (version !== "v1" || !iv || !tag || !encrypted) {
        throw new Error("Unsupported MAL token format");
    }
    const decipher = (0, crypto_1.createDecipheriv)("aes-256-gcm", malTokenSecret(ctx), Buffer.from(iv, "base64"));
    decipher.setAuthTag(Buffer.from(tag, "base64"));
    return Buffer.concat([decipher.update(Buffer.from(encrypted, "base64")), decipher.final()]).toString("utf8");
}
function parseMalOauthState(value) {
    try {
        const parsed = JSON.parse(value);
        if (typeof parsed.userId === "string" && typeof parsed.codeVerifier === "string") {
            return { userId: parsed.userId, codeVerifier: parsed.codeVerifier };
        }
    }
    catch {
        return null;
    }
    return null;
}
function escapeHtml(value) {
    const replacements = {
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;",
    };
    return value.replace(/[&<>"']/g, (char) => replacements[char]);
}
function buildMalConnectedPage(profile) {
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
async function ensureAppUser(ctx, userId, username) {
    const displayName = username ?? `player_${userId.slice(0, 6)}`;
    return ctx.prisma.user.upsert({
        where: { userId },
        update: { displayName },
        create: { userId, displayName },
    });
}
async function storeMalTokens(ctx, userId, token, profile) {
    const updateData = {
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
async function getValidMalAccessToken(ctx, userId) {
    if (!ctx.env.MAL_CLIENT_ID || !ctx.env.MAL_CLIENT_SECRET) {
        return { ok: false, statusCode: 500, error: "MAL OAuth not configured" };
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
        return { ok: false, statusCode: 401, error: "MyAnimeList account not linked; link MyAnimeList first" };
    }
    const shouldRefresh = user.malAccessTokenExpiresAt.getTime() <= Date.now() + MAL_TOKEN_REFRESH_BUFFER_MS;
    if (!shouldRefresh) {
        return { ok: true, accessToken: decryptMalToken(ctx, user.malAccessToken) };
    }
    if (!user.malRefreshToken) {
        return { ok: false, statusCode: 401, error: "MyAnimeList authorization expired; reconnect MyAnimeList" };
    }
    try {
        const refreshToken = decryptMalToken(ctx, user.malRefreshToken);
        const token = await (0, mal_1.refreshMalAccessToken)({
            clientId: ctx.env.MAL_CLIENT_ID,
            clientSecret: ctx.env.MAL_CLIENT_SECRET,
            refreshToken,
        });
        const updateData = {
            malAccessToken: encryptMalToken(ctx, token.access_token),
            malAccessTokenExpiresAt: accessTokenExpiresAt(token.expires_in),
        };
        if (token.refresh_token) {
            updateData.malRefreshToken = encryptMalToken(ctx, token.refresh_token);
        }
        await ctx.prisma.user.update({ where: { userId }, data: updateData });
        return { ok: true, accessToken: token.access_token };
    }
    catch {
        return { ok: false, statusCode: 401, error: "MyAnimeList authorization expired; reconnect MyAnimeList" };
    }
}
async function syncTopAnimeCatalog(ctx, maxPages = 5) {
    if (!ctx.env.MAL_CLIENT_ID)
        return;
    const pageSize = 100;
    for (let page = 0; page < maxPages; page += 1) {
        const payload = await (0, mal_1.fetchTopAnimePage)({
            clientId: ctx.env.MAL_CLIENT_ID,
            limit: pageSize,
            offset: page * pageSize,
        });
        for (const item of payload.data ?? []) {
            const node = item.node;
            await ctx.prisma.anime.upsert({
                where: { provider_providerId: { provider: "myanimelist", providerId: String(node.id) } },
                update: {
                    title: node.title,
                    genres: (node.genres ?? []).map((g) => g.name),
                    episodes: node.num_episodes ?? null,
                    year: node.start_season?.year ?? null,
                },
                create: {
                    provider: "myanimelist",
                    providerId: String(node.id),
                    title: node.title,
                    genres: (node.genres ?? []).map((g) => g.name),
                    episodes: node.num_episodes ?? null,
                    year: node.start_season?.year ?? null,
                },
            });
        }
        if (!payload.paging?.next)
            break;
    }
}
function buildServer(ctx) {
    const app = (0, fastify_1.default)({
        logger: true,
        serializerOpts: {
            replacer: (_key, value) => typeof value === "bigint" ? value.toString() : value,
        },
    });
    app.register(cors_1.default, { origin: true });
    app.register(rate_limit_1.default, {
        max: 120,
        timeWindow: "1 minute",
        redis: ctx.redis,
    });
    app.register(swagger_1.default, {
        openapi: {
            info: {
                title: "AnimeQuest API",
                version: "0.1.0",
            },
        },
    });
    app.register(swagger_ui_1.default, {
        routePrefix: "/docs",
    });
    app.log.info("[Dozzle][DB] Fastify server initialized with Redis-backed rate limiting");
    // Health check
    app.get("/health", async () => {
        return { ok: true };
    });
    // Auth middleware for protected routes
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
            const acct = await (0, nakama_1.fetchNakamaAccount)({
                nakamaHttp: ctx.env.NAKAMA_HTTP,
                serverKey: ctx.env.NAKAMA_SERVER_KEY,
                sessionToken: token,
            });
            req.userId = acct.userId;
            req.username = acct.username;
        }
        catch (error) {
            req.log.warn({ error }, "Auth failed");
            return reply.code(401).send({ error: "Invalid session token" });
        }
    });
    // Ensure user exists in app-db
    app.post("/api/me/ensure", async (req) => {
        const userId = req.userId;
        const user = await ensureAppUser(ctx, userId, req.username);
        req.log.info({ userId }, "[Dozzle][DB] upsert user profile");
        return {
            id: user.userId,
            displayName: user.displayName,
        };
    });
    // Anime search / list for deck table UI
    app.get("/api/anime", async (req) => {
        const userId = req.userId;
        const query = req.query;
        const q = query.q?.trim();
        const limit = Math.min(Number.parseInt(query.limit ?? "20", 10) || 20, 100);
        const rows = await ctx.prisma.anime.findMany({
            where: q
                ? {
                    title: {
                        contains: q,
                        mode: "insensitive",
                    },
                }
                : undefined,
            orderBy: { createdAt: "desc" },
            take: limit,
            select: {
                animeId: true,
                title: true,
                year: true,
                episodes: true,
                genres: true,
                trailerYoutubeId: true,
                provider: true,
                providerId: true,
                watchEntries: {
                    where: { userId },
                    select: { status: true },
                    take: 1,
                },
            },
        });
        req.log.info({ userId, count: rows.length, search: q ?? "" }, "[Dozzle][DB] anime catalog query");
        const items = rows.map((row) => {
            const watchStatus = normalizeWatchStatus(row.watchEntries[0]?.status ?? "");
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
                lists: watchStatus ? [watchStatus] : [],
                genres: row.genres,
                trailerYoutubeId: row.trailerYoutubeId,
                provider: row.provider,
                providerId: row.providerId,
            };
        });
        return { items };
    });
    app.patch("/api/anime/:id/watching", async (req, reply) => {
        const userId = req.userId;
        const params = req.params;
        const body = req.body;
        if (typeof body?.isWatching !== "boolean") {
            return reply.code(400).send({ error: "isWatching must be a boolean" });
        }
        const anime = await ctx.prisma.anime.findUnique({ where: { animeId: params.id } });
        if (!anime) {
            return reply.code(404).send({ error: "Anime not found" });
        }
        const status = body.isWatching ? "watching" : "planned";
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
        return {
            id: watchEntry.animeId,
            isWatching: watchEntry.status === "watching",
            watchStatus: watchEntry.status,
            lists: [watchEntry.status],
        };
    });
    app.patch("/api/anime/:id/lists", async (req, reply) => {
        const userId = req.userId;
        const params = req.params;
        const body = req.body;
        const add = Array.isArray(body?.add)
            ? body.add.filter((item) => typeof item === "string" && item.trim().length > 0)
            : [];
        const remove = Array.isArray(body?.remove)
            ? body.remove.filter((item) => typeof item === "string" && item.trim().length > 0)
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
        const shouldRemoveCurrent = currentStatus != null && remove.some((item) => item.trim().toLowerCase() === currentStatus);
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
        }
        else if (shouldRemoveCurrent) {
            await ctx.prisma.watchEntry.delete({
                where: {
                    userId_animeId: {
                        userId,
                        animeId: params.id,
                    },
                },
            });
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
            lists: nextStatus ? [nextStatus] : [],
        };
    });
    app.get("/api/mal/oauth/start", async (req, reply) => {
        const userId = req.userId;
        if (!isMalOAuthConfigured(ctx))
            return reply.code(500).send({ error: "MAL OAuth not configured" });
        await ensureAppUser(ctx, userId, req.username);
        const state = (0, crypto_1.randomUUID)();
        const codeVerifier = (0, crypto_1.randomUUID)().replace(/-/g, "") + (0, crypto_1.randomUUID)().replace(/-/g, "");
        await ctx.redis.setex(`mal:oauth:state:${state}`, MAL_OAUTH_STATE_TTL_SECONDS, JSON.stringify({ userId, codeVerifier }));
        req.log.info({ userId }, "[Dozzle][MAL] oauth start URL generated");
        return {
            url: (0, mal_1.buildMalAuthorizationUrl)({
                clientId: ctx.env.MAL_CLIENT_ID,
                redirectUri: ctx.env.MAL_REDIRECT_URI,
                state,
                codeVerifier,
            }),
        };
    });
    app.get("/api/mal/oauth/callback", async (req, reply) => {
        const query = req.query;
        if (!isMalOAuthConfigured(ctx))
            return reply.code(500).send({ error: "MAL OAuth not configured" });
        if (!query.code || !query.state)
            return reply.code(400).send({ error: "Missing code/state" });
        const stateKey = `mal:oauth:state:${query.state}`;
        const rawState = await ctx.redis.get(stateKey);
        const oauthState = rawState ? parseMalOauthState(rawState) : null;
        if (!oauthState)
            return reply.code(400).send({ error: "Invalid or expired oauth state" });
        const token = await (0, mal_1.exchangeMalCodeForToken)({
            clientId: ctx.env.MAL_CLIENT_ID,
            clientSecret: ctx.env.MAL_CLIENT_SECRET,
            code: query.code,
            codeVerifier: oauthState.codeVerifier,
            redirectUri: ctx.env.MAL_REDIRECT_URI,
        });
        let profile = null;
        try {
            profile = await (0, mal_1.fetchCurrentMalUser)({ clientId: ctx.env.MAL_CLIENT_ID, accessToken: token.access_token });
        }
        catch (error) {
            req.log.warn({ userId: oauthState.userId, error }, "[Dozzle][MAL] linked token but profile lookup failed");
        }
        await ensureAppUser(ctx, oauthState.userId);
        await storeMalTokens(ctx, oauthState.userId, token, profile);
        await ctx.redis.del(stateKey);
        req.log.info({ userId: oauthState.userId, malUsername: profile?.name ?? null }, "[Dozzle][MAL] oauth callback linked account");
        return reply.type("text/html").send(buildMalConnectedPage(profile));
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
        const userId = req.userId;
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
        const reconnectRequired = Boolean(linked && !user?.malRefreshToken && user?.malAccessTokenExpiresAt && user.malAccessTokenExpiresAt.getTime() <= Date.now() + MAL_TOKEN_REFRESH_BUFFER_MS);
        return {
            configured,
            linked,
            malUsername: user?.malUsername ?? null,
            reconnectRequired,
        };
    });
    app.post("/api/mal/oauth/refresh", async (req, reply) => {
        const userId = req.userId;
        const result = await getValidMalAccessToken(ctx, userId);
        if (!result.ok)
            return reply.code(result.statusCode).send({ error: result.error });
        req.log.info({ userId }, "[Dozzle][MAL] access token refreshed or still valid");
        return { ok: true };
    });
    app.post("/api/mal/import", async (req, reply) => {
        const userId = req.userId;
        const tokenResult = await getValidMalAccessToken(ctx, userId);
        if (!tokenResult.ok) {
            req.log.warn({ userId, error: tokenResult.error }, "[Dozzle][MAL] import blocked");
            return reply.code(tokenResult.statusCode).send({ error: tokenResult.error });
        }
        req.log.info({ userId }, "[Dozzle][MAL] import started");
        try {
            let imported = 0;
            for (let offset = 0; offset < 1000; offset += 100) {
                const payload = await (0, mal_1.fetchUserAnimeList)({
                    clientId: ctx.env.MAL_CLIENT_ID,
                    accessToken: tokenResult.accessToken,
                    username: "@me",
                    limit: 100,
                    offset,
                });
                for (const item of payload.data ?? []) {
                    const node = item.node;
                    const row = await ctx.prisma.anime.upsert({
                        where: { provider_providerId: { provider: "myanimelist", providerId: String(node.id) } },
                        update: {
                            title: node.title,
                            genres: (node.genres ?? []).map((g) => g.name),
                            episodes: node.num_episodes ?? null,
                            year: node.start_season?.year ?? null,
                        },
                        create: {
                            provider: "myanimelist",
                            providerId: String(node.id),
                            title: node.title,
                            genres: (node.genres ?? []).map((g) => g.name),
                            episodes: node.num_episodes ?? null,
                            year: node.start_season?.year ?? null,
                        },
                    });
                    await ctx.prisma.watchEntry.upsert({
                        where: { userId_animeId: { userId, animeId: row.animeId } },
                        update: {
                            status: normalizeMalWatchStatus(item.list_status?.status),
                            score: item.list_status?.score ?? null,
                            episodesWatched: item.list_status?.num_episodes_watched ?? 0,
                            updatedAt: new Date(),
                        },
                        create: {
                            userId,
                            animeId: row.animeId,
                            status: normalizeMalWatchStatus(item.list_status?.status),
                            score: item.list_status?.score ?? null,
                            episodesWatched: item.list_status?.num_episodes_watched ?? 0,
                        },
                    });
                    imported += 1;
                }
                if (!payload.paging?.next)
                    break;
            }
            req.log.info({ userId, imported }, "[Dozzle][MAL] import completed");
            return { ok: true, imported };
        }
        catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            req.log.error({ userId, error }, "[Dozzle][MAL] import failed");
            if (message.includes("401")) {
                return reply.code(401).send({ error: "MyAnimeList authorization expired; reconnect MyAnimeList" });
            }
            return reply.code(502).send({ error: "MAL import request failed" });
        }
    });
    // List quests
    app.get("/api/quests", async () => {
        const quests = await ctx.prisma.quest.findMany({
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
            items: quests.map((quest) => ({
                id: quest.questId.toString(),
                code: quest.code,
                title: quest.title,
                description: quest.description,
                requirements: quest.requirements,
                rewards: quest.rewards,
            })),
        };
    });
    // Accept quest
    app.post("/api/quests/:code/accept", async (req, reply) => {
        const userId = req.userId;
        const params = req.params;
        const code = params.code;
        const quest = await ctx.prisma.quest.findUnique({
            where: { code },
        });
        if (!quest) {
            return reply.code(404).send({ error: "Quest not found" });
        }
        await ctx.prisma.userQuest.upsert({
            where: {
                userId_questId: {
                    userId,
                    questId: quest.questId,
                },
            },
            update: {
                status: "active",
                updatedAt: new Date(),
            },
            create: {
                userId,
                questId: quest.questId,
                status: "active",
                progress: {},
            },
        });
        req.log.info({ userId, code }, "[Dozzle][DB] quest accepted upsert");
        return { ok: true };
    });
    // Safe table viewer (read-only, whitelisted)
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
        const params = req.params;
        const query = req.query;
        const name = params.name;
        if (!ALLOWED_TABLES.has(name)) {
            return reply.code(403).send({ error: "Table not allowed" });
        }
        const limit = Math.min(Number.parseInt(query.limit ?? "50", 10) || 50, 200);
        const offset = Math.max(Number.parseInt(query.offset ?? "0", 10) || 0, 0);
        const rowsResult = await ctx.prisma.$queryRawUnsafe(`SELECT * FROM ${name} ORDER BY 1 LIMIT $1 OFFSET $2`, limit, offset);
        req.log.info({ table: name, limit, offset }, "[Dozzle][DB] table viewer query");
        const rows = rowsResult;
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
            await syncTopAnimeCatalog(ctx);
            app.log.info("MAL catalog sync completed");
        }
        catch (error) {
            app.log.error({ error }, "MAL catalog sync failed");
        }
    };
    if (ctx.env.MAL_CLIENT_ID) {
        runCatalogSync();
        setInterval(runCatalogSync, Math.max(ctx.env.MAL_SYNC_INTERVAL_MINUTES, 5) * 60 * 1000);
    }
    else {
        app.log.warn("MAL_CLIENT_ID not configured; MAL catalog sync disabled");
    }
    return app;
}
