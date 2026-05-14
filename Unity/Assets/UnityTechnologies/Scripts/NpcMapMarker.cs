using UnityEngine;

[DisallowMultipleComponent]
public class NpcMapMarker : MonoBehaviour
{
    public string markerLabel = "";
    public Color markerColor = new Color(0.14f, 0.32f, 0.78f, 1f);
    public bool showOnMap = true;

    public string ResolveLabel()
    {
        if (!string.IsNullOrWhiteSpace(markerLabel))
        {
            return markerLabel.Trim();
        }

        var questGiver = GetComponent<NPCQuestGiver>();
        if (questGiver != null && !string.IsNullOrWhiteSpace(questGiver.npcName))
        {
            return questGiver.npcName.Trim();
        }

        var animeGuide = GetComponent<NpcAnimeCatalogInteractable>();
        if (animeGuide != null && !string.IsNullOrWhiteSpace(animeGuide.npcName))
        {
            return animeGuide.npcName.Trim();
        }

        return gameObject.name;
    }
}
