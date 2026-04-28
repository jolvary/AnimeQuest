using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class QuestPanelController : MonoBehaviour
{
    public Font preferredFont;

    private Text _descriptionText;
    private Text _contentText;
    private ScrollRect _contentScrollRect;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void RefreshQuests()
    {
        EnsureTextElements();
        _descriptionText.text = "Quests database: available weekly quests with requirements and rewards.";
        _contentText.text = "Loading quests...";

        try
        {
            string json = await ApiClient.Instance.GetQuests();
            _contentText.text = FormatQuestsAsTable(json);
            ResetScrollToTop();
            Debug.Log("Quests: " + json);
        }
        catch (Exception ex)
        {
            _contentText.text = "Failed to load quests.";
            ResetScrollToTop();
            Debug.LogError("Failed to load quests: " + ex.Message);
        }
    }

    public void OpenFromNpc(string npcName, string questCode)
    {
        Debug.Log($"Opened quest panel from NPC {npcName} for quest {questCode}");
        RefreshQuests();
    }

    public async void AcceptQuest(string questCode)
    {
        try
        {
            string json = await ApiClient.Instance.AcceptQuest(questCode);
            Debug.Log("Quest accepted: " + json);
            RefreshQuests();
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to accept quest: " + ex.Message);
        }
    }

    private void EnsureTextElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement("QuestDescription", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -138f), new Vector2(-48f, -68f), 20, FontStyle.Bold);
        }

        if (_contentScrollRect == null || _contentText == null)
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
        viewportRect.offsetMin = new Vector2(48f, 56f);
        viewportRect.offsetMax = new Vector2(-48f, -170f);

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

        var contentObj = new GameObject("QuestContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        var contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(12f, 0f);
        contentRect.offsetMax = new Vector2(-12f, 0f);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _contentText = contentObj.GetComponent<Text>();
        _contentText.fontSize = 18;
        _contentText.fontStyle = FontStyle.Normal;
        _contentText.resizeTextForBestFit = true;
        _contentText.resizeTextMinSize = 12;
        _contentText.resizeTextMaxSize = 18;
        _contentText.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        _contentText.alignment = TextAnchor.UpperLeft;
        _contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _contentText.verticalOverflow = VerticalWrapMode.Truncate;
        _contentText.supportRichText = false;
        _contentText.text = string.Empty;

        _contentScrollRect.content = contentRect;
        _contentScrollRect.verticalNormalizedPosition = 1f;
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
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, size - 8);
        text.resizeTextMaxSize = size;
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
        if (_contentText != null) _contentText.font = fontToUse;
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

    private string FormatQuestsAsTable(string json)
    {
        var response = JsonUtility.FromJson<QuestResponse>(json);
        if (response == null || response.items == null || response.items.Length == 0)
        {
            return json;
        }

        var table = new StringBuilder();
        table.AppendLine("Code | Title | Requirements | Rewards");
        table.AppendLine("------------------------------------------------------------");

        foreach (var item in response.items)
        {
            string requirements = item.requirements == null
                ? "none"
                : $"ratings: {item.requirements.ratings}, episodes: {item.requirements.episodes}, completed_series: {item.requirements.completed_series}";

            string rewards = item.rewards == null
                ? "none"
                : $"xp: {item.rewards.xp}, coins: {item.rewards.coins}, item: {item.rewards.item}";

            table.AppendLine($"{Safe(item.code)} | {Safe(item.title)} | {requirements} | {rewards}");
            table.AppendLine($"  Description: {Safe(item.description)}");
            table.AppendLine();
        }

        return table.ToString().TrimEnd();
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("\n", " ").Trim();
    }

    private void ResetScrollToTop()
    {
        if (_contentScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _contentScrollRect.verticalNormalizedPosition = 1f;
    }

    [Serializable]
    private class QuestResponse
    {
        public QuestItem[] items;
    }

    [Serializable]
    private class QuestItem
    {
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
    }
}
