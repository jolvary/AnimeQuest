using UnityEngine;

public class NPCQuestGiver : MonoBehaviour, IInteractable
{
    [Header("NPC")]
    public string npcName = "Quest NPC";
    public string questCode = "starter-quest";

    [Header("References")]
    public UIManager uiManager;
    public QuestPanelController questPanelController;

    public void Interact(PlayerInteractor interactor)
    {
        if (uiManager != null)
        {
            uiManager.OpenQuestsPanel();
        }

        if (questPanelController != null)
        {
            questPanelController.OpenFromNpc(npcName, questCode);
        }

        Debug.Log($"{npcName}: opened quest UI for quest code {questCode}");
    }

    public string GetPrompt()
    {
        return $"Talk to {npcName}";
    }
}