# AnimeQuest

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

## Enlace de referencia

Repositorio del proyecto utilizado como inspiración:

https://github.com/jolvary/AnimeQuest/

---

## Concepto del proyecto

<img src="https://i.gyazo.com/ddc8094729e58c6f727a7be14ab39de7.png" alt="Anime Data Visualization" width="50">

AnimeQuest utiliza análisis de datos para explorar tendencias del anime y ofrecer información visual sobre la industria y la comunidad.



---

## Ejemplo de código

El siguiente ejemplo muestra un fragmento simplificado de un **script en javascript para obtener información de anime** usando scraping.

```js
const CLIENT_ID = process.env.MAL_CLIENT_ID;

async function top3Season(year, season) {
  const url = `https://api.myanimelist.net/v2/anime/season/${year}/${season}?sort=anime_score&limit=3&fields=title,mean`;

  const res = await fetch(url, {
    headers: { "X-MAL-CLIENT-ID": CLIENT_ID },
  });

  const data = await res.json();
  return data.data.map((x) => x.node);
}

top3Season(2024, "spring").then(console.log).catch(console.error);
```
## Conclusión

AnimeQuest demuestra cómo el análisis de datos puede utilizarse para estudiar la industria del anime y comprender mejor las preferencias de los fans.  
Al combinar scraping, procesamiento de datos y visualización, el proyecto ofrece una nueva forma de explorar el universo del anime.

---
<sub>Autor: Álvaro Jiménez Ortiz</sub>