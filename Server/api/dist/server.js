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
const nakama_1 = require("./nakama");
const mal_1 = require("./mal");
const WATCH_STATUSES = ["watching", "completed", "planned", "dropped", "on_hold"];
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
async function syncTopAnimeCatalog(ctx, maxPages = 5) {
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
    // Health check
    app.get("/health", async () => {
        return { ok: true };
    });
    // Auth middleware for protected routes
    app.addHook("preHandler", async (req, reply) => {
        if (!req.url.startsWith("/api/")) {
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
        const displayName = req.username ?? `player_${userId.slice(0, 6)}`;
        const user = await ctx.prisma.user.upsert({
            where: { userId },
            update: { displayName },
            create: {
                userId,
                displayName,
            },
        });
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
    app.post("/api/mal/import", async (req, reply) => {
        const userId = req.userId;
        const body = req.body;
        const username = body?.username?.trim();
        if (!username)
            return reply.code(400).send({ error: "username is required" });
        if (!ctx.env.MAL_ACCESS_TOKEN)
            return reply.code(500).send({ error: "MAL_ACCESS_TOKEN not configured" });
        let imported = 0;
        for (let offset = 0; offset < 1000; offset += 100) {
            const payload = await (0, mal_1.fetchUserAnimeList)({
                clientId: ctx.env.MAL_CLIENT_ID,
                accessToken: ctx.env.MAL_ACCESS_TOKEN,
                username,
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
                const malStatus = item.list_status?.status ?? "plan_to_watch";
                const normalizedStatus = malStatus === "watching" ? "watching" : malStatus === "completed" ? "completed" : malStatus === "dropped" ? "dropped" : malStatus === "on_hold" ? "on_hold" : "planned";
                await ctx.prisma.watchEntry.upsert({
                    where: { userId_animeId: { userId, animeId: row.animeId } },
                    update: {
                        status: normalizedStatus,
                        score: item.list_status?.score ?? null,
                        episodesWatched: item.list_status?.num_episodes_watched ?? 0,
                        updatedAt: new Date(),
                    },
                    create: {
                        userId,
                        animeId: row.animeId,
                        status: normalizedStatus,
                        score: item.list_status?.score ?? null,
                        episodesWatched: item.list_status?.num_episodes_watched ?? 0,
                    },
                });
                imported += 1;
            }
            if (!payload.paging?.next)
                break;
        }
        return { ok: true, imported };
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
    runCatalogSync();
    setInterval(runCatalogSync, Math.max(ctx.env.MAL_SYNC_INTERVAL_MINUTES, 5) * 60 * 1000);
    return app;
}
