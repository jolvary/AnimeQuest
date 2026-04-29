using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AnimeCatalogPanelController : MonoBehaviour
{
    public string defaultSearch = "";
    public int defaultLimit = 20;
    public Font preferredFont;

    private Text _descriptionText;
    private Text _statusText;
    private ScrollRect _deckScrollRect;
    private RectTransform _deckContent;
    private bool _isIncognitoMode;

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
            _descriptionText.text = enabled
                ? "Anime Deck (Incognito): browse entries only. Personal anime list actions are hidden."
                : "Anime Deck: browse entries with quick actions and expandable details.";
        }

        if (_deckContent != null)
        {
            RenderActionVisibility();
        }
    }

    public async void RefreshCatalog()
    {
        EnsureDeckElements();
        _descriptionText.text = _isIncognitoMode
            ? "Anime Deck (Incognito): browse entries only. Personal anime list actions are hidden."
            : "Anime Deck: browse entries with quick actions and expandable details.";
        _statusText.text = "Loading anime deck...";

        try
        {
            string json = await ApiClient.Instance.GetAnime(defaultSearch, defaultLimit);
            var response = JsonUtility.FromJson<AnimeDeckResponse>(json);

            if (response == null || response.items == null || response.items.Length == 0)
            {
                _statusText.text = "No anime found.";
                ClearCards();
                return;
            }

            RenderDeck(response.items);
            RenderActionVisibility();
            _statusText.text = $"Loaded {response.items.Length} anime entries.";
            ResetScrollToTop();
        }
        catch (Exception ex)
        {
            _statusText.text = "Failed to load anime deck.";
            ClearCards();
            Debug.LogError("Failed to load anime deck: " + ex.Message);
        }
    }

    private void EnsureDeckElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement(
                "AnimeDescription",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(48f, -132f),
                new Vector2(-48f, -62f),
                20,
                FontStyle.Bold
            );
        }

        if (_statusText == null)
        {
            _statusText = CreateTextElement(
                "AnimeStatus",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(48f, -172f),
                new Vector2(-48f, -102f),
                16,
                FontStyle.Normal
            );
        }

        if (_deckScrollRect == null || _deckContent == null)
        {
            CreateDeckContainer();
        }

        ApplyFonts();
    }

    private void CreateDeckContainer()
    {
        var viewportObj = new GameObject("AnimeDeckViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 48f);
        viewportRect.offsetMax = new Vector2(-48f, -190f);

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
        layout.padding = new RectOffset(8, 8, 8, 8);

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
        cardRect.sizeDelta = new Vector2(0f, 168f);

        var cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(1f, 1f, 1f, 0.92f);

        var cardLayout = card.GetComponent<LayoutElement>();
        cardLayout.minHeight = 168f;

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(card.transform, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.offsetMin = new Vector2(12f, 12f);
        rowRect.offsetMax = new Vector2(-12f, -12f);

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        CreatePoster(item, row.transform);
        CreateInfoArea(item, row.transform);
        CreateActionsArea(item, row.transform, card.transform);
    }

    private void CreatePoster(AnimeDeckItem item, Transform parent)
    {
        var posterObj = new GameObject("Poster", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
        posterObj.transform.SetParent(parent, false);

        var layout = posterObj.GetComponent<LayoutElement>();
        layout.preferredWidth = 72f;
        layout.preferredHeight = 108f;

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
        layout.flexibleWidth = 1f;

        var vLayout = infoObj.GetComponent<VerticalLayoutGroup>();
        vLayout.spacing = 4f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;

        CreateLabel(infoObj.transform, item.title, 18, FontStyle.Bold, TextAnchor.UpperLeft);
        CreateLabel(infoObj.transform, Safe(item.briefDescription), 14, FontStyle.Normal, TextAnchor.UpperLeft);

        string metadata = $"Episodes: {FormatEpisodes(item.episodes)}  •  Release: {Safe(item.releaseDate)}";
        CreateLabel(infoObj.transform, metadata, 13, FontStyle.Italic, TextAnchor.UpperLeft);

        var expanded = CreateLabel(infoObj.transform, Safe(item.description), 13, FontStyle.Normal, TextAnchor.UpperLeft);
        expanded.gameObject.SetActive(false);
        expanded.name = "ExpandedDescription";
    }

    private void CreateActionsArea(AnimeDeckItem item, Transform parent, Transform card)
    {
        var actionsObj = new GameObject("Actions", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        actionsObj.transform.SetParent(parent, false);

        var layout = actionsObj.GetComponent<LayoutElement>();
        layout.preferredWidth = 220f;

        var vLayout = actionsObj.GetComponent<VerticalLayoutGroup>();
        vLayout.spacing = 6f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;

        if (!_isIncognitoMode)
        {
            CreateActionButton(actionsObj.transform, item.isWatching ? "Watching ✓" : "Add to Watching", async () =>
            {
                await ApiClient.Instance.PatchWatching(item.id, !item.isWatching);
                RefreshCatalog();
            });

            CreateActionButton(actionsObj.transform, "Add to Planned", async () =>
            {
                await ApiClient.Instance.PatchLists(item.id, new[] { "planned" }, null);
                RefreshCatalog();
            });

            CreateActionButton(actionsObj.transform, "Add to Completed", async () =>
            {
                await ApiClient.Instance.PatchLists(item.id, new[] { "completed" }, null);
                RefreshCatalog();
            });
        }

        CreateActionButton(actionsObj.transform, "Expand / Collapse", () =>
        {
            ToggleExpanded(card);
        });
    }

    private Text CreateLabel(Transform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);

        var layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = fontSize + 6f;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        return text;
    }

    private void CreateActionButton(Transform parent, string label, Action onClick)
    {
        var buttonObj = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);

        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minHeight = 30f;

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
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private void RenderActionVisibility()
    {
        if (_deckContent == null) return;

        for (int i = 0; i < _deckContent.childCount; i++)
        {
            var actions = _deckContent.GetChild(i).Find("Row/Actions");
            if (actions == null) continue;

            for (int j = 0; j < actions.childCount; j++)
            {
                var actionName = actions.GetChild(j).name;
                bool isExpandButton = actionName.IndexOf("Expand / Collapse", StringComparison.OrdinalIgnoreCase) >= 0;
                actions.GetChild(j).gameObject.SetActive(!_isIncognitoMode || isExpandButton);
            }
        }
    }

    private void ToggleExpanded(Transform card)
    {
        var expanded = card.Find("Row/Info/ExpandedDescription");
        if (expanded == null) return;

        bool next = !expanded.gameObject.activeSelf;
        expanded.gameObject.SetActive(next);

        var layout = card.GetComponent<LayoutElement>();
        layout.minHeight = next ? 220f : 168f;
    }

    private System.Collections.IEnumerator LoadPoster(string url, RawImage target)
    {
        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var texture = DownloadHandlerTexture.GetContent(req);
            if (texture != null)
            {
                target.texture = texture;
                target.color = Color.white;
            }
        }
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

    [Serializable]
    private class AnimeDeckResponse
    {
        public AnimeDeckItem[] items;
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
    }
}
