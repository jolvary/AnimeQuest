using UnityEngine;
using System;

public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private MainMenuAuthController mainMenuAuthController;
    [SerializeField] private UIManager uiManager;

    private void Start()
    {
        DozzleLogger.Action("Game bootstrap start");
        ResolveMainMenuController();

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

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
            await AuthenticateBackendAndOpenGame();
        }
        catch (Exception ex)
        {
            string message = "Login failed: " + ex.Message;
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
            await AuthenticateBackendAndOpenGame();
        }
        catch (Exception ex)
        {
            string message = "Register failed: " + ex.Message;
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
                await ApiClient.Instance.PostEnsureMe();
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

    private async System.Threading.Tasks.Task AuthenticateBackendAndOpenGame()
    {
        string me = await ApiClient.Instance.PostEnsureMe();
        DozzleLogger.Action("Authenticated and ensured user", me);

        mainMenuAuthController?.animeCatalogPanelController?.SetIncognitoMode(false);
        mainMenuAuthController?.userCatalogPanelController?.SetIncognitoMode(false);

        uiManager?.HideAll();
        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(false);
        }
    }
}
