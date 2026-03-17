using UnityEngine;

public class TableViewerPanelController : MonoBehaviour
{
    public async void OpenTable(string tableName)
    {
        try
        {
            string json = await ApiClient.Instance.GetTable(tableName, 50, 0);
            Debug.Log($"Table {tableName}: " + json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load table: " + ex.Message);
        }
    }
}