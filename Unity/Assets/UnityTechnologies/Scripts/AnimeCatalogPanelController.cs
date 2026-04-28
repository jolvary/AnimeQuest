using UnityEngine;
using UnityEngine.UI;

public class AnimeCatalogPanelController : MonoBehaviour
{
    public string defaultSearch = "";
    public int defaultLimit = 20;
    public Font preferredFont;

    private Text _descriptionText;
    private Text _contentText;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void RefreshCatalog()
    {
        EnsureTextElements();
        _descriptionText.text = "Anime database: latest catalog entries (title, year, genres, episodes, provider).";
        _contentText.text = "Loading anime catalog...";

        try
        {
            string json = await ApiClient.Instance.GetAnime(defaultSearch, defaultLimit);
            _contentText.text = json;
            Debug.Log("Anime catalog: " + json);
        }
        catch (System.Exception ex)
        {
            _contentText.text = "Failed to load anime catalog.";
            Debug.LogError("Failed to load anime catalog: " + ex.Message);
        }
    }

    private void EnsureTextElements()
    {
        if (_descriptionText == null)
        {
            _descriptionText = CreateTextElement("AnimeDescription", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -80f), new Vector2(-60f, -10f), 24, FontStyle.Bold);
        }

        if (_contentText == null)
        {
            _contentText = CreateTextElement("AnimeContent", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 30f), new Vector2(-60f, -140f), 18, FontStyle.Normal);
            _contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _contentText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        ApplyFonts();
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
}
