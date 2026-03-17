using UnityEngine;

public class QuestPanelController : MonoBehaviour
{
    public async void RefreshQuests()
    {
        try
        {
            string json = await ApiClient.Instance.GetQuests();
            Debug.Log("Quests: " + json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load quests: " + ex.Message);
        }
    }

    public void OpenFromNpc(string npcName, string questCode)
    {
        Debug.Log($"Opened quest panel from NPC {npcName} for quest {questCode}");
        RefreshQuests();
    }

    public async void AcceptQuest(string questCode)
    {
        try
        {
            string json = await ApiClient.Instance.AcceptQuest(questCode);
            Debug.Log("Quest accepted: " + json);
            RefreshQuests();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to accept quest: " + ex.Message);
        }
    }
}