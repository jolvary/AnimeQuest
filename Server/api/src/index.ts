import "dotenv/config";
import Redis from "ioredis";
import { PrismaClient } from "@prisma/client";
import { buildServer } from "./server";

function mustGet(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing env var: ${name}`);
  }
  return value;
}

async function main() {
  const prisma = new PrismaClient();
  const redis = new Redis(mustGet("REDIS_URL"));

  const app = buildServer({
    prisma,
    redis,
    env: {
      PORT: Number.parseInt(process.env.PORT ?? "3000", 10),
      DATABASE_URL: mustGet("DATABASE_URL"),
      REDIS_URL: mustGet("REDIS_URL"),
      NAKAMA_HTTP: mustGet("NAKAMA_HTTP"),
      NAKAMA_SERVER_KEY: mustGet("NAKAMA_SERVER_KEY"),
    },
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