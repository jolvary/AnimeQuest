using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StarterAssets;
using System;
using System.Threading.Tasks;

public class MainMenuAuthController : MonoBehaviour
{
    [Header("Visual style")]
    public Sprite panelSprite;
    public Font panelFont;

    [Header("References")]
    public UIManager uiManager;
    public AnimeCatalogPanelController animeCatalogPanelController;
    public AnimeCatalogPanelController userCatalogPanelController;

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


    private void Awake()
    {
        EnsureCanvasRoot();
        EnsureEventSystemExists();
    }

    private void Start()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (animeCatalogPanelController == null)
        {
            animeCatalogPanelController = FindFirstObjectByType<AnimeCatalogPanelController>();
        }

        ResolveUserCatalogController();
        ResolveVisualStyle();
        BuildPanels();
        ShowLoginPanel();
        RefreshInteractionState();
    }

    public void ShowLoginPanel()
    {
        bool isLoggedIn = NakamaAuthManager.Instance != null && NakamaAuthManager.Instance.IsAuthenticated && !NakamaAuthManager.Instance.IsIncognitoSession;

        if (_loginPanel != null) _loginPanel.SetActive(!isLoggedIn);
        if (_registerPanel != null) _registerPanel.SetActive(false);
        if (_loggedInPanel != null) _loggedInPanel.SetActive(isLoggedIn);
        if (_loginStatus != null) _loginStatus.text = string.Empty;
        RefreshInteractionState();

        if (isLoggedIn)
        {
            RefreshMyAnimeListStatus();
        }
    }

    public void ShowRegisterPanel()
    {
        if (_loginPanel != null) _loginPanel.SetActive(false);
        if (_registerPanel != null) _registerPanel.SetActive(true);
        if (_loggedInPanel != null) _loggedInPanel.SetActive(false);
        if (_registerStatus != null) _registerStatus.text = string.Empty;
        RefreshInteractionState();
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

        if (panelFont == null && uiManager != null)
        {
            panelFont = uiManager.panelTitleFont;
        }

        if (panelFont == null)
        {
            panelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        rect.sizeDelta = new Vector2(860f, 520f);

        var image = panel.GetComponent<Image>();
        image.sprite = panelSprite;
        image.type = Image.Type.Simple;
        image.color = Color.white;

        panel.SetActive(active);
        return panel;
    }

    private void BuildLoginPanel(Transform parent)
    {
        CreateHeader(parent, "MAIN MENU");

        _loginUsername = CreateInput(parent, "UsernameInput", new Vector2(0.5f, 0.62f), "Enter Username...");
        _loginPassword = CreateInput(parent, "PasswordInput", new Vector2(0.5f, 0.47f), "Enter Password...", true);

        CreateButton(parent, "LoginButton", "Login", new Vector2(0.5f, 0.34f), new Vector2(220f, 48f), new Color(0.22f, 0.86f, 0.21f), OnLoginPressed);
        CreateButton(parent, "RegisterNavButton", "Create Account", new Vector2(0.79f, 0.22f), new Vector2(210f, 42f), new Color(0.87f, 0.17f, 0.16f), ShowRegisterPanel);
        CreateButton(parent, "IncognitoButton", "Enter in Incognito", new Vector2(0.23f, 0.22f), new Vector2(250f, 42f), new Color(0.32f, 0.50f, 0.76f), OnIncognitoPressed);
        CreateCloseButton(parent);

        _loginStatus = CreateLabel(parent, "LoginStatus", new Vector2(0.5f, 0.14f), new Vector2(600f, 36f), 30, Color.red, string.Empty);
    }

    private void BuildRegisterPanel(Transform parent)
    {
        CreateHeader(parent, "REGISTER USER");
        CreateCloseButton(parent);

        _registerUsername = CreateInput(parent, "RegisterUsernameInput", new Vector2(0.5f, 0.62f), "Create Username...");
        _registerPassword = CreateInput(parent, "RegisterPasswordInput", new Vector2(0.5f, 0.47f), "Create Password...", true);

        CreateButton(parent, "CreateAccountButton", "Create Account", new Vector2(0.5f, 0.34f), new Vector2(240f, 48f), new Color(0.23f, 0.77f, 0.27f), OnRegisterPressed);
        CreateButton(parent, "GoToLoginButton", "Go to Login", new Vector2(0.5f, 0.22f), new Vector2(240f, 42f), new Color(0.30f, 0.50f, 0.80f), ShowLoginPanel);

        _registerStatus = CreateLabel(parent, "RegisterStatus", new Vector2(0.5f, 0.14f), new Vector2(600f, 36f), 30, new Color(1f, 0.85f, 0.2f), string.Empty);
    }



    private void BuildLoggedInPanel(Transform parent)
    {
        CreateHeader(parent, "MAIN MENU");
        CreateCloseButton(parent);
        CreateLabel(parent, "LoggedInLabel", new Vector2(0.5f, 0.61f), new Vector2(700f, 42f), 28, Color.black, "You are already logged in.");
        _linkMalButton = CreateButton(parent, "LinkMalButton", "Link MyAnimeList account", new Vector2(0.5f, 0.47f), new Vector2(390f, 48f), new Color(0.22f, 0.62f, 0.88f), OnLinkMyAnimeListPressed);
        _importMalButton = CreateButton(parent, "ImportMalButton", "Import MyAnimeList", new Vector2(0.5f, 0.34f), new Vector2(310f, 48f), new Color(0.23f, 0.77f, 0.27f), OnImportMyAnimeListPressed);
        _malImportStatus = CreateLabel(parent, "MalImportStatus", new Vector2(0.5f, 0.24f), new Vector2(740f, 36f), 24, Color.black, string.Empty);
        CreateButton(parent, "LogoutButton", "Log out", new Vector2(0.5f, 0.13f), new Vector2(240f, 52f), new Color(0.82f, 0.19f, 0.19f), OnLogoutPressed);
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
    }

    public void SetRegisterStatus(string message)
    {
        if (_registerStatus != null)
        {
            _registerStatus.text = message ?? string.Empty;
        }
    }

    private void OnLoginPressed()
    {
        string username = _loginUsername != null ? _loginUsername.text.Trim() : string.Empty;
        string password = _loginPassword != null ? _loginPassword.text : string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _loginStatus.text = "WRONG USERNAME OR PASSWORD";
            return;
        }

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
            _registerStatus.text = "USERNAME AND PASSWORD REQUIRED";
            return;
        }

        SetRegisterStatus("Registering account...");
        DozzleLogger.Action("Register requested", $"username={username}");
        onRegisterRequested?.Invoke(username, password);
    }

    private void OnLogoutPressed()
    {
        DozzleLogger.Action("Logout requested");
        onLogoutRequested?.Invoke();
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
        if (ApiClient.Instance == null || _malImportStatus == null)
        {
            return;
        }

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
    }

    private async Task PollMyAnimeListLinkStatus()
    {
        _isPollingMalLink = true;
        try
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(2000);
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

            ApplyMyAnimeListStatus(null);
            if (_malImportStatus != null) _malImportStatus.text = "Finish authorization in your browser.";
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
        RefreshInteractionState();
        if (_loggedInPanel != null && _loggedInPanel.activeSelf)
        {
            RefreshMyAnimeListStatus();
        }
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
            _playerInputs = FindFirstObjectByType<StarterAssetsInputs>();
            if (_playerInputs == null) return;
        }

        _playerInputs.cursorLocked = enabled;
        _playerInputs.cursorInputForLook = enabled;
        _playerInputs.movementInputEnabled = enabled;

        if (!enabled)
        {
            _playerInputs.MoveInput(Vector2.zero);
            _playerInputs.LookInput(Vector2.zero);
            _playerInputs.JumpInput(false);
            _playerInputs.SprintInput(false);
        }
    }

    private InputField CreateInput(Transform parent, string name, Vector2 anchor, string placeholderText, bool isPassword = false)
    {
        var inputObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        inputObj.transform.SetParent(parent, false);

        var rect = inputObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(520f, 78f);

        var image = inputObj.GetComponent<Image>();
        image.color = new Color(0.92f, 0.92f, 0.92f, 1f);

        var inputField = inputObj.GetComponent<InputField>();
        inputField.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;

        var text = CreateInputText(inputObj.transform, "Text", string.Empty, Color.black, FontStyle.Normal);
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = false;

        var placeholder = CreateInputText(inputObj.transform, "Placeholder", placeholderText, new Color(0.65f, 0.65f, 0.65f, 1f), FontStyle.Italic);
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
        textRect.offsetMin = new Vector2(18f, 10f);
        textRect.offsetMax = new Vector2(-18f, -10f);

        var text = textObj.GetComponent<Text>();
        text.text = value;
        text.font = panelFont;
        text.fontSize = 40;
        text.fontStyle = style;
        text.color = color;

        return text;
    }

    private void CreateHeader(Transform parent, string title)
    {
        CreateLabel(parent, "Header", new Vector2(0.5f, 0.86f), new Vector2(500f, 62f), 54, Color.black, title, FontStyle.Italic);
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

        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size, Color tint, UnityAction onClick)
    {
        var buttonObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObj.transform.SetParent(parent, false);

        var rect = buttonObj.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var image = buttonObj.GetComponent<Image>();
        image.color = tint;

        var button = buttonObj.GetComponent<Button>();
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
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.black;

        return button;
    }
}
