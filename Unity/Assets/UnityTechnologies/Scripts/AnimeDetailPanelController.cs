using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AnimeDetailPanelController : MonoBehaviour
{
    private static readonly Color RootColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color HeaderColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color SurfaceColor = new Color(1f, 1f, 1f, 0f);
    private static readonly Color TextColor = new Color(0.17f, 0.10f, 0.04f, 1f);
    private static readonly Color MutedTextColor = new Color(0.34f, 0.25f, 0.15f, 1f);
    private static readonly Color AccentColor = new Color(0.48f, 0.28f, 0.12f, 0.94f);
    private static readonly Color PosterFallbackColor = new Color(0.87f, 0.82f, 0.72f, 1f);

    private static readonly PlatformDefinition[] Platforms =
    {
        new PlatformDefinition { id = "netflix", name = "Netflix", iconUrl = "https://www.google.com/s2/favicons?domain=netflix.com&sz=128", color = new Color(0.88f, 0.04f, 0.05f, 1f) },
        new PlatformDefinition { id = "prime", name = "Prime Video", iconUrl = "https://www.google.com/s2/favicons?domain=primevideo.com&sz=128", color = new Color(0.05f, 0.35f, 0.64f, 1f) },
        new PlatformDefinition { id = "disney", name = "Disney+", iconUrl = "https://www.google.com/s2/favicons?domain=disneyplus.com&sz=128", color = new Color(0.05f, 0.16f, 0.48f, 1f) },
        new PlatformDefinition { id = "hulu", name = "Hulu", iconUrl = "https://www.google.com/s2/favicons?domain=hulu.com&sz=128", color = new Color(0.09f, 0.67f, 0.32f, 1f) },
        new PlatformDefinition { id = "max", name = "Max", iconUrl = "https://www.google.com/s2/favicons?domain=max.com&sz=128", color = new Color(0.06f, 0.20f, 0.82f, 1f) },
        new PlatformDefinition { id = "hidive", name = "HIDIVE", iconUrl = "https://www.google.com/s2/favicons?domain=hidive.com&sz=128", color = new Color(0.02f, 0.49f, 0.74f, 1f) },
    };

    private static readonly AvailabilityRule[] AvailabilityRules =
    {
        new AvailabilityRule { match = "naruto", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "one piece", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "demon slayer", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "attack on titan", platformIds = "hulu,prime" },
        new AvailabilityRule { match = "jujutsu kaisen", platformIds = "hulu" },
        new AvailabilityRule { match = "death note", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "fullmetal alchemist", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "my hero academia", platformIds = "hulu" },
        new AvailabilityRule { match = "spy x family", platformIds = "hulu" },
        new AvailabilityRule { match = "dragon ball", platformIds = "hulu" },
        new AvailabilityRule { match = "cowboy bebop", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "pokemon", platformIds = "netflix" },
        new AvailabilityRule { match = "vinland saga", platformIds = "netflix,prime" },
        new AvailabilityRule { match = "evangelion", platformIds = "netflix" },
        new AvailabilityRule { match = "sailor moon", platformIds = "hulu" },
        new AvailabilityRule { match = "bleach", platformIds = "hulu,disney" },
        new AvailabilityRule { match = "chainsaw man", platformIds = "hulu" },
        new AvailabilityRule { match = "hunter x hunter", platformIds = "netflix,hulu" },
        new AvailabilityRule { match = "akira", platformIds = "hulu" },
        new AvailabilityRule { match = "your name", platformIds = "prime" },
    };

    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void AnimeQuest_ShowYouTubeTrailer(string youtubeId);
#endif

    public Font preferredFont;

    private GameObject _root;
    private Image _posterImage;
    private Image _trailerImage;
    private Text _titleText;
    private Text _subtitleText;
    private Text _scoreText;
    private Text _rankText;
    private Text _episodesText;
    private Text _statusText;
    private Text _synopsisText;
    private Text _platformStatusText;
    private Text _trailerLabelText;
    private Text _syncStatusText;
    private RectTransform _platformContent;
    private AnimeDetailItem _currentAnime;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public void OpenAnime(AnimeDetailItem item)
    {
        if (item == null) return;

        EnsureUi();
        _currentAnime = item;
        Render(item);

        if (!string.IsNullOrWhiteSpace(item.id) && ApiClient.Instance != null)
        {
            RefreshDetails(item.id);
        }
    }

    private async void RefreshDetails(string expectedId)
    {
        SetSyncStatus("Loading trailer...");

        try
        {
            string json = await ApiClient.Instance.GetAnimeDetails(expectedId);
            var item = JsonUtility.FromJson<AnimeDetailItem>(json);
            if (item == null || _currentAnime == null || item.id != _currentAnime.id) return;

            _currentAnime = item;
            Render(item);
            SetSyncStatus(string.IsNullOrWhiteSpace(item.trailerYoutubeId) ? "Trailer unavailable." : string.Empty);
        }
        catch (Exception ex)
        {
            SetSyncStatus("Trailer unavailable.");
            DozzleLogger.Error("Anime detail load failed", ex);
        }
    }

    private void EnsureUi()
    {
        if (_root != null) return;

        _root = new GameObject("AnimeDetailRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _root.transform.SetParent(transform, false);
        _root.transform.SetAsFirstSibling();
        Stretch(_root.GetComponent<RectTransform>(), new Vector2(54f, 54f), new Vector2(-54f, -54f));
        _root.GetComponent<Image>().color = RootColor;

        CreateHeader();
        CreatePosterColumn();
        CreateDetailSummary();
        CreateTrailerCard();
        CreatePlatformSection();
        CreateSynopsisSection();

        ApplyFonts();
    }

    private void CreateHeader()
    {
        var header = CreateSurface("Header", _root.transform, HeaderColor);
        StretchTop(header.GetComponent<RectTransform>(), 0f, 0f, 74f);

        _titleText = CreateAnchoredText(
            header.transform,
            "Title",
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(24f, 4f),
            new Vector2(-72f, 42f),
            28,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            TextColor
        );

        _subtitleText = CreateAnchoredText(
            header.transform,
            "Subtitle",
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(24f, -30f),
            new Vector2(-72f, 0f),
            17,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            MutedTextColor
        );
    }

    private void CreatePosterColumn()
    {
        var posterFrame = CreateSurface("PosterFrame", _root.transform, SurfaceColor);
        SetTopLeft(posterFrame.GetComponent<RectTransform>(), 20f, -100f, 250f, 390f);

        var posterObj = new GameObject("Poster", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        posterObj.transform.SetParent(posterFrame.transform, false);
        Stretch(posterObj.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -12f));
        _posterImage = posterObj.GetComponent<Image>();
        _posterImage.color = PosterFallbackColor;
        _posterImage.preserveAspect = true;

    }

    private void CreateDetailSummary()
    {
        var summary = CreateSurface("Summary", _root.transform, SurfaceColor);
        StretchTop(summary.GetComponent<RectTransform>(), 292f, -356f, 126f, -100f);

        _scoreText = CreateAnchoredText(summary.transform, "Score", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(20f, 24f), new Vector2(128f, -20f), 19, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor);
        _rankText = CreateAnchoredText(summary.transform, "Status", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(152f, 64f), new Vector2(-24f, -18f), 20, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor);
        _episodesText = CreateAnchoredText(summary.transform, "Episodes", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(152f, 28f), new Vector2(-24f, -58f), 15, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor);
        _statusText = CreateAnchoredText(summary.transform, "ListStatus", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(152f, 4f), new Vector2(-24f, -88f), 15, FontStyle.Italic, TextAnchor.MiddleLeft, MutedTextColor);
    }

    private void CreateTrailerCard()
    {
        var trailer = CreateSurface("TrailerCard", _root.transform, SurfaceColor);
        SetTopRight(trailer.GetComponent<RectTransform>(), -20f, -100f, 308f, 224f);

        var label = CreateAnchoredText(trailer.transform, "TrailerTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -36f), new Vector2(-12f, -8f), 20, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor);
        label.text = "Trailer";

        var imageObj = new GameObject("TrailerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        imageObj.transform.SetParent(trailer.transform, false);
        Stretch(imageObj.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -48f));
        _trailerImage = imageObj.GetComponent<Image>();
        _trailerImage.color = PosterFallbackColor;
        _trailerImage.preserveAspect = true;
        imageObj.GetComponent<Button>().onClick.AddListener(OpenTrailer);

        var playBadge = CreateSurface("PlayBadge", imageObj.transform, new Color(0f, 0f, 0f, 0.64f));
        var badgeRect = playBadge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.anchoredPosition = Vector2.zero;
        badgeRect.sizeDelta = new Vector2(112f, 42f);

        _trailerLabelText = CreateAnchoredText(playBadge.transform, "PlayLabel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
    }

    private void CreatePlatformSection()
    {
        var platforms = CreateSurface("Platforms", _root.transform, SurfaceColor);
        SetTopRight(platforms.GetComponent<RectTransform>(), -20f, -340f, 308f, 134f);

        CreateAnchoredText(platforms.transform, "AvailableIn", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -32f), new Vector2(-12f, -4f), 18, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor).text = "Available in:";
        _platformStatusText = CreateAnchoredText(platforms.transform, "PlatformStatus", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 4f), new Vector2(-12f, 28f), 13, FontStyle.Italic, TextAnchor.MiddleCenter, MutedTextColor);

        var scrollObj = new GameObject("PlatformScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollObj.transform.SetParent(platforms.transform, false);
        Stretch(scrollObj.GetComponent<RectTransform>(), new Vector2(12f, 30f), new Vector2(-12f, -40f));

        var viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportObj.transform.SetParent(scrollObj.transform, false);
        Stretch(viewportObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        viewportObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        var contentObj = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);
        _platformContent = contentObj.GetComponent<RectTransform>();
        _platformContent.anchorMin = new Vector2(0f, 0f);
        _platformContent.anchorMax = new Vector2(0f, 1f);
        _platformContent.pivot = new Vector2(0f, 0.5f);
        _platformContent.offsetMin = Vector2.zero;
        _platformContent.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scroll = scrollObj.GetComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.viewport = viewportObj.GetComponent<RectTransform>();
        scroll.content = _platformContent;
    }

    private void CreateSynopsisSection()
    {
        var synopsis = CreateSurface("Synopsis", _root.transform, SurfaceColor);
        Stretch(synopsis.GetComponent<RectTransform>(), new Vector2(292f, 28f), new Vector2(-356f, -246f));

        CreateAnchoredText(synopsis.transform, "SynopsisTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -42f), new Vector2(-18f, -8f), 22, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor).text = "Synopsis";

        var line = CreateSurface("SynopsisLine", synopsis.transform, AccentColor);
        StretchTop(line.GetComponent<RectTransform>(), 18f, -18f, 2f, -48f);

        var scrollObj = new GameObject("SynopsisScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollObj.transform.SetParent(synopsis.transform, false);
        Stretch(scrollObj.GetComponent<RectTransform>(), new Vector2(18f, 18f), new Vector2(-18f, -58f));

        var viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportObj.transform.SetParent(scrollObj.transform, false);
        Stretch(viewportObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        viewportObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        var contentObj = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);
        var contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        _synopsisText = CreateAnchoredText(contentObj.transform, "SynopsisText", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -400f), new Vector2(0f, 0f), 17, FontStyle.Normal, TextAnchor.UpperLeft, TextColor);
        _synopsisText.verticalOverflow = VerticalWrapMode.Overflow;

        contentObj.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollObj.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.viewport = viewportObj.GetComponent<RectTransform>();
        scroll.content = contentRect;

        _syncStatusText = CreateAnchoredText(_root.transform, "SyncStatus", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(292f, 4f), new Vector2(-20f, 26f), 13, FontStyle.Italic, TextAnchor.MiddleRight, MutedTextColor);
    }

    private void Render(AnimeDetailItem item)
    {
        _titleText.text = Safe(item.title, "Anime");
        _subtitleText.text = BuildSubtitle(item);
        _scoreText.text = $"MAL Score: {FormatMalScore(item.malScore)}";
        _rankText.text = "Anime details";
        _episodesText.text = $"Episodes: {FormatWatchedEpisodes(item.episodesWatched, item.episodes)}  |  Release: {Safe(item.releaseDate, "?")}  |  Genres: {FormatGenres(item.genres)}";
        _statusText.text = $"Your list: {FormatStatus(item.watchStatus)}";
        _synopsisText.text = Safe(item.description, "No synopsis available yet.");

        SetImage(_posterImage, BuildPosterUrl(item), PosterFallbackColor);
        RenderTrailer(item);
        RenderPlatforms(item);
        ApplyFonts();
    }

    private void RenderTrailer(AnimeDetailItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.trailerYoutubeId))
        {
            _trailerLabelText.text = "Play";
            SetImage(_trailerImage, BuildTrailerThumbnailUrl(item.trailerYoutubeId), PosterFallbackColor);
            return;
        }

        _trailerLabelText.text = "Search";
        _trailerImage.sprite = null;
        _trailerImage.color = PosterFallbackColor;
    }

    private void RenderPlatforms(AnimeDetailItem item)
    {
        ClearChildren(_platformContent);
        var platforms = ResolvePlatforms(item);
        _platformStatusText.text = platforms.Count == 0 ? "Not available on streaming" : string.Empty;

        foreach (var platform in platforms)
        {
            CreatePlatformCard(_platformContent, platform, item.title);
        }
    }

    private void CreatePlatformCard(Transform parent, PlatformDefinition platform, string title)
    {
        var cardObj = new GameObject($"Platform_{platform.id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        cardObj.transform.SetParent(parent, false);
        cardObj.GetComponent<Image>().color = platform.color;

        var layout = cardObj.GetComponent<LayoutElement>();
        layout.minWidth = 78f;
        layout.preferredWidth = 78f;
        layout.minHeight = 64f;
        layout.preferredHeight = 64f;
        layout.flexibleWidth = 0f;

        var vertical = cardObj.GetComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(7, 7, 5, 5);
        vertical.spacing = 3f;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = false;
        vertical.childForceExpandHeight = false;

        cardObj.GetComponent<Button>().onClick.AddListener(() => Application.OpenURL(BuildPlatformUrl(platform.id, title)));

        var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        iconObj.transform.SetParent(cardObj.transform, false);
        var iconLayout = iconObj.GetComponent<LayoutElement>();
        iconLayout.minWidth = 30f;
        iconLayout.preferredWidth = 30f;
        iconLayout.minHeight = 30f;
        iconLayout.preferredHeight = 30f;
        var icon = iconObj.GetComponent<Image>();
        icon.color = Color.white;
        icon.preserveAspect = true;
        SetImage(icon, platform.iconUrl, Color.white);

        var label = CreateLayoutText(cardObj.transform, platform.name, 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 22f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private void OpenTrailer()
    {
        if (_currentAnime == null) return;
        if (!string.IsNullOrWhiteSpace(_currentAnime.trailerYoutubeId))
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AnimeQuest_ShowYouTubeTrailer(_currentAnime.trailerYoutubeId);
            return;
#endif
        }

        Application.OpenURL(BuildTrailerUrl(_currentAnime));
    }

    private void SetSyncStatus(string value)
    {
        if (_syncStatusText == null) return;
        _syncStatusText.text = value ?? string.Empty;
    }

    private static string BuildSubtitle(AnimeDetailItem item)
    {
        return $"{FormatGenres(item.genres)} | {Safe(item.releaseDate, "?")} | {FormatEpisodes(item.episodes)} episodes";
    }

    private static string BuildPosterUrl(AnimeDetailItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.databaseImageUrl)) return item.databaseImageUrl;
        if (!string.IsNullOrWhiteSpace(item.imageUrl)) return item.imageUrl;
        if (!string.IsNullOrWhiteSpace(item.trailerYoutubeId)) return BuildTrailerThumbnailUrl(item.trailerYoutubeId);
        return string.Empty;
    }

    private static string BuildTrailerThumbnailUrl(string youtubeId)
    {
        return $"https://img.youtube.com/vi/{youtubeId}/hqdefault.jpg";
    }

    private static string BuildTrailerUrl(AnimeDetailItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.trailerYoutubeId))
        {
            return $"https://www.youtube.com/watch?v={UnityWebRequest.EscapeURL(item.trailerYoutubeId)}";
        }

        return $"https://www.youtube.com/results?search_query={UnityWebRequest.EscapeURL(Safe(item.title, "anime") + " trailer")}";
    }

    private static string BuildPlatformUrl(string platformId, string title)
    {
        string query = UnityWebRequest.EscapeURL(Safe(title, "anime"));
        switch (platformId)
        {
            case "netflix":
                return $"https://www.netflix.com/search?q={query}";
            case "prime":
                return $"https://www.primevideo.com/search/ref=atv_nb_sr?phrase={query}";
            case "disney":
                return "https://www.disneyplus.com/search";
            case "hulu":
                return $"https://www.hulu.com/search?q={query}";
            case "max":
                return $"https://www.max.com/search?q={query}";
            case "hidive":
                return $"https://www.hidive.com/search?q={query}";
            default:
                return $"https://www.google.com/search?q={query}";
        }
    }

    private List<PlatformDefinition> ResolvePlatforms(AnimeDetailItem item)
    {
        if (item == null) return new List<PlatformDefinition>();

        string csv = !string.IsNullOrWhiteSpace(item.streamingPlatforms)
            ? item.streamingPlatforms
            : ResolveKnownPlatformCsv(item.title);

        var list = new List<PlatformDefinition>();
        if (string.IsNullOrWhiteSpace(csv)) return list;

        string[] ids = csv.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawId in ids)
        {
            var platform = FindPlatform(rawId);
            if (platform != null && !ContainsPlatform(list, platform.id))
            {
                list.Add(platform);
            }
        }

        return list;
    }

    private static string ResolveKnownPlatformCsv(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        string normalizedTitle = title.Trim().ToLowerInvariant();
        foreach (var rule in AvailabilityRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.match) && normalizedTitle.Contains(rule.match))
            {
                return rule.platformIds;
            }
        }

        return string.Empty;
    }

    private static PlatformDefinition FindPlatform(string id)
    {
        string normalized = NormalizePlatformId(id);
        foreach (var platform in Platforms)
        {
            if (string.Equals(platform.id, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(platform.name, id, StringComparison.OrdinalIgnoreCase))
            {
                return platform;
            }
        }

        return null;
    }

    private static string NormalizePlatformId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        string normalized = id.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("+", string.Empty);
        if (normalized == "amazon" || normalized == "amazonprime" || normalized == "primevideo") return "prime";
        if (normalized == "disneyplus") return "disney";
        if (normalized == "hbomax") return "max";
        return normalized;
    }

    private static bool ContainsPlatform(List<PlatformDefinition> platforms, string id)
    {
        foreach (var platform in platforms)
        {
            if (string.Equals(platform.id, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SetImage(Image target, string url, Color fallbackColor)
    {
        if (target == null) return;

        target.sprite = null;
        target.color = fallbackColor;
        if (string.IsNullOrWhiteSpace(url)) return;

        string requestUrl = BuildImageRequestUrl(url);
        if (SpriteCache.TryGetValue(requestUrl, out var cached) && cached != null)
        {
            target.sprite = cached;
            target.color = Color.white;
            return;
        }

        if (isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            StartCoroutine(LoadSprite(url, target));
        }
    }

    private IEnumerator LoadSprite(string url, Image target)
    {
        yield return TryLoadSprite(url, target);
        if (target == null || target.sprite != null) yield break;

        string fallbackUrl = BuildPosterFallbackUrl(url);
        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            yield return TryLoadSprite(fallbackUrl, target);
        }
    }

    private IEnumerator TryLoadSprite(string url, Image target)
    {
        string requestUrl = BuildImageRequestUrl(url);
        using (var req = UnityWebRequestTexture.GetTexture(requestUrl))
        {
            yield return req.SendWebRequest();
            if (target == null || req.result != UnityWebRequest.Result.Success) yield break;

            Texture2D texture = null;
            try
            {
                texture = DownloadHandlerTexture.GetContent(req);
            }
            catch
            {
                texture = null;
            }

            if (texture == null) yield break;

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            SpriteCache[requestUrl] = sprite;
            target.sprite = sprite;
            target.color = Color.white;
        }
    }

    private static string BuildPosterFallbackUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string webpExtension = ".webp";
        int index = url.IndexOf(webpExtension, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        return url.Substring(0, index) + ".jpg" + url.Substring(index + webpExtension.Length);
    }

    private static string BuildImageRequestUrl(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (ApiClient.Instance == null) return url;
        return ApiClient.Instance.BuildImageProxyUrl(url);
#else
        return url;
#endif
    }

    private GameObject CreateSurface(string name, Transform parent, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = color;
        return obj;
    }

    private Text CreateAnchoredText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        var textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(parent, false);

        var rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var text = textObj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private Text CreateLayoutText(Transform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color, float height)
    {
        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        textObj.transform.SetParent(parent, false);

        var layout = textObj.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;

        var text = textObj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private void ApplyFonts()
    {
        Font font = ResolveFont();
        if (font == null) return;

        foreach (var label in GetComponentsInChildren<Text>(true))
        {
            label.font = font;
        }
    }

    private Font ResolveFont()
    {
        if (preferredFont != null) return preferredFont;

        try
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch
        {
            return null;
        }
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void StretchTop(RectTransform rect, float left, float right, float height, float top = 0f)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(left, top - height);
        rect.offsetMax = new Vector2(right, top);
    }

    private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetTopRight(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static string Safe(string value, string fallback = "-")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatMalScore(float score)
    {
        return score <= 0f ? "-" : score.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatEpisodes(int episodes)
    {
        return episodes <= 0 ? "?" : episodes.ToString();
    }

    private static string FormatWatchedEpisodes(int watched, int total)
    {
        string watchedText = watched <= 0 ? "0" : watched.ToString();
        return total > 0 ? $"{watchedText}/{total}" : watchedText;
    }

    private static string FormatGenres(string[] genres)
    {
        if (genres == null || genres.Length == 0) return "Unknown genres";
        return string.Join(", ", genres);
    }

    private static string FormatStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Not in your list";
        string normalized = status.Trim().Replace("_", " ");
        return char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
    }

    [Serializable]
    public class AnimeDetailItem
    {
        public string id;
        public string title;
        public string briefDescription;
        public string description;
        public string imageUrl;
        public string databaseImageUrl;
        public int episodes;
        public string releaseDate;
        public bool isWatching;
        public string watchStatus;
        public int score;
        public int episodesWatched;
        public string[] lists;
        public string[] genres;
        public string trailerYoutubeId;
        public float malScore;
        public string provider;
        public string providerId;
        public string streamingPlatforms;
    }

    private class PlatformDefinition
    {
        public string id;
        public string name;
        public string iconUrl;
        public Color color;
    }

    private class AvailabilityRule
    {
        public string match;
        public string platformIds;
    }
}
