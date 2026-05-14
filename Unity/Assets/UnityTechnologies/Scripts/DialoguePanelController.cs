using System;
using UnityEngine;
using UnityEngine.UI;

public class DialoguePanelController : MonoBehaviour
{
    private static readonly Color TextColor = new Color(0.17f, 0.10f, 0.04f, 1f);
    private static readonly Color ButtonColor = new Color(0.42f, 0.27f, 0.14f, 0.95f);

    public class DialogueOption
    {
        public string label;
        public Action action;

        public DialogueOption(string label, Action action)
        {
            this.label = label;
            this.action = action;
        }
    }

    public Font preferredFont;

    private Text _speakerText;
    private Text _bodyText;
    private RectTransform _optionsRoot;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public void Show(string speaker, string body, DialogueOption[] options)
    {
        EnsureElements();
        _speakerText.text = string.IsNullOrWhiteSpace(speaker) ? "Guide" : speaker.Trim();
        _bodyText.text = string.IsNullOrWhiteSpace(body) ? "Welcome to AnimeQuest." : body.Trim();
        RenderOptions(options);
    }

    private void EnsureElements()
    {
        if (_speakerText == null)
        {
            _speakerText = CreateText(
                "DialogueSpeaker",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(96f, -122f),
                new Vector2(-96f, -74f),
                32,
                FontStyle.Bold,
                TextAnchor.MiddleCenter
            );
        }

        if (_bodyText == null)
        {
            _bodyText = CreateText(
                "DialogueBody",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(112f, -390f),
                new Vector2(-112f, -155f),
                22,
                FontStyle.Normal,
                TextAnchor.UpperLeft
            );
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        if (_optionsRoot == null)
        {
            var optionsObject = new GameObject("DialogueOptions", typeof(RectTransform), typeof(VerticalLayoutGroup));
            optionsObject.transform.SetParent(transform, false);

            _optionsRoot = optionsObject.GetComponent<RectTransform>();
            _optionsRoot.anchorMin = new Vector2(0.5f, 0f);
            _optionsRoot.anchorMax = new Vector2(0.5f, 0f);
            _optionsRoot.pivot = new Vector2(0.5f, 0f);
            _optionsRoot.anchoredPosition = new Vector2(0f, 78f);
            _optionsRoot.sizeDelta = new Vector2(920f, 260f);

            var layout = optionsObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        ApplyFonts();
    }

    private Text CreateText(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, FontStyle style, TextAnchor alignment)
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
        text.alignment = alignment;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private void RenderOptions(DialogueOption[] options)
    {
        ClearOptions();

        if (options == null || options.Length == 0)
        {
            CreateOptionButton(new DialogueOption("Close", () => gameObject.SetActive(false)));
            return;
        }

        foreach (var option in options)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.label)) continue;
            CreateOptionButton(option);
        }
    }

    private void CreateOptionButton(DialogueOption option)
    {
        var buttonObject = new GameObject("DialogueOption", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(_optionsRoot, false);

        var layout = buttonObject.GetComponent<LayoutElement>();
        layout.minHeight = 42f;
        layout.preferredHeight = 42f;

        var image = buttonObject.GetComponent<Image>();
        image.color = ButtonColor;

        var button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(() => option.action?.Invoke());

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);

        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 2f);
        labelRect.offsetMax = new Vector2(-12f, -2f);

        var text = labelObject.GetComponent<Text>();
        text.text = option.label.Trim();
        text.font = ResolveFont();
        text.fontSize = 19;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private void ClearOptions()
    {
        if (_optionsRoot == null) return;

        for (int i = _optionsRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(_optionsRoot.GetChild(i).gameObject);
        }
    }

    private void ApplyFonts()
    {
        Font font = ResolveFont();
        if (_speakerText != null) _speakerText.font = font;
        if (_bodyText != null) _bodyText.font = font;
    }

    private Font ResolveFont()
    {
        return preferredFont != null ? preferredFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
