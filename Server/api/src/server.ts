import Fastify from "fastify";
import cors from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import swagger from "@fastify/swagger";
import swaggerUI from "@fastify/swagger-ui";
import Redis from "ioredis";
import { Prisma, PrismaClient } from "@prisma/client";
import { fetchNakamaAccount } from "./nakama";

export type AppContext = {
  prisma: PrismaClient;
  redis: Redis;
  env: {
    PORT: number;
    DATABASE_URL: string;
    REDIS_URL: string;
    NAKAMA_HTTP: string;
    NAKAMA_SERVER_KEY: string;
  };
};

type TableRow = Record<string, unknown>;
type AnimeListRow = Prisma.AnimeGetPayload<{
  select: {
    animeId: true;
    title: true;
    year: true;
    episodes: true;
    genres: true;
    trailerYoutubeId: true;
    provider: true;
    providerId: true;
    watchEntries: {
      select: {
        status: true;
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

const WATCH_STATUSES = ["watching", "completed", "planned", "dropped", "on_hold"] as const;
type WatchStatus = (typeof WATCH_STATUSES)[number];

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
  lists: string[];
  genres: string[];
  trailerYoutubeId: string | null;
  provider: string;
  providerId: string;
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

function buildBriefDescription(row: AnimeListRow): string {
  if (row.genres.length === 0) {
    return "Anime catalog entry";
  }

  return `${row.genres.slice(0, 3).join(" • ")} anime`;
}

function buildDescription(row: AnimeListRow): string {
  const genreText = row.genres.length > 0 ? row.genres.join(", ") : "varied genres";
  const episodesText = row.episodes != null ? `${row.episodes} episodes` : "episode count TBD";
  const yearText = row.year != null ? `${row.year}` : "unknown release year";

  return `${row.title} is listed as ${genreText}, with ${episodesText}, released around ${yearText}.`;
}

function posterUrl(row: AnimeListRow): string {
  if (row.trailerYoutubeId) {
    return `https://img.youtube.com/vi/${row.trailerYoutubeId}/hqdefault.jpg`;
  }

  return `https://placehold.co/72x108?text=${encodeURIComponent(row.title.slice(0, 2).toUpperCase())}`;
}

function normalizeWatchStatus(value: string): WatchStatus | null {
  return WATCH_STATUSES.includes(value as WatchStatus) ? (value as WatchStatus) : null;
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

  // Ensure user exists in app-db
  app.post("/api/me/ensure", async (req) => {
    const userId = req.userId!;
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
    const userId = req.userId!;
    const query = req.query as { q?: string; limit?: string };
    const q = query.q?.trim();
    const limit = Math.min(Number.parseInt(query.limit ?? "20", 10) || 20, 100);

    const rows: AnimeListRow[] = await ctx.prisma.anime.findMany({
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

    const items: AnimeDeckItem[] = rows.map((row) => {
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

    return {
      id: watchEntry.animeId,
      isWatching: watchEntry.status === "watching",
      watchStatus: watchEntry.status,
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
    } else if (shouldRemoveCurrent) {
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

  // List quests
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

  // Accept quest
  app.post("/api/quests/:code/accept", async (req, reply) => {
    const userId = req.userId!;
    const params = req.params as { code: string };
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

  return app;
}
