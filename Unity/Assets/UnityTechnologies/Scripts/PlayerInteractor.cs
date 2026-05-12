using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class PlayerInteractor : MonoBehaviour
{
    private const float ContactTimeoutSeconds = 0.2f;

    [Header("References")]
    public Transform cam;
    public TextMeshProUGUI promptText;

    [Header("Settings")]
    public float interactDistance = 3f;
    public LayerMask interactMask = ~0;

    private IInteractable current;
    private readonly Dictionary<Collider, float> _contactColliders = new Dictionary<Collider, float>();
    private readonly List<Collider> _staleContacts = new List<Collider>();

    private void Awake()
    {
        SetPrompt(false, "");
    }

    private void Update()
    {
        RefreshCurrentInteractable();

        bool interactPressed =
            (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (current != null && interactPressed)
        {
            current.Interact(this);
        }
    }

    private void RefreshCurrentInteractable()
    {
        current = null;

        PruneStaleContacts();
        foreach (var collider in _contactColliders.Keys)
        {
            var interactable = collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                current = interactable;
                SetPrompt(true, current.GetPrompt());
                return;
            }
        }

        SetPrompt(false, "");
    }

    private void OnTriggerEnter(Collider other)
    {
        AddInteractableCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        AddInteractableCollider(other);
    }

    private void OnTriggerExit(Collider other)
    {
        _contactColliders.Remove(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        AddInteractableCollider(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        AddInteractableCollider(collision.collider);
    }

    private void OnCollisionExit(Collision collision)
    {
        _contactColliders.Remove(collision.collider);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        AddInteractableCollider(hit.collider);
    }

    private void AddInteractableCollider(Collider other)
    {
        if (other == null) return;
        if ((interactMask.value & (1 << other.gameObject.layer)) == 0) return;
        if (other.GetComponentInParent<IInteractable>() == null) return;

        _contactColliders[other] = Time.unscaledTime;
    }

    private void PruneStaleContacts()
    {
        _staleContacts.Clear();
        foreach (var item in _contactColliders)
        {
            var collider = item.Key;
            if (collider == null ||
                !collider.enabled ||
                !collider.gameObject.activeInHierarchy ||
                Time.unscaledTime - item.Value > ContactTimeoutSeconds)
            {
                _staleContacts.Add(collider);
            }
        }

        foreach (var collider in _staleContacts)
        {
            _contactColliders.Remove(collider);
        }
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
