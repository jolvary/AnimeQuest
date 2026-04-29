using UnityEngine;

public class NPCQuestGiver : MonoBehaviour, IInteractable
{
    [Header("NPC")]
    public string npcName = "Quest NPC";
    public string questCode = "starter-quest";

    [Header("References")]
    public UIManager uiManager;
    public QuestPanelController questPanelController;
    public MainMenuAuthController mainMenuAuthController;

    public void Interact(PlayerInteractor interactor)
    {
        uiManager?.HideAll();

        var menuController = GetOrCreateMainMenuAuthController();
        if (menuController != null)
        {
            menuController.gameObject.SetActive(true);
            menuController.ShowLoginPanel();
        }

        Debug.Log($"{npcName}: opened Main Menu for quest code {questCode}");
    }


    private MainMenuAuthController GetOrCreateMainMenuAuthController()
    {
        if (mainMenuAuthController != null)
        {
            return mainMenuAuthController;
        }

        mainMenuAuthController = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        return mainMenuAuthController;
    }


    public string GetPrompt()
    {
        return $"Talk to {npcName}";
    }
}
