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

function optional(name: string): string | undefined {
  const value = process.env[name]?.trim();
  return value ? value : undefined;
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
      MAL_CLIENT_ID: "56edd5ea198727230731b1cdfddd25e0",
      MAL_ACCESS_TOKEN: "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImp0aSI6ImY5YjZlODlmMzdjMzA3ZmIzYjFmM2Q3M2RlOTZlZjRmZDc0MjU3NjdiYTYyZWQzMDM1N2VhMmZkMTVkN2I4OTkwYTUzMTdhMWE0NWQ3MjBlIn0.eyJhdWQiOiI1NmVkZDVlYTE5ODcyNzIzMDczMWIxY2RmZGRkMjVlMCIsImp0aSI6ImY5YjZlODlmMzdjMzA3ZmIzYjFmM2Q3M2RlOTZlZjRmZDc0MjU3NjdiYTYyZWQzMDM1N2VhMmZkMTVkN2I4OTkwYTUzMTdhMWE0NWQ3MjBlIiwiaWF0IjoxNzc3NTU4NTA2LCJuYmYiOjE3Nzc1NTg1MDYsImV4cCI6MTc4MDE1MDUwNiwic3ViIjoiNTYzNjUwNCIsInNjb3BlcyI6W119.NReH5nN5NuYl8M1aVnP4YdV9iXg6VkwDM-7iOl5FZtjkAG6IXwbvB-khR1PrqofgU7BtRV3hj1uOpVrOHDg_y4OnRpwyGVc5isHq-I33cer62xIe8X1WQFdURf7pj51gWNwd_6czJbS8r6hQiqOI5oML1etK6CvZuGxHro8A9f_slE5zyofVgIhqe95c0oIaDu0aMrREX8Bu9FYnjoD1zYBWOQwXh86VCCrBLiypYSXVJwljPfdiMeShnDm_UVGeCPIg8pyZCYq0Z3QH8s8jtFIw_e0UVhGbyyUnrdCF3uI7LhFat3ToW5OVNMvCtDG2A6OaDUUgixafI17-r1_6Vg",
      MAL_SYNC_INTERVAL_MINUTES: Number.parseInt(process.env.MAL_SYNC_INTERVAL_MINUTES ?? "60", 10),
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
