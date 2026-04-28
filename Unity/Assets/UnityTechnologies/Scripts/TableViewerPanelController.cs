using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class TableViewerPanelController : MonoBehaviour
{
    public Font preferredFont;

    [SerializeField]
    private List<string> defaultTables = new List<string>
    {
        "users",
        "anime",
        "quests",
        "watch_entries",
        "user_quests",
        "achievements",
        "user_achievements"
    };

    private Text _descriptionText;
    private Text _contentText;
    private ScrollRect _contentScrollRect;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void OpenTable(string tableName)
    {
        EnsureTextElements();

        if (string.Equals(tableName, "all", System.StringComparison.OrdinalIgnoreCase))
        {
            _descriptionText.text = "Database viewer: combined dump for all configured tables.";
            _contentText.text = "Loading all tables...";

            var builder = new StringBuilder();
            foreach (var table in defaultTables)
            {
                builder.AppendLine($"\n===== {table.ToUpperInvariant()} =====");
                try
                {
                    string tableJson = await ApiClient.Instance.GetTable(table, 50, 0);
                    builder.AppendLine(tableJson);
                }
                catch (System.Exception ex)
                {
                    builder.AppendLine($"Failed to load {table}: {ex.Message}");
                }
            }

            string allTablesData = builder.ToString();
            _contentText.text = allTablesData;
            ResetScrollToTop();
            Debug.Log("All tables: " + allTablesData);
            return;
        }

        _descriptionText.text = $"Database viewer: table '{tableName}' (read-only sample rows).";
        _contentText.text = $"Loading table {tableName}...";

        try
        {
            string json = await ApiClient.Instance.GetTable(tableName, 50, 0);
            _contentText.text = json;
            ResetScrollToTop();
            Debug.Log($"Table {tableName}: " + json);
        }
        catch (System.Exception ex)
        {
            _contentText.text = "Failed to load table.";
            ResetScrollToTop();
            Debug.LogError("Failed to load table: " + ex.Message);
        }
    }

    private void EnsureTextElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement("TableDescription", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -92f), new Vector2(-48f, -16f), 24, FontStyle.Bold);
        }

        if (_contentScrollRect == null || _contentText == null)
        {
            CreateScrollableContent();
            _contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _contentText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        ApplyFonts();
    }

    private void CreateScrollableContent()
    {
        var viewportObj = new GameObject("TableContentViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 42f);
        viewportRect.offsetMax = new Vector2(-48f, -132f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        var viewportMask = viewportObj.GetComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        _contentScrollRect = viewportObj.GetComponent<ScrollRect>();
        _contentScrollRect.horizontal = false;
        _contentScrollRect.vertical = true;
        _contentScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _contentScrollRect.scrollSensitivity = 32f;
        _contentScrollRect.viewport = viewportRect;

        var contentObj = new GameObject("TableContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        var contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(0f, 0f);
        contentRect.offsetMax = new Vector2(0f, 0f);

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
        _contentText.text = string.Empty;
        _contentText.supportRichText = false;

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
            if (loadedFont != null && loadedFont.name.IndexOf("BMYEONSUNG", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        if (_descriptionText != null && _descriptionText.font != null) return _descriptionText.font;
        if (_contentText != null && _contentText.font != null) return _contentText.font;

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
        if (_contentScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _contentScrollRect.verticalNormalizedPosition = 1f;
    }
}
