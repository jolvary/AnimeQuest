using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class PlayerInteractor : MonoBehaviour
{
    private const float ContactTimeoutSeconds = 0.2f;
    private const int MaxNearbyColliders = 64;

    [Header("References")]
    public Transform cam;
    public TextMeshProUGUI promptText;

    [Header("Settings")]
    public float interactDistance = 3f;
    public LayerMask interactMask = ~0;

    private IInteractable current;
    private readonly Dictionary<Collider, float> _contactColliders = new Dictionary<Collider, float>();
    private readonly List<Collider> _staleContacts = new List<Collider>();
    private readonly Collider[] _nearbyColliders = new Collider[MaxNearbyColliders];

    private void Awake()
    {
        ResolvePromptText();
        SetPrompt(false, "");
    }

    private void OnEnable()
    {
        ResolvePromptText();
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
        current = FindBestInteractable();
        if (current != null)
        {
            SetPrompt(true, current.GetPrompt());
            return;
        }

        SetPrompt(false, "");
    }

    private IInteractable FindBestInteractable()
    {
        IInteractable best = null;
        float bestSqrDistance = float.PositiveInfinity;

        foreach (var collider in _contactColliders.Keys)
        {
            ConsiderInteractableCollider(collider, ref best, ref bestSqrDistance);
        }

        float radius = Mathf.Max(0.1f, interactDistance);
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            _nearbyColliders,
            EffectiveInteractMask(),
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < count; i++)
        {
            ConsiderInteractableCollider(_nearbyColliders[i], ref best, ref bestSqrDistance);
        }

        return best;
    }

    private void ConsiderInteractableCollider(Collider other, ref IInteractable best, ref float bestSqrDistance)
    {
        var interactable = ResolveInteractable(other);
        if (interactable == null) return;

        Vector3 closestPoint = other.ClosestPoint(transform.position);
        float sqrDistance = (closestPoint - transform.position).sqrMagnitude;
        if (sqrDistance >= bestSqrDistance) return;

        best = interactable;
        bestSqrDistance = sqrDistance;
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
        if (ResolveInteractable(other) == null) return;

        _contactColliders[other] = Time.unscaledTime;
    }

    private IInteractable ResolveInteractable(Collider other)
    {
        if (other == null || !other.enabled || !other.gameObject.activeInHierarchy) return null;
        if ((EffectiveInteractMask() & (1 << other.gameObject.layer)) == 0) return null;

        return other.GetComponentInParent<IInteractable>();
    }

    private int EffectiveInteractMask()
    {
        return interactMask.value == 0 ? ~0 : interactMask.value;
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
        ResolvePromptText();
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
        else
        {
            promptText.text = string.Empty;
        }
    }

    private void ResolvePromptText()
    {
        if (promptText != null) return;

        var texts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var text in texts)
        {
            if (text != null && text.name == "PromptText")
            {
                promptText = text;
                return;
            }
        }
    }
}
