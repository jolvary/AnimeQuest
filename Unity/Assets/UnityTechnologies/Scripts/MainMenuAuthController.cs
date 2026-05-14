using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StarterAssets;
using System;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MainMenuAuthController : MonoBehaviour
{
    private static readonly Color InkColor = new Color(0.16f, 0.08f, 0.03f, 1f);
    private static readonly Color MutedInkColor = new Color(0.29f, 0.20f, 0.12f, 1f);
    private static readonly Color PanelSurfaceColor = new Color(0.67f, 0.43f, 0.22f, 0.18f);
    private static readonly Color FieldColor = new Color(0.95f, 0.86f, 0.66f, 0.88f);
    private static readonly Color FieldPlaceholderColor = new Color(0.42f, 0.32f, 0.22f, 0.72f);
    private static readonly Color FrameColor = new Color(0.23f, 0.13f, 0.06f, 0.92f);
    private static readonly Color PrimaryButtonColor = new Color(0.25f, 0.48f, 0.22f, 0.96f);
    private static readonly Color SecondaryButtonColor = new Color(0.35f, 0.45f, 0.56f, 0.94f);
    private static readonly Color DangerButtonColor = new Color(0.57f, 0.20f, 0.15f, 0.96f);
    private static readonly Color AccentButtonColor = new Color(0.60f, 0.40f, 0.17f, 0.96f);
    private static readonly Color ButtonTextColor = new Color(1f, 0.94f, 0.80f, 1f);
    private static readonly Color StatusColor = new Color(0.52f, 0.08f, 0.03f, 1f);

    [Header("Visual style")]
    public Sprite panelSprite;
    public Font panelFont;

    [Header("References")]
    public UIManager uiManager;
    public AnimeCatalogPanelController animeCatalogPanelController;
    public AnimeCatalogPanelController userCatalogPanelController;

    [Header("Startup")]
    public bool openOnStart;

    [Header("Events")]
    public UnityEvent<string, string> onLoginRequested = new UnityEvent<string, string>();
    public UnityEvent<string, string> onRegisterRequested = new UnityEvent<string, string>();
    public UnityEvent onIncognitoRequested = new UnityEvent();
    public UnityEvent onLogoutRequested = new UnityEvent();

    private GameObject _loginPanel;
    private GameObject _registerPanel;
    private GameObject _loggedInPanel;

    private InputField _loginUsername;
    private InputField _loginPassword;
    private Text _loginStatus;

    private InputField _registerUsername;
    private InputField _registerPassword;
    private Text _registerStatus;
    private Button _linkMalButton;
    private Button _importMalButton;
    private Text _malImportStatus;
    private StarterAssetsInputs _playerInputs;
    private bool _isMalLinked;
    private bool _isPollingMalLink;
    private bool _isRefreshingMalStatus;
    private bool _authRequestInProgress;
    private bool _hasSpawnPoint;
    private bool _isInitialized;
    private bool _wasExplicitlyOpened;
    private Vector3 _spawnPosition;
    private float _spawnRotationY;


    private void Awake()
    {
        EnsureCanvasRoot();
        EnsureEventSystemExists();
    }

    private void Start()
    {
        InitializeIfNeeded();
        if (openOnStart || _wasExplicitlyOpened)
        {
            ShowLoginPanel();
        }
        else
        {
            PrepareHidden();
        }
    }

    public void PrepareHidden()
    {
        InitializeIfNeeded();
        HideAuthPanels();
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
        else
        {
            SetPlayerMovementEnabled(true);
        }
    }

    public void ShowLoginPanel()
    {
        _wasExplicitlyOpened = true;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        InitializeIfNeeded();
        bool isLoggedIn = NakamaAuthManager.Instance != null && NakamaAuthManager.Instance.IsAuthenticated && !NakamaAuthManager.Instance.IsIncognitoSession;

        if (_loginPanel != null) _loginPanel.SetActive(!isLoggedIn);
        if (_registerPanel != null) _registerPanel.SetActive(false);
        if (_loggedInPanel != null) _loggedInPanel.SetActive(isLoggedIn);
        if (_loginStatus != null && !_authRequestInProgress) _loginStatus.text = string.Empty;
        RefreshInteractionState();

        if (isLoggedIn)
        {
            _authRequestInProgress = false;
            RefreshMyAnimeListStatus();
        }
    }

    public void ShowRegisterPanel()
    {
        InitializeIfNeeded();
        if (_authRequestInProgress)
        {
            SetLoginStatus("Please wait for login to finish.");
            return;
        }

        if (_loginPanel != null) _loginPanel.SetActive(false);
        if (_registerPanel != null) _registerPanel.SetActive(true);
        if (_loggedInPanel != null) _loggedInPanel.SetActive(false);
        if (_registerStatus != null) _registerStatus.text = string.Empty;
        RefreshInteractionState();
    }

    private void InitializeIfNeeded()
    {
        if (_isInitialized) return;

        EnsureCanvasRoot();
        EnsureEventSystemExists();

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        }

        if (animeCatalogPanelController == null)
        {
            animeCatalogPanelController = FindFirstObjectByType<AnimeCatalogPanelController>(FindObjectsInactive.Include);
        }

        ResolveUserCatalogController();
        ResolveVisualStyle();
        BuildPanels();
        CaptureSpawnPointIfNeeded();
        _isInitialized = true;
    }

    private void HideAuthPanels()
    {
        if (_loginPanel != null) _loginPanel.SetActive(false);
        if (_registerPanel != null) _registerPanel.SetActive(false);
        if (_loggedInPanel != null) _loggedInPanel.SetActive(false);
    }


    private void EnsureCanvasRoot()
    {
        if (GetComponent<Canvas>() == null)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
        }

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }
    }

    private static void EnsureEventSystemExists()
    {
        var existingEventSystem = FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (existingEventSystem != null)
        {
            if (existingEventSystem.GetComponent<BaseInputModule>() == null)
            {
                existingEventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            return;
        }

        var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

#if ENABLE_INPUT_SYSTEM
        eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#endif
    }

    private void ResolveVisualStyle()
    {
        if (panelSprite == null && uiManager != null)
        {
            panelSprite = uiManager.fantasyWoodenBoardSprite;
        }

        panelFont = ResolvePanelFont(panelFont, uiManager != null ? uiManager.panelTitleFont : null);
        if (uiManager != null && uiManager.panelTitleFont == null && IsPreferredPanelFont(panelFont))
        {
            uiManager.panelTitleFont = panelFont;
        }
    }

    private void ResolveUserCatalogController()
    {
        if (userCatalogPanelController == null && uiManager != null)
        {
            userCatalogPanelController = uiManager.userCatalogPanelController;
        }

        if (userCatalogPanelController != null) return;

        var controllers = FindObjectsByType<AnimeCatalogPanelController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var controller in controllers)
        {
            if (controller != null && controller.userCatalogOnly)
            {
                userCatalogPanelController = controller;
                return;
            }
        }
    }

    private void BuildPanels()
    {
        _loginPanel = CreatePanel("LoginPanel", true);
        _registerPanel = CreatePanel("RegisterPanel", false);

        _loggedInPanel = CreatePanel("LoggedInPanel", false);

        BuildLoginPanel(_loginPanel.transform);
        BuildRegisterPanel(_registerPanel.transform);
        BuildLoggedInPanel(_loggedInPanel.transform);
    }

    private GameObject CreatePanel(string name, bool active)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(transform, false);

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(920f, 560f);

        var image = panel.GetComponent<Image>();
        image.sprite = panelSprite;
        image.type = Image.Type.Simple;
        image.color = Color.white;

        panel.SetActive(active);
        return panel;
    }

    private void BuildLoginPanel(Transform parent)
    {
        CreateHeader(parent, "AnimeQuest");
        CreateSubHeader(parent, "Main Menu");
        CreateSurface(parent, "LoginSurface", new Vector2(0.5f, 0.48f), new Vector2(650f, 250f));

        _loginUsername = CreateInput(parent, "UsernameInput", new Vector2(0.5f, 0.58f), "Username");
        _loginPassword = CreateInput(parent, "PasswordInput", new Vector2(0.5f, 0.45f), "Password", true);

        CreateButton(parent, "LoginButton", "Login", new Vector2(0.5f, 0.32f), new Vector2(250f, 52f), PrimaryButtonColor, OnLoginPressed);
        CreateButton(parent, "IncognitoButton", "Incognito", new Vector2(0.32f, 0.20f), new Vector2(230f, 46f), SecondaryButtonColor, OnIncognitoPressed);
        CreateButton(parent, "RegisterNavButton", "Create Account", new Vector2(0.68f, 0.20f), new Vector2(250f, 46f), DangerButtonColor, ShowRegisterPanel);
        CreateCloseButton(parent);

        _loginStatus = CreateLabel(parent, "LoginStatus", new Vector2(0.5f, 0.11f), new Vector2(620f, 34f), 21, StatusColor, string.Empty, FontStyle.Bold);
    }

    private void BuildRegisterPanel(Transform parent)
    {
        CreateHeader(parent, "AnimeQuest");
        CreateSubHeader(parent, "Create Account");
        CreateSurface(parent, "RegisterSurface", new Vector2(0.5f, 0.48f), new Vector2(650f, 250f));
        CreateCloseButton(parent);

        _registerUsername = CreateInput(parent, "RegisterUsernameInput", new Vector2(0.5f, 0.58f), "Username");
        _registerPassword = CreateInput(parent, "RegisterPasswordInput", new Vector2(0.5f, 0.45f), "Password", true);

        CreateButton(parent, "CreateAccountButton", "Create Account", new Vector2(0.5f, 0.32f), new Vector2(270f, 52f), PrimaryButtonColor, OnRegisterPressed);
        CreateButton(parent, "GoToLoginButton", "Back to Login", new Vector2(0.5f, 0.20f), new Vector2(240f, 46f), SecondaryButtonColor, ShowLoginPanel);

        _registerStatus = CreateLabel(parent, "RegisterStatus", new Vector2(0.5f, 0.11f), new Vector2(620f, 34f), 21, StatusColor, string.Empty, FontStyle.Bold);
    }



    private void BuildLoggedInPanel(Transform parent)
    {
        CreateHeader(parent, "AnimeQuest");
        CreateSubHeader(parent, "Account");
        CreateSurface(parent, "AccountSurface", new Vector2(0.5f, 0.48f), new Vector2(690f, 280f));
        CreateCloseButton(parent);
        CreateLabel(parent, "LoggedInLabel", new Vector2(0.5f, 0.61f), new Vector2(700f, 34f), 22, InkColor, "Signed in", FontStyle.Bold);
        _linkMalButton = CreateButton(parent, "LinkMalButton", "Link MyAnimeList", new Vector2(0.5f, 0.49f), new Vector2(340f, 50f), SecondaryButtonColor, OnLinkMyAnimeListPressed);
        _importMalButton = CreateButton(parent, "ImportMalButton", "Import List", new Vector2(0.5f, 0.38f), new Vector2(260f, 50f), PrimaryButtonColor, OnImportMyAnimeListPressed);
        _malImportStatus = CreateLabel(parent, "MalImportStatus", new Vector2(0.5f, 0.27f), new Vector2(700f, 36f), 20, MutedInkColor, string.Empty);
        CreateButton(parent, "UnstuckButton", "Unstuck", new Vector2(0.34f, 0.15f), new Vector2(210f, 48f), AccentButtonColor, OnUnstuckPressed);
        CreateButton(parent, "LogoutButton", "Log out", new Vector2(0.66f, 0.15f), new Vector2(210f, 48f), DangerButtonColor, OnLogoutPressed);
        ApplyMyAnimeListStatus(null);
    }


    private void CreateCloseButton(Transform parent)
    {
        var closeButtonObject = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        closeButtonObject.transform.SetParent(parent, false);

        var rect = closeButtonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(48f, 48f);

        var image = closeButtonObject.GetComponent<Image>();
        image.sprite = uiManager != null ? uiManager.closeButtonSprite : null;
        image.type = Image.Type.Simple;
        image.color = Color.white;

        var button = closeButtonObject.GetComponent<Button>();
        button.onClick.AddListener(OnClosePressed);

        if (image.sprite == null)
        {
            var fallbackText = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            fallbackText.transform.SetParent(closeButtonObject.transform, false);

            var fallbackRect = fallbackText.GetComponent<RectTransform>();
            fallbackRect.anchorMin = Vector2.zero;
            fallbackRect.anchorMax = Vector2.one;
            fallbackRect.offsetMin = Vector2.zero;
            fallbackRect.offsetMax = Vector2.zero;

            var text = fallbackText.GetComponent<Text>();
            text.text = "X";
            text.font = panelFont != null ? panelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 28;
            text.color = Color.black;
        }
    }
    public void SetLoginStatus(string message)
    {
        if (_loginStatus != null)
        {
            _loginStatus.text = message ?? string.Empty;
        }

        ShowStatusErrorIfNeeded(message);
        UpdateAuthProgressFromStatus(message);
    }

    public void SetRegisterStatus(string message)
    {
        if (_registerStatus != null)
        {
            _registerStatus.text = message ?? string.Empty;
        }

        ShowStatusErrorIfNeeded(message);
        UpdateAuthProgressFromStatus(message);
    }

    private void ShowStatusErrorIfNeeded(string message)
    {
        if (!IsErrorStatus(message)) return;
        uiManager?.ShowErrorAlert(message);
    }

    private static bool IsErrorStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        string normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("failed") ||
               normalized.Contains("wrong") ||
               normalized.Contains("required") ||
               normalized.Contains("unavailable") ||
               normalized.Contains("reconnect") ||
               normalized.Contains("error");
    }

    private void UpdateAuthProgressFromStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        string normalized = message.Trim().ToLowerInvariant();
        if (normalized.Contains("failed") || normalized.Contains("successful") || normalized.Contains("ready") || normalized.Contains("logged out") || normalized.Contains("required") || normalized.Contains("wrong") || normalized.Contains("error") || normalized.Contains("unavailable"))
        {
            _authRequestInProgress = false;
        }
    }

    private void OnLoginPressed()
    {
        string username = _loginUsername != null ? _loginUsername.text.Trim() : string.Empty;
        string password = _loginPassword != null ? _loginPassword.text : string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _authRequestInProgress = false;
            SetLoginStatus("WRONG USERNAME OR PASSWORD");
            return;
        }

        _authRequestInProgress = true;
        SetLoginStatus("Login requested...");
        DozzleLogger.Action("Login requested", $"username={username}");
        onLoginRequested?.Invoke(username, password);
    }

    private void OnRegisterPressed()
    {
        string username = _registerUsername != null ? _registerUsername.text.Trim() : string.Empty;
        string password = _registerPassword != null ? _registerPassword.text : string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _authRequestInProgress = false;
            SetRegisterStatus("USERNAME AND PASSWORD REQUIRED");
            return;
        }

        _authRequestInProgress = true;
        SetRegisterStatus("Registering account...");
        DozzleLogger.Action("Register requested", $"username={username}");
        onRegisterRequested?.Invoke(username, password);
    }

    private void OnLogoutPressed()
    {
        _authRequestInProgress = false;
        DozzleLogger.Action("Logout requested");
        onLogoutRequested?.Invoke();
    }

    private void OnUnstuckPressed()
    {
        Transform player = ResolveLocalPlayer();
        if (player == null)
        {
            DozzleLogger.Error("Unstuck failed", "No local player transform found.");
            uiManager?.ShowErrorAlert("Could not find the player to unstuck.");
            return;
        }

        CaptureSpawnPointIfNeeded();
        Vector3 spawnPosition = ResolveSpawnPosition();
        float spawnRotationY = ResolveSpawnRotationY();
        TeleportPlayer(player, spawnPosition, spawnRotationY);
        DozzleLogger.Action("Player unstuck requested", $"x={spawnPosition.x:0.##};y={spawnPosition.y:0.##};z={spawnPosition.z:0.##};rotationY={spawnRotationY:0.##}");
    }

    private async void OnLinkMyAnimeListPressed()
    {
        if (ApiClient.Instance == null || _isPollingMalLink)
        {
            return;
        }


        try
        {
            SetMyAnimeListButtons(false);
            if (_malImportStatus != null) _malImportStatus.text = "Opening MyAnimeList authorization...";
            string url = await ApiClient.Instance.StartMyAnimeListLink();
            Application.OpenURL(url);
            DozzleLogger.Action("MAL link opened");
            await PollMyAnimeListLinkStatus();
        }
        catch (Exception ex)
        {
            _isMalLinked = false;
            if (_malImportStatus != null) _malImportStatus.text = "MyAnimeList link failed";
            DozzleLogger.Error("MAL link failed", ex);
            ApplyMyAnimeListStatus(null);
        }
    }

    private async void RefreshMyAnimeListStatus()
    {
        if (ApiClient.Instance == null || _malImportStatus == null || _isRefreshingMalStatus)
        {
            return;
        }

        _isRefreshingMalStatus = true;
        try
        {
            SetMyAnimeListButtons(false);
            _malImportStatus.text = "Checking MyAnimeList link...";
            var status = await ApiClient.Instance.GetMyAnimeListOAuthStatus();
            ApplyMyAnimeListStatus(status);
        }
        catch (Exception ex)
        {
            _isMalLinked = false;
            _malImportStatus.text = "MyAnimeList status unavailable";
            DozzleLogger.Error("MAL status failed", ex);
            SetMyAnimeListButtons(true);
        }
        finally
        {
            _isRefreshingMalStatus = false;
        }
    }

    private async Task PollMyAnimeListLinkStatus()
    {
        _isPollingMalLink = true;
        try
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(2000);
                if (ApiClient.Instance == null)
                {
                    return;
                }

                var status = await ApiClient.Instance.GetMyAnimeListOAuthStatus();
                if (status != null && status.linked)
                {
                    ApplyMyAnimeListStatus(status);
                    return;
                }

                if (_malImportStatus != null)
                {
                    _malImportStatus.text = "Waiting for MyAnimeList authorization...";
                }
            }

            var finalStatus = ApiClient.Instance != null ? await ApiClient.Instance.GetMyAnimeListOAuthStatus() : null;
            ApplyMyAnimeListStatus(finalStatus);
            if (!_isMalLinked && _malImportStatus != null) _malImportStatus.text = "Finish authorization in your browser.";
        }
        finally
        {
            _isPollingMalLink = false;
        }
    }

    private async void OnImportMyAnimeListPressed()
    {
        if (!_isMalLinked)
        {
            if (_malImportStatus != null) _malImportStatus.text = "Link MyAnimeList first.";
            return;
        }

        try
        {
            SetMyAnimeListButtons(false);
            if (_malImportStatus != null) _malImportStatus.text = "Importing from MyAnimeList...";
            await ApiClient.Instance.ImportMyAnimeList();
            DozzleLogger.Action("MAL import completed");
            if (_malImportStatus != null) _malImportStatus.text = "Import complete.";
            animeCatalogPanelController?.RefreshCatalog();
            ResolveUserCatalogController();
            userCatalogPanelController?.RefreshCatalog();
            RefreshMyAnimeListStatus();
            gameObject.SetActive(false);
            uiManager?.OpenUserCatalogPanel();
        }
        catch (System.Exception ex)
        {
            bool needsReconnect = ex.Message.IndexOf("reconnect", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("not linked", StringComparison.OrdinalIgnoreCase) >= 0;
            if (needsReconnect)
            {
                _isMalLinked = false;
                if (_malImportStatus != null) _malImportStatus.text = "Reconnect MyAnimeList.";
                ApplyMyAnimeListStatus(null);
            }
            else if (_malImportStatus != null)
            {
                _malImportStatus.text = "Import failed";
            }

            DozzleLogger.Error("MAL import failed", ex);
            SetMyAnimeListButtons(true);
        }
    }

    private void ApplyMyAnimeListStatus(ApiClient.MalOAuthStatusResponse status)
    {
        if (status != null && !status.configured)
        {
            _isMalLinked = false;
            if (_malImportStatus != null)
            {
                _malImportStatus.text = "MyAnimeList linking unavailable on this server.";
            }

            SetButtonLabel(_linkMalButton, "MyAnimeList unavailable");
            SetButtonInteractable(_linkMalButton, false);
            SetButtonInteractable(_importMalButton, false);
            return;
        }

        _isMalLinked = status != null && status.linked && !status.reconnectRequired;
        string linkedName = !string.IsNullOrWhiteSpace(status?.malUsername) ? $": {status.malUsername}" : string.Empty;

        if (_malImportStatus != null)
        {
            if (_isMalLinked)
            {
                _malImportStatus.text = $"MyAnimeList linked{linkedName}.";
            }
            else if (status != null && status.reconnectRequired)
            {
                _malImportStatus.text = "Reconnect MyAnimeList.";
            }
            else
            {
                _malImportStatus.text = "MyAnimeList not linked.";
            }
        }

        SetButtonLabel(_linkMalButton, _isMalLinked ? "Reconnect MyAnimeList" : "Link MyAnimeList account");
        SetButtonInteractable(_linkMalButton, true);
        SetButtonInteractable(_importMalButton, _isMalLinked);
    }

    private void SetMyAnimeListButtons(bool linkEnabled)
    {
        SetButtonInteractable(_linkMalButton, linkEnabled);
        SetButtonInteractable(_importMalButton, linkEnabled && _isMalLinked);
    }

    private void SetButtonInteractable(Button button, bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }
    }

    private void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;
        var text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
        }
    }

    private void OnIncognitoPressed()
    {
        if (_authRequestInProgress)
        {
            SetLoginStatus("Please wait for login to finish.");
            DozzleLogger.Action("Incognito blocked", "auth request in progress");
            return;
        }

        DozzleLogger.Action("Incognito requested from main menu");
        if (animeCatalogPanelController != null)
        {
            animeCatalogPanelController.SetIncognitoMode(true);
        }
        if (userCatalogPanelController != null)
        {
            userCatalogPanelController.SetIncognitoMode(true);
        }

        onIncognitoRequested?.Invoke();
        uiManager?.HideAll();

        gameObject.SetActive(false);
    }

    private void OnClosePressed()
    {
        if (_authRequestInProgress)
        {
            SetLoginStatus("Please wait for login to finish.");
            DozzleLogger.Action("Main menu close blocked", "auth request in progress");
            return;
        }

        bool isLoggedIn = NakamaAuthManager.Instance != null && NakamaAuthManager.Instance.IsAuthenticated && !NakamaAuthManager.Instance.IsIncognitoSession;
        DozzleLogger.Action("Main menu close pressed", $"isLoggedIn={isLoggedIn}");

        if (!isLoggedIn)
        {
            OnIncognitoPressed();
            return;
        }

        uiManager?.HideAll();
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!_isInitialized) return;

        CaptureSpawnPointIfNeeded();
        RefreshInteractionState();
        if (_loggedInPanel != null && _loggedInPanel.activeSelf)
        {
            RefreshMyAnimeListStatus();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) return;
        if (!_isInitialized) return;

        CaptureSpawnPointIfNeeded();
        RefreshInteractionState();
        if (_loggedInPanel != null && _loggedInPanel.activeSelf)
        {
            RefreshMyAnimeListStatus();
        }
    }

    private void OnDisable()
    {
        bool canLockPointer = CanUsePointerLock();
        Cursor.lockState = canLockPointer ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !canLockPointer;
        SetPlayerMovementEnabled(true);
    }

    private void RefreshInteractionState()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SetPlayerMovementEnabled(false);
    }

    private void SetPlayerMovementEnabled(bool enabled)
    {
        if (_playerInputs == null)
        {
            _playerInputs = FindFirstObjectByType<StarterAssetsInputs>(FindObjectsInactive.Include);
            if (_playerInputs == null) return;
        }

        bool pointerLookEnabled = enabled && CanUsePointerLock();
        _playerInputs.cursorLocked = pointerLookEnabled;
        _playerInputs.cursorInputForLook = pointerLookEnabled;
        _playerInputs.movementInputEnabled = enabled;

        if (!enabled)
        {
            _playerInputs.MoveInput(Vector2.zero);
            _playerInputs.LookInput(Vector2.zero);
            _playerInputs.JumpInput(false);
            _playerInputs.SprintInput(false);
        }
    }

    private void CaptureSpawnPointIfNeeded()
    {
        if (_hasSpawnPoint) return;

        Transform player = ResolveLocalPlayer();
        if (player == null) return;

        _spawnPosition = player.position;
        _spawnRotationY = player.eulerAngles.y;
        _hasSpawnPoint = true;
        DozzleLogger.Action("Unstuck spawn captured", $"x={_spawnPosition.x:0.##};y={_spawnPosition.y:0.##};z={_spawnPosition.z:0.##};rotationY={_spawnRotationY:0.##}");
    }

    private Vector3 ResolveSpawnPosition()
    {
        if (TryFindSpawnTransform(out Transform spawn))
        {
            return spawn.position;
        }

        return _hasSpawnPoint ? _spawnPosition : new Vector3(0f, 2f, 0f);
    }

    private float ResolveSpawnRotationY()
    {
        if (TryFindSpawnTransform(out Transform spawn))
        {
            return spawn.eulerAngles.y;
        }

        return _hasSpawnPoint ? _spawnRotationY : 0f;
    }

    private static bool TryFindSpawnTransform(out Transform spawn)
    {
        spawn = null;

        string[] tags = { "Respawn", "Spawn" };
        foreach (string tag in tags)
        {
            try
            {
                GameObject tagged = GameObject.FindGameObjectWithTag(tag);
                if (tagged != null)
                {
                    spawn = tagged.transform;
                    return true;
                }
            }
            catch (UnityException)
            {
                // Some projects do not define both common spawn tags.
            }
        }

        string[] names = { "PlayerSpawn", "Spawn", "spawn", "StartPosition", "PlayerStart" };
        foreach (string name in names)
        {
            GameObject named = GameObject.Find(name);
            if (named != null)
            {
                spawn = named.transform;
                return true;
            }
        }

        return false;
    }

    private Transform ResolveLocalPlayer()
    {
        if (_playerInputs == null)
        {
            _playerInputs = FindFirstObjectByType<StarterAssetsInputs>(FindObjectsInactive.Include);
        }

        if (_playerInputs != null) return _playerInputs.transform;

        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null) return taggedPlayer.transform;
        }
        catch (UnityException)
        {
        }

        CharacterController characterController = FindFirstObjectByType<CharacterController>(FindObjectsInactive.Include);
        return characterController != null ? characterController.transform : null;
    }

    private void TeleportPlayer(Transform player, Vector3 position, float rotationY)
    {
        if (player == null) return;

        CharacterController characterController = player.GetComponent<CharacterController>();
        bool wasControllerEnabled = characterController != null && characterController.enabled;
        if (wasControllerEnabled)
        {
            characterController.enabled = false;
        }

        player.position = position;
        player.rotation = Quaternion.Euler(0f, rotationY, 0f);
        Physics.SyncTransforms();

        if (wasControllerEnabled)
        {
            characterController.enabled = true;
        }

        StarterAssetsInputs inputs = player.GetComponent<StarterAssetsInputs>();
        if (inputs != null)
        {
            _playerInputs = inputs;
            inputs.MoveInput(Vector2.zero);
            inputs.LookInput(Vector2.zero);
            inputs.JumpInput(false);
            inputs.SprintInput(false);
        }
    }

    private static bool CanUsePointerLock()
    {
        if (Application.isMobilePlatform) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
        return false;
#else
        return true;
#endif
    }

    private InputField CreateInput(Transform parent, string name, Vector2 anchor, string placeholderText, bool isPassword = false)
    {
        var inputObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField), typeof(Outline));
        inputObj.transform.SetParent(parent, false);

        var rect = inputObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(540f, 58f);

        var image = inputObj.GetComponent<Image>();
        image.color = FieldColor;

        var outline = inputObj.GetComponent<Outline>();
        outline.effectColor = FrameColor;
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = false;

        var inputField = inputObj.GetComponent<InputField>();
        inputField.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;
        inputField.caretColor = InkColor;
        inputField.selectionColor = new Color(0.48f, 0.28f, 0.12f, 0.32f);

        var text = CreateInputText(inputObj.transform, "Text", string.Empty, Color.black, FontStyle.Normal);
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;

        var placeholder = CreateInputText(inputObj.transform, "Placeholder", placeholderText, FieldPlaceholderColor, FontStyle.Italic);
        placeholder.alignment = TextAnchor.MiddleLeft;

        inputField.textComponent = text;
        inputField.placeholder = placeholder;

        return inputField;
    }

    private Text CreateInputText(Transform parent, string name, string value, Color color, FontStyle style)
    {
        var textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(parent, false);

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(22f, 6f);
        textRect.offsetMax = new Vector2(-22f, -6f);

        var text = textObj.GetComponent<Text>();
        text.text = value;
        text.font = panelFont;
        text.fontSize = 27;
        text.fontStyle = style;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        return text;
    }

    private void CreateHeader(Transform parent, string title)
    {
        var header = CreateLabel(parent, "Header", new Vector2(0.5f, 0.84f), new Vector2(620f, 64f), 48, InkColor, title, FontStyle.Bold);
        AddTextShadow(header, new Color(0.94f, 0.75f, 0.44f, 0.42f), new Vector2(3f, -3f));
    }

    private void CreateSubHeader(Transform parent, string title)
    {
        CreateLabel(parent, "SubHeader", new Vector2(0.5f, 0.75f), new Vector2(520f, 30f), 20, MutedInkColor, title, FontStyle.Bold);
    }

    private void CreateSurface(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        var surfaceObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        surfaceObj.transform.SetParent(parent, false);

        var rect = surfaceObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var image = surfaceObj.GetComponent<Image>();
        image.color = PanelSurfaceColor;
        image.raycastTarget = false;

        var outline = surfaceObj.GetComponent<Outline>();
        outline.effectColor = new Color(0.27f, 0.16f, 0.08f, 0.42f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
    }

    private Text CreateLabel(Transform parent, string name, Vector2 anchor, Vector2 size, int fontSize, Color color, string value, FontStyle style = FontStyle.Normal)
    {
        var labelObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObj.transform.SetParent(parent, false);

        var rect = labelObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var text = labelObj.GetComponent<Text>();
        text.font = panelFont;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.text = value;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;

        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size, Color tint, UnityAction onClick)
    {
        var buttonObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline));
        buttonObj.transform.SetParent(parent, false);

        var rect = buttonObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var image = buttonObj.GetComponent<Image>();
        image.color = tint;

        var outline = buttonObj.GetComponent<Outline>();
        outline.effectColor = FrameColor;
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = false;

        var shadow = buttonObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.10f, 0.06f, 0.03f, 0.42f);
        shadow.effectDistance = new Vector2(4f, -4f);
        shadow.useGraphicAlpha = false;

        var button = buttonObj.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.97f, 0.86f, 1f);
        colors.pressedColor = new Color(0.82f, 0.78f, 0.68f, 1f);
        colors.disabledColor = new Color(0.43f, 0.36f, 0.28f, 0.62f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textObj.GetComponent<Text>();
        text.text = label;
        text.font = panelFont;
        text.fontSize = 23;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = ButtonTextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        AddTextShadow(text, new Color(0.08f, 0.04f, 0.02f, 0.72f), new Vector2(1.5f, -1.5f));

        return button;
    }

    private static void AddTextShadow(Text text, Color color, Vector2 distance)
    {
        if (text == null) return;

        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = false;
    }

    private static Font ResolvePanelFont(Font currentFont, Font managerFont)
    {
        if (IsPreferredPanelFont(currentFont)) return currentFont;
        if (IsPreferredPanelFont(managerFont)) return managerFont;

#if UNITY_EDITOR
        Font editorFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/BMYEONSUNG_ttf.ttf");
        if (editorFont != null) return editorFont;
#endif

        Font loadedFont = FindLoadedFont("BMYEONSUNG_ttf");
        if (loadedFont == null) loadedFont = FindLoadedFont("BMYEONSUNG");
        if (loadedFont != null) return loadedFont;

        Font resourceFont = Resources.Load<Font>("BMYEONSUNG_ttf");
        if (resourceFont == null) resourceFont = Resources.Load<Font>("BMYEONSUNG");
        if (resourceFont != null) return resourceFont;

        if (managerFont != null) return managerFont;
        if (currentFont != null) return currentFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Font FindLoadedFont(string fontNamePart)
    {
        Font[] loadedFonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (Font loadedFont in loadedFonts)
        {
            if (loadedFont != null && loadedFont.name.IndexOf(fontNamePart, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loadedFont;
            }
        }

        return null;
    }

    private static bool IsPreferredPanelFont(Font font)
    {
        return font != null && font.name.IndexOf("BMYEONSUNG", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
