using UnityEngine;
using System;

public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private MainMenuAuthController mainMenuAuthController;
    [SerializeField] private UIManager uiManager;

    private async void Start()
    {
        EnsureMainMenuControllerExists();

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(true);
            mainMenuAuthController.onLoginRequested.AddListener(HandleLoginRequested);
            mainMenuAuthController.onRegisterRequested.AddListener(HandleRegisterRequested);
            mainMenuAuthController.onIncognitoRequested.AddListener(HandleIncognitoRequested);
            mainMenuAuthController.onLogoutRequested.AddListener(HandleLogoutRequested);
        }

        try
        {
            if (mainMenuAuthController == null)
            {
                await NakamaAuthManager.Instance.LoginDeviceAsync();
                await AuthenticateBackendAndOpenGame();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Bootstrap failed: " + ex.Message);
        }
    }

    private void EnsureMainMenuControllerExists()
    {
        if (mainMenuAuthController != null)
        {
            return;
        }

        mainMenuAuthController = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        if (mainMenuAuthController != null)
        {
            return;
        }

        var menuObject = new GameObject("MainMenuAuthCanvas");
        mainMenuAuthController = menuObject.AddComponent<MainMenuAuthController>();
        mainMenuAuthController.uiManager = uiManager != null ? uiManager : FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        mainMenuAuthController.animeCatalogPanelController = FindFirstObjectByType<AnimeCatalogPanelController>(FindObjectsInactive.Include);
    }

    private async void HandleLoginRequested(string username, string password)
    {
        try
        {
            await NakamaAuthManager.Instance.LoginAsync(username, password);
            await AuthenticateBackendAndOpenGame();
        }
        catch (Exception ex)
        {
            string message = "Login failed: " + ex.Message;
            Debug.LogError(message);
            mainMenuAuthController?.SetLoginStatus(message);
        }
    }

    private async void HandleRegisterRequested(string username, string password)
    {
        try
        {
            await NakamaAuthManager.Instance.RegisterAsync(username, password);
            await AuthenticateBackendAndOpenGame();
        }
        catch (Exception ex)
        {
            string message = "Register failed: " + ex.Message;
            Debug.LogError(message);
            mainMenuAuthController?.SetRegisterStatus(message);
        }
    }


    private async void HandleIncognitoRequested()
    {
        try
        {
            if (NakamaAuthManager.Instance != null && !NakamaAuthManager.Instance.IsAuthenticated)
            {
                await NakamaAuthManager.Instance.LoginDeviceAsync();
                await ApiClient.Instance.PostEnsureMe();
            }

            mainMenuAuthController?.animeCatalogPanelController?.SetIncognitoMode(true);
            uiManager?.OpenAnimePanel();
        }
        catch (Exception ex)
        {
            string message = "Incognito login failed: " + ex.Message;
            Debug.LogError(message);
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
            Debug.LogError(message);
            mainMenuAuthController?.SetLoginStatus(message);
        }
    }

    private async System.Threading.Tasks.Task AuthenticateBackendAndOpenGame()
    {
        string me = await ApiClient.Instance.PostEnsureMe();
        Debug.Log("Authenticated and ensured user: " + me);

        mainMenuAuthController?.animeCatalogPanelController?.SetIncognitoMode(false);

        uiManager?.HideAll();
        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(false);
        }
    }
}
