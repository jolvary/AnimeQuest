using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AnimeCatalogPanelController : MonoBehaviour
{
    private static readonly string[] StatusLabels = { "Not in your list", "Watching", "Completed", "Planned", "Dropped", "On Hold" };
    private static readonly string[] StatusValues = { "", "watching", "completed", "planned", "dropped", "on_hold" };
    private static readonly Color DeckSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.22f);
    private static readonly Color DeckInputColor = new Color(0.96f, 0.90f, 0.78f, 0.36f);
    private const int MaxPosterFailureLogs = 10;
    private const int UnknownEpisodeCap = 9999;
    private const float DeckCardHeight = 218f;
    private static int PosterFailureLogCount;

    public string defaultSearch = "";
    public int defaultLimit = 100;
    public bool userCatalogOnly;
    public Font preferredFont;

    private Text _descriptionText;
    private InputField _searchInput;
    private RectTransform _searchBar;
    private RectTransform _pagingBar;
    private Text _pageText;
    private Button _previousPageButton;
    private Button _nextPageButton;
    private Text _statusText;
    private ScrollRect _deckScrollRect;
    private RectTransform _deckContent;
    private bool _isIncognitoMode;
    private int _currentOffset;
    private bool _hasNextPage;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public void SetIncognitoMode(bool enabled)
    {
        _isIncognitoMode = enabled;
        if (_descriptionText != null)
        {
            _descriptionText.text = BuildDescriptionText();
        }

        if (_deckContent != null)
        {
            RenderActionVisibility();
        }
    }

    public async void RefreshCatalog()
    {
        EnsureDeckElements();
        int pageSize = PageSize();
        _descriptionText.text = BuildDescriptionText();
        if (_searchInput != null && !_searchInput.isFocused)
        {
            _searchInput.text = defaultSearch ?? string.Empty;
        }
        _statusText.text = userCatalogOnly ? "Loading your anime catalog..." : "Loading MyAnimeList catalog...";

        try
        {
            string json = userCatalogOnly
                ? await ApiClient.Instance.GetUserAnime(defaultSearch, pageSize, _currentOffset)
                : await ApiClient.Instance.GetAnime(defaultSearch, pageSize, _currentOffset);
            var response = JsonUtility.FromJson<AnimeDeckResponse>(json);
            _hasNextPage = response != null && response.hasMore;
            UpdatePaginationControls();

            if (response == null || response.items == null || response.items.Length == 0)
            {
                _statusText.text = _currentOffset > 0
                    ? "No anime found on this page."
                    : userCatalogOnly
                        ? "No user anime yet. Link and import MyAnimeList first."
                        : "No anime found.";
                ClearCards();
                return;
            }

            RenderDeck(response.items);
            RenderActionVisibility();
            int firstItem = _currentOffset + 1;
            int lastItem = _currentOffset + response.items.Length;
            _statusText.text = userCatalogOnly
                ? $"Showing {firstItem}-{lastItem} from your catalog."
                : $"Showing {firstItem}-{lastItem} MyAnimeList catalog entries.";
            ResetScrollToTop();
        }
        catch (Exception ex)
        {
            _statusText.text = userCatalogOnly ? "Failed to load your anime catalog." : "Failed to load MyAnimeList catalog.";
            _hasNextPage = false;
            UpdatePaginationControls();
            ClearCards();
            DozzleLogger.Error(userCatalogOnly ? "Failed to load user anime catalog" : "Failed to load anime catalog", ex);
        }
    }

    private string BuildDescriptionText()
    {
        if (_isIncognitoMode)
        {
            return userCatalogOnly
                ? "Your Anime Catalog (Incognito): personal list actions are hidden."
                : "MyAnimeList Catalog (Incognito): browse entries only. Personal anime list actions are hidden.";
        }

        return userCatalogOnly
            ? "Your Anime Catalog: anime imported or updated for this account."
            : "MyAnimeList Catalog: all synced MAL titles with your status overlaid.";
    }

    private void EnsureDeckElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement(
                "AnimeDescription",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(74f, -122f),
                new Vector2(-60f, -66f),
                20,
                FontStyle.Bold
            );
        }

        if (_searchBar == null || _searchInput == null)
        {
            CreateSearchBar();
        }

        if (_statusText == null)
        {
            _statusText = CreateTextElement(
                "AnimeStatus",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(74f, -214f),
                new Vector2(-372f, -178f),
                16,
                FontStyle.Normal
            );
        }

        if (_pagingBar == null || _pageText == null)
        {
            CreatePagingBar();
        }

        if (_deckScrollRect == null || _deckContent == null)
        {
            CreateDeckContainer();
        }

        ApplyFonts();
        UpdatePaginationControls();
    }

    private void CreateSearchBar()
    {
        var searchObj = new GameObject("AnimeSearchBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        searchObj.transform.SetParent(transform, false);

        _searchBar = searchObj.GetComponent<RectTransform>();
        _searchBar.anchorMin = new Vector2(0f, 1f);
        _searchBar.anchorMax = new Vector2(1f, 1f);
        _searchBar.offsetMin = new Vector2(84f, -168f);
        _searchBar.offsetMax = new Vector2(-96f, -146f);

        var layout = searchObj.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        _searchInput = CreateSearchInput(searchObj.transform);
        CreateSearchButton(searchObj.transform, "Search", () =>
        {
            defaultSearch = _searchInput != null ? _searchInput.text.Trim() : string.Empty;
            _currentOffset = 0;
            RefreshCatalog();
        });
        CreateSearchButton(searchObj.transform, "Clear", () =>
        {
            defaultSearch = string.Empty;
            _currentOffset = 0;
            if (_searchInput != null) _searchInput.text = string.Empty;
            RefreshCatalog();
        });
    }

    private InputField CreateSearchInput(Transform parent)
    {
        var inputObj = new GameObject("AnimeSearchInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField), typeof(LayoutElement));
        inputObj.transform.SetParent(parent, false);

        var layout = inputObj.GetComponent<LayoutElement>();
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
        layout.flexibleWidth = 1f;

        var image = inputObj.GetComponent<Image>();
        image.color = DeckInputColor;

        var input = inputObj.GetComponent<InputField>();
        input.text = defaultSearch ?? string.Empty;
        input.lineType = InputField.LineType.SingleLine;

        var text = CreateInputText(inputObj.transform, "Text", string.Empty, Color.black, FontStyle.Normal);
        var placeholder = CreateInputText(inputObj.transform, "Placeholder", "Search anime...", new Color(0.40f, 0.32f, 0.22f, 0.75f), FontStyle.Italic);
        input.textComponent = text;
        input.placeholder = placeholder;

        return input;
    }

    private Text CreateInputText(Transform parent, string name, string value, Color color, FontStyle style)
    {
        var textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(parent, false);

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);

        var text = textObj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = 15;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        return text;
    }

    private void CreateSearchButton(Transform parent, string label, Action onClick)
    {
        var buttonObj = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);

        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 82f;
        layout.preferredWidth = 82f;
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
        layout.flexibleWidth = 0f;

        var image = buttonObj.GetComponent<Image>();
        image.color = new Color(0.42f, 0.27f, 0.14f, 0.95f);

        var button = buttonObj.GetComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textObj.GetComponent<Text>();
        text.text = label;
        text.font = ResolveFont();
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private void CreatePagingBar()
    {
        var pagingObj = new GameObject("AnimePagingBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        pagingObj.transform.SetParent(transform, false);

        _pagingBar = pagingObj.GetComponent<RectTransform>();
        _pagingBar.anchorMin = new Vector2(1f, 1f);
        _pagingBar.anchorMax = new Vector2(1f, 1f);
        _pagingBar.offsetMin = new Vector2(-432f, -218f);
        _pagingBar.offsetMax = new Vector2(-96f, -180f);

        var layout = pagingObj.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        _previousPageButton = CreatePagingButton(pagingObj.transform, "Prev", GoToPreviousPage);
        _pageText = CreatePagingLabel(pagingObj.transform);
        _nextPageButton = CreatePagingButton(pagingObj.transform, "Next", GoToNextPage);
    }

    private Button CreatePagingButton(Transform parent, string label, Action onClick)
    {
        var buttonObj = new GameObject($"Btn_{label}Page", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);

        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 72f;
        layout.preferredWidth = 72f;
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;
        layout.flexibleWidth = 0f;

        var image = buttonObj.GetComponent<Image>();
        image.color = new Color(0.42f, 0.27f, 0.14f, 0.95f);

        var button = buttonObj.GetComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textObj.GetComponent<Text>();
        text.text = label;
        text.font = ResolveFont();
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        return button;
    }

    private Text CreatePagingLabel(Transform parent)
    {
        var labelObj = new GameObject("PageLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        labelObj.transform.SetParent(parent, false);

        var layout = labelObj.GetComponent<LayoutElement>();
        layout.minWidth = 112f;
        layout.preferredWidth = 112f;
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;
        layout.flexibleWidth = 0f;

        var text = labelObj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = 14;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private void CreateDeckContainer()
    {
        var viewportObj = new GameObject("AnimeDeckViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(58f, 70f);
        viewportRect.offsetMax = new Vector2(-76f, -242f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        var viewportMask = viewportObj.GetComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        _deckScrollRect = viewportObj.GetComponent<ScrollRect>();
        _deckScrollRect.horizontal = false;
        _deckScrollRect.vertical = true;
        _deckScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _deckScrollRect.scrollSensitivity = 26f;
        _deckScrollRect.viewport = viewportRect;

        var contentObj = new GameObject("AnimeDeckContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _deckContent = contentObj.GetComponent<RectTransform>();
        _deckContent.anchorMin = new Vector2(0f, 1f);
        _deckContent.anchorMax = new Vector2(1f, 1f);
        _deckContent.pivot = new Vector2(0.5f, 1f);
        _deckContent.offsetMin = new Vector2(0f, 0f);
        _deckContent.offsetMax = new Vector2(0f, 0f);

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 12f;
        layout.padding = new RectOffset(8, 8, 8, 18);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _deckScrollRect.content = _deckContent;
    }

    private void RenderDeck(AnimeDeckItem[] items)
    {
        ClearCards();

        foreach (var item in items)
        {
            CreateDeckCard(item);
        }

        ApplyFonts();
    }

    private void ClearCards()
    {
        if (_deckContent == null) return;

        for (int i = _deckContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_deckContent.GetChild(i).gameObject);
        }
    }

    private void CreateDeckCard(AnimeDeckItem item)
    {
        var card = new GameObject($"AnimeCard_{item.id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        card.transform.SetParent(_deckContent, false);

        var cardRect = card.GetComponent<RectTransform>();
        cardRect.sizeDelta = new Vector2(0f, DeckCardHeight);

        var cardImage = card.GetComponent<Image>();
        cardImage.color = DeckSurfaceColor;

        var cardLayout = card.GetComponent<LayoutElement>();
        cardLayout.minHeight = DeckCardHeight;
        cardLayout.preferredHeight = DeckCardHeight;

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(card.transform, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.offsetMin = new Vector2(12f, 12f);
        rowRect.offsetMax = new Vector2(-12f, -12f);

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 16f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        CreatePoster(item, row.transform);
        CreateInfoArea(item, row.transform);
        if (!_isIncognitoMode)
        {
            CreateActionsArea(item, row.transform);
        }
    }

    private void CreatePoster(AnimeDeckItem item, Transform parent)
    {
        var posterObj = new GameObject("Poster", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
        posterObj.transform.SetParent(parent, false);

        var layout = posterObj.GetComponent<LayoutElement>();
        layout.minWidth = 82f;
        layout.preferredWidth = 82f;
        layout.flexibleWidth = 0f;
        layout.preferredHeight = 124f;

        var poster = posterObj.GetComponent<RawImage>();
        poster.color = new Color(0.87f, 0.82f, 0.72f, 1f);

        if (!string.IsNullOrWhiteSpace(item.imageUrl))
        {
            if (isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                StartCoroutine(LoadPoster(item.imageUrl, poster));
            }
        }
    }

    private void CreateInfoArea(AnimeDeckItem item, Transform parent)
    {
        var infoObj = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        infoObj.transform.SetParent(parent, false);

        var layout = infoObj.GetComponent<LayoutElement>();
        layout.minWidth = 360f;
        layout.preferredWidth = 640f;
        layout.flexibleWidth = 1f;

        var vLayout = infoObj.GetComponent<VerticalLayoutGroup>();
        vLayout.spacing = 4f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;

        CreateLabel(infoObj.transform, item.title, 18, FontStyle.Bold, TextAnchor.UpperLeft, 28f);
        CreateLabel(infoObj.transform, Safe(item.description), 14, FontStyle.Normal, TextAnchor.UpperLeft, 66f);

        string metadata = $"Episodes: {FormatEpisodes(item.episodes)}  |  Release: {Safe(item.releaseDate)} | Genres: {FormatGenres(item.genres)}";
        CreateLabel(infoObj.transform, metadata, 13, FontStyle.Italic, TextAnchor.UpperLeft, 24f);
    }

    private void CreateActionsArea(AnimeDeckItem item, Transform parent)
    {
        var actionsObj = new GameObject("Actions", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        actionsObj.transform.SetParent(parent, false);

        var layout = actionsObj.GetComponent<LayoutElement>();
        layout.minWidth = 220f;
        layout.preferredWidth = 220f;
        layout.flexibleWidth = 0f;

        var vLayout = actionsObj.GetComponent<VerticalLayoutGroup>();
        vLayout.spacing = 3f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = true;
        vLayout.childForceExpandHeight = false;

        CreateLabel(actionsObj.transform, "Status", 13, FontStyle.Bold, TextAnchor.MiddleCenter, 20f);
        CreateStatusDropdown(actionsObj.transform, item);
        CreateLabel(actionsObj.transform, "Watched Episodes", 12, FontStyle.Bold, TextAnchor.MiddleCenter, 18f);
        CreateEpisodeStepper(actionsObj.transform, item);
        CreateLabel(actionsObj.transform, "Score", 12, FontStyle.Bold, TextAnchor.MiddleCenter, 18f);
        CreateScoreDropdown(actionsObj.transform, item);
    }

    private void CreateStatusDropdown(Transform parent, AnimeDeckItem item)
    {
        var dropdownObj = new GameObject("StatusDropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown), typeof(LayoutElement));
        dropdownObj.transform.SetParent(parent, false);

        var layout = dropdownObj.GetComponent<LayoutElement>();
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;

        var image = dropdownObj.GetComponent<Image>();
        image.color = new Color(0.95f, 0.90f, 0.78f, 1f);

        var dropdown = dropdownObj.GetComponent<Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.options = BuildStatusOptions();
        dropdown.captionText = CreateDropdownText(dropdownObj.transform, "Label", new Vector2(10f, 0f), new Vector2(-42f, 0f), TextAnchor.MiddleLeft, Color.black);
        var arrowText = CreateDropdownArrowText(dropdownObj.transform);
        arrowText.text = "v";
        CreateDropdownTemplate(dropdown);

        dropdown.value = CurrentStatusIndex(item.watchStatus);
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener((index) =>
        {
            if (index < 0 || index >= StatusValues.Length) return;
            SetWatchStatus(item, StatusValues[index]);
        });
    }

    private void CreateEpisodeStepper(Transform parent, AnimeDeckItem item)
    {
        var rowObj = new GameObject("EpisodeStepper", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObj.transform.SetParent(parent, false);

        var layout = rowObj.GetComponent<LayoutElement>();
        layout.minHeight = 28f;
        layout.preferredHeight = 28f;

        var row = rowObj.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 4f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        CreateSmallActionButton(rowObj.transform, "-", () => ChangeEpisodesWatched(item, -1));
        CreateValueLabel(rowObj.transform, FormatWatchedEpisodes(item.episodesWatched, item.episodes), 72f);
        CreateSmallActionButton(rowObj.transform, "+", () => ChangeEpisodesWatched(item, 1));
    }

    private void CreateScoreDropdown(Transform parent, AnimeDeckItem item)
    {
        var dropdownObj = new GameObject("ScoreDropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown), typeof(LayoutElement));
        dropdownObj.transform.SetParent(parent, false);

        var layout = dropdownObj.GetComponent<LayoutElement>();
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;

        var image = dropdownObj.GetComponent<Image>();
        image.color = new Color(0.95f, 0.90f, 0.78f, 1f);

        var dropdown = dropdownObj.GetComponent<Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.options = BuildScoreOptions();
        dropdown.captionText = CreateDropdownText(dropdownObj.transform, "Label", new Vector2(10f, 0f), new Vector2(-42f, 0f), TextAnchor.MiddleLeft, Color.black);
        var arrowText = CreateDropdownArrowText(dropdownObj.transform);
        arrowText.text = "v";
        CreateDropdownTemplate(dropdown);

        dropdown.value = Mathf.Clamp(item.score, 0, 10);
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener((index) => UpdateAnimeProgress(item, index, item.episodesWatched));
    }

    private void CreateSmallActionButton(Transform parent, string label, Action onClick)
    {
        var buttonObj = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);

        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 28f;
        layout.preferredWidth = 28f;
        layout.minHeight = 26f;
        layout.preferredHeight = 26f;
        layout.flexibleWidth = 0f;

        buttonObj.GetComponent<Image>().color = new Color(0.42f, 0.27f, 0.14f, 0.95f);
        var button = buttonObj.GetComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(buttonObj.transform, false);
        var rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = textObj.GetComponent<Text>();
        text.text = label;
        text.font = ResolveFont();
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private void CreateValueLabel(Transform parent, string value, float width)
    {
        var obj = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);

        var layout = obj.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = 26f;
        layout.preferredHeight = 26f;
        layout.flexibleWidth = 1f;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private List<Dropdown.OptionData> BuildStatusOptions()
    {
        var options = new List<Dropdown.OptionData>();
        foreach (var label in StatusLabels)
        {
            options.Add(new Dropdown.OptionData(label));
        }

        return options;
    }

    private List<Dropdown.OptionData> BuildScoreOptions()
    {
        var options = new List<Dropdown.OptionData> { new Dropdown.OptionData("-") };
        for (int score = 1; score <= 10; score++)
        {
            options.Add(new Dropdown.OptionData(score.ToString()));
        }

        return options;
    }

    private int CurrentStatusIndex(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
        for (int i = 0; i < StatusValues.Length; i++)
        {
            if (StatusValues[i] == normalized) return i;
        }

        return 0;
    }

    private void CreateDropdownTemplate(Dropdown dropdown)
    {
        var templateObj = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        templateObj.transform.SetParent(dropdown.transform, false);
        templateObj.SetActive(false);

        var templateRect = templateObj.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -2f);
        templateRect.sizeDelta = new Vector2(0f, 180f);

        var templateImage = templateObj.GetComponent<Image>();
        templateImage.color = new Color(0.98f, 0.95f, 0.86f, 1f);

        var scrollRect = templateObj.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        var viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportObj.transform.SetParent(templateObj.transform, false);
        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.08f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        var contentObj = new GameObject("Content", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);
        var contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        var contentLayout = contentObj.GetComponent<VerticalLayoutGroup>();
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandHeight = false;

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var itemObj = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle), typeof(LayoutElement));
        itemObj.transform.SetParent(contentObj.transform, false);
        var itemLayout = itemObj.GetComponent<LayoutElement>();
        itemLayout.minHeight = 30f;
        itemLayout.preferredHeight = 30f;
        var itemImage = itemObj.GetComponent<Image>();
        itemImage.color = new Color(0.42f, 0.27f, 0.14f, 0.95f);
        var itemToggle = itemObj.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemImage;

        var itemText = CreateDropdownText(itemObj.transform, "Item Label", new Vector2(10f, 0f), new Vector2(-10f, 0f), TextAnchor.MiddleLeft, Color.white);

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        dropdown.template = templateRect;
        dropdown.itemText = itemText;
    }

    private Text CreateDropdownText(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment, Color color)
    {
        var textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(parent, false);

        var rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var text = textObj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = 13;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private Text CreateDropdownArrowText(Transform parent)
    {
        var textObj = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(parent, false);

        var rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(28f, 0f);
        rect.anchoredPosition = new Vector2(-8f, 0f);

        var text = textObj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.black;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private async void SetWatchStatus(AnimeDeckItem item, string status)
    {
        string currentStatus = string.IsNullOrWhiteSpace(item.watchStatus) ? string.Empty : item.watchStatus.Trim().ToLowerInvariant();
        string nextStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
        if (currentStatus == nextStatus) return;

        try
        {
            _statusText.text = $"Updating {item.title}...";
            if (string.IsNullOrWhiteSpace(nextStatus))
            {
                await ApiClient.Instance.PatchLists(item.id, null, string.IsNullOrWhiteSpace(currentStatus) ? null : new[] { currentStatus });
            }
            else
            {
                await ApiClient.Instance.PatchLists(item.id, new[] { nextStatus }, null);
            }

            DozzleLogger.Action("Anime status updated", $"animeId={item.id};status={(string.IsNullOrWhiteSpace(nextStatus) ? "none" : nextStatus)}");
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            _statusText.text = "Failed to update anime status.";
            DozzleLogger.Error("Anime status update failed", ex);
        }
    }

    private void ChangeEpisodesWatched(AnimeDeckItem item, int delta)
    {
        if (item == null) return;
        int cap = item.episodes > 0 ? item.episodes : UnknownEpisodeCap;
        int nextEpisodesWatched = Mathf.Clamp(item.episodesWatched + delta, 0, cap);
        if (nextEpisodesWatched == item.episodesWatched) return;
        UpdateAnimeProgress(item, item.score, nextEpisodesWatched);
    }

    private async void UpdateAnimeProgress(AnimeDeckItem item, int score, int episodesWatched)
    {
        if (_isIncognitoMode || item == null || ApiClient.Instance == null) return;

        int cap = item.episodes > 0 ? item.episodes : UnknownEpisodeCap;
        int nextEpisodesWatched = Mathf.Clamp(episodesWatched, 0, cap);
        int nextScore = Mathf.Clamp(score, 0, 10);
        string status = string.IsNullOrWhiteSpace(item.watchStatus) ? string.Empty : item.watchStatus.Trim().ToLowerInvariant();

        try
        {
            _statusText.text = $"Updating {item.title} progress...";
            await ApiClient.Instance.PatchAnimeProgress(item.id, status, nextScore, nextEpisodesWatched);
            DozzleLogger.Action("Anime progress updated", $"animeId={item.id};episodesWatched={nextEpisodesWatched};score={nextScore}");
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            _statusText.text = "Failed to update anime progress.";
            DozzleLogger.Error("Anime progress update failed", ex);
        }
    }

    private Text CreateLabel(Transform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment, float minHeight = 0f)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);

        var layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = minHeight > 0f ? minHeight : fontSize + 6f;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        return text;
    }

    private void RenderActionVisibility()
    {
        if (_deckContent == null) return;

        for (int i = 0; i < _deckContent.childCount; i++)
        {
            var actions = _deckContent.GetChild(i).Find("Row/Actions");
            if (actions != null)
            {
                actions.gameObject.SetActive(!_isIncognitoMode);
            }
        }
    }

    private System.Collections.IEnumerator LoadPoster(string url, RawImage target)
    {
        yield return TryLoadPoster(url, target);
        if (target == null || target.texture != null) yield break;

        string fallbackUrl = BuildPosterFallbackUrl(url);
        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            DozzleLogger.Action("Anime poster retry", $"url={url};fallback={fallbackUrl}");
            yield return TryLoadPoster(fallbackUrl, target);
        }

        if (target != null && target.texture == null)
        {
            DozzleLogger.Error("Anime poster load failed", url);
        }
    }

    private System.Collections.IEnumerator TryLoadPoster(string url, RawImage target)
    {
        string requestUrl = BuildPosterRequestUrl(url);
        using (var req = UnityWebRequestTexture.GetTexture(requestUrl))
        {
            yield return req.SendWebRequest();
            if (target == null) yield break;
            if (req.result != UnityWebRequest.Result.Success)
            {
                LogPosterRequestFailure(url, requestUrl, req);
                yield break;
            }

            Texture2D texture = null;
            try
            {
                texture = DownloadHandlerTexture.GetContent(req);
            }
            catch
            {
                texture = null;
            }

            if (texture != null)
            {
                target.texture = texture;
                target.color = Color.white;
            }
        }
    }

    private static string BuildPosterRequestUrl(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (ApiClient.Instance == null) return url;
        return ApiClient.Instance.BuildImageProxyUrl(url);
#else
        return url;
#endif
    }

    private static void LogPosterRequestFailure(string originalUrl, string requestUrl, UnityWebRequest request)
    {
        if (PosterFailureLogCount >= MaxPosterFailureLogs) return;
        PosterFailureLogCount += 1;
        DozzleLogger.Error("Anime poster request failed", $"url={originalUrl};request={requestUrl};status={request.responseCode};error={request.error}");
    }

    private static string BuildPosterFallbackUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string webpExtension = ".webp";
        int index = url.IndexOf(webpExtension, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        return url.Substring(0, index) + ".jpg" + url.Substring(index + webpExtension.Length);
    }

    private Text CreateTextElement(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, FontStyle style)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(transform, false);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var text = obj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.text = string.Empty;
        return text;
    }

    private void ApplyFonts()
    {
        Font fontToUse = ResolveFont();
        if (fontToUse == null) return;

        if (_descriptionText != null) _descriptionText.font = fontToUse;
        if (_statusText != null) _statusText.font = fontToUse;
        if (_pageText != null) _pageText.font = fontToUse;
        if (_searchInput != null)
        {
            if (_searchInput.textComponent != null) _searchInput.textComponent.font = fontToUse;
            if (_searchInput.placeholder is Text placeholder) placeholder.font = fontToUse;
        }
        if (_searchBar != null)
        {
            var searchLabels = _searchBar.GetComponentsInChildren<Text>(true);
            foreach (var label in searchLabels)
            {
                label.font = fontToUse;
            }
        }
        if (_pagingBar != null)
        {
            var pagingLabels = _pagingBar.GetComponentsInChildren<Text>(true);
            foreach (var label in pagingLabels)
            {
                label.font = fontToUse;
            }
        }

        if (_deckContent == null) return;

        var labels = _deckContent.GetComponentsInChildren<Text>(true);
        foreach (var label in labels)
        {
            label.font = fontToUse;
        }
    }

    private Font ResolveFont()
    {
        if (preferredFont != null) return preferredFont;

        Font[] loadedFonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (var loadedFont in loadedFonts)
        {
            if (loadedFont != null && loadedFont.name.IndexOf("BMYEONSUNG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        try
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch
        {
            return null;
        }
    }

    private int PageSize()
    {
        return Math.Max(defaultLimit, 1);
    }

    private void GoToPreviousPage()
    {
        if (_currentOffset <= 0) return;
        _currentOffset = Math.Max(0, _currentOffset - PageSize());
        RefreshCatalog();
    }

    private void GoToNextPage()
    {
        if (!_hasNextPage) return;
        _currentOffset += PageSize();
        RefreshCatalog();
    }

    private void UpdatePaginationControls()
    {
        int pageNumber = (_currentOffset / PageSize()) + 1;
        if (_pageText != null) _pageText.text = $"Page {pageNumber}";
        if (_previousPageButton != null) _previousPageButton.interactable = _currentOffset > 0;
        if (_nextPageButton != null) _nextPageButton.interactable = _hasNextPage;
    }

    private void ResetScrollToTop()
    {
        if (_deckScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _deckScrollRect.verticalNormalizedPosition = 1f;
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
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
        if (genres == null || genres.Length == 0) return "-";
        return string.Join(", ", genres);
    }

    [Serializable]
    private class AnimeDeckResponse
    {
        public AnimeDeckItem[] items;
        public int limit;
        public int offset;
        public bool hasMore;
    }

    [Serializable]
    private class AnimeDeckItem
    {
        public string id;
        public string title;
        public string briefDescription;
        public string description;
        public string imageUrl;
        public int episodes;
        public string releaseDate;
        public bool isWatching;
        public string watchStatus;
        public int score;
        public int episodesWatched;
        public string[] lists;
        public string[] genres;
        public string trailerYoutubeId;
        public string provider;
        public string providerId;
    }
}
