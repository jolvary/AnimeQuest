using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CharacterPanelController : MonoBehaviour
{
    private static readonly Color TextColor = new Color(0.17f, 0.10f, 0.04f, 1f);
    private static readonly Color ButtonColor = new Color(0.48f, 0.28f, 0.12f, 0.86f);
    private static readonly Color DisabledButtonColor = new Color(0.36f, 0.30f, 0.23f, 0.72f);

    public Font preferredFont;

    private Text _titleText;
    private Text _statusText;
    private ScrollRect _scrollRect;
    private RectTransform _content;
    private bool _isLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapPanelSlot()
    {
        if (FindFirstObjectByType<CharacterPanelBootstrap>(FindObjectsInactive.Include) != null) return;

        var obj = new GameObject("CharacterPanelBootstrap");
        DontDestroyOnLoad(obj);
        obj.AddComponent<CharacterPanelBootstrap>();
    }

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void RefreshCharacters()
    {
        if (_isLoading) return;

        EnsureElements();
        DisableLegacyTableDumpElements();
        ClearCards();
        SetStatus("Loading characters...");

        if (ApiClient.Instance == null || NakamaAuthManager.Instance == null || !NakamaAuthManager.Instance.IsAuthenticated || NakamaAuthManager.Instance.IsIncognitoSession)
        {
            SetStatus("Log in to unlock and select characters.");
            CreateInfoCard("Robot Kyle", "Available by default. Log in to unlock more characters through quests and XP.");
            ResetScrollToTop();
            return;
        }

        _isLoading = true;
        try
        {
            string json = await ApiClient.Instance.GetCharacterProgression();
            var progression = JsonUtility.FromJson<ApiClient.CharacterProgressionResponse>(json);
            if (progression == null || progression.profile == null || progression.characters == null)
            {
                SetStatus("No character data returned.");
                return;
            }

            SetStatus($"Level {progression.profile.level} | XP {progression.profile.experiencePoints}/{progression.profile.nextLevelExperience} | Coins {progression.profile.coins}");
            foreach (var character in progression.characters)
            {
                CreateCharacterCard(character);
            }

            ResetScrollToTop();
            DozzleLogger.Action("Character panel loaded", $"count={progression.characters.Length};selected={progression.profile.selectedCharacterKey}");
        }
        catch (Exception ex)
        {
            SetStatus("Failed to load characters.");
            DozzleLogger.Error("Failed to load character panel", ex);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void EnsureElements()
    {
        if (_titleText == null)
        {
            _titleText = CreateTextElement("CharacterTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(70f, -102f), new Vector2(-70f, -54f), 25, FontStyle.Bold);
            _titleText.text = "Characters";
            _titleText.alignment = TextAnchor.MiddleLeft;
        }

        if (_statusText == null)
        {
            _statusText = CreateTextElement("CharacterStatus", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(70f, -154f), new Vector2(-70f, -110f), 16, FontStyle.Bold);
        }

        if (_scrollRect == null || _content == null)
        {
            CreateScrollableContent();
        }

        ApplyFonts();
    }

    private void CreateScrollableContent()
    {
        var viewportObj = new GameObject("CharacterViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(70f, 82f);
        viewportRect.offsetMax = new Vector2(-70f, -184f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        var mask = viewportObj.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        _scrollRect = viewportObj.GetComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 30f;
        _scrollRect.viewport = viewportRect;

        var contentObj = new GameObject("CharacterContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _content = contentObj.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.offsetMin = Vector2.zero;
        _content.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scrollRect.content = _content;
    }

    private void CreateCharacterCard(ApiClient.CharacterItem character)
    {
        if (character == null || _content == null) return;

        var cardObj = new GameObject($"CharacterCard_{SafeName(character.key)}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        cardObj.transform.SetParent(_content, false);
        cardObj.GetComponent<Image>().color = Color.clear;

        var layoutElement = cardObj.GetComponent<LayoutElement>();
        layoutElement.minHeight = 122f;
        layoutElement.preferredHeight = 146f;

        var layout = cardObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        string status = character.selected ? "Selected" : character.unlocked ? "Unlocked" : BuildLockText(character);
        CreateRowLabel(cardObj.transform, Safe(character.displayName), 20, FontStyle.Bold, 28f);
        CreateRowLabel(cardObj.transform, Safe(character.description), 14, FontStyle.Normal, 34f);
        CreateRowLabel(cardObj.transform, status, 14, FontStyle.Bold, 24f);

        if (character.unlocked)
        {
            CreateButton(cardObj.transform, character.selected ? "Selected" : "Select", () => SelectCharacter(character), !character.selected);
        }
    }

    private void CreateInfoCard(string title, string body)
    {
        var cardObj = new GameObject("CharacterInfoCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        cardObj.transform.SetParent(_content, false);
        cardObj.GetComponent<Image>().color = Color.clear;
        cardObj.GetComponent<LayoutElement>().preferredHeight = 110f;

        var layout = cardObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateRowLabel(cardObj.transform, title, 20, FontStyle.Bold, 28f);
        CreateRowLabel(cardObj.transform, body, 14, FontStyle.Normal, 42f);
    }

    private async void SelectCharacter(ApiClient.CharacterItem character)
    {
        if (character == null || ApiClient.Instance == null) return;

        try
        {
            SetStatus($"Selecting {character.displayName}...");
            string json = await ApiClient.Instance.SelectCharacter(character.key, character.robotColor);
            DozzleLogger.Action("Character selected", json);
            ForceWorldCharacterRefresh();
            StartCoroutine(ForceWorldCharacterRefreshAfterSelection());
            RefreshCharacters();
        }
        catch (Exception ex)
        {
            SetStatus("Character could not be selected.");
            DozzleLogger.Error("Failed to select character", ex);
        }
    }

    private static void ForceWorldCharacterRefresh()
    {
        var world = FindFirstObjectByType<NakamaWorldMultiplayerController>(FindObjectsInactive.Include);
        if (world != null)
        {
            world.ForceCharacterProgressionRefresh();
        }

        var skin = FindFirstObjectByType<WorldCharacterSkinApplier>(FindObjectsInactive.Include);
        if (skin != null)
        {
            skin.ForceApplyNow();
        }
    }

    private IEnumerator ForceWorldCharacterRefreshAfterSelection()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        ForceWorldCharacterRefresh();
        yield return new WaitForSecondsRealtime(1.25f);
        ForceWorldCharacterRefresh();
    }

    private void CreateButton(Transform parent, string label, Action onClick, bool interactable)
    {
        var buttonObj = new GameObject("CharacterButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);

        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 130f;
        layout.preferredWidth = 170f;
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;

        buttonObj.GetComponent<Image>().color = interactable ? ButtonColor : DisabledButtonColor;
        var button = buttonObj.GetComponent<Button>();
        button.interactable = interactable;
        if (interactable)
        {
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        CreateChildText(buttonObj.transform, "Text", label, 15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
    }

    private Text CreateRowLabel(Transform parent, string value, int fontSize, FontStyle style, float minHeight)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);

        var layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = minHeight;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private Text CreateTextElement(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, FontStyle style)
    {
        var existing = transform.Find(name);
        if (existing != null)
        {
            return existing.GetComponent<Text>() ?? existing.gameObject.AddComponent<Text>();
        }

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
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
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
        text.raycastTarget = false;
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

    private void DisableLegacyTableDumpElements()
    {
        string[] names = { "TableTitle", "TableDescription", "TableStatus", "TableContentViewport", "TableContent" };
        foreach (string childName in names)
        {
            var child = transform.Find(childName);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        var tableViewer = GetComponent<TableViewerPanelController>();
        if (tableViewer != null)
        {
            tableViewer.enabled = false;
        }
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

        var loadedFonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (var loadedFont in loadedFonts)
        {
            if (loadedFont != null && loadedFont.name.IndexOf("BMYEONSUNG", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void SetStatus(string value)
    {
        if (_statusText != null) _statusText.text = value;
    }

    private void ResetScrollToTop()
    {
        if (_scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 1f;
    }

    private static string BuildLockText(ApiClient.CharacterItem character)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.unlockQuestCode)) parts.Add($"quest {character.unlockQuestCode}");
        if (character.unlockLevel > 1) parts.Add($"level {character.unlockLevel}");
        return parts.Count == 0 ? "Locked" : $"Locked: requires {string.Join(" and ", parts)}";
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("\n", " ").Trim();
    }

    private static string SafeName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace(" ", "_").Replace("/", "_");
    }
}

internal class CharacterPanelBootstrap : MonoBehaviour
{
    private static readonly MethodInfo RefreshCursorStateMethod = typeof(UIManager).GetMethod("RefreshCursorState", BindingFlags.Instance | BindingFlags.NonPublic);

    private UIManager _uiManager;
    private CharacterPanelController _controller;
    private float _nextPatchAt;

    private IEnumerator Start()
    {
        while (_uiManager == null)
        {
            TryPatchPanelSlot();
            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextPatchAt)
        {
            TryPatchPanelSlot();
            _nextPatchAt = Time.unscaledTime + 1.5f;
        }

        if (Keyboard.current == null || IsTextInputFocused()) return;
        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            OpenCharacterPanel();
        }
    }

    private void TryPatchPanelSlot()
    {
        _uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        if (_uiManager == null || _uiManager.tablePanel == null) return;

        _controller = _uiManager.tablePanel.GetComponent<CharacterPanelController>();
        if (_controller == null)
        {
            _controller = _uiManager.tablePanel.AddComponent<CharacterPanelController>();
        }

        _controller.ConfigureFont(_uiManager.panelTitleFont);
        PatchToolbarButton();
    }

    private void PatchToolbarButton()
    {
        var toolbar = GameObject.Find("PanelIconToolbar");
        if (toolbar == null) return;

        Transform buttonTransform = toolbar.transform.Find("Icon_Characters") ?? toolbar.transform.Find("Icon_Tables");
        if (buttonTransform == null) return;

        buttonTransform.name = "Icon_Characters";
        var label = buttonTransform.Find("Label")?.GetComponent<Text>();
        if (label != null) label.text = "P";

        var button = buttonTransform.GetComponent<Button>();
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OpenCharacterPanel);
    }

    private void OpenCharacterPanel()
    {
        if (_uiManager == null || _uiManager.tablePanel == null)
        {
            TryPatchPanelSlot();
        }

        if (_uiManager == null || _uiManager.tablePanel == null || _controller == null) return;

        _uiManager.HideAll();
        _uiManager.tablePanel.SetActive(true);
        RefreshUiInputState();
        _controller.RefreshCharacters();
    }

    private void RefreshUiInputState()
    {
        RefreshCursorStateMethod?.Invoke(_uiManager, null);
    }

    private static bool IsTextInputFocused()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
    }
}
