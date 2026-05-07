using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;

public class GameBootstrap : MonoBehaviour
{
    private const float SessionLeaseHeartbeatIntervalSeconds = 25f;

    [SerializeField] private MainMenuAuthController mainMenuAuthController;
    [SerializeField] private UIManager uiManager;

    private bool _sessionLeaseActive;
    private bool _sessionLeaseHeartbeatInFlight;
    private float _nextSessionLeaseHeartbeatAt;

    private void Start()
    {
        DozzleLogger.Action("Game bootstrap start");
        ResolveMainMenuController();

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = true;
#endif

        if (mainMenuAuthController == null)
        {
            DozzleLogger.Error("Main menu UI is missing", "Add MainMenuAuthController to the scene under UI/Canvas instead of relying on runtime creation.");
            return;
        }

        mainMenuAuthController.gameObject.SetActive(true);
        mainMenuAuthController.onLoginRequested.AddListener(HandleLoginRequested);
        mainMenuAuthController.onRegisterRequested.AddListener(HandleRegisterRequested);
        mainMenuAuthController.onIncognitoRequested.AddListener(HandleIncognitoRequested);
        mainMenuAuthController.onLogoutRequested.AddListener(HandleLogoutRequested);
    }

    private void Update()
    {
        ApplyPanelSafeAreaLayout();
        MaintainSessionLease();

        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (uiManager != null && uiManager.HasAnyPanelOpen())
            {
                uiManager.HideAll();
                DozzleLogger.Action("Panel closed with Escape");
                return;
            }

            ToggleMainMenu();
            return;
        }

        if (IsTextInputFocused()) return;

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            ToggleMainMenu();
        }
    }

    private void ToggleMainMenu()
    {
        ResolveMainMenuController();
        if (mainMenuAuthController == null) return;

        bool shouldOpen = !mainMenuAuthController.gameObject.activeSelf;
        if (shouldOpen)
        {
            uiManager?.HideAll();
            mainMenuAuthController.gameObject.SetActive(true);
            mainMenuAuthController.ShowLoginPanel();
        }
        else
        {
            uiManager?.HideAll();
            mainMenuAuthController.gameObject.SetActive(false);
        }

        DozzleLogger.Action("Main menu toggled", $"open={shouldOpen}");
    }

    private void ResolveMainMenuController()
    {
        if (mainMenuAuthController == null)
        {
            mainMenuAuthController = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        }

        if (mainMenuAuthController == null)
        {
            return;
        }

        if (mainMenuAuthController.uiManager == null)
        {
            mainMenuAuthController.uiManager = uiManager != null ? uiManager : FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        }

        if (mainMenuAuthController.animeCatalogPanelController == null)
        {
            mainMenuAuthController.animeCatalogPanelController = FindFirstObjectByType<AnimeCatalogPanelController>(FindObjectsInactive.Include);
        }

        if (mainMenuAuthController.userCatalogPanelController == null && mainMenuAuthController.uiManager != null)
        {
            mainMenuAuthController.userCatalogPanelController = mainMenuAuthController.uiManager.userCatalogPanelController;
        }
    }

    private async void HandleLoginRequested(string username, string password)
    {
        DozzleLogger.Action("Handle login", $"username={username}");
        try
        {
            await NakamaAuthManager.Instance.LoginAsync(username, password);
            await AcquireSessionLeaseOrLogout();
            OpenAuthenticatedGame();
            EnsureBackendUserInBackground("login");
        }
        catch (Exception ex)
        {
            string message = IsSessionConflict(ex)
                ? "Login failed: this account is already logged in on another device."
                : "Login failed: " + ex.Message;
            DozzleLogger.Error("Handle login failed", ex);
            mainMenuAuthController?.SetLoginStatus(message);
        }
    }

    private async void HandleRegisterRequested(string username, string password)
    {
        DozzleLogger.Action("Handle register", $"username={username}");
        try
        {
            await NakamaAuthManager.Instance.RegisterAsync(username, password);
            await AcquireSessionLeaseOrLogout();
            OpenAuthenticatedGame();
            EnsureBackendUserInBackground("register");
        }
        catch (Exception ex)
        {
            string message = IsSessionConflict(ex)
                ? "Register failed: this account is already logged in on another device."
                : "Register failed: " + ex.Message;
            DozzleLogger.Error("Handle register failed", ex);
            mainMenuAuthController?.SetRegisterStatus(message);
        }
    }


    private async void HandleIncognitoRequested()
    {
        DozzleLogger.Action("Handle incognito");
        try
        {
            if (NakamaAuthManager.Instance != null && !NakamaAuthManager.Instance.IsAuthenticated)
            {
                await NakamaAuthManager.Instance.LoginDeviceAsync();
                EnsureBackendUserInBackground("incognito");
            }

            mainMenuAuthController?.animeCatalogPanelController?.SetIncognitoMode(true);
            mainMenuAuthController?.userCatalogPanelController?.SetIncognitoMode(true);
            uiManager?.OpenAnimePanel();
        }
        catch (Exception ex)
        {
            string message = "Incognito login failed: " + ex.Message;
            DozzleLogger.Error("Handle incognito failed", ex);
            mainMenuAuthController?.SetLoginStatus(message);
            if (mainMenuAuthController != null)
            {
                mainMenuAuthController.gameObject.SetActive(true);
                mainMenuAuthController.ShowLoginPanel();
            }
        }
    }

    private async void HandleLogoutRequested()
    {
        DozzleLogger.Action("Handle logout");
        try
        {
            await ReleaseSessionLeaseIfNeeded();

            if (NakamaAuthManager.Instance != null)
            {
                await NakamaAuthManager.Instance.LogoutAsync();
            }

            uiManager?.HideAll();
            if (mainMenuAuthController != null)
            {
                mainMenuAuthController.gameObject.SetActive(true);
                mainMenuAuthController.ShowLoginPanel();
                mainMenuAuthController.SetLoginStatus("You have been logged out.");
            }
        }
        catch (Exception ex)
        {
            string message = "Logout failed: " + ex.Message;
            DozzleLogger.Error("Handle logout failed", ex);
            mainMenuAuthController?.SetLoginStatus(message);
        }
    }

    private void OpenAuthenticatedGame()
    {
        ResolveMainMenuController();
        mainMenuAuthController?.animeCatalogPanelController?.SetIncognitoMode(false);
        mainMenuAuthController?.userCatalogPanelController?.SetIncognitoMode(false);
        mainMenuAuthController?.SetLoginStatus("Login successful.");
        mainMenuAuthController?.SetRegisterStatus("Account ready.");

        uiManager?.HideAll();
        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(false);
        }

        uiManager?.ConnectGlobalChatRoom();
        uiManager?.OpenAnimePanel();
    }

    private async void EnsureBackendUserInBackground(string source)
    {
        if (ApiClient.Instance == null || NakamaAuthManager.Instance == null || !NakamaAuthManager.Instance.IsAuthenticated)
        {
            return;
        }

        try
        {
            string me = await ApiClient.Instance.PostEnsureMe();
            DozzleLogger.Action("Authenticated and ensured user", $"source={source};{me}");
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Backend user ensure failed", ex);
        }
    }

    private async System.Threading.Tasks.Task AcquireSessionLeaseOrLogout()
    {
        if (ApiClient.Instance == null || NakamaAuthManager.Instance == null || NakamaAuthManager.Instance.IsIncognitoSession)
        {
            return;
        }

        try
        {
            await ApiClient.Instance.AcquireSessionLease();
            _sessionLeaseActive = true;
            _nextSessionLeaseHeartbeatAt = Time.unscaledTime + SessionLeaseHeartbeatIntervalSeconds;
            DozzleLogger.Action("Active account session acquired", $"client={ApiClient.Instance.ClientInstanceId.Substring(0, 8)}");
        }
        catch
        {
            _sessionLeaseActive = false;
            if (NakamaAuthManager.Instance != null)
            {
                await NakamaAuthManager.Instance.LogoutAsync();
            }
            throw;
        }
    }

    private void MaintainSessionLease()
    {
        if (!_sessionLeaseActive || _sessionLeaseHeartbeatInFlight || ApiClient.Instance == null || NakamaAuthManager.Instance == null)
        {
            return;
        }

        if (!NakamaAuthManager.Instance.IsAuthenticated || NakamaAuthManager.Instance.IsIncognitoSession)
        {
            _sessionLeaseActive = false;
            return;
        }

        if (Time.unscaledTime < _nextSessionLeaseHeartbeatAt)
        {
            return;
        }

        SendSessionLeaseHeartbeat();
    }

    private async void SendSessionLeaseHeartbeat()
    {
        _sessionLeaseHeartbeatInFlight = true;
        try
        {
            await ApiClient.Instance.HeartbeatSessionLease();
            _nextSessionLeaseHeartbeatAt = Time.unscaledTime + SessionLeaseHeartbeatIntervalSeconds;
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Active account session heartbeat failed", ex);
            _nextSessionLeaseHeartbeatAt = Time.unscaledTime + 5f;

            if (IsSessionConflict(ex))
            {
                _sessionLeaseActive = false;
                await ForceLogoutToLoginPanel("This account was opened on another device.");
            }
        }
        finally
        {
            _sessionLeaseHeartbeatInFlight = false;
        }
    }

    private async System.Threading.Tasks.Task ReleaseSessionLeaseIfNeeded()
    {
        if (!_sessionLeaseActive || ApiClient.Instance == null)
        {
            _sessionLeaseActive = false;
            return;
        }

        try
        {
            await ApiClient.Instance.ReleaseSessionLease();
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Active account session release failed", ex);
        }
        finally
        {
            _sessionLeaseActive = false;
        }
    }

    private async System.Threading.Tasks.Task ForceLogoutToLoginPanel(string status)
    {
        try
        {
            if (NakamaAuthManager.Instance != null)
            {
                await NakamaAuthManager.Instance.LogoutAsync();
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Forced logout failed", ex);
        }

        uiManager?.HideAll();
        ResolveMainMenuController();
        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(true);
            mainMenuAuthController.ShowLoginPanel();
            mainMenuAuthController.SetLoginStatus(status);
        }
    }

    private void ApplyPanelSafeAreaLayout()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        }

        if (uiManager == null) return;

        ApplyAnimePanelSafeArea(uiManager.animePanel);
        ApplyAnimePanelSafeArea(uiManager.userCatalogPanel);
        ApplyFriendsPanelSafeArea(uiManager.friendsPanel);
        ApplyMatchingPanelSafeArea(uiManager.matchingPanel);
        ApplyQuestPanelSafeArea(uiManager.questsPanel);
        ApplyChatPanelSafeArea(uiManager.chatPanel);
        ApplyTablePanelSafeArea(uiManager.tablePanel);
    }

    private static void ApplyAnimePanelSafeArea(GameObject panel)
    {
        SetOffsets(panel, "AnimeDescription", new Vector2(72f, -142f), new Vector2(-72f, -86f));
        SetOffsets(panel, "AnimeSearchBar", new Vector2(72f, -196f), new Vector2(-104f, -160f));
        SetOffsets(panel, "AnimeStatus", new Vector2(72f, -242f), new Vector2(-390f, -204f));
        SetOffsets(panel, "AnimePagingBar", new Vector2(-464f, -246f), new Vector2(-104f, -204f));
        SetOffsets(panel, "AnimeDeckViewport", new Vector2(72f, 104f), new Vector2(-72f, -270f));
    }

    private static void ApplyFriendsPanelSafeArea(GameObject panel)
    {
        SetOffsets(panel, "FriendsTitle", new Vector2(72f, -124f), new Vector2(-72f, -70f));
        SetOffsets(panel, "FriendsStatus", new Vector2(72f, -178f), new Vector2(-72f, -138f));
        SetOffsets(panel, "FriendSearchInput", new Vector2(72f, -248f), new Vector2(-72f, -190f));
        SetOffsets(panel, "FriendsViewport", new Vector2(72f, 96f), new Vector2(-72f, -270f));
    }

    private static void ApplyMatchingPanelSafeArea(GameObject panel)
    {
        SetOffsets(panel, "MatchingTitle", new Vector2(72f, -124f), new Vector2(-72f, -70f));
        SetOffsets(panel, "MatchingStatus", new Vector2(72f, -180f), new Vector2(-72f, -138f));
        SetOffsets(panel, "MatchingViewport", new Vector2(72f, 96f), new Vector2(-72f, -212f));
    }

    private static void ApplyQuestPanelSafeArea(GameObject panel)
    {
        SetAnchoredPosition(panel, "PanelTitle", new Vector2(0f, -48f));
        SetOffsets(panel, "QuestDescription", new Vector2(72f, -158f), new Vector2(-72f, -110f));
        SetOffsets(panel, "QuestStatus", new Vector2(72f, -202f), new Vector2(-72f, -162f));
        SetOffsets(panel, "QuestContentViewport", new Vector2(72f, 96f), new Vector2(-72f, -232f));
    }

    private static void ApplyChatPanelSafeArea(GameObject panel)
    {
        SetOffsets(panel, "ChatTitle", new Vector2(72f, -124f), new Vector2(-72f, -70f));
        SetOffsets(panel, "ChatStatus", new Vector2(72f, -172f), new Vector2(-72f, -132f));
        SetOffsets(panel, "ChatViewport", new Vector2(72f, 166f), new Vector2(-72f, -196f));
        SetOffsets(panel, "ChatMessageInput", new Vector2(72f, 96f), new Vector2(-246f, 156f));
        SetAnchoredPosition(panel, "SendButton", new Vector2(-72f, 96f));
    }

    private static void ApplyTablePanelSafeArea(GameObject panel)
    {
        SetOffsets(panel, "TableDescription", new Vector2(72f, -124f), new Vector2(-72f, -48f));
        SetOffsets(panel, "TableContentViewport", new Vector2(72f, 96f), new Vector2(-72f, -162f));
    }

    private static void SetOffsets(GameObject panel, string childName, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform rect = FindChildRect(panel, childName);
        if (rect == null) return;

        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void SetAnchoredPosition(GameObject panel, string childName, Vector2 anchoredPosition)
    {
        RectTransform rect = FindChildRect(panel, childName);
        if (rect == null) return;

        rect.anchoredPosition = anchoredPosition;
    }

    private static RectTransform FindChildRect(GameObject panel, string childName)
    {
        if (panel == null || string.IsNullOrWhiteSpace(childName)) return null;
        Transform child = FindChildRecursive(panel.transform, childName);
        return child != null ? child.GetComponent<RectTransform>() : null;
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName) return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null) return nested;
        }

        return null;
    }

    private static bool IsSessionConflict(Exception ex)
    {
        return ex != null && ex.Message != null && ex.Message.Contains(ApiClient.SessionConflictMarker);
    }

    private static bool IsTextInputFocused()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null) return false;
        return EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
    }
}
