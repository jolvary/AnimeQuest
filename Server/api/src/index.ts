import "dotenv/config";
import Redis from "ioredis";
import { PrismaClient } from "@prisma/client";
import type { FastifyReply, FastifyRequest } from "fastify";
import { buildServer } from "./server";
import { fetchNakamaAccount } from "./nakama";

type ClientLogBody = {
  level?: string;
  action?: string;
  details?: string;
  message?: string;
  timestamp?: string;
  platform?: string;
  unityVersion?: string;
};

type SessionLeaseBody = {
  clientId?: string;
  platform?: string;
  unityVersion?: string;
};

type ActiveSessionLease = {
  clientId: string;
  userId: string;
  username?: string;
  platform?: string;
  unityVersion?: string;
  acquiredAt: string;
};

type SessionLeaseDeps = {
  redis: Redis;
  nakamaHttp: string;
  serverKey: string;
};

const ALLOWED_IMAGE_HOSTS = new Set([
  "api-cdn.myanimelist.net",
  "cdn.myanimelist.net",
  "myanimelist.net",
  "img.youtube.com",
  "i.ytimg.com",
  "placehold.co",
]);

const ACTIVE_SESSION_TTL_SECONDS = 75;

function mustGet(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing env var: ${name}`);
  }
  return value;
}

function optional(name: string): string | undefined {
  const value = process.env[name]?.trim();
  return value ? value : undefined;
}

function optionalInt(name: string): number | undefined {
  const raw = process.env[name]?.trim();
  if (!raw) return undefined;

  const value = Number.parseInt(raw, 10);
  if (!Number.isFinite(value) || value <= 0) return undefined;
  return value;
}

function compact(value: unknown, maxLength = 500): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (!trimmed) return null;
  return trimmed.length > maxLength ? `${trimmed.slice(0, maxLength)}...` : trimmed;
}

function isAllowedImageHost(hostname: string) {
  const normalized = hostname.toLowerCase();
  return ALLOWED_IMAGE_HOSTS.has(normalized) || normalized.endsWith(".myanimelist.net");
}

async function ensureAppSchema(prisma: PrismaClient) {
  await prisma.$executeRawUnsafe('ALTER TABLE anime ADD COLUMN IF NOT EXISTS image_url TEXT');
  await prisma.$executeRawUnsafe('ALTER TABLE anime ADD COLUMN IF NOT EXISTS synopsis TEXT');
}

function registerClientLogIntake(app: ReturnType<typeof buildServer>) {
  app.post("/client/logs", async (req) => {
    const body = (req.body ?? {}) as ClientLogBody;
    const level = compact(body.level, 20)?.toLowerCase() ?? "action";
    const action = compact(body.action, 120) ?? "Unity event";
    const details = compact(body.details, 1000);
    const message = compact(body.message, 1000);

    const payload = {
      source: "unity",
      level,
      action,
      details,
      message,
      clientTimestamp: compact(body.timestamp, 80),
      platform: compact(body.platform, 80),
      unityVersion: compact(body.unityVersion, 80),
    };

    const text = details ? `[Dozzle][Unity] ${action} | ${details}` : `[Dozzle][Unity] ${action}`;
    if (level === "error") {
      req.log.error(payload, text);
    } else if (level === "warning" || level === "warn") {
      req.log.warn(payload, text);
    } else {
      req.log.info(payload, text);
    }

    return { ok: true };
  });
}

function registerClientImageProxy(app: ReturnType<typeof buildServer>) {
  app.get("/client/image", async (req, reply) => {
    const query = req.query as { url?: string };
    if (!query.url) {
      return reply.code(400).send({ error: "Missing image URL" });
    }

    let url: URL;
    try {
      url = new URL(query.url);
    } catch {
      return reply.code(400).send({ error: "Invalid image URL" });
    }

    if (url.protocol !== "https:" && url.protocol !== "http:") {
      return reply.code(400).send({ error: "Unsupported image URL protocol" });
    }

    if (!isAllowedImageHost(url.hostname)) {
      return reply.code(403).send({ error: "Image host not allowed" });
    }

    try {
      const response = await fetch(url.toString(), {
        headers: {
          "user-agent": "AnimeQuest/0.1 image proxy",
          accept: "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8",
        },
      });

      if (!response.ok) {
        req.log.warn({ url: url.toString(), status: response.status }, "[Dozzle][Image] upstream poster request failed");
        return reply.code(response.status === 404 ? 404 : 502).send({ error: "Image request failed" });
      }

      const contentType = response.headers.get("content-type") ?? "application/octet-stream";
      if (!contentType.toLowerCase().startsWith("image/")) {
        req.log.warn({ url: url.toString(), contentType }, "[Dozzle][Image] upstream poster returned non-image content");
        return reply.code(415).send({ error: "URL did not return an image" });
      }

      const body = Buffer.from(await response.arrayBuffer());
      reply.header("Cache-Control", "public, max-age=86400");
      reply.header("Content-Type", contentType);
      return reply.send(body);
    } catch (error) {
      req.log.error({ url: url.toString(), error }, "[Dozzle][Image] poster proxy failed");
      return reply.code(502).send({ error: "Image proxy failed" });
    }
  });
}

function activeSessionKey(userId: string) {
  return `active-session:${userId}`;
}

function parseActiveSession(raw: string | null): ActiveSessionLease | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<ActiveSessionLease>;
    if (typeof parsed.clientId === "string" && typeof parsed.userId === "string") {
      return {
        clientId: parsed.clientId,
        userId: parsed.userId,
        username: typeof parsed.username === "string" ? parsed.username : undefined,
        platform: typeof parsed.platform === "string" ? parsed.platform : undefined,
        unityVersion: typeof parsed.unityVersion === "string" ? parsed.unityVersion : undefined,
        acquiredAt: typeof parsed.acquiredAt === "string" ? parsed.acquiredAt : new Date().toISOString(),
      };
    }
  } catch {
    return null;
  }

  return null;
}

function readBearerToken(req: FastifyRequest) {
  const header = req.headers.authorization;
  const value = Array.isArray(header) ? header[0] : header;
  if (!value?.startsWith("Bearer ")) return null;
  return value.slice("Bearer ".length).trim();
}

function readSessionClientId(body: SessionLeaseBody) {
  const clientId = compact(body.clientId, 128);
  if (!clientId) return null;
  return /^[A-Za-z0-9_-]{16,128}$/.test(clientId) ? clientId : null;
}

async function authenticateLeaseRequest(req: FastifyRequest, reply: FastifyReply, deps: SessionLeaseDeps) {
  const token = readBearerToken(req);
  if (!token) {
    reply.code(401).send({ error: "Missing Bearer token" });
    return null;
  }

  try {
    return await fetchNakamaAccount({
      nakamaHttp: deps.nakamaHttp,
      serverKey: deps.serverKey,
      sessionToken: token,
    });
  } catch (error) {
    req.log.warn({ error }, "[Dozzle][Session] lease auth failed");
    reply.code(401).send({ error: "Invalid session token" });
    return null;
  }
}

async function reserveActiveSession(
  req: FastifyRequest,
  reply: FastifyReply,
  deps: SessionLeaseDeps,
  mode: "acquire" | "heartbeat"
) {
  const acct = await authenticateLeaseRequest(req, reply, deps);
  if (!acct) return null;

  const body = (req.body ?? {}) as SessionLeaseBody;
  const clientId = readSessionClientId(body);
  if (!clientId) {
    return reply.code(400).send({ error: "clientId must be 16-128 URL-safe characters" });
  }

  const key = activeSessionKey(acct.userId);
  const existing = parseActiveSession(await deps.redis.get(key));
  if (existing && existing.clientId !== clientId) {
    req.log.warn({ userId: acct.userId, existingClientId: existing.clientId, requestedClientId: clientId }, "[Dozzle][Session] duplicate login blocked");
    return reply.code(409).send({ error: "Account already logged in elsewhere" });
  }

  const lease: ActiveSessionLease = {
    clientId,
    userId: acct.userId,
    username: acct.username,
    platform: compact(body.platform, 80) ?? undefined,
    unityVersion: compact(body.unityVersion, 80) ?? undefined,
    acquiredAt: existing?.acquiredAt ?? new Date().toISOString(),
  };

  await deps.redis.set(key, JSON.stringify(lease), "EX", ACTIVE_SESSION_TTL_SECONDS);
  req.log.info({ userId: acct.userId, mode, clientId, ttl: ACTIVE_SESSION_TTL_SECONDS }, "[Dozzle][Session] active session lease refreshed");
  return { ok: true, expiresInSeconds: ACTIVE_SESSION_TTL_SECONDS };
}

async function releaseActiveSession(req: FastifyRequest, reply: FastifyReply, deps: SessionLeaseDeps) {
  const acct = await authenticateLeaseRequest(req, reply, deps);
  if (!acct) return null;

  const body = (req.body ?? {}) as SessionLeaseBody;
  const clientId = readSessionClientId(body);
  if (!clientId) {
    return reply.code(400).send({ error: "clientId must be 16-128 URL-safe characters" });
  }

  const key = activeSessionKey(acct.userId);
  const existing = parseActiveSession(await deps.redis.get(key));
  if (existing?.clientId === clientId) {
    await deps.redis.del(key);
    req.log.info({ userId: acct.userId, clientId }, "[Dozzle][Session] active session lease released");
  }

  return { ok: true };
}

function registerClientSessionLeases(app: ReturnType<typeof buildServer>, deps: SessionLeaseDeps) {
  app.post("/client/session/acquire", async (req, reply) => reserveActiveSession(req, reply, deps, "acquire"));
  app.post("/client/session/heartbeat", async (req, reply) => reserveActiveSession(req, reply, deps, "heartbeat"));
  app.post("/client/session/release", async (req, reply) => releaseActiveSession(req, reply, deps));
}

async function main() {
  const prisma = new PrismaClient();
  const redis = new Redis(mustGet("REDIS_URL"));

  await ensureAppSchema(prisma);

  const app = buildServer({
    prisma,
    redis,
    env: {
      PORT: Number.parseInt(process.env.PORT ?? "3000", 10),
      DATABASE_URL: mustGet("DATABASE_URL"),
      REDIS_URL: mustGet("REDIS_URL"),
      NAKAMA_HTTP: mustGet("NAKAMA_HTTP"),
      NAKAMA_SERVER_KEY: mustGet("NAKAMA_SERVER_KEY"),
      MAL_CLIENT_ID: optional("MAL_CLIENT_ID"),
      MAL_CLIENT_SECRET: optional("MAL_CLIENT_SECRET"),
      MAL_REDIRECT_URI: optional("MAL_REDIRECT_URI"),
      MAL_TOKEN_ENCRYPTION_KEY: optional("MAL_TOKEN_ENCRYPTION_KEY"),
      MAL_SYNC_INTERVAL_MINUTES: Number.parseInt(process.env.MAL_SYNC_INTERVAL_MINUTES ?? "60", 10),
      MAL_CATALOG_SYNC_MAX_PAGES: optionalInt("MAL_CATALOG_SYNC_MAX_PAGES"),
    },
  });

  registerClientLogIntake(app);
  registerClientImageProxy(app);
  registerClientSessionLeases(app, {
    redis,
    nakamaHttp: mustGet("NAKAMA_HTTP"),
    serverKey: mustGet("NAKAMA_SERVER_KEY"),
  });

  const port = Number.parseInt(process.env.PORT ?? "3000", 10);

  await app.listen({
    port,
    host: "0.0.0.0",
  });

  console.log(`API listening on http://0.0.0.0:${port}`);
}

main().catch((error) => {
  console.error("Failed to start API:", error);
  process.exit(1);
});
