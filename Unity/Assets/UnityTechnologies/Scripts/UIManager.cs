using UnityEngine;
using UnityEngine.InputSystem;

public class UIManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject chatPanel;
    public GameObject friendsPanel;
    public GameObject questsPanel;
    public GameObject animePanel;
    public GameObject tablePanel;

    [Header("Controllers")]
    public QuestPanelController questPanelController;
    public AnimeCatalogPanelController animeCatalogPanelController;
    public TableViewerPanelController tableViewerPanelController;

    private void Start()
    {
        HideAll();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) Toggle(chatPanel);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) Toggle(friendsPanel);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) OpenQuestsPanel();
        if (Keyboard.current.digit4Key.wasPressedThisFrame) OpenAnimePanel();
        if (Keyboard.current.digit5Key.wasPressedThisFrame) OpenTablePanel("anime");
    }

    public void OpenQuestsPanel()
    {
        ToggleExclusive(questsPanel);
        questPanelController?.RefreshQuests();
    }

    public void OpenAnimePanel()
    {
        ToggleExclusive(animePanel);
        animeCatalogPanelController?.RefreshCatalog();
    }

    public void OpenTablePanel(string tableName)
    {
        ToggleExclusive(tablePanel);
        tableViewerPanelController?.OpenTable(tableName);
    }

    public void OpenChatPanel()
    {
        ToggleExclusive(chatPanel);
    }

    public void OpenFriendsPanel()
    {
        ToggleExclusive(friendsPanel);
    }

    public void HideAll()
    {
        if (chatPanel) chatPanel.SetActive(false);
        if (friendsPanel) friendsPanel.SetActive(false);
        if (questsPanel) questsPanel.SetActive(false);
        if (animePanel) animePanel.SetActive(false);
        if (tablePanel) tablePanel.SetActive(false);
    }

    private void Toggle(GameObject panel)
    {
        if (!panel) return;
        panel.SetActive(!panel.activeSelf);
    }

    private void ToggleExclusive(GameObject target)
    {
        HideAll();
        if (target) target.SetActive(true);
    }
}