import "dotenv/config";
import Redis from "ioredis";
import { PrismaClient } from "@prisma/client";
import { buildServer } from "./server";

type ClientLogBody = {
  level?: string;
  action?: string;
  details?: string;
  message?: string;
  timestamp?: string;
  platform?: string;
  unityVersion?: string;
};

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
