using UnityEngine;
using TMPro;

public class PlayerInteractor : MonoBehaviour
{
    public Transform cam;
    public float interactDistance = 3f;

    [Header("UI")]
    public TextMeshProUGUI promptText;

    private IInteractable _current;

    void Update()
    {
        DetectInteractable();

        if (_current != null && Input.GetKeyDown(KeyCode.E))
        {
            _current.Interact();
        }
    }

    void DetectInteractable()
    {
        _current = null;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, interactDistance))
        {
            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                _current = interactable;
                promptText.text = $"{_current.GetPrompt()} (E)";
                promptText.enabled = true;
                return;
            }
        }

        promptText.enabled = false;
    }
}
