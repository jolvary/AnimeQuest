# AnimeQuest <img src="https://i.gyazo.com/ddc8094729e58c6f727a7be14ab39de7.png" alt="Anime Data Visualization" width="50">

AnimeQuest is a Unity 3D anime hub where players explore a small city, talk to NPCs, browse a synchronized MyAnimeList catalog, track anime progress, compare matches with friends, and open anime details with streaming platform links and embedded trailers.

The project targets both WebGL and Android. WebGL is intended to be hosted through Cloudflare Workers plus R2, while the backend services can run in Docker and be exposed through Cloudflare Tunnels.

## Current Features

- 3D Unity city with player movement, camera control, NPC interaction, panel UI, and map.
- Main menu with login, account creation, and incognito mode.
- Incognito session on startup so users can browse the anime catalog before logging in.
- Panel wheel opened with `Tab` on desktop/WebGL or the top-left `Panels` button on Android.
- Anime catalog synced from MyAnimeList ranking data.
- User anime list import from MyAnimeList OAuth.
- Anime details panel with poster, synopsis, MAL score, trailer, and streaming provider shortcuts.
- Trailer IDs are persisted in the database once discovered.
- MAL score lookups are cached in Redis and local anime rows include `mal_score` for ordering/filtering.
- Genre NPCs can open anime lists filtered by MAL genre and ordered by MAL score.
- Starter quest NPC introduces the app and can show personalized MAL recommendations.
- Friends, chat, matching, character selection, quests, and multiplayer world presence through Nakama.
- City map opened with `M`, showing buildings, grass, roads, NPCs, the player, and friends.
- Development logs are written to container stdout so they can be inspected in Dozzle.

## Controls

### Desktop and WebGL

| Action | Control |
| --- | --- |
| Move | `WASD` |
| Camera | Mouse |
| Jump | `Space` |
| Sprint | `Shift` |
| Interact with NPC | `E` |
| Open panel wheel | `Tab` |
| Open map | `M` |

### Android

| Action | Control |
| --- | --- |
| Open panel wheel | Top-left `Panels` button |
| Move | Bottom-left virtual joystick |
| Camera | Drag on the right side of the screen |
| Jump | Bottom-right `J` button |
| Sprint | Bottom-right `S` button |
| Talk to NPC | Bottom-right `Talk` button |

The Android build is configured for horizontal orientation.

## Architecture

```text
Unity Client (WebGL / Android)
        |
        v
Node.js Fastify API
        |
        +--> PostgreSQL (game data, anime catalog, quests, progress)
        +--> Redis (MAL score cache, rate limiting, OAuth state)
        +--> Nakama (auth, friends, chat, multiplayer)
        +--> MyAnimeList API (catalog, user lists, suggestions)
        +--> Jikan API (trailer fallback lookup)
```

## Repository Layout

```text
AnimeQuest/
  Unity/                 Unity project and builds
  Server/api/            Fastify API, Prisma schema, MAL integration
  Infra/                 Docker Compose, Nakama config, PostgreSQL init
  Assets/                Root-level project assets
  ProjectSettings/       Unity project settings
  Packages/              Unity package manifest
```

Large generated files such as APKs, WebGL builds, ZIPs, Unity `Library`, `.wrangler`, and build debug folders should not be committed.

## Backend Services

The Docker stack lives in `Infra/docker-compose.yml`.

Services:

| Service | Purpose | Local Port |
| --- | --- | --- |
| `api` | Fastify REST API and Swagger docs | `3000` |
| `app-db` | PostgreSQL database for AnimeQuest data | `5432` |
| `redis` | Cache and rate limiting | `6379` |
| `nakama` | Auth, friends, chat, multiplayer | `7350`, `7351` |
| `nakama-db` | PostgreSQL database used by Nakama | `5433` |
| `dozzle` | Container log viewer | `8080` |

Run locally:

```powershell
cd Infra
docker compose up --build
```

## API Environment

The API requires these values:

```text
DATABASE_URL
REDIS_URL
NAKAMA_HTTP
NAKAMA_SERVER_KEY
```

MyAnimeList integration uses:

```text
MAL_CLIENT_ID
MAL_CLIENT_SECRET
MAL_REDIRECT_URI
MAL_TOKEN_ENCRYPTION_KEY
MAL_SYNC_INTERVAL_MINUTES
MAL_CATALOG_SYNC_MAX_PAGES
MAL_CATALOG_SYNC_ON_START
MAL_CATALOG_SYNC_REQUIRED
```

`MAL_CATALOG_SYNC_ON_START=true` starts the catalog sync when the API boots. `MAL_CATALOG_SYNC_REQUIRED=true` keeps `/health` unavailable until the initial sync is ready, which prevents the Unity client from starting against a partial catalog.

## Main API Endpoints

Swagger is the source of truth at `/docs`. Current important routes:

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/health` | API health and catalog sync status |
| `POST` | `/api/me/ensure` | Ensures the Nakama-authenticated player exists in the app DB |
| `GET` | `/api/anime` | Shared anime catalog, with user status overlay |
| `GET` | `/api/anime/genre/:genre` | Genre catalog for NPCs, ordered by MAL score |
| `GET` | `/api/anime/suggestions` | MyAnimeList personalized suggestions |
| `GET` | `/api/anime/:id/details` | Anime details, trailer hydration, provider data |
| `GET` | `/api/anime/user` | Current user's anime list |
| `GET` | `/api/anime/matches` | Anime overlap with other users |
| `PATCH` | `/api/anime/:id/watching` | Updates watch progress, status, and score |
| `PATCH` | `/api/anime/:id/lists` | Updates list membership/status |
| `GET` | `/api/mal/oauth/start` | Starts MAL OAuth |
| `GET` | `/api/mal/oauth/callback` | Completes MAL OAuth |
| `GET` | `/api/mal/oauth/status` | Checks MAL link status |
| `POST` | `/api/mal/oauth/refresh` | Refreshes MAL token |
| `POST` | `/api/mal/import` | Imports the linked MAL anime list |
| `GET` | `/api/quests` | Quest catalog and user quest state |
| `GET` | `/api/table/:name` | Internal/debug table preview |

Most app routes require a Nakama bearer token in `Authorization: Bearer <token>`. Incognito sessions can browse catalog data but cannot use friends, chat, personalized recommendations, or persistent account features.

## Data Model

The Prisma schema is in `Server/api/prisma/schema.prisma`.

Main tables:

- `users`: player profile, selected character, MAL OAuth tokens, last position.
- `anime`: local MAL-backed anime catalog with multilingual titles, genres, MAL score, poster URL, and trailer YouTube ID.
- `watch_entries`: user status, score, watched episodes, and progress per anime.
- `quests`: static quest definitions.
- `user_quests`: quest state per user.

The app database schema is created by `Infra/postgresql/init.sql`; runtime schema initialization should stay in infrastructure rather than hidden app boot logic.

## MyAnimeList Integration

AnimeQuest uses the official MAL API for:

- Global anime ranking catalog sync.
- User list import.
- Personalized suggestions through `/v2/anime/suggestions`.
- Anime details with multilingual title data.

Jikan is used only as a trailer fallback when MAL data does not already include a usable trailer ID.

Genre NPCs should use the local API route:

```text
GET /api/anime/genre/:genre?q=&limit=100&offset=0
```

This route searches title, English title, Japanese title, Spanish title, and synonyms. Results are ordered by `mal_score` descending, with missing scores last.

## Unity Setup Notes

Important scripts:

- `Unity/Assets/UnityTechnologies/Scripts/UIManager.cs`
- `Unity/Assets/UnityTechnologies/Scripts/GameBootstrap.cs`
- `Unity/Assets/UnityTechnologies/Scripts/ApiClient.cs`
- `Unity/Assets/UnityTechnologies/Scripts/NakamaAuthManager.cs`
- `Unity/Assets/UnityTechnologies/Scripts/AnimeCatalogPanelController.cs`
- `Unity/Assets/UnityTechnologies/Scripts/AnimeDetailPanelController.cs`
- `Unity/Assets/UnityTechnologies/Scripts/NPCQuestGiver.cs`
- `Unity/Assets/UnityTechnologies/Scripts/NpcAnimeCatalogInteractable.cs`
- `Unity/Assets/UnityTechnologies/Scripts/MapPanelController.cs`

NPC setup:

1. Put the NPC GameObject on the `NPC` layer.
2. Add or keep a collider for the mesh/body.
3. Add an interaction script such as `NPCQuestGiver` or `NpcAnimeCatalogInteractable`.
4. Set the interaction radius in the script inspector.
5. For map markers, add or keep `NpcMapMarker`.

The player interaction prompt is hidden while panels are open so it does not draw over UI.

## Cloudflare R2

R2 hosting notes:

- The `.unityweb` files are Brotli-compressed and must be served with `Content-Encoding: br`.
- `AnimeQuestBuild.framework.js.unityweb` should use `Content-Type: application/javascript`.
- `AnimeQuestBuild.wasm.unityweb` should use `Content-Type: application/wasm`.
- `AnimeQuestBuild.data.unityweb` can use `Content-Type: application/octet-stream`.
- The loader JS should use `Content-Type: application/javascript`.
- If Unity reports "Unable to parse ... .unityweb", check `Content-Encoding` first.

Set the R2 CORS policy with:

```powershell
npx wrangler r2 bucket cors set animequest --file Unity/AnimeQuestBuild/cors.json -y
```

The `cors.json` file must contain a top-level `rules` array.

## Android Build

For Android devices outside the local machine, configure public backend endpoints in the Unity inspectors:

- `ApiClient.androidPublicBaseUrl`: public HTTPS API URL.
- `NakamaAuthManager.androidPublicScheme`: normally `https`.
- `NakamaAuthManager.androidPublicHost`: public Nakama host.
- `NakamaAuthManager.androidPublicPort`: normally `443`.

This is needed because `localhost` inside an APK is the phone itself, not the development machine. For local emulator-only testing, the code can resolve localhost to `10.0.2.2`.

## Author

Alvaro Jimenez Ortiz
