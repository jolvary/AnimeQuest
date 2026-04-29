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
            Debug.LogError("Login failed: " + ex.Message);
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
            Debug.LogError("Register failed: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task AuthenticateBackendAndOpenGame()
    {
        string me = await ApiClient.Instance.PostEnsureMe();
        Debug.Log("Authenticated and ensured user: " + me);

        uiManager?.HideAll();
        if (mainMenuAuthController != null)
        {
            mainMenuAuthController.gameObject.SetActive(false);
        }
    }
}
