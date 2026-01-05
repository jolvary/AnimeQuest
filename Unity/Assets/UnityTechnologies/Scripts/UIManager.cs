using UnityEngine;
using UnityEngine.InputSystem;

public class UIManager : MonoBehaviour
{
    public GameObject chatPanel;
    public GameObject friendsPanel;
    public GameObject questsPanel;
    public GameObject animePanel;
    public GameObject tablePanel;

    void Start()
    {
        HideAll();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) Toggle(chatPanel);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) Toggle(friendsPanel);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) Toggle(questsPanel);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) Toggle(animePanel);
        if (Keyboard.current.digit5Key.wasPressedThisFrame) Toggle(tablePanel);
    }

    void Toggle(GameObject panel)
    {
        if (!panel) return;
        panel.SetActive(!panel.activeSelf);
    }

    public void HideAll()
    {
        if (chatPanel) chatPanel.SetActive(false);
        if (friendsPanel) friendsPanel.SetActive(false);
        if (questsPanel) questsPanel.SetActive(false);
        if (animePanel) animePanel.SetActive(false);
        if (tablePanel) tablePanel.SetActive(false);
    }
}
