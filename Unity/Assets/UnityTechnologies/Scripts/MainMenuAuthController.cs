using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using StarterAssets;

public class MainMenuAuthController : MonoBehaviour
{
    [Header("Visual style")]
    public Sprite panelSprite;
    public Font panelFont;

    [Header("References")]
    public UIManager uiManager;
    public AnimeCatalogPanelController animeCatalogPanelController;

    [Header("Events")]
    public UnityEvent<string, string> onLoginRequested = new UnityEvent<string, string>();
    public UnityEvent<string, string> onRegisterRequested = new UnityEvent<string, string>();
    public UnityEvent onIncognitoRequested = new UnityEvent();

    private GameObject _loginPanel;
    private GameObject _registerPanel;

    private InputField _loginUsername;
    private InputField _loginPassword;
    private Text _loginStatus;

    private InputField _registerUsername;
    private InputField _registerPassword;
    private Text _registerStatus;
    private StarterAssetsInputs _playerInputs;


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

        ResolveVisualStyle();
        BuildPanels();
        ShowLoginPanel();
        RefreshInteractionState();
    }

    public void ShowLoginPanel()
    {
        if (_loginPanel != null) _loginPanel.SetActive(true);
        if (_registerPanel != null) _registerPanel.SetActive(false);
        if (_loginStatus != null) _loginStatus.text = string.Empty;
        RefreshInteractionState();
    }

    public void ShowRegisterPanel()
    {
        if (_loginPanel != null) _loginPanel.SetActive(false);
        if (_registerPanel != null) _registerPanel.SetActive(true);
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

    private void BuildPanels()
    {
        _loginPanel = CreatePanel("LoginPanel", true);
        _registerPanel = CreatePanel("RegisterPanel", false);

        BuildLoginPanel(_loginPanel.transform);
        BuildRegisterPanel(_registerPanel.transform);
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

        _loginStatus = CreateLabel(parent, "LoginStatus", new Vector2(0.5f, 0.14f), new Vector2(600f, 36f), 30, Color.red, string.Empty);
    }

    private void BuildRegisterPanel(Transform parent)
    {
        CreateHeader(parent, "REGISTER USER");

        _registerUsername = CreateInput(parent, "RegisterUsernameInput", new Vector2(0.5f, 0.62f), "Create Username...");
        _registerPassword = CreateInput(parent, "RegisterPasswordInput", new Vector2(0.5f, 0.47f), "Create Password...", true);

        CreateButton(parent, "CreateAccountButton", "Create Account", new Vector2(0.5f, 0.34f), new Vector2(240f, 48f), new Color(0.23f, 0.77f, 0.27f), OnRegisterPressed);
        CreateButton(parent, "GoToLoginButton", "Go to Login", new Vector2(0.5f, 0.22f), new Vector2(240f, 42f), new Color(0.30f, 0.50f, 0.80f), ShowLoginPanel);

        _registerStatus = CreateLabel(parent, "RegisterStatus", new Vector2(0.5f, 0.14f), new Vector2(600f, 36f), 30, new Color(1f, 0.85f, 0.2f), string.Empty);
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
        onRegisterRequested?.Invoke(username, password);
    }

    private void OnIncognitoPressed()
    {
        if (animeCatalogPanelController != null)
        {
            animeCatalogPanelController.SetIncognitoMode(true);
        }

        onIncognitoRequested?.Invoke();
        uiManager?.HideAll();

        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshInteractionState();
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

    private void CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size, Color tint, UnityAction onClick)
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
    }
}
