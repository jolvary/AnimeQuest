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
    // Anime search / list
    app.get("/api/anime", async (req) => {
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
            },
        });
        return {
            items: rows.map((row) => ({
                id: row.animeId.toString(),
                title: row.title,
                year: row.year,
                episodes: row.episodes,
                genres: row.genres,
                trailerYoutubeId: row.trailerYoutubeId,
                provider: row.provider,
                providerId: row.providerId,
            })),
        };
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
    return app;
}
