using UnityEngine;

public class NPCQuestGiver : MonoBehaviour, IInteractable
{
    public UIManager ui;
    public string npcName = "Quest NPC";

    public void Interact()
    {
        if (ui) ui.questsPanel.SetActive(true);
        Debug.Log($"{npcName}: Open quests UI");
    }

    public string GetPrompt() => $"Talk to {npcName}";
}
