using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using StarterAssets;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class UIManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject chatPanel;
    public GameObject friendsPanel;
    public GameObject questsPanel;
    public GameObject animePanel;
    public GameObject tablePanel;

    [Header("Controllers")]
    public QuestPanelController questPanelController;
    public AnimeCatalogPanelController animeCatalogPanelController;
    public TableViewerPanelController tableViewerPanelController;

    [Header("Visuals")]
    public Sprite fantasyWoodenBoardSprite;
    public Sprite closeButtonSprite;
    public Font panelTitleFont;

    [Header("Input")]
    public StarterAssetsInputs playerInputs;

    private bool _isUiInteractionEnabled;

    private void Start()
    {
        if (playerInputs == null)
        {
            playerInputs = FindFirstObjectByType<StarterAssetsInputs>();
        }

        ApplyPanelVisuals();
        AddCloseButtons();
        HideAll();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) ToggleExclusive(chatPanel);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) ToggleExclusive(friendsPanel);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) OpenQuestsPanel();
        if (Keyboard.current.digit4Key.wasPressedThisFrame) OpenAnimePanel();
        if (Keyboard.current.digit5Key.wasPressedThisFrame) OpenTablePanel("anime");
    }

    public void OpenQuestsPanel()
    {
        bool isOpening = ToggleExclusive(questsPanel);
        if (isOpening)
        {
            questPanelController?.RefreshQuests();
        }
    }

    public void OpenAnimePanel()
    {
        ToggleExclusive(animePanel);
        animeCatalogPanelController?.RefreshCatalog();
    }

    public void OpenTablePanel(string tableName)
    {
        ToggleExclusive(tablePanel);
        tableViewerPanelController?.OpenTable(tableName);
    }

    public void OpenChatPanel()
    {
        ToggleExclusive(chatPanel);
    }

    public void OpenFriendsPanel()
    {
        ToggleExclusive(friendsPanel);
    }

    public void HideAll()
    {
        if (chatPanel) chatPanel.SetActive(false);
        if (friendsPanel) friendsPanel.SetActive(false);
        if (questsPanel) questsPanel.SetActive(false);
        if (animePanel) animePanel.SetActive(false);
        if (tablePanel) tablePanel.SetActive(false);

        RefreshCursorState();
    }

    private void Toggle(GameObject panel)
    {
        if (!panel) return;
        panel.SetActive(!panel.activeSelf);
    }

    private bool ToggleExclusive(GameObject target)
    {
        bool isOpening = target && !target.activeSelf;
        HideAll();
        if (isOpening)
        {
            target.SetActive(true);
        }

        RefreshCursorState();
        return isOpening;
    }

    private void ApplyPanelVisuals()
    {
        if (fantasyWoodenBoardSprite == null) return;

        ApplyPanelSprite(chatPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(friendsPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(questsPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(animePanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(tablePanel, fantasyWoodenBoardSprite);
    }

    private static void ApplyPanelSprite(GameObject panel, Sprite sprite)
    {
        if (!panel || sprite == null) return;

        var image = panel.GetComponent<Image>();
        if (!image) return;

        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.color = Color.white;
    }

    private void AddCloseButtons()
    {
        AddCloseButton(chatPanel);
        AddCloseButton(friendsPanel);
        AddCloseButton(questsPanel);
        AddCloseButton(animePanel);
        AddCloseButton(tablePanel);
        AddWeeklyQuestTitle();
    }

    private void AddCloseButton(GameObject panel)
    {
        if (!panel) return;
        if (panel.transform.Find("CloseButton")) return;

        var closeObject = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        closeObject.transform.SetParent(panel.transform, false);

        var rect = closeObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(48f, 48f);

        var image = closeObject.GetComponent<Image>();
        image.sprite = closeButtonSprite;
        image.color = Color.white;
        image.type = Image.Type.Simple;

        var button = closeObject.GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            panel.SetActive(false);
            RefreshCursorState();
        });
    }

    private void RefreshCursorState()
    {
        bool anyPanelOpen = IsAnyPanelOpen();
        if (anyPanelOpen == _isUiInteractionEnabled) return;

        _isUiInteractionEnabled = anyPanelOpen;

        Cursor.lockState = anyPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = anyPanelOpen;

        if (playerInputs != null)
        {
            playerInputs.cursorLocked = !anyPanelOpen;
            playerInputs.cursorInputForLook = !anyPanelOpen;
            if (anyPanelOpen)
            {
                playerInputs.LookInput(Vector2.zero);
            }
        }
    }

    private bool IsAnyPanelOpen()
    {
        return (chatPanel && chatPanel.activeSelf) ||
               (friendsPanel && friendsPanel.activeSelf) ||
               (questsPanel && questsPanel.activeSelf) ||
               (animePanel && animePanel.activeSelf) ||
               (tablePanel && tablePanel.activeSelf);
    }

    private void AddWeeklyQuestTitle()
    {
        if (!questsPanel) return;
        if (questsPanel.transform.Find("PanelTitle")) return;

        var titleObject = new GameObject("PanelTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        titleObject.transform.SetParent(questsPanel.transform, false);

        var rect = titleObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -24f);
        rect.sizeDelta = new Vector2(520f, 60f);

        var text = titleObject.GetComponent<Text>();
        text.text = "Weekly quests";

        if (panelTitleFont == null)
        {
#if UNITY_EDITOR
            panelTitleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/BMYEONSUNG_ttf.ttf");
#endif
        }

        text.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 34;
        text.color = new Color(0.16f, 0.09f, 0.03f, 1f);
    }
}
