using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    public Transform cam;
    public TextMeshProUGUI promptText;

    [Header("Settings")]
    public float interactDistance = 3f;
    public LayerMask interactMask = ~0;

    private IInteractable current;

    private void Update()
    {
        DetectInteractable();

        bool interactPressed =
            (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (current != null && interactPressed)
        {
            current.Interact(this);
        }
    }

    private void DetectInteractable()
    {
        current = null;

        if (cam == null)
        {
            SetPrompt(false, "");
            return;
        }

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, interactDistance, interactMask))
        {
            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                current = interactable;
                SetPrompt(true, $"{current.GetPrompt()}");
                return;
            }
        }

        SetPrompt(false, "");
    }

    private void SetPrompt(bool visible, string text)
    {
        if (promptText == null) return;

        promptText.enabled = visible;
        if (visible)
        {
#if UNITY_IOS || UNITY_ANDROID
            promptText.text = $"{text} (Tap)";
#else
            promptText.text = $"{text} (E)";
#endif
        }
    }
}