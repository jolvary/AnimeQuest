using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class QuestPanelController : MonoBehaviour
{
    private static readonly Color QuestTextColor = new Color(0.17f, 0.10f, 0.04f, 1f);
    private const string ClaimedQuestsPrefsPrefix = "animequest_claimed_quests_";

    public Font preferredFont;

    private Text _descriptionText;
    private Text _statusText;
    private ScrollRect _contentScrollRect;
    private RectTransform _content;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void RefreshQuests()
    {
        EnsureTextElements();
        SetStatus("Loading quests...");
        ClearCards();

        try
        {
            string json = await ApiClient.Instance.GetQuests();
            var response = JsonUtility.FromJson<QuestResponse>(json);
            var stats = await LoadUserAnimeStats();

            if (response == null || response.items == null || response.items.Length == 0)
            {
                SetStatus("No quests found.");
                ResetScrollToTop();
                return;
            }

            int visibleQuestCount = 0;
            foreach (var item in response.items)
            {
                if (item == null || IsQuestClaimedToday(item.code)) continue;
                CreateQuestCard(item, stats);
                visibleQuestCount += 1;
            }

            if (visibleQuestCount == 0)
            {
                SetStatus("All available quests are claimed for today.");
                ResetScrollToTop();
                return;
            }

            SetStatus($"Loaded {visibleQuestCount} weekly quests.");
            ResetScrollToTop();
            DozzleLogger.Action("Quests loaded", json);
        }
        catch (Exception ex)
        {
            ClearCards();
            SetStatus("Failed to load quests.");
            ResetScrollToTop();
            DozzleLogger.Error("Failed to load quests", ex);
        }
    }

    public void OpenFromNpc(string npcName, string questCode)
    {
        DozzleLogger.Action("Opened quest panel from NPC", $"npc={npcName}, quest={questCode}");
        RefreshQuests();
    }

    public async void AcceptQuest(string questCode)
    {
        try
        {
            string json = await ApiClient.Instance.AcceptQuest(questCode);
            DozzleLogger.Action("Quest accepted", json);
            RefreshQuests();
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Failed to accept quest", ex);
        }
    }

    public async void ClaimQuest(string questCode)
    {
        try
        {
            SetStatus("Claiming quest reward...");
            string json = await ApiClient.Instance.ClaimQuest(questCode);
            MarkQuestClaimedToday(questCode);
            DozzleLogger.Action("Quest reward claimed", json);
            RefreshQuests();
        }
        catch (Exception ex)
        {
            SetStatus("Quest reward could not be claimed.");
            DozzleLogger.Error("Failed to claim quest reward", ex);
        }
    }

    private async System.Threading.Tasks.Task<UserAnimeStats> LoadUserAnimeStats()
    {
        var stats = new UserAnimeStats();
        if (ApiClient.Instance == null || NakamaAuthManager.Instance == null || !NakamaAuthManager.Instance.IsAuthenticated)
        {
            return stats;
        }

        const int pageSize = 500;
        for (int offset = 0; ; offset += pageSize)
        {
            string json = await ApiClient.Instance.GetUserAnime(string.Empty, pageSize, offset);
            var response = JsonUtility.FromJson<UserAnimeResponse>(json);
            if (response == null || response.items == null) break;

            foreach (var item in response.items)
            {
                if (item == null) continue;
                if (item.score > 0) stats.ratings += 1;
                stats.episodes += Math.Max(item.episodesWatched, 0);
                if (string.Equals(item.watchStatus, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    stats.completedSeries += 1;
                }
            }

            if (!response.hasMore || response.items.Length == 0) break;
        }

        return stats;
    }

    private void EnsureTextElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement("QuestDescription", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -148f), new Vector2(-60f, -100f), 18, FontStyle.Bold);
            _descriptionText.text = "Weekly quests";
        }

        if (_statusText == null)
        {
            _statusText = CreateTextElement("QuestStatus", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -188f), new Vector2(-60f, -148f), 15, FontStyle.Normal);
        }

        if (_contentScrollRect == null || _content == null)
        {
            CreateScrollableContent();
        }

        ApplyFonts();
    }

    private void CreateScrollableContent()
    {
        var viewportObj = new GameObject("QuestContentViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(60f, 76f);
        viewportRect.offsetMax = new Vector2(-60f, -216f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        var viewportMask = viewportObj.GetComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        _contentScrollRect = viewportObj.GetComponent<ScrollRect>();
        _contentScrollRect.horizontal = false;
        _contentScrollRect.vertical = true;
        _contentScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _contentScrollRect.scrollSensitivity = 28f;
        _contentScrollRect.viewport = viewportRect;

        var contentObj = new GameObject("QuestContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _content = contentObj.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.offsetMin = Vector2.zero;
        _content.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 12f;
        layout.padding = new RectOffset(8, 8, 8, 20);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _contentScrollRect.content = _content;
        _contentScrollRect.verticalNormalizedPosition = 1f;
    }

    private void CreateQuestCard(QuestItem item, UserAnimeStats stats)
    {
        if (item == null || _content == null) return;

        float progress = CalculateProgress(item.requirements, stats);
        bool canClaim = progress >= 1f;

        var cardObj = new GameObject($"QuestCard_{SafeName(item.code)}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        cardObj.transform.SetParent(_content, false);
        cardObj.GetComponent<Image>().color = Color.clear;

        var layoutElement = cardObj.GetComponent<LayoutElement>();
        layoutElement.minHeight = 172f;
        layoutElement.preferredHeight = 196f;

        var layout = cardObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateRowLabel(cardObj.transform, Safe(item.title), 19, FontStyle.Bold, 26f);
        CreateRowLabel(cardObj.transform, Safe(item.description), 14, FontStyle.Normal, 34f);
        CreateRowLabel(cardObj.transform, BuildRequirementsText(item.requirements, stats), 13, FontStyle.Normal, 22f);
        CreateRowLabel(cardObj.transform, BuildRewardsText(item.rewards), 13, FontStyle.Normal, 22f);
        CreateProgressBar(cardObj.transform, progress);

        if (canClaim)
        {
            CreateQuestButton(cardObj.transform, "Claim Reward", () => ClaimQuest(item.code), true);
        }
        else
        {
            CreateQuestButton(cardObj.transform, "Accept", () => AcceptQuest(item.code), true);
        }
    }

    private void CreateProgressBar(Transform parent, float progress)
    {
        var rowObj = new GameObject("QuestProgressRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowObj.transform.SetParent(parent, false);
        rowObj.GetComponent<LayoutElement>().minHeight = 28f;
        var row = rowObj.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 10f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        var trackObj = new GameObject("QuestProgressTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        trackObj.transform.SetParent(rowObj.transform, false);
        var trackLayout = trackObj.GetComponent<LayoutElement>();
        trackLayout.minWidth = 360f;
        trackLayout.preferredWidth = 520f;
        trackLayout.minHeight = 20f;
        trackLayout.preferredHeight = 20f;
        trackObj.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 0.28f);

        var fillObj = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObj.transform.SetParent(trackObj.transform, false);
        var fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillObj.GetComponent<Image>().color = new Color(0.20f, 0.75f, 0.25f, 0.95f);

        CreateRowLabel(rowObj.transform, $"{Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%", 14, FontStyle.Bold, 24f, width: 66f);
    }

    private void CreateQuestButton(Transform parent, string label, Action onClick, bool interactable)
    {
        var buttonObj = new GameObject("QuestButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);
        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 130f;
        layout.preferredWidth = 160f;
        layout.minHeight = 32f;
        layout.preferredHeight = 32f;
        buttonObj.GetComponent<Image>().color = interactable ? new Color(0.86f, 0.76f, 0.50f, 1f) : new Color(0.50f, 0.44f, 0.34f, 0.8f);

        var button = buttonObj.GetComponent<Button>();
        button.interactable = interactable;
        if (interactable)
        {
            button.onClick.AddListener(() => onClick?.Invoke());
        }
        CreateChildText(buttonObj.transform, "Text", label, 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black);
    }

    private Text CreateRowLabel(Transform parent, string value, int fontSize, FontStyle style, float minHeight, float width = 0f)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        var layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = minHeight;
        if (width > 0f)
        {
            layout.minWidth = width;
            layout.preferredWidth = width;
        }

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = QuestTextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
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
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, size - 8);
        text.resizeTextMaxSize = size;
        text.color = QuestTextColor;
        text.alignment = TextAnchor.UpperLeft;
        text.text = string.Empty;
        return text;
    }

    private Text CreateChildText(Transform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    private void ClearCards()
    {
        if (_content == null) return;
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            Destroy(_content.GetChild(i).gameObject);
        }
    }

    private void ApplyFonts()
    {
        Font fontToUse = ResolveFont();
        if (fontToUse == null) return;

        foreach (var label in GetComponentsInChildren<Text>(true))
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
            if (loadedFont != null && loadedFont.name.IndexOf("BMYEONSUNG_ttf", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        foreach (var loadedFont in loadedFonts)
        {
            if (loadedFont != null && loadedFont.name.IndexOf("BMYEONSUNG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static float CalculateProgress(QuestRequirements requirements, UserAnimeStats stats)
    {
        if (requirements == null) return 1f;

        float total = 0f;
        int parts = 0;

        AddRequirementProgress(requirements.ratings, stats.ratings, ref total, ref parts);
        AddRequirementProgress(requirements.episodes, stats.episodes, ref total, ref parts);
        AddRequirementProgress(requirements.completed_series, stats.completedSeries, ref total, ref parts);

        return parts == 0 ? 1f : Mathf.Clamp01(total / parts);
    }

    private static void AddRequirementProgress(int required, int current, ref float total, ref int parts)
    {
        if (required <= 0) return;
        total += Mathf.Clamp01((float)Math.Max(current, 0) / required);
        parts += 1;
    }

    private static string BuildRequirementsText(QuestRequirements requirements, UserAnimeStats stats)
    {
        if (requirements == null) return "Requirements: none";
        var parts = new List<string>();
        if (requirements.ratings > 0) parts.Add($"ratings {Math.Min(stats.ratings, requirements.ratings)}/{requirements.ratings}");
        if (requirements.episodes > 0) parts.Add($"episodes {Math.Min(stats.episodes, requirements.episodes)}/{requirements.episodes}");
        if (requirements.completed_series > 0) parts.Add($"completed {Math.Min(stats.completedSeries, requirements.completed_series)}/{requirements.completed_series}");
        return parts.Count == 0 ? "Requirements: none" : $"Requirements: {string.Join(" | ", parts)}";
    }

    private static string BuildRewardsText(QuestRewards rewards)
    {
        if (rewards == null) return "Rewards: none";
        var parts = new List<string>();
        if (rewards.xp > 0) parts.Add($"XP {rewards.xp}");
        if (rewards.coins > 0) parts.Add($"Coins {rewards.coins}");
        if (!string.IsNullOrWhiteSpace(rewards.character)) parts.Add($"Character {FormatCharacterKey(rewards.character)}");
        if (!string.IsNullOrWhiteSpace(rewards.item)) parts.Add(rewards.item);
        return parts.Count == 0 ? "Rewards: none" : $"Rewards: {string.Join(" | ", parts)}";
    }

    private static string FormatCharacterKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("_", " ");
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("\n", " ").Trim();
    }

    private static string SafeName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(" ", "_").Replace("/", "_");
    }

    private void SetStatus(string value)
    {
        if (_statusText != null) _statusText.text = value;
    }

    private void ResetScrollToTop()
    {
        if (_contentScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _contentScrollRect.verticalNormalizedPosition = 1f;
    }

    private static bool IsQuestClaimedToday(string questCode)
    {
        if (string.IsNullOrWhiteSpace(questCode)) return false;
        string claimed = PlayerPrefs.GetString(ClaimedQuestsPrefsKey(), string.Empty);
        if (string.IsNullOrWhiteSpace(claimed)) return false;
        string token = $"|{questCode.Trim()}|";
        return $"|{claimed}|".IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void MarkQuestClaimedToday(string questCode)
    {
        if (string.IsNullOrWhiteSpace(questCode)) return;
        string key = ClaimedQuestsPrefsKey();
        string claimed = PlayerPrefs.GetString(key, string.Empty);
        string trimmedCode = questCode.Trim();
        if ($"|{claimed}|".IndexOf($"|{trimmedCode}|", StringComparison.OrdinalIgnoreCase) >= 0) return;

        PlayerPrefs.SetString(key, string.IsNullOrWhiteSpace(claimed) ? trimmedCode : $"{claimed}|{trimmedCode}");
        PlayerPrefs.Save();
    }

    private static string ClaimedQuestsPrefsKey()
    {
        return ClaimedQuestsPrefsPrefix + DateTime.UtcNow.ToString("yyyyMMdd");
    }

    private class UserAnimeStats
    {
        public int ratings;
        public int episodes;
        public int completedSeries;
    }

    [Serializable]
    private class UserAnimeResponse
    {
        public UserAnimeItem[] items;
        public bool hasMore;
    }

    [Serializable]
    private class UserAnimeItem
    {
        public string watchStatus;
        public int score;
        public int episodesWatched;
    }

    [Serializable]
    private class QuestResponse
    {
        public QuestItem[] items;
    }

    [Serializable]
    private class QuestItem
    {
        public string id;
        public string code;
        public string title;
        public string description;
        public QuestRequirements requirements;
        public QuestRewards rewards;
    }

    [Serializable]
    private class QuestRequirements
    {
        public int ratings;
        public int episodes;
        public int completed_series;
    }

    [Serializable]
    private class QuestRewards
    {
        public int xp;
        public int coins;
        public string item;
        public string character;
    }
}
