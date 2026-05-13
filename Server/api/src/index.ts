import 'dotenv/config';
import Redis from 'ioredis';
import { PrismaClient } from '@prisma/client';
import type { FastifyReply, FastifyRequest } from 'fastify';
import { buildServer, syncTopAnimeCatalog, type AppContext } from './server';
import { fetchNakamaAccount } from './nakama';

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

type CharacterSelectBody = {
  characterKey?: string;
  robotColor?: string;
};

type AnimeProgressPatchBody = {
  status?: string | null;
  score?: number | null;
  episodesWatched?: number | null;
};

type PlayerStatePatchBody = {
  x?: number;
  y?: number;
  z?: number;
  rotationY?: number;
};

type CharacterDefinition = {
  key: string;
  displayName: string;
  description: string;
  kind: 'robot' | 'robot_color' | 'prefab_slot';
  robotColor?: string;
  prefabSlot?: string;
  unlockLevel?: number;
  unlockQuestCode?: string;
  assetStoreUrl?: string;
};

type QuestSeedDefinition = {
  code: string;
  title: string;
  description: string;
  requirements: Record<string, number>;
  rewards: Record<string, number | string>;
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
  'api-cdn.myanimelist.net',
  'cdn.myanimelist.net',
  'myanimelist.net',
  'img.youtube.com',
  'i.ytimg.com',
  'www.google.com',
  'placehold.co',
]);

const ACTIVE_SESSION_TTL_SECONDS = 75;
const XP_PER_LEVEL = 100;
const DEFAULT_CHARACTER_KEY = 'robot_kyle';
const DEFAULT_ROBOT_COLOR = 'default';
const WATCH_STATUSES = ['watching', 'completed', 'planned', 'dropped', 'on_hold'] as const;
type WatchStatus = (typeof WATCH_STATUSES)[number];

const CHARACTER_CATALOG: CharacterDefinition[] = [
  {
    key: 'robot_kyle',
    displayName: 'Robot Kyle',
    description: 'The default AnimeQuest robot body.',
    kind: 'robot',
    robotColor: DEFAULT_ROBOT_COLOR,
    unlockLevel: 1,
  },
  {
    key: 'robot_blue',
    displayName: 'Blue Robot Kyle',
    description: 'A blue RobotKyle material variant unlocked from the warm-up quest.',
    kind: 'robot_color',
    robotColor: 'blue',
    unlockLevel: 2,
    unlockQuestCode: 'watch_5_eps',
  },
  {
    key: 'robot_green',
    displayName: 'Green Robot Kyle',
    description: 'A green RobotKyle material variant for players who rate anime.',
    kind: 'robot_color',
    robotColor: 'green',
    unlockLevel: 2,
    unlockQuestCode: 'rate_3_titles',
  },
  {
    key: 'robot_red',
    displayName: 'Red Robot Kyle',
    description: 'A level-gated RobotKyle material variant.',
    kind: 'robot_color',
    robotColor: 'red',
    unlockLevel: 3,
  },
  {
    key: 'ghost_character',
    displayName: 'Ghost Character',
    description: 'The Ghost Character asset, unlocked by completing a series.',
    kind: 'prefab_slot',
    prefabSlot: 'GhostCharacter',
    unlockLevel: 3,
    unlockQuestCode: 'complete_series',
    assetStoreUrl: 'https://assetstore.unity.com/packages/3d/characters/creatures/ghost-character-free-267003',
  },
  {
    key: 'skeleton',
    displayName: 'Stylized Skeleton',
    description: 'Prefab slot for the Stylized Low Poly Skeleton asset.',
    kind: 'prefab_slot',
    prefabSlot: 'StylizedLowPolySkeleton',
    unlockLevel: 4,
    assetStoreUrl: 'https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/stylized-low-poly-skeleton-306857',
  },
  {
    key: 'tiny_hero',
    displayName: 'Tiny Hero Male',
    description: 'MaleCharacterPBR from RPG Tiny Hero Duo, using the root-motion sword and shield animation set.',
    kind: 'prefab_slot',
    prefabSlot: 'MaleCharacterPBR',
    unlockLevel: 5,
    assetStoreUrl: 'https://assetstore.unity.com/packages/3d/characters/humanoids/rpg-tiny-hero-duo-pbr-polyart-225148',
  },
  {
    key: 'tiny_hero_female',
    displayName: 'Tiny Hero Female',
    description: 'FemaleCharacterPBR from RPG Tiny Hero Duo, using the root-motion sword and shield animation set.',
    kind: 'prefab_slot',
    prefabSlot: 'FemaleCharacterPBR',
    unlockLevel: 5,
    assetStoreUrl: 'https://assetstore.unity.com/packages/3d/characters/humanoids/rpg-tiny-hero-duo-pbr-polyart-225148',
  },
  {
    key: 'robot_hero',
    displayName: 'Robot Hero',
    description: 'Prefab slot for the Robot Hero PBR HP Polyart asset.',
    kind: 'prefab_slot',
    prefabSlot: 'RobotHero',
    unlockLevel: 6,
    assetStoreUrl: 'https://assetstore.unity.com/packages/3d/characters/robots/robot-hero-pbr-hp-polyart-106154',
  },
  {
    key: 'scifi_hp_character',
    displayName: 'Sci-Fi Warrior HP',
    description: 'HPCharacter from SciFiWarriorPBRHPPolyart.',
    kind: 'prefab_slot',
    prefabSlot: 'HPCharacter',
    unlockLevel: 8,
  },
  {
    key: 'scifi_pbr_character',
    displayName: 'Sci-Fi Warrior PBR',
    description: 'PBRCharacter from SciFiWarriorPBRHPPolyart.',
    kind: 'prefab_slot',
    prefabSlot: 'PBRCharacter',
    unlockLevel: 9,
  },
  {
    key: 'scifi_polyart_character',
    displayName: 'Sci-Fi Warrior Polyart',
    description: 'PolyartCharacter from SciFiWarriorPBRHPPolyart.',
    kind: 'prefab_slot',
    prefabSlot: 'PolyartCharacter',
    unlockLevel: 10,
  },
];

const QUEST_CATALOG: QuestSeedDefinition[] = [
  {
    code: 'watch_5_eps',
    title: 'Warm-up Marathon',
    description: 'Watch five anime episodes this week.',
    requirements: { episodes: 5 },
    rewards: { xp: 50, coins: 100, character: 'robot_blue' },
  },
  {
    code: 'rate_3_titles',
    title: 'Critic Apprentice',
    description: 'Rate three different anime titles.',
    requirements: { ratings: 3 },
    rewards: { xp: 40, item: 'review_badge', character: 'robot_green' },
  },
  {
    code: 'complete_series',
    title: 'Finale Hunter',
    description: 'Complete one anime series.',
    requirements: { completed_series: 1 },
    rewards: { xp: 100, coins: 250, character: 'ghost_character' },
  },
  {
    code: 'watch_12_eps',
    title: 'Season Sprint',
    description: 'Watch twelve anime episodes.',
    requirements: { episodes: 12 },
    rewards: { xp: 90, coins: 160 },
  },
  {
    code: 'watch_24_eps',
    title: 'Binge Legend',
    description: 'Watch twenty-four anime episodes.',
    requirements: { episodes: 24 },
    rewards: { xp: 160, coins: 320 },
  },
  {
    code: 'rate_5_titles',
    title: 'Sharp-Eyed Critic',
    description: 'Rate five different anime titles.',
    requirements: { ratings: 5 },
    rewards: { xp: 90, coins: 140, item: 'critic_pin' },
  },
  {
    code: 'complete_3_series',
    title: 'Completionist Path',
    description: 'Complete three anime series.',
    requirements: { completed_series: 3 },
    rewards: { xp: 220, coins: 500 },
  },
  {
    code: 'balanced_fan',
    title: 'Balanced Fan',
    description: 'Watch, rate, and finish anime to prove a rounded profile.',
    requirements: { episodes: 10, ratings: 2, completed_series: 1 },
    rewards: { xp: 150, coins: 250, item: 'balanced_badge' },
  },
];

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

function optionalBool(name: string, fallback: boolean): boolean {
  const raw = process.env[name]?.trim().toLowerCase();
  if (!raw) return fallback;
  if (['1', 'true', 'yes', 'on'].includes(raw)) return true;
  if (['0', 'false', 'no', 'off'].includes(raw)) return false;
  return fallback;
}

async function runStartupCatalogSync(ctx: AppContext) {
  if (!ctx.env.MAL_CLIENT_ID || !ctx.env.MAL_CATALOG_SYNC_ON_START || !ctx.catalogSync) return;

  ctx.catalogSync.startupStartedAt = new Date().toISOString();
  console.log(
    `MAL catalog startup sync started (maxPages=${ctx.env.MAL_CATALOG_SYNC_MAX_PAGES ?? 'all'}, required=${ctx.env.MAL_CATALOG_SYNC_REQUIRED})`
  );

  try {
    const result = await syncTopAnimeCatalog(ctx, ctx.env.MAL_CATALOG_SYNC_MAX_PAGES, (progress) => {
      ctx.catalogSync!.pages = progress.event === 'page' ? progress.page : progress.page - 1;
      ctx.catalogSync!.upserted = progress.upserted;
      console.log(
        `MAL catalog sync ${progress.event}: page=${progress.page}, offset=${progress.offset}, upserted=${progress.upserted}` +
          (progress.pageCount == null ? '' : `, pageCount=${progress.pageCount}, hasNext=${progress.hasNext}`)
      );
    });
    ctx.catalogSync.pages = result.pages;
    ctx.catalogSync.upserted = result.upserted;
    ctx.catalogSync.startupReady = true;
    ctx.catalogSync.startupCompletedAt = new Date().toISOString();
    console.log(`MAL catalog startup sync completed: pages=${result.pages}, upserted=${result.upserted}`);
  } catch (error) {
    ctx.catalogSync.startupError = error instanceof Error ? error.message : String(error);
    ctx.catalogSync.startupCompletedAt = new Date().toISOString();
    console.error('MAL catalog startup sync failed:', error);
    if (ctx.env.MAL_CATALOG_SYNC_REQUIRED) {
      process.exitCode = 1;
      setTimeout(() => process.exit(1), 1000);
    } else {
      ctx.catalogSync.startupReady = true;
    }
  }
}

function compact(value: unknown, maxLength = 500): string | null {
  if (typeof value !== 'string') return null;
  const trimmed = value.trim();
  if (!trimmed) return null;
  return trimmed.length > maxLength ? `${trimmed.slice(0, maxLength)}...` : trimmed;
}

function isAllowedImageHost(hostname: string) {
  const normalized = hostname.toLowerCase();
  return ALLOWED_IMAGE_HOSTS.has(normalized) || normalized.endsWith('.myanimelist.net');
}

function levelForExperience(experiencePoints: number) {
  return Math.max(1, Math.floor(Math.max(0, experiencePoints) / XP_PER_LEVEL) + 1);
}

function nextLevelExperience(level: number) {
  return Math.max(1, level) * XP_PER_LEVEL;
}

function readJsonRecord(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {};
}

function readNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;
}

function readString(value: unknown) {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
}

function hasOwn(value: object, key: string) {
  return Object.prototype.hasOwnProperty.call(value, key);
}

function normalizeWatchStatus(value?: string | null): WatchStatus | null {
  const normalized = value?.trim().toLowerCase();
  return WATCH_STATUSES.includes(normalized as WatchStatus) ? (normalized as WatchStatus) : null;
}

function readOptionalInteger(value: unknown) {
  if (value == null) return null;
  if (typeof value !== 'number' || !Number.isFinite(value)) return undefined;
  return Math.floor(value);
}

function readFiniteNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function isKnownCharacterKey(value?: string | null) {
  return !!value && CHARACTER_CATALOG.some((item) => item.key === value);
}

function normalizeCharacterKey(value?: string | null) {
  const key = value?.trim();
  return key && isKnownCharacterKey(key) ? key : DEFAULT_CHARACTER_KEY;
}

function characterDefinition(key: string) {
  return CHARACTER_CATALOG.find((item) => item.key === key) ?? CHARACTER_CATALOG[0];
}

function isQuestGatedCharacter(key: string) {
  return !!characterDefinition(key).unlockQuestCode;
}

function levelUnlockedCharacters(level: number) {
  return CHARACTER_CATALOG.filter((item) => !item.unlockQuestCode && (item.unlockLevel ?? 1) <= level).map((item) => item.key);
}

function mergeUnlockedCharacters(
  existing: string[] | null | undefined,
  level: number,
  rewardCharacter?: string | null,
  completedRewardCharacters: string[] = []
) {
  const values = new Set<string>([DEFAULT_CHARACTER_KEY]);
  for (const key of existing ?? []) {
    if (isKnownCharacterKey(key) && !isQuestGatedCharacter(key)) values.add(key);
  }

  for (const key of levelUnlockedCharacters(level)) values.add(key);

  for (const key of completedRewardCharacters) {
    if (isKnownCharacterKey(key)) values.add(key);
  }

  if (rewardCharacter && isKnownCharacterKey(rewardCharacter)) {
    values.add(rewardCharacter);
  }

  return [...values];
}

function sameStringSet(left: string[], right: string[]) {
  if (left.length !== right.length) return false;
  const values = new Set(left);
  return right.every((value) => values.has(value));
}

function buildCharacterResponse(user: {
  userId: string;
  displayName: string;
  experiencePoints: number;
  level: number;
  coins: number;
  unlockedCharacters: string[];
  selectedCharacterKey: string;
  robotColor: string;
}) {
  const unlocked = new Set(user.unlockedCharacters?.length ? user.unlockedCharacters : [DEFAULT_CHARACTER_KEY]);
  const selectedKey = unlocked.has(normalizeCharacterKey(user.selectedCharacterKey)) ? normalizeCharacterKey(user.selectedCharacterKey) : DEFAULT_CHARACTER_KEY;
  return {
    profile: {
      userId: user.userId,
      displayName: user.displayName,
      experiencePoints: user.experiencePoints,
      level: user.level,
      nextLevelExperience: nextLevelExperience(user.level),
      coins: user.coins,
      selectedCharacterKey: selectedKey,
      robotColor: user.robotColor || DEFAULT_ROBOT_COLOR,
    },
    characters: CHARACTER_CATALOG.map((character) => ({
      key: character.key,
      displayName: character.displayName,
      description: character.description,
      kind: character.kind,
      robotColor: character.robotColor ?? null,
      prefabSlot: character.prefabSlot ?? null,
      unlockLevel: character.unlockLevel ?? 1,
      unlockQuestCode: character.unlockQuestCode ?? null,
      assetStoreUrl: character.assetStoreUrl ?? null,
      unlocked: unlocked.has(character.key),
      selected: selectedKey === character.key,
    })),
  };
}

async function loadCompletedRewardCharacters(prisma: PrismaClient, userId: string) {
  const completed = await prisma.userQuest.findMany({
    where: { userId, status: 'completed' },
    include: { quest: { select: { rewards: true } } },
  });

  return completed
    .map((row) => readString(readJsonRecord(row.quest.rewards).character))
    .filter((value): value is string => !!value && isKnownCharacterKey(value));
}

async function ensureProgressionUser(prisma: PrismaClient, userId: string, username?: string) {
  const displayName = username ?? `player_${userId.slice(0, 6)}`;
  const user = await prisma.user.upsert({
    where: { userId },
    update: { displayName },
    create: {
      userId,
      displayName,
      experiencePoints: 0,
      level: 1,
      coins: 0,
      unlockedCharacters: [DEFAULT_CHARACTER_KEY],
      selectedCharacterKey: DEFAULT_CHARACTER_KEY,
      robotColor: DEFAULT_ROBOT_COLOR,
    },
    select: {
      userId: true,
      displayName: true,
      experiencePoints: true,
      level: true,
      coins: true,
      unlockedCharacters: true,
      selectedCharacterKey: true,
      robotColor: true,
    },
  });

  const level = levelForExperience(user.experiencePoints);
  const completedRewardCharacters = await loadCompletedRewardCharacters(prisma, userId);
  const unlockedCharacters = mergeUnlockedCharacters(user.unlockedCharacters, level, null, completedRewardCharacters);
  const selectedCharacterKey = unlockedCharacters.includes(normalizeCharacterKey(user.selectedCharacterKey))
    ? normalizeCharacterKey(user.selectedCharacterKey)
    : DEFAULT_CHARACTER_KEY;
  const shouldUpdate =
    level !== user.level ||
    !sameStringSet(unlockedCharacters, user.unlockedCharacters) ||
    selectedCharacterKey !== user.selectedCharacterKey;
  if (!shouldUpdate) return user;

  return prisma.user.update({
    where: { userId },
    data: { level, unlockedCharacters, selectedCharacterKey },
    select: {
      userId: true,
      displayName: true,
      experiencePoints: true,
      level: true,
      coins: true,
      unlockedCharacters: true,
      selectedCharacterKey: true,
      robotColor: true,
    },
  });
}

async function loadQuestStats(prisma: PrismaClient, userId: string) {
  const [ratings, completedSeries, episodeAggregate] = await Promise.all([
    prisma.watchEntry.count({ where: { userId, score: { gt: 0 } } }),
    prisma.watchEntry.count({ where: { userId, status: 'completed' } }),
    prisma.watchEntry.aggregate({ where: { userId }, _sum: { episodesWatched: true } }),
  ]);

  return {
    ratings,
    completed_series: completedSeries,
    episodes: episodeAggregate._sum.episodesWatched ?? 0,
  };
}

function calculateQuestProgress(requirements: Record<string, unknown>, stats: { ratings: number; completed_series: number; episodes: number }) {
  const requiredRatings = readNumber(requirements.ratings);
  const requiredEpisodes = readNumber(requirements.episodes);
  const requiredCompleted = readNumber(requirements.completed_series);
  let total = 0;
  let parts = 0;

  const add = (required: number, current: number) => {
    if (required <= 0) return;
    total += Math.min(Math.max(current, 0) / required, 1);
    parts += 1;
  };

  add(requiredRatings, stats.ratings);
  add(requiredEpisodes, stats.episodes);
  add(requiredCompleted, stats.completed_series);

  return parts === 0 ? 1 : Math.min(total / parts, 1);
}

async function ensureAppSchema(prisma: PrismaClient) {
  await prisma.$executeRawUnsafe('ALTER TABLE anime ADD COLUMN IF NOT EXISTS image_url TEXT');
  await prisma.$executeRawUnsafe('ALTER TABLE anime ADD COLUMN IF NOT EXISTS synopsis TEXT');
  await prisma.$executeRawUnsafe('ALTER TABLE users ADD COLUMN IF NOT EXISTS experience_points INT NOT NULL DEFAULT 0');
  await prisma.$executeRawUnsafe('ALTER TABLE users ADD COLUMN IF NOT EXISTS level INT NOT NULL DEFAULT 1');
  await prisma.$executeRawUnsafe('ALTER TABLE users ADD COLUMN IF NOT EXISTS coins INT NOT NULL DEFAULT 0');
  await prisma.$executeRawUnsafe(`ALTER TABLE users ADD COLUMN IF NOT EXISTS unlocked_characters TEXT[] NOT NULL DEFAULT ARRAY['robot_kyle']`);
  await prisma.$executeRawUnsafe(`ALTER TABLE users ADD COLUMN IF NOT EXISTS selected_character_key TEXT NOT NULL DEFAULT 'robot_kyle'`);
  await prisma.$executeRawUnsafe(`ALTER TABLE users ADD COLUMN IF NOT EXISTS robot_color TEXT NOT NULL DEFAULT 'default'`);
}

async function seedQuestCatalog(prisma: PrismaClient) {
  for (const quest of QUEST_CATALOG) {
    await prisma.quest.upsert({
      where: { code: quest.code },
      update: {
        title: quest.title,
        description: quest.description,
        requirements: quest.requirements,
        rewards: quest.rewards,
      },
      create: quest,
    });
  }
}

function registerClientLogIntake(app: ReturnType<typeof buildServer>) {
  app.post('/client/logs', async (req) => {
    const body = (req.body ?? {}) as ClientLogBody;
    const level = compact(body.level, 20)?.toLowerCase() ?? 'action';
    const action = compact(body.action, 120) ?? 'Unity event';
    const details = compact(body.details, 1000);
    const message = compact(body.message, 1000);

    const payload = {
      source: 'unity',
      level,
      action,
      details,
      message,
      clientTimestamp: compact(body.timestamp, 80),
      platform: compact(body.platform, 80),
      unityVersion: compact(body.unityVersion, 80),
    };

    const text = details ? `[Dozzle][Unity] ${action} | ${details}` : `[Dozzle][Unity] ${action}`;
    if (level === 'error') {
      req.log.error(payload, text);
    } else if (level === 'warning' || level === 'warn') {
      req.log.warn(payload, text);
    } else {
      req.log.info(payload, text);
    }

    return { ok: true };
  });
}

function registerClientImageProxy(app: ReturnType<typeof buildServer>) {
  app.get('/client/image', async (req, reply) => {
    const query = req.query as { url?: string };
    if (!query.url) {
      return reply.code(400).send({ error: 'Missing image URL' });
    }

    let url: URL;
    try {
      url = new URL(query.url);
    } catch {
      return reply.code(400).send({ error: 'Invalid image URL' });
    }

    if (url.protocol !== 'https:' && url.protocol !== 'http:') {
      return reply.code(400).send({ error: 'Unsupported image URL protocol' });
    }

    if (!isAllowedImageHost(url.hostname)) {
      return reply.code(403).send({ error: 'Image host not allowed' });
    }

    try {
      const response = await fetch(url.toString(), {
        headers: {
          'user-agent': 'AnimeQuest/0.1 image proxy',
          accept: 'image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8',
        },
      });

      if (!response.ok) {
        req.log.warn({ url: url.toString(), status: response.status }, '[Dozzle][Image] upstream poster request failed');
        return reply.code(response.status === 404 ? 404 : 502).send({ error: 'Image request failed' });
      }

      const contentType = response.headers.get('content-type') ?? 'application/octet-stream';
      if (!contentType.toLowerCase().startsWith('image/')) {
        req.log.warn({ url: url.toString(), contentType }, '[Dozzle][Image] upstream poster returned non-image content');
        return reply.code(415).send({ error: 'URL did not return an image' });
      }

      const body = Buffer.from(await response.arrayBuffer());
      reply.header('Cache-Control', 'public, max-age=86400');
      reply.header('Content-Type', contentType);
      return reply.send(body);
    } catch (error) {
      req.log.error({ url: url.toString(), error }, '[Dozzle][Image] poster proxy failed');
      return reply.code(502).send({ error: 'Image proxy failed' });
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
    if (typeof parsed.clientId === 'string' && typeof parsed.userId === 'string') {
      return {
        clientId: parsed.clientId,
        userId: parsed.userId,
        username: typeof parsed.username === 'string' ? parsed.username : undefined,
        platform: typeof parsed.platform === 'string' ? parsed.platform : undefined,
        unityVersion: typeof parsed.unityVersion === 'string' ? parsed.unityVersion : undefined,
        acquiredAt: typeof parsed.acquiredAt === 'string' ? parsed.acquiredAt : new Date().toISOString(),
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
  if (!value?.startsWith('Bearer ')) return null;
  return value.slice('Bearer '.length).trim();
}

function readSessionClientId(body: SessionLeaseBody) {
  const clientId = compact(body.clientId, 128);
  if (!clientId) return null;
  return /^[A-Za-z0-9_-]{16,128}$/.test(clientId) ? clientId : null;
}

async function authenticateLeaseRequest(req: FastifyRequest, reply: FastifyReply, deps: SessionLeaseDeps) {
  const token = readBearerToken(req);
  if (!token) {
    reply.code(401).send({ error: 'Missing Bearer token' });
    return null;
  }

  try {
    return await fetchNakamaAccount({
      nakamaHttp: deps.nakamaHttp,
      serverKey: deps.serverKey,
      sessionToken: token,
    });
  } catch (error) {
    req.log.warn({ error }, '[Dozzle][Session] lease auth failed');
    reply.code(401).send({ error: 'Invalid session token' });
    return null;
  }
}

async function reserveActiveSession(
  req: FastifyRequest,
  reply: FastifyReply,
  deps: SessionLeaseDeps,
  mode: 'acquire' | 'heartbeat'
) {
  const acct = await authenticateLeaseRequest(req, reply, deps);
  if (!acct) return null;

  const body = (req.body ?? {}) as SessionLeaseBody;
  const clientId = readSessionClientId(body);
  if (!clientId) {
    return reply.code(400).send({ error: 'clientId must be 16-128 URL-safe characters' });
  }

  const key = activeSessionKey(acct.userId);
  const existing = parseActiveSession(await deps.redis.get(key));
  if (existing && existing.clientId !== clientId) {
    req.log.warn({ userId: acct.userId, existingClientId: existing.clientId, requestedClientId: clientId }, '[Dozzle][Session] duplicate login blocked');
    return reply.code(409).send({ error: 'Account already logged in elsewhere' });
  }

  const lease: ActiveSessionLease = {
    clientId,
    userId: acct.userId,
    username: acct.username,
    platform: compact(body.platform, 80) ?? undefined,
    unityVersion: compact(body.unityVersion, 80) ?? undefined,
    acquiredAt: existing?.acquiredAt ?? new Date().toISOString(),
  };

  await deps.redis.set(key, JSON.stringify(lease), 'EX', ACTIVE_SESSION_TTL_SECONDS);
  req.log.info({ userId: acct.userId, mode, clientId, ttl: ACTIVE_SESSION_TTL_SECONDS }, '[Dozzle][Session] active session lease refreshed');
  return { ok: true, expiresInSeconds: ACTIVE_SESSION_TTL_SECONDS };
}

async function releaseActiveSession(req: FastifyRequest, reply: FastifyReply, deps: SessionLeaseDeps) {
  const acct = await authenticateLeaseRequest(req, reply, deps);
  if (!acct) return null;

  const body = (req.body ?? {}) as SessionLeaseBody;
  const clientId = readSessionClientId(body);
  if (!clientId) {
    return reply.code(400).send({ error: 'clientId must be 16-128 URL-safe characters' });
  }

  const key = activeSessionKey(acct.userId);
  const existing = parseActiveSession(await deps.redis.get(key));
  if (existing?.clientId === clientId) {
    await deps.redis.del(key);
    req.log.info({ userId: acct.userId, clientId }, '[Dozzle][Session] active session lease released');
  }

  return { ok: true };
}

function registerClientSessionLeases(app: ReturnType<typeof buildServer>, deps: SessionLeaseDeps) {
  app.post('/client/session/acquire', async (req, reply) => reserveActiveSession(req, reply, deps, 'acquire'));
  app.post('/client/session/heartbeat', async (req, reply) => reserveActiveSession(req, reply, deps, 'heartbeat'));
  app.post('/client/session/release', async (req, reply) => releaseActiveSession(req, reply, deps));
}

function registerAnimeProgressRoutes(app: ReturnType<typeof buildServer>, prisma: PrismaClient) {
  app.patch('/api/anime/:id/progress', async (req, reply) => {
    const userId = req.userId!;
    const params = req.params as { id: string };
    const body = (req.body ?? {}) as AnimeProgressPatchBody;
    const hasStatus = hasOwn(body, 'status');
    const hasScore = hasOwn(body, 'score');
    const hasEpisodesWatched = hasOwn(body, 'episodesWatched');

    if (!hasStatus && !hasScore && !hasEpisodesWatched) {
      return reply.code(400).send({ error: 'Request must include status, score, and/or episodesWatched' });
    }

    const anime = await prisma.anime.findUnique({
      where: { animeId: params.id },
      select: { animeId: true, episodes: true },
    });
    if (!anime) {
      return reply.code(404).send({ error: 'Anime not found' });
    }

    let requestedStatus: WatchStatus | null = null;
    if (hasStatus && body.status != null && body.status.trim().length > 0) {
      requestedStatus = normalizeWatchStatus(body.status);
      if (!requestedStatus) {
        return reply.code(400).send({ error: 'Invalid watch status' });
      }
    }

    let requestedScore: number | null | undefined;
    if (hasScore) {
      const score = readOptionalInteger(body.score);
      if (score === undefined || (score !== null && (score < 0 || score > 10))) {
        return reply.code(400).send({ error: 'score must be between 0 and 10' });
      }
      requestedScore = score == null || score === 0 ? null : score;
    }

    let requestedEpisodesWatched: number | undefined;
    if (hasEpisodesWatched) {
      const episodesWatched = readOptionalInteger(body.episodesWatched);
      if (episodesWatched == null || episodesWatched === undefined || episodesWatched < 0) {
        return reply.code(400).send({ error: 'episodesWatched must be a non-negative integer' });
      }

      requestedEpisodesWatched = anime.episodes != null && anime.episodes > 0
        ? Math.min(episodesWatched, anime.episodes)
        : episodesWatched;
    }

    const existingEntry = await prisma.watchEntry.findUnique({
      where: { userId_animeId: { userId, animeId: params.id } },
      select: { status: true, score: true, episodesWatched: true },
    });
    const nextEpisodesWatched = requestedEpisodesWatched ?? existingEntry?.episodesWatched ?? 0;
    const nextScore = requestedScore !== undefined ? requestedScore : existingEntry?.score ?? null;
    const nextStatus = requestedStatus ?? normalizeWatchStatus(existingEntry?.status) ?? (nextEpisodesWatched > 0 ? 'watching' : 'planned');

    const watchEntry = await prisma.watchEntry.upsert({
      where: { userId_animeId: { userId, animeId: params.id } },
      update: {
        status: nextStatus,
        score: nextScore,
        episodesWatched: nextEpisodesWatched,
        updatedAt: new Date(),
      },
      create: {
        userId,
        animeId: params.id,
        status: nextStatus,
        score: nextScore,
        episodesWatched: nextEpisodesWatched,
      },
    });

    req.log.info(
      { userId, animeId: params.id, status: watchEntry.status, score: watchEntry.score, episodesWatched: watchEntry.episodesWatched },
      '[Dozzle][DB] anime progress upsert'
    );
    return {
      id: watchEntry.animeId,
      isWatching: watchEntry.status === 'watching',
      watchStatus: watchEntry.status,
      score: watchEntry.score,
      episodesWatched: watchEntry.episodesWatched,
      lists: [watchEntry.status],
    };
  });
}

function registerPlayerStateRoutes(app: ReturnType<typeof buildServer>, prisma: PrismaClient) {
  app.get('/api/player/state', async (req) => {
    const userId = req.userId!;
    const user = await prisma.user.findUnique({
      where: { userId },
      select: {
        lastPositionX: true,
        lastPositionY: true,
        lastPositionZ: true,
        lastRotationY: true,
        lastPositionUpdatedAt: true,
      },
    });

    const hasPosition = user?.lastPositionX != null && user.lastPositionY != null && user.lastPositionZ != null;
    req.log.info({ userId, hasPosition }, '[Dozzle][DB] player position query');
    return {
      hasPosition,
      x: user?.lastPositionX ?? 0,
      y: user?.lastPositionY ?? 0,
      z: user?.lastPositionZ ?? 0,
      rotationY: user?.lastRotationY ?? 0,
      updatedAt: user?.lastPositionUpdatedAt?.toISOString() ?? null,
    };
  });

  app.patch('/api/player/state', async (req, reply) => {
    const userId = req.userId!;
    const body = (req.body ?? {}) as PlayerStatePatchBody;
    const x = readFiniteNumber(body.x);
    const y = readFiniteNumber(body.y);
    const z = readFiniteNumber(body.z);
    const rotationY = readFiniteNumber(body.rotationY);

    if (x == null || y == null || z == null || rotationY == null) {
      return reply.code(400).send({ error: 'x, y, z, and rotationY must be finite numbers' });
    }

    await ensureProgressionUser(prisma, userId, req.username);
    const updated = await prisma.user.update({
      where: { userId },
      data: {
        lastPositionX: x,
        lastPositionY: y,
        lastPositionZ: z,
        lastRotationY: rotationY,
        lastPositionUpdatedAt: new Date(),
      },
      select: {
        lastPositionX: true,
        lastPositionY: true,
        lastPositionZ: true,
        lastRotationY: true,
        lastPositionUpdatedAt: true,
      },
    });

    req.log.info({ userId, x, y, z, rotationY }, '[Dozzle][DB] player position saved');
    return {
      hasPosition: true,
      x: updated.lastPositionX ?? x,
      y: updated.lastPositionY ?? y,
      z: updated.lastPositionZ ?? z,
      rotationY: updated.lastRotationY ?? rotationY,
      updatedAt: updated.lastPositionUpdatedAt?.toISOString() ?? null,
    };
  });
}

function registerCharacterProgressionRoutes(app: ReturnType<typeof buildServer>, prisma: PrismaClient) {
  app.get('/api/characters', async (req) => {
    const userId = req.userId!;
    const user = await ensureProgressionUser(prisma, userId, req.username);
    req.log.info({ userId, level: user.level, selectedCharacterKey: user.selectedCharacterKey }, '[Dozzle][DB] character progression query');
    return buildCharacterResponse(user);
  });

  app.post('/api/characters/select', async (req, reply) => {
    const userId = req.userId!;
    const body = (req.body ?? {}) as CharacterSelectBody;
    const requestedKey = normalizeCharacterKey(body.characterKey);
    const definition = characterDefinition(requestedKey);
    const user = await ensureProgressionUser(prisma, userId, req.username);

    if (!user.unlockedCharacters.includes(definition.key)) {
      return reply.code(403).send({ error: 'Character is locked' });
    }

    const robotColor = definition.robotColor ?? compact(body.robotColor, 32) ?? user.robotColor ?? DEFAULT_ROBOT_COLOR;
    const updated = await prisma.user.update({
      where: { userId },
      data: {
        selectedCharacterKey: definition.key,
        robotColor,
      },
      select: {
        userId: true,
        displayName: true,
        experiencePoints: true,
        level: true,
        coins: true,
        unlockedCharacters: true,
        selectedCharacterKey: true,
        robotColor: true,
      },
    });

    req.log.info({ userId, characterKey: definition.key, robotColor }, '[Dozzle][DB] character selected');
    return buildCharacterResponse(updated);
  });

  app.post('/api/quests/:code/claim', async (req, reply) => {
    const userId = req.userId!;
    const params = req.params as { code: string };
    const quest = await prisma.quest.findUnique({ where: { code: params.code } });
    if (!quest) {
      return reply.code(404).send({ error: 'Quest not found' });
    }

    const requirements = readJsonRecord(quest.requirements);
    const rewards = readJsonRecord(quest.rewards);
    const stats = await loadQuestStats(prisma, userId);
    const progressPercent = calculateQuestProgress(requirements, stats);
    if (progressPercent < 1) {
      return reply.code(400).send({ error: 'Quest requirements are not complete', progressPercent, stats });
    }

    const existingQuest = await prisma.userQuest.findUnique({
      where: { userId_questId: { userId, questId: quest.questId } },
      select: { status: true },
    });

    const rewardXp = existingQuest?.status === 'completed' ? 0 : readNumber(rewards.xp);
    const rewardCoins = existingQuest?.status === 'completed' ? 0 : readNumber(rewards.coins);
    const rewardCharacter = existingQuest?.status === 'completed' ? null : readString(rewards.character);

    const updatedUser = await prisma.$transaction(async (tx) => {
      const user = await tx.user.upsert({
        where: { userId },
        update: { displayName: req.username ?? `player_${userId.slice(0, 6)}` },
        create: {
          userId,
          displayName: req.username ?? `player_${userId.slice(0, 6)}`,
          experiencePoints: 0,
          level: 1,
          coins: 0,
          unlockedCharacters: [DEFAULT_CHARACTER_KEY],
          selectedCharacterKey: DEFAULT_CHARACTER_KEY,
          robotColor: DEFAULT_ROBOT_COLOR,
        },
        select: {
          experiencePoints: true,
          coins: true,
          unlockedCharacters: true,
        },
      });

      const completedQuestRows = await tx.userQuest.findMany({
        where: { userId, status: 'completed' },
        include: { quest: { select: { rewards: true } } },
      });
      const completedRewardCharacters = completedQuestRows
        .map((row) => readString(readJsonRecord(row.quest.rewards).character))
        .filter((value): value is string => !!value && isKnownCharacterKey(value));

      const nextExperiencePoints = user.experiencePoints + rewardXp;
      const nextLevel = levelForExperience(nextExperiencePoints);
      const nextUnlockedCharacters = mergeUnlockedCharacters(user.unlockedCharacters, nextLevel, rewardCharacter, completedRewardCharacters);

      await tx.userQuest.upsert({
        where: { userId_questId: { userId, questId: quest.questId } },
        update: { status: 'completed', progress: stats, updatedAt: new Date() },
        create: { userId, questId: quest.questId, status: 'completed', progress: stats },
      });

      return tx.user.update({
        where: { userId },
        data: {
          experiencePoints: nextExperiencePoints,
          level: nextLevel,
          coins: user.coins + rewardCoins,
          unlockedCharacters: nextUnlockedCharacters,
        },
        select: {
          userId: true,
          displayName: true,
          experiencePoints: true,
          level: true,
          coins: true,
          unlockedCharacters: true,
          selectedCharacterKey: true,
          robotColor: true,
        },
      });
    });

    req.log.info(
      { userId, code: params.code, rewardXp, rewardCoins, rewardCharacter, level: updatedUser.level },
      '[Dozzle][DB] quest claimed and progression updated'
    );

    return {
      ok: true,
      alreadyCompleted: existingQuest?.status === 'completed',
      progressPercent,
      rewards: { xp: rewardXp, coins: rewardCoins, character: rewardCharacter },
      characterProgression: buildCharacterResponse(updatedUser),
    };
  });
}

async function main() {
  const prisma = new PrismaClient();
  const redis = new Redis(mustGet('REDIS_URL'));
  const nakamaHttp = mustGet('NAKAMA_HTTP');
  const nakamaServerKey = mustGet('NAKAMA_SERVER_KEY');

  await ensureAppSchema(prisma);
  await seedQuestCatalog(prisma);

  const ctx: AppContext = {
    prisma,
    redis,
    env: {
      PORT: Number.parseInt(process.env.PORT ?? '3000', 10),
      DATABASE_URL: mustGet('DATABASE_URL'),
      REDIS_URL: mustGet('REDIS_URL'),
      NAKAMA_HTTP: nakamaHttp,
      NAKAMA_SERVER_KEY: nakamaServerKey,
      MAL_CLIENT_ID: optional('MAL_CLIENT_ID'),
      MAL_CLIENT_SECRET: optional('MAL_CLIENT_SECRET'),
      MAL_REDIRECT_URI: optional('MAL_REDIRECT_URI'),
      MAL_TOKEN_ENCRYPTION_KEY: optional('MAL_TOKEN_ENCRYPTION_KEY'),
      MAL_SYNC_INTERVAL_MINUTES: Number.parseInt(process.env.MAL_SYNC_INTERVAL_MINUTES ?? '60', 10),
      MAL_CATALOG_SYNC_MAX_PAGES: optionalInt('MAL_CATALOG_SYNC_MAX_PAGES'),
      MAL_CATALOG_SYNC_ON_START: optionalBool('MAL_CATALOG_SYNC_ON_START', true),
      MAL_CATALOG_SYNC_REQUIRED: optionalBool('MAL_CATALOG_SYNC_REQUIRED', true),
    },
    catalogSync: {
      startupReady: false,
    },
  };
  ctx.catalogSync!.startupReady = !ctx.env.MAL_CLIENT_ID || !ctx.env.MAL_CATALOG_SYNC_ON_START;

  const app = buildServer(ctx);

  registerClientLogIntake(app);
  registerClientImageProxy(app);
  registerClientSessionLeases(app, {
    redis,
    nakamaHttp,
    serverKey: nakamaServerKey,
  });
  registerAnimeProgressRoutes(app, prisma);
  registerPlayerStateRoutes(app, prisma);
  registerCharacterProgressionRoutes(app, prisma);

  const port = Number.parseInt(process.env.PORT ?? '3000', 10);

  await app.listen({
    port,
    host: '0.0.0.0',
  });

  console.log(`API listening on http://0.0.0.0:${port}`);
  void runStartupCatalogSync(ctx);
}

main().catch((error) => {
  console.error('Failed to start API:', error);
  process.exit(1);
});
