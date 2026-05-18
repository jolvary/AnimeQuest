using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StarterAssets;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class UIManager : MonoBehaviour
{
    private const int MaxChatBadgeCount = 99;

    [Header("Panels")]
    public GameObject chatPanel;
    public GameObject friendsPanel;
    public GameObject questsPanel;
    public GameObject animePanel;
    public GameObject userCatalogPanel;
    public GameObject animeDetailPanel;
    public GameObject matchingPanel;
    public GameObject mapPanel;
    public GameObject tablePanel;
    public GameObject dialoguePanel;

    [Header("Controllers")]
    public NakamaChatPanelController chatPanelController;
    public NakamaFriendsPanelController friendsPanelController;
    public QuestPanelController questPanelController;
    public AnimeCatalogPanelController animeCatalogPanelController;
    public AnimeCatalogPanelController userCatalogPanelController;
    public AnimeDetailPanelController animeDetailPanelController;
    public MatchingPanelController matchingPanelController;
    public MapPanelController mapPanelController;
    public TableViewerPanelController tableViewerPanelController;
    public DialoguePanelController dialoguePanelController;

    [Header("Visuals")]
    public Sprite fantasyWoodenBoardSprite;
    public Sprite closeButtonSprite;
    public Sprite errorExclamationSprite;
    public Font panelTitleFont;

    [Header("Input")]
    public StarterAssetsInputs playerInputs;

    [Header("Mobile")]
    public bool forceMobileControlsInEditor;

    private bool _isUiInteractionEnabled;
    private GameObject _panelWheelRoot;
    private Button _chatToolbarButton;
    private GameObject _chatNotificationBadge;
    private Text _chatNotificationBadgeText;
    private readonly Dictionary<string, int> _chatUnreadByChannel = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _pendingChatNotificationsByChannel = new Dictionary<string, int>();
    private readonly object _chatNotificationLock = new object();
    private GameObject _mobileControls;
    private PlayerInteractor _playerInteractor;
    private GameObject _webGlPointerLockOverlay;
    private bool _webGlPointerLockReady;
    private float _webGlPointerLockRequestedAt;
    private GameObject _errorAlert;
    private Text _errorAlertText;
    private float _hideErrorAlertAt;
    private readonly Queue<string> _pendingErrorAlerts = new Queue<string>();
    private readonly object _pendingErrorAlertLock = new object();
    private static Sprite _mobileCircleSprite;

    private void OnEnable()
    {
        DozzleLogger.ErrorReported += QueueErrorAlert;
        NakamaChatPanelController.ChatMessageReceived += QueueChatNotification;
    }

    private void OnDisable()
    {
        DozzleLogger.ErrorReported -= QueueErrorAlert;
        NakamaChatPanelController.ChatMessageReceived -= QueueChatNotification;
    }

    private void Start()
    {
        if (playerInputs == null)
        {
            playerInputs = FindFirstObjectByType<StarterAssetsInputs>();
        }

        ResolvePreferredFont();
        EnsureChatPanel();
        EnsureFriendsPanel();
        EnsureUserCatalogPanel();
        EnsureAnimeDetailPanel();
        EnsureMatchingPanel();
        EnsureMapPanel();
        EnsureDialoguePanel();
        ConfigurePanelControllers();
        ApplyPanelVisuals();
        AddCloseButtons();
        EnsurePanelWheel();
        EnsureMobileControls();
        EnsureWebGlPointerLockOverlay();
        EnsureErrorAlert();
        HideAll();
        RefreshOverlayVisibility();
    }

    private void Update()
    {
        UpdateErrorAlert();
        FlushChatNotifications();
        ClearVisibleChatChannelNotifications();
        MaintainWebGlPointerLockState();
        RefreshOverlayVisibility();

        if (Keyboard.current == null || IsTextInputFocused()) return;

        if (Keyboard.current.cKey.wasPressedThisFrame) OpenChatPanel();
        if (Keyboard.current.oKey.wasPressedThisFrame) OpenFriendsPanel();
        if (Keyboard.current.qKey.wasPressedThisFrame) OpenQuestsPanel();
        if (Keyboard.current.lKey.wasPressedThisFrame) OpenAnimePanel();
        if (Keyboard.current.uKey.wasPressedThisFrame) OpenUserCatalogPanel();
        if (Keyboard.current.nKey.wasPressedThisFrame) OpenMatchingPanel();
        if (Keyboard.current.mKey.wasPressedThisFrame) OpenMapPanel();
        if (Keyboard.current.tabKey.wasPressedThisFrame) TogglePanelWheel();
    }

    public bool HasAnyPanelOpen()
    {
        return IsAnyPanelOpen();
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
        if (animeCatalogPanelController != null)
        {
            animeCatalogPanelController.defaultLimit = 100;
            animeCatalogPanelController.UseGlobalCatalog();
        }
        ToggleExclusive(animePanel);
        animeCatalogPanelController?.RefreshCatalog();
    }

    public void OpenUserCatalogPanel()
    {
        EnsureUserCatalogPanel();
        userCatalogPanelController?.UseUserCatalog();
        ToggleExclusive(userCatalogPanel);
        userCatalogPanelController?.RefreshCatalog();
    }

    public void OpenAnimeGenrePanel(string genre)
    {
        if (animeCatalogPanelController == null) return;
        animeCatalogPanelController.defaultLimit = 100;
        animeCatalogPanelController.UseGenreCatalog(genre);
        HideAll();
        if (animePanel != null)
        {
            animePanel.SetActive(true);
        }

        RefreshCursorState();
        animeCatalogPanelController.RefreshCatalog();
    }

    public void OpenAnimeSuggestionsPanel()
    {
        if (animeCatalogPanelController == null) return;
        animeCatalogPanelController.UseSuggestedCatalog();
        HideAll();
        if (animePanel != null)
        {
            animePanel.SetActive(true);
        }

        RefreshCursorState();
        animeCatalogPanelController.RefreshCatalog();
    }

    public void OpenMatchingPanel()
    {
        EnsureMatchingPanel();
        ToggleExclusive(matchingPanel);
        matchingPanelController?.RefreshMatches();
    }

    public void OpenMapPanel()
    {
        EnsureMapPanel();
        bool isOpening = ToggleExclusive(mapPanel);
        if (isOpening)
        {
            mapPanelController?.RefreshMap();
        }
    }

    public void OpenMainMenuPanel()
    {
        HideAll();

        var mainMenu = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        if (mainMenu != null)
        {
            mainMenu.ShowLoginPanel();
        }

        RefreshCursorState();
    }

    public void OpenDialoguePanel(string speaker, string body, DialoguePanelController.DialogueOption[] options)
    {
        EnsureDialoguePanel();
        HideAll();
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(true);
        }

        RefreshCursorState();
        dialoguePanelController?.Show(speaker, body, options);
    }

    public void CloseDialoguePanel()
    {
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
        }

        RefreshCursorState();
    }

    public void OpenAnimeDetailPanel(AnimeDetailPanelController.AnimeDetailItem item)
    {
        if (item == null) return;

        EnsureAnimeDetailPanel();
        HideAll();
        if (animeDetailPanel != null)
        {
            animeDetailPanel.SetActive(true);
        }

        RefreshCursorState();
        animeDetailPanelController?.OpenAnime(item);
    }

    public void OpenTablePanel(string tableName)
    {
        ToggleExclusive(tablePanel);
        tableViewerPanelController?.OpenTable(tableName);
    }

    public void OpenCharactersPanel()
    {
        if (tablePanel == null) return;

        var characterController = tablePanel.GetComponent<CharacterPanelController>();
        if (characterController == null)
        {
            characterController = tablePanel.AddComponent<CharacterPanelController>();
        }

        characterController.ConfigureFont(panelTitleFont);
        HideAll();
        tablePanel.SetActive(true);
        RefreshCursorState();
        characterController.RefreshCharacters();
    }

    public void OpenChatPanel()
    {
        EnsureChatPanel();
        string channelKey = chatPanelController != null ? chatPanelController.GeneralChannelKey : NakamaChatPanelController.BuildRoomChannelKey("animequest-lobby");
        bool isOpening = ToggleExclusive(chatPanel);
        if (isOpening)
        {
            ClearChatNotificationBadge(channelKey);
            chatPanelController?.OpenGeneralChat();
        }
    }

    public void ConnectGlobalChatRoom()
    {
        EnsureChatPanel();
        chatPanelController?.ConnectGlobalRoomInBackground();
    }

    public void OpenChatPanelForUser(string userId, string username)
    {
        EnsureChatPanel();
        string channelKey = NakamaChatPanelController.BuildDirectChannelKey(userId);
        ClearChatNotificationBadge(channelKey);
        HideAll();
        if (chatPanel != null)
        {
            chatPanel.SetActive(true);
        }

        RefreshCursorState();
        chatPanelController?.OpenDirectMessage(userId, username);
    }

    public void OpenFriendsPanel()
    {
        EnsureFriendsPanel();
        bool isOpening = ToggleExclusive(friendsPanel);
        if (isOpening)
        {
            friendsPanelController?.RefreshFriends();
        }
    }

    public void ShowErrorAlert(string message)
    {
        EnsureErrorAlert();
        if (_errorAlert == null || _errorAlertText == null) return;

        _errorAlertText.text = string.IsNullOrWhiteSpace(message) ? "Something went wrong." : message;
        _errorAlert.SetActive(true);
        _hideErrorAlertAt = Time.unscaledTime + 4.5f;
    }

    public void HideAll()
    {
        if (chatPanel) chatPanel.SetActive(false);
        if (friendsPanel) friendsPanel.SetActive(false);
        if (questsPanel) questsPanel.SetActive(false);
        if (animePanel) animePanel.SetActive(false);
        if (userCatalogPanel) userCatalogPanel.SetActive(false);
        if (animeDetailPanel) animeDetailPanel.SetActive(false);
        if (matchingPanel) matchingPanel.SetActive(false);
        if (mapPanel) mapPanel.SetActive(false);
        if (tablePanel) tablePanel.SetActive(false);
        if (dialoguePanel) dialoguePanel.SetActive(false);
        HidePanelWheel();

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

    private void EnsureChatPanel()
    {
        if (chatPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_Chat") : null;
            if (existing == null && parent != null)
            {
                existing = parent.Find("ChatPanel");
            }

            if (existing != null)
            {
                chatPanel = existing.gameObject;
            }
        }

        if (chatPanel == null)
        {
            Transform parent = ResolvePanelParent();
            chatPanel = new GameObject("Panel_Chat", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(NakamaChatPanelController));
            chatPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(chatPanel);
        }

        if (chatPanelController == null)
        {
            chatPanelController = chatPanel.GetComponent<NakamaChatPanelController>();
        }

        if (chatPanelController == null)
        {
            chatPanelController = chatPanel.AddComponent<NakamaChatPanelController>();
        }

        chatPanelController.ConfigureFont(panelTitleFont);
        chatPanel.SetActive(false);
    }

    private void EnsureFriendsPanel()
    {
        if (friendsPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_Friends") : null;
            if (existing == null && parent != null)
            {
                existing = parent.Find("FriendsPanel");
            }

            if (existing != null)
            {
                friendsPanel = existing.gameObject;
            }
        }

        if (friendsPanel == null)
        {
            Transform parent = ResolvePanelParent();
            friendsPanel = new GameObject("Panel_Friends", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(NakamaFriendsPanelController));
            friendsPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(friendsPanel);
        }

        if (friendsPanelController == null)
        {
            friendsPanelController = friendsPanel.GetComponent<NakamaFriendsPanelController>();
        }

        if (friendsPanelController == null)
        {
            friendsPanelController = friendsPanel.AddComponent<NakamaFriendsPanelController>();
        }

        friendsPanelController.Configure(this, panelTitleFont);
        friendsPanel.SetActive(false);
    }

    private void EnsureUserCatalogPanel()
    {
        if (userCatalogPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_UserCatalog") : null;
            if (existing != null)
            {
                userCatalogPanel = existing.gameObject;
            }
        }

        if (userCatalogPanel == null)
        {
            Transform parent = ResolvePanelParent();
            userCatalogPanel = new GameObject("Panel_UserCatalog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AnimeCatalogPanelController));
            userCatalogPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(userCatalogPanel);
        }

        if (userCatalogPanelController == null)
        {
            userCatalogPanelController = userCatalogPanel.GetComponent<AnimeCatalogPanelController>();
        }

        if (userCatalogPanelController == null)
        {
            userCatalogPanelController = userCatalogPanel.AddComponent<AnimeCatalogPanelController>();
        }

        userCatalogPanel.name = "Panel_UserCatalog";
        userCatalogPanelController.userCatalogOnly = true;
        userCatalogPanelController.defaultLimit = 100;
        userCatalogPanelController.UseUserCatalog();
        userCatalogPanelController.Configure(this, panelTitleFont);
        userCatalogPanel.SetActive(false);
    }

    private void EnsureMatchingPanel()
    {
        if (matchingPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_Matching") : null;
            if (existing != null)
            {
                matchingPanel = existing.gameObject;
            }
        }

        if (matchingPanel == null)
        {
            Transform parent = ResolvePanelParent();
            matchingPanel = new GameObject("Panel_Matching", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MatchingPanelController));
            matchingPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(matchingPanel);
        }

        if (matchingPanelController == null)
        {
            matchingPanelController = matchingPanel.GetComponent<MatchingPanelController>();
        }

        if (matchingPanelController == null)
        {
            matchingPanelController = matchingPanel.AddComponent<MatchingPanelController>();
        }

        matchingPanel.name = "Panel_Matching";
        matchingPanelController.defaultLimit = 100;
        matchingPanelController.ConfigureFont(panelTitleFont);
        matchingPanel.SetActive(false);
    }

    private void EnsureMapPanel()
    {
        if (mapPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_Map") : null;
            if (existing != null)
            {
                mapPanel = existing.gameObject;
            }
        }

        if (mapPanel == null)
        {
            Transform parent = ResolvePanelParent();
            mapPanel = new GameObject("Panel_Map", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MapPanelController));
            mapPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(mapPanel);
        }

        if (mapPanelController == null)
        {
            mapPanelController = mapPanel.GetComponent<MapPanelController>();
        }

        if (mapPanelController == null)
        {
            mapPanelController = mapPanel.AddComponent<MapPanelController>();
        }

        mapPanel.name = "Panel_Map";
        mapPanelController.ConfigureFont(panelTitleFont);
        mapPanel.SetActive(false);
    }

    private void EnsureAnimeDetailPanel()
    {
        if (animeDetailPanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_AnimeDetail") : null;

            if (existing != null)
            {
                animeDetailPanel = existing.gameObject;
            }
        }

        if (animeDetailPanel == null)
        {
            Transform parent = ResolvePanelParent();
            animeDetailPanel = new GameObject("Panel_AnimeDetail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AnimeDetailPanelController));
            animeDetailPanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(animeDetailPanel);
        }

        if (animeDetailPanelController == null)
        {
            animeDetailPanelController = animeDetailPanel.GetComponent<AnimeDetailPanelController>();
        }

        if (animeDetailPanelController == null)
        {
            animeDetailPanelController = animeDetailPanel.AddComponent<AnimeDetailPanelController>();
        }

        animeDetailPanel.name = "Panel_AnimeDetail";
        animeDetailPanelController.ConfigureFont(panelTitleFont);
        animeDetailPanel.SetActive(false);
    }

    private void EnsureDialoguePanel()
    {
        if (dialoguePanel == null)
        {
            Transform parent = ResolvePanelParent();
            Transform existing = parent != null ? parent.Find("Panel_Dialogue") : null;
            if (existing != null)
            {
                dialoguePanel = existing.gameObject;
            }
        }

        if (dialoguePanel == null)
        {
            Transform parent = ResolvePanelParent();
            dialoguePanel = new GameObject("Panel_Dialogue", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DialoguePanelController));
            dialoguePanel.transform.SetParent(parent != null ? parent : transform, false);
            ConfigureGeneratedPanelRect(dialoguePanel);
        }

        if (dialoguePanelController == null)
        {
            dialoguePanelController = dialoguePanel.GetComponent<DialoguePanelController>();
        }

        if (dialoguePanelController == null)
        {
            dialoguePanelController = dialoguePanel.AddComponent<DialoguePanelController>();
        }

        dialoguePanel.name = "Panel_Dialogue";
        dialoguePanelController.ConfigureFont(panelTitleFont);
        dialoguePanel.SetActive(false);
    }

    private Transform ResolvePanelParent()
    {
        if (animePanel != null && animePanel.transform.parent != null)
        {
            return animePanel.transform.parent;
        }

        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            return canvas.transform;
        }

        return transform;
    }

    private void ConfigureGeneratedPanelRect(GameObject panel)
    {
        var rect = panel.GetComponent<RectTransform>();
        var sourceRect = animePanel != null ? animePanel.GetComponent<RectTransform>() : null;

        if (sourceRect != null)
        {
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            rect.pivot = sourceRect.pivot;
            rect.anchoredPosition = sourceRect.anchoredPosition;
            rect.sizeDelta = sourceRect.sizeDelta;
            rect.offsetMin = sourceRect.offsetMin;
            rect.offsetMax = sourceRect.offsetMax;
        }
        else
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1200f, 720f);
        }
    }

    private void ApplyPanelVisuals()
    {
        if (fantasyWoodenBoardSprite == null) return;

        ApplyPanelSprite(chatPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(friendsPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(questsPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(animePanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(userCatalogPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(animeDetailPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(matchingPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(mapPanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(tablePanel, fantasyWoodenBoardSprite);
        ApplyPanelSprite(dialoguePanel, fantasyWoodenBoardSprite);
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
        AddCloseButton(userCatalogPanel);
        AddCloseButton(animeDetailPanel);
        AddCloseButton(matchingPanel);
        AddCloseButton(mapPanel);
        AddCloseButton(tablePanel);
        AddCloseButton(dialoguePanel);
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

    private void EnsurePanelWheel()
    {
        if (_panelWheelRoot != null) return;

        Transform parent = ResolvePanelParent();
        _panelWheelRoot = new GameObject("PanelWheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        _panelWheelRoot.transform.SetParent(parent != null ? parent : transform, false);
        _panelWheelRoot.transform.SetAsLastSibling();

        var rootRect = _panelWheelRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var rootImage = _panelWheelRoot.GetComponent<Image>();
        rootImage.color = new Color(0f, 0f, 0f, 0.34f);

        var rootButton = _panelWheelRoot.GetComponent<Button>();
        rootButton.transition = Selectable.Transition.None;
        rootButton.onClick.AddListener(HidePanelWheel);

        var hub = new GameObject("WheelHub", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        hub.transform.SetParent(_panelWheelRoot.transform, false);
        var hubRect = hub.GetComponent<RectTransform>();
        hubRect.anchorMin = new Vector2(0.5f, 0.5f);
        hubRect.anchorMax = new Vector2(0.5f, 0.5f);
        hubRect.pivot = new Vector2(0.5f, 0.5f);
        hubRect.anchoredPosition = Vector2.zero;
        hubRect.sizeDelta = new Vector2(190f, 190f);
        hub.GetComponent<Image>().color = new Color(0.16f, 0.09f, 0.03f, 0.78f);

        var title = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        title.transform.SetParent(hub.transform, false);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(14f, 14f);
        titleRect.offsetMax = new Vector2(-14f, -14f);

        var titleText = title.GetComponent<Text>();
        titleText.text = "Panels";
        titleText.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontSize = 28;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = Color.white;
        titleText.raycastTarget = false;

        const float radius = 222f;
        CreateWheelButton("Wheel_MainMenu", "Menu", 90f, radius, OpenMainMenuPanel);
        CreateWheelButton("Wheel_Anime", "Anime", 50f, radius, OpenAnimePanel);
        CreateWheelButton("Wheel_UserCatalog", "My List", 10f, radius, OpenUserCatalogPanel);
        CreateWheelButton("Wheel_Quests", "Quests", -30f, radius, OpenQuestsPanel);
        CreateWheelButton("Wheel_Friends", "Friends", -70f, radius, OpenFriendsPanel);
        _chatToolbarButton = CreateWheelButton("Wheel_Chat", "Chat", -110f, radius, OpenChatPanel);
        CreateWheelButton("Wheel_Matching", "Matches", -150f, radius, OpenMatchingPanel);
        CreateWheelButton("Wheel_Characters", "Chars", 170f, radius, OpenCharactersPanel);
        CreateWheelButton("Wheel_Map", "Map", 130f, radius, OpenMapPanel);
        EnsureChatNotificationBadge();
        RefreshChatNotificationBadge();

        _panelWheelRoot.SetActive(false);
    }

    private Button CreateWheelButton(string name, string label, float angleDegrees, float radius, Action onClick)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(_panelWheelRoot.transform, false);

        float radians = angleDegrees * Mathf.Deg2Rad;
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius);
        rect.sizeDelta = new Vector2(126f, 58f);

        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.48f, 0.28f, 0.12f, 0.94f);

        var button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            HidePanelWheel(refreshCursor: false);
            onClick?.Invoke();
        });

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 2f);
        labelRect.offsetMax = new Vector2(-6f, -2f);

        var text = labelObject.GetComponent<Text>();
        text.text = label;
        text.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 19;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.raycastTarget = false;

        return button;
    }

    private void TogglePanelWheel()
    {
        EnsurePanelWheel();
        bool shouldOpen = _panelWheelRoot != null && !_panelWheelRoot.activeSelf;
        HideAll();
        if (shouldOpen && _panelWheelRoot != null)
        {
            _panelWheelRoot.transform.SetAsLastSibling();
            _panelWheelRoot.SetActive(true);
        }

        RefreshCursorState();
    }

    private void HidePanelWheel()
    {
        HidePanelWheel(refreshCursor: true);
    }

    private void HidePanelWheel(bool refreshCursor)
    {
        if (_panelWheelRoot == null || !_panelWheelRoot.activeSelf) return;
        _panelWheelRoot.SetActive(false);
        if (refreshCursor)
        {
            RefreshCursorState();
        }
    }

    private bool IsPanelWheelOpen()
    {
        return _panelWheelRoot != null && _panelWheelRoot.activeSelf;
    }

    private void QueueChatNotification(string channelKey)
    {
        string key = NormalizeChatChannelKey(channelKey);
        lock (_chatNotificationLock)
        {
            _pendingChatNotificationsByChannel.TryGetValue(key, out int current);
            _pendingChatNotificationsByChannel[key] = current + 1;
        }
    }

    private void FlushChatNotifications()
    {
        Dictionary<string, int> pending;
        lock (_chatNotificationLock)
        {
            if (_pendingChatNotificationsByChannel.Count == 0) return;
            pending = new Dictionary<string, int>(_pendingChatNotificationsByChannel);
            _pendingChatNotificationsByChannel.Clear();
        }

        foreach (var item in pending)
        {
            string key = NormalizeChatChannelKey(item.Key);
            if (!IsChatPanelVisible() || !IsActiveChatChannel(key))
            {
                _chatUnreadByChannel.TryGetValue(key, out int current);
                _chatUnreadByChannel[key] = Mathf.Min(MaxChatBadgeCount, current + item.Value);
            }
        }

        RefreshChatNotificationBadge();
    }

    private void ClearChatNotificationBadge(string channelKey)
    {
        string key = NormalizeChatChannelKey(channelKey);
        lock (_chatNotificationLock)
        {
            _pendingChatNotificationsByChannel.Remove(key);
        }

        _chatUnreadByChannel.Remove(key);
        RefreshChatNotificationBadge();
    }

    private void ClearVisibleChatChannelNotifications()
    {
        if (!IsChatPanelVisible() || chatPanelController == null)
        {
            return;
        }

        string key = NormalizeChatChannelKey(chatPanelController.CurrentChannelKey);
        bool shouldRefresh = false;

        lock (_chatNotificationLock)
        {
            shouldRefresh |= _pendingChatNotificationsByChannel.Remove(key);
        }

        shouldRefresh |= _chatUnreadByChannel.Remove(key);
        if (shouldRefresh)
        {
            RefreshChatNotificationBadge();
        }
    }

    private void EnsureChatNotificationBadge()
    {
        if (_chatToolbarButton == null || _chatNotificationBadge != null) return;

        _chatNotificationBadge = new GameObject("NotificationBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _chatNotificationBadge.transform.SetParent(_chatToolbarButton.transform, false);
        _chatNotificationBadge.transform.SetAsLastSibling();

        var rect = _chatNotificationBadge.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(4f, -4f);
        rect.sizeDelta = new Vector2(24f, 24f);

        var image = _chatNotificationBadge.GetComponent<Image>();
        image.color = new Color(0.86f, 0.07f, 0.05f, 0.96f);
        image.raycastTarget = false;

        var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(_chatNotificationBadge.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _chatNotificationBadgeText = labelObject.GetComponent<Text>();
        _chatNotificationBadgeText.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _chatNotificationBadgeText.alignment = TextAnchor.MiddleCenter;
        _chatNotificationBadgeText.fontSize = 13;
        _chatNotificationBadgeText.fontStyle = FontStyle.Bold;
        _chatNotificationBadgeText.color = Color.white;
        _chatNotificationBadgeText.raycastTarget = false;
        _chatNotificationBadge.SetActive(false);
    }

    private void RefreshChatNotificationBadge()
    {
        if (_chatToolbarButton == null) return;

        EnsureChatNotificationBadge();
        if (_chatNotificationBadge == null || _chatNotificationBadgeText == null) return;

        int unread = GetTotalChatUnreadCount();
        bool hasUnread = unread > 0;
        _chatNotificationBadge.SetActive(hasUnread);
        if (hasUnread)
        {
            _chatNotificationBadgeText.text = unread >= MaxChatBadgeCount ? "99+" : unread.ToString();
        }
    }

    private int GetTotalChatUnreadCount()
    {
        int total = 0;
        foreach (var value in _chatUnreadByChannel.Values)
        {
            total += Mathf.Max(0, value);
            if (total >= MaxChatBadgeCount) return MaxChatBadgeCount;
        }

        return total;
    }

    private bool IsChatPanelVisible()
    {
        return chatPanel != null && chatPanel.activeInHierarchy;
    }

    private bool IsActiveChatChannel(string channelKey)
    {
        return chatPanelController != null &&
               string.Equals(chatPanelController.CurrentChannelKey, NormalizeChatChannelKey(channelKey), StringComparison.Ordinal);
    }

    private static string NormalizeChatChannelKey(string channelKey)
    {
        return string.IsNullOrWhiteSpace(channelKey) ? "chat" : channelKey.Trim();
    }

    private void EnsureMobileControls()
    {
        if (_mobileControls != null) return;

        Transform parent = ResolvePanelParent();
        _mobileControls = new GameObject("MobileControls", typeof(RectTransform));
        _mobileControls.transform.SetParent(parent != null ? parent : transform, false);

        var rootRect = _mobileControls.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        CreateMobileButton("PanelWheelButton", "Panels", new Vector2(0f, 1f), new Vector2(106f, -78f), new Vector2(146f, 70f), new Color(0.48f, 0.28f, 0.12f, 0.88f), TogglePanelWheel, false);

        var moveBase = CreateTouchSurface("MoveStick", _mobileControls.transform, new Vector2(0f, 0f), new Vector2(146f, 146f), new Vector2(188f, 188f), new Color(0.12f, 0.07f, 0.03f, 0.24f));
        var moveKnob = CreateTouchSurface("Knob", moveBase.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 76f), new Color(0.48f, 0.28f, 0.12f, 0.74f));
        moveKnob.GetComponent<Image>().raycastTarget = false;
        var moveControl = moveBase.AddComponent<MobileMoveTouchControl>();
        moveControl.Configure(playerInputs, moveKnob.GetComponent<RectTransform>(), 76f);

        var lookPad = CreateStretchTouchSurface("LookTouchArea", _mobileControls.transform, new Vector2(0.42f, 0f), Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.001f));
        lookPad.transform.SetAsFirstSibling();
        var lookControl = lookPad.AddComponent<MobileLookTouchControl>();
        lookControl.Configure(playerInputs, 0.72f);

        var jumpButton = CreateHoldButton("JumpButton", "J", new Vector2(1f, 0f), new Vector2(-104f, 222f), MobileHoldAction.Jump);
        jumpButton.transform.SetParent(_mobileControls.transform, false);

        var sprintButton = CreateHoldButton("SprintButton", "S", new Vector2(1f, 0f), new Vector2(-220f, 112f), MobileHoldAction.Sprint);
        sprintButton.transform.SetParent(_mobileControls.transform, false);

        CreateMobileButton("InteractButton", "Talk", new Vector2(1f, 0f), new Vector2(-104f, 112f), new Vector2(96f, 96f), new Color(0.78f, 0.18f, 0.07f, 0.9f), TriggerMobileInteract, true);

        _mobileControls.SetActive(false);
    }

    private void EnsureWebGlPointerLockOverlay()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_webGlPointerLockOverlay != null) return;

        Transform parent = ResolvePanelParent();
        _webGlPointerLockOverlay = new GameObject("WebGLPointerLockClickCatcher", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        _webGlPointerLockOverlay.transform.SetParent(parent != null ? parent : transform, false);
        _webGlPointerLockOverlay.transform.SetAsFirstSibling();

        var rect = _webGlPointerLockOverlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = _webGlPointerLockOverlay.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.001f);
        image.raycastTarget = true;

        var button = _webGlPointerLockOverlay.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(RequestWebGlPointerLockFromGesture);
        _webGlPointerLockOverlay.SetActive(false);
#endif
    }

    private GameObject CreateTouchSurface(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = obj.GetComponent<Image>();
        image.color = color;
        if (Mathf.Abs(size.x - size.y) < 0.1f)
        {
            image.sprite = GetMobileCircleSprite();
            image.type = Image.Type.Simple;
        }

        return obj;
    }

    private GameObject CreateStretchTouchSurface(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;

        return obj;
    }

    private GameObject CreateMobileButton(string name, string label, Vector2 anchor, Vector2 position, Vector2 size, Color color, Action onClick, bool circular)
    {
        var obj = CreateTouchSurface(name, _mobileControls.transform, anchor, position, size, color);
        if (!circular)
        {
            var image = obj.GetComponent<Image>();
            image.sprite = null;
        }

        var button = obj.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateMobileButtonColors(color);
        if (onClick != null)
        {
            button.onClick.AddListener(() => onClick());
        }

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(obj.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var text = labelObject.GetComponent<Text>();
        text.text = label;
        text.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = circular ? 22 : 24;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.raycastTarget = false;

        return obj;
    }

    private static ColorBlock CreateMobileButtonColors(Color normal)
    {
        Color pressed = Color.Lerp(normal, Color.black, 0.22f);
        Color highlighted = Color.Lerp(normal, Color.white, 0.12f);

        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = highlighted;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.35f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        return colors;
    }

    private static Sprite GetMobileCircleSprite()
    {
        if (_mobileCircleSprite != null) return _mobileCircleSprite;

        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MobileCircleSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                byte alpha = distance <= radius ? (byte)255 : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        _mobileCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _mobileCircleSprite.name = "MobileCircleSprite";
        return _mobileCircleSprite;
    }

    private GameObject CreateHoldButton(string name, string label, Vector2 anchor, Vector2 position, MobileHoldAction action)
    {
        var obj = CreateTouchSurface(name, _mobileControls.transform, anchor, position, new Vector2(76f, 76f), new Color(0.48f, 0.28f, 0.12f, 0.82f));
        var control = obj.AddComponent<MobileHoldButtonControl>();
        control.Configure(playerInputs, action);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(obj.transform, false);
        var rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = labelObject.GetComponent<Text>();
        text.text = label;
        text.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.raycastTarget = false;

        return obj;
    }

    private void TriggerMobileInteract()
    {
        if (_playerInteractor == null)
        {
            _playerInteractor = FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Exclude);
        }

        _playerInteractor?.TryInteract();
    }

    private void EnsureErrorAlert()
    {
        if (_errorAlert != null) return;

        Transform parent = ResolvePanelParent();
        _errorAlert = new GameObject("ErrorAlert", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _errorAlert.transform.SetParent(parent != null ? parent : transform, false);

        var rect = _errorAlert.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -28f);
        rect.sizeDelta = new Vector2(760f, 86f);

        var background = _errorAlert.GetComponent<Image>();
        background.sprite = fantasyWoodenBoardSprite;
        background.type = Image.Type.Simple;
        background.color = fantasyWoodenBoardSprite != null ? Color.white : new Color(0.22f, 0.11f, 0.05f, 0.92f);
        background.raycastTarget = false;

        var iconObject = new GameObject("RedExclamation", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(_errorAlert.transform, false);
        var iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(28f, 0f);
        iconRect.sizeDelta = new Vector2(56f, 56f);

        var iconImage = iconObject.GetComponent<Image>();
        iconImage.sprite = errorExclamationSprite;
        iconImage.color = errorExclamationSprite != null ? Color.white : new Color(0.86f, 0.07f, 0.05f, 1f);
        iconImage.raycastTarget = false;

        var iconLabel = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        iconLabel.transform.SetParent(iconObject.transform, false);
        var iconLabelRect = iconLabel.GetComponent<RectTransform>();
        iconLabelRect.anchorMin = Vector2.zero;
        iconLabelRect.anchorMax = Vector2.one;
        iconLabelRect.offsetMin = Vector2.zero;
        iconLabelRect.offsetMax = Vector2.zero;

        var iconText = iconLabel.GetComponent<Text>();
        iconText.text = "!";
        iconText.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        iconText.alignment = TextAnchor.MiddleCenter;
        iconText.fontSize = 40;
        iconText.fontStyle = FontStyle.Bold;
        iconText.color = Color.white;
        iconText.raycastTarget = false;
        iconText.enabled = errorExclamationSprite == null;

        var messageObject = new GameObject("Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        messageObject.transform.SetParent(_errorAlert.transform, false);
        var messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0f, 0f);
        messageRect.anchorMax = new Vector2(1f, 1f);
        messageRect.offsetMin = new Vector2(104f, 12f);
        messageRect.offsetMax = new Vector2(-28f, -12f);

        _errorAlertText = messageObject.GetComponent<Text>();
        _errorAlertText.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _errorAlertText.alignment = TextAnchor.MiddleLeft;
        _errorAlertText.fontSize = 24;
        _errorAlertText.color = new Color(0.85f, 0.04f, 0.02f, 1f);
        _errorAlertText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _errorAlertText.verticalOverflow = VerticalWrapMode.Truncate;
        _errorAlertText.raycastTarget = false;

        _errorAlert.SetActive(false);
    }

    private void QueueErrorAlert(string action, string message)
    {
        string actionText = string.IsNullOrWhiteSpace(action) ? "Error" : action;
        string messageText = string.IsNullOrWhiteSpace(message) ? actionText : $"{actionText}: {message}";

        lock (_pendingErrorAlertLock)
        {
            _pendingErrorAlerts.Enqueue(messageText);
        }
    }

    private void UpdateErrorAlert()
    {
        string nextMessage = null;
        lock (_pendingErrorAlertLock)
        {
            if (_pendingErrorAlerts.Count > 0)
            {
                nextMessage = _pendingErrorAlerts.Dequeue();
            }
        }

        if (!string.IsNullOrWhiteSpace(nextMessage))
        {
            ShowErrorAlert(nextMessage);
        }

        if (_errorAlert != null && _errorAlert.activeSelf && Time.unscaledTime >= _hideErrorAlertAt)
        {
            _errorAlert.SetActive(false);
        }
    }

    private void MaintainWebGlPointerLockState()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!ShouldUseWebGlPointerLockOverlay()) return;

        bool gameplayActive = !IsAnyPanelOpen() && !IsMainMenuOpen();
        if (!gameplayActive)
        {
            if (_webGlPointerLockReady)
            {
                _webGlPointerLockReady = false;
                RefreshCursorState();
            }

            return;
        }

        if (_webGlPointerLockReady && Time.unscaledTime - _webGlPointerLockRequestedAt > 0.35f && Cursor.lockState != CursorLockMode.Locked)
        {
            _webGlPointerLockReady = false;
            RefreshCursorState();
        }
#endif
    }

    private void RequestWebGlPointerLockFromGesture()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!ShouldUseWebGlPointerLockOverlay()) return;

        _webGlPointerLockReady = true;
        _webGlPointerLockRequestedAt = Time.unscaledTime;
        _isUiInteractionEnabled = true;
        RefreshCursorState();
#endif
    }

    private void RefreshCursorState()
    {
        bool anyPanelOpen = IsAnyPanelOpen();
        bool mainMenuOpen = IsMainMenuOpen();
        bool gameplayInputEnabled = !anyPanelOpen && !mainMenuOpen;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!gameplayInputEnabled)
        {
            _webGlPointerLockReady = false;
        }
#endif

        RefreshOverlayVisibility(anyPanelOpen, mainMenuOpen);
        _isUiInteractionEnabled = anyPanelOpen || mainMenuOpen;

        bool canLockPointer = CanUsePointerLock();
        bool shouldLockPointer = gameplayInputEnabled && canLockPointer && ShouldLockPointerNow();

        Cursor.lockState = shouldLockPointer ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLockPointer;

        if (playerInputs != null)
        {
            playerInputs.cursorLocked = shouldLockPointer;
            playerInputs.cursorInputForLook = shouldLockPointer;
            playerInputs.movementInputEnabled = gameplayInputEnabled;
            if (!gameplayInputEnabled)
            {
                playerInputs.LookInput(Vector2.zero);
                playerInputs.MoveInput(Vector2.zero);
                playerInputs.JumpInput(false);
                playerInputs.SprintInput(false);
            }
        }
    }

    private bool ShouldLockPointerNow()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return _webGlPointerLockReady;
#else
        return true;
#endif
    }

    private static bool CanUsePointerLock()
    {
        return !Application.isMobilePlatform;
    }

    private void RefreshOverlayVisibility()
    {
        RefreshOverlayVisibility(IsAnyPanelOpen(), IsMainMenuOpen());
    }

    private void RefreshOverlayVisibility(bool anyPanelOpen)
    {
        RefreshOverlayVisibility(anyPanelOpen, IsMainMenuOpen());
    }

    private void RefreshOverlayVisibility(bool anyPanelOpen, bool mainMenuOpen)
    {
        if (_playerInteractor == null)
        {
            _playerInteractor = FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Exclude);
        }
        _playerInteractor?.SetPromptSuppressed(anyPanelOpen || mainMenuOpen);

        if (_mobileControls != null)
        {
            bool showMobileControls = ShouldUseMobileControls() && !mainMenuOpen && !anyPanelOpen;
            _mobileControls.SetActive(showMobileControls);
            if (showMobileControls)
            {
                _mobileControls.transform.SetAsLastSibling();
            }
        }

        RefreshWebGlPointerLockOverlay(anyPanelOpen, mainMenuOpen);
    }

    private void RefreshWebGlPointerLockOverlay(bool anyPanelOpen, bool mainMenuOpen)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_webGlPointerLockOverlay == null) return;

        bool showOverlay = ShouldUseWebGlPointerLockOverlay() && !mainMenuOpen && !anyPanelOpen && Cursor.lockState != CursorLockMode.Locked;
        _webGlPointerLockOverlay.SetActive(showOverlay);
#endif
    }

    private bool ShouldUseMobileControls()
    {
#if UNITY_EDITOR
        return forceMobileControlsInEditor;
#elif UNITY_ANDROID
        return true;
#else
        return false;
#endif
    }

    private static bool ShouldUseWebGlPointerLockOverlay()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return !Application.isMobilePlatform;
#else
        return false;
#endif
    }

    private static bool IsMainMenuOpen()
    {
        var mainMenu = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        return mainMenu != null && mainMenu.gameObject.activeInHierarchy;
    }

    private bool IsAnyPanelOpen()
    {
        return (chatPanel && chatPanel.activeSelf) ||
               (friendsPanel && friendsPanel.activeSelf) ||
               (questsPanel && questsPanel.activeSelf) ||
               (animePanel && animePanel.activeSelf) ||
               (userCatalogPanel && userCatalogPanel.activeSelf) ||
               (animeDetailPanel && animeDetailPanel.activeSelf) ||
               (matchingPanel && matchingPanel.activeSelf) ||
               (mapPanel && mapPanel.activeSelf) ||
               (tablePanel && tablePanel.activeSelf) ||
               (dialoguePanel && dialoguePanel.activeSelf) ||
               IsPanelWheelOpen();
    }

    private static bool IsTextInputFocused()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
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

        text.font = panelTitleFont != null ? panelTitleFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 34;
        text.color = new Color(0.16f, 0.09f, 0.03f, 1f);
    }

    private void ConfigurePanelControllers()
    {
        chatPanelController?.ConfigureFont(panelTitleFont);
        friendsPanelController?.Configure(this, panelTitleFont);
        questPanelController?.ConfigureFont(panelTitleFont);
        if (animeCatalogPanelController != null)
        {
            animeCatalogPanelController.Configure(this, panelTitleFont);
        }
        if (animeCatalogPanelController != null)
        {
            animeCatalogPanelController.userCatalogOnly = false;
            animeCatalogPanelController.defaultLimit = 100;
            animeCatalogPanelController.UseGlobalCatalog();
        }
        if (userCatalogPanelController != null)
        {
            userCatalogPanelController.Configure(this, panelTitleFont);
        }
        if (userCatalogPanelController != null)
        {
            userCatalogPanelController.userCatalogOnly = true;
            userCatalogPanelController.defaultLimit = 100;
            userCatalogPanelController.UseUserCatalog();
        }
        animeDetailPanelController?.ConfigureFont(panelTitleFont);
        matchingPanelController?.ConfigureFont(panelTitleFont);
        mapPanelController?.ConfigureFont(panelTitleFont);
        dialoguePanelController?.ConfigureFont(panelTitleFont);
        if (matchingPanelController != null)
        {
            matchingPanelController.defaultLimit = 100;
        }
        tableViewerPanelController?.ConfigureFont(panelTitleFont);
    }

    private void ResolvePreferredFont()
    {
        if (panelTitleFont != null) return;

#if UNITY_EDITOR
        panelTitleFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/BMYEONSUNG_ttf.ttf");
#endif

        if (panelTitleFont == null) panelTitleFont = FindLoadedFont("BMYEONSUNG_ttf");
        if (panelTitleFont == null) panelTitleFont = FindLoadedFont("BMYEONSUNG");

        if (panelTitleFont == null)
        {
            panelTitleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    private static Font FindLoadedFont(string fontNamePart)
    {
        var loadedFonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (var loadedFont in loadedFonts)
        {
            if (loadedFont != null && loadedFont.name.IndexOf(fontNamePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        return null;
    }
}

public enum MobileHoldAction
{
    Jump,
    Sprint
}

public class MobileMoveTouchControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private StarterAssetsInputs _inputs;
    private RectTransform _rect;
    private RectTransform _knob;
    private float _radius = 68f;
    private int _pointerId = int.MinValue;

    public void Configure(StarterAssetsInputs inputs, RectTransform knob, float radius)
    {
        _inputs = inputs;
        _knob = knob;
        _radius = Mathf.Max(1f, radius);
    }

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerId = eventData.pointerId;
        UpdateDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_pointerId != eventData.pointerId) return;
        UpdateDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_pointerId != eventData.pointerId) return;
        ResetInput();
    }

    private void UpdateDrag(PointerEventData eventData)
    {
        if (_rect == null) _rect = GetComponent<RectTransform>();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint)) return;

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, _radius);
        if (_knob != null)
        {
            _knob.anchoredPosition = clamped;
        }

        _inputs?.MoveInput(clamped / _radius);
    }

    private void ResetInput()
    {
        _pointerId = int.MinValue;
        if (_knob != null)
        {
            _knob.anchoredPosition = Vector2.zero;
        }

        _inputs?.MoveInput(Vector2.zero);
    }

    private void OnDisable()
    {
        ResetInput();
    }
}

public class MobileLookTouchControl : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private StarterAssetsInputs _inputs;
    private float _sensitivity = 0.12f;
    private int _pointerId = int.MinValue;
    private bool _dragging;

    public void Configure(StarterAssetsInputs inputs, float sensitivity)
    {
        _inputs = inputs;
        _sensitivity = Mathf.Max(0.01f, sensitivity);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerId = eventData.pointerId;
        _dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_pointerId != eventData.pointerId) return;
        _inputs?.LookInput(eventData.delta * _sensitivity);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (_pointerId != eventData.pointerId) return;
        ResetInput();
    }

    private void Update()
    {
        if (!_dragging)
        {
            _inputs?.LookInput(Vector2.zero);
        }
    }

    private void ResetInput()
    {
        _pointerId = int.MinValue;
        _dragging = false;
        _inputs?.LookInput(Vector2.zero);
    }

    private void OnDisable()
    {
        ResetInput();
    }
}

public class MobileHoldButtonControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private StarterAssetsInputs _inputs;
    private MobileHoldAction _action;
    private bool _pressed;

    public void Configure(StarterAssetsInputs inputs, MobileHoldAction action)
    {
        _inputs = inputs;
        _action = action;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pressed = true;
        Apply(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pressed = false;
        Apply(false);
    }

    private void OnDisable()
    {
        if (!_pressed) return;
        _pressed = false;
        Apply(false);
    }

    private void Apply(bool value)
    {
        if (_inputs == null) return;

        if (_action == MobileHoldAction.Jump)
        {
            _inputs.JumpInput(value);
        }
        else if (_action == MobileHoldAction.Sprint)
        {
            _inputs.SprintInput(value);
        }
    }
}
