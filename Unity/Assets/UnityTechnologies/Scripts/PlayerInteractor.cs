using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    public Transform cam;
    public float interactDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI promptText;

    private IInteractable current;

    void Update()
    {
        DetectInteractable();

        if (current != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            current.Interact();
        }
    }

    void DetectInteractable()
    {
        current = null;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, interactDistance))
        {

            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {

                current = interactable;
                if (promptText != null)
                {
                    promptText.text = $"{current.GetPrompt()} (E)";
                    promptText.enabled = true;
                }
                return;
            }
        }

        Debug.Log("No Hit!");

        if (promptText != null)
            promptText.enabled = false;
    }
}
