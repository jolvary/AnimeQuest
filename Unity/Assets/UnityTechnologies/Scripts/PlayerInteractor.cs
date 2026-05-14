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
    private bool _promptConfigured;
    private bool _promptSuppressed;
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
        if (_promptSuppressed)
        {
            SetPrompt(false, "");
            return;
        }

        RefreshCurrentInteractable();

        bool interactPressed =
            (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (interactPressed)
        {
            TryInteract();
        }
    }

    public bool TryInteract()
    {
        if (_promptSuppressed) return false;

        RefreshCurrentInteractable();
        if (current == null) return false;

        current.Interact(this);
        return true;
    }

    public void SetPromptSuppressed(bool suppressed)
    {
        if (_promptSuppressed == suppressed) return;

        _promptSuppressed = suppressed;
        if (_promptSuppressed)
        {
            SetPrompt(false, "");
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

        ConfigurePromptText();
        promptText.enabled = visible;
        if (visible)
        {
#if UNITY_IOS || UNITY_ANDROID
            promptText.text = text;
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

    private void ConfigurePromptText()
    {
        if (_promptConfigured || promptText == null) return;

        var rect = promptText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -86f);
        rect.sizeDelta = new Vector2(760f, 64f);

        promptText.alignment = TextAlignmentOptions.Center;
        promptText.textWrappingMode = TextWrappingModes.NoWrap;
        promptText.overflowMode = TextOverflowModes.Overflow;
        promptText.fontSize = 34f;
        promptText.fontStyle = FontStyles.Bold;
        promptText.color = Color.white;
        promptText.outlineColor = new Color32(55, 31, 16, 255);
        promptText.outlineWidth = 0.18f;
        promptText.raycastTarget = false;
        promptText.transform.SetAsLastSibling();

        _promptConfigured = true;
    }
}
