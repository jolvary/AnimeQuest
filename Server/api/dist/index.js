"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
require("dotenv/config");
const ioredis_1 = __importDefault(require("ioredis"));
const client_1 = require("@prisma/client");
const server_1 = require("./server");
function mustGet(name) {
    const value = process.env[name];
    if (!value) {
        throw new Error(`Missing env var: ${name}`);
    }
    return value;
}
async function main() {
    const prisma = new client_1.PrismaClient();
    const redis = new ioredis_1.default(mustGet("REDIS_URL"));
    const app = (0, server_1.buildServer)({
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
