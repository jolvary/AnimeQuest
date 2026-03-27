# AnimeQuest <img src="https://i.gyazo.com/ddc8094729e58c6f727a7be14ab39de7.png" alt="Anime Data Visualization" width="50">

**AnimeQuest** es un proyecto que explora el mundo del anime mediante la recopilación y análisis de datos.  
La plataforma recopila información de diferentes sitios web de anime y la transforma en visualizaciones que ayudan a comprender tendencias, géneros populares y hábitos de los fans.

El objetivo de AnimeQuest es combinar **anime y análisis de datos** para descubrir información interesante sobre la industria y los gustos de la comunidad.

---

## Funciones principales

1. **Recolección automática de datos**
   - El sistema utiliza herramientas de scraping para obtener información de páginas de anime como Anime-Planet.
   - Se recopilan datos como:
     - episodios
     - género
     - fecha de emisión
     - puntuación

2. **Procesamiento de datos**
   - Los datos recopilados se limpian y organizan para poder analizarlos correctamente.
   - Se preparan para ser utilizados en herramientas de análisis.

3. **Visualización de información**
   - Los datos se muestran en dashboards y gráficos.
   - Permiten observar tendencias como:
     - géneros más populares
     - países donde se ve más anime
     - evolución de producción de anime.

---

## Arcquitectura del proyecto
```
Unity Client (WebGL / Mobile)
        ↓
   Node.js API (REST + GraphQL)
        ↓
   PostgreSQL (Game Data)
        ↑
   Redis (Cache / Events)
        ↑
   Nakama (Auth, Multiplayer, Chat)
```
### Responsibilidades

| Layer        | Responsibility |
|-------------|--------------|
| Unity       | 3D world, UI, interaction, player input |
| Nakama      | Authentication, sessions, multiplayer, chat, friends |
| API         | Game logic, quests, anime catalog, DTOs |
| PostgreSQL  | Persistent data (users, anime, quests, progress) |
| Redis       | Caching, rate limiting, event queues |

---

## Integracion a futuro: MyAnimeList

Responsibilidades:
- Fetch anime data
- Import user lists
- Normalize into DB

Arquitectura:

```
Unity Client (WebGL / Mobile)
        ↓
   Node.js API (REST + GraphQL)
        ↓
   MyAnimelist API (REST)
        ↓
   PostgreSQL (Game Data)
        ↑
   Redis (Cache / Events)
        ↑
   Nakama (Auth, Multiplayer, Chat)
```


---

## Author


Álvaro Jiménez Ortiz
