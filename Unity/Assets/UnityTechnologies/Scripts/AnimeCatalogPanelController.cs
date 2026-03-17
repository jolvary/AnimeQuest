using UnityEngine;

public class AnimeCatalogPanelController : MonoBehaviour
{
    public string defaultSearch = "";
    public int defaultLimit = 20;

    public async void RefreshCatalog()
    {
        try
        {
            string json = await ApiClient.Instance.GetAnime(defaultSearch, defaultLimit);
            Debug.Log("Anime catalog: " + json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load anime catalog: " + ex.Message);
        }
    }
}