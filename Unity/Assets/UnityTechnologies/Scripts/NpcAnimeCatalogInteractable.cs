using UnityEngine;

[DisallowMultipleComponent]
public class NpcAnimeCatalogInteractable : MonoBehaviour, IInteractable
{
    public enum AnimeListMode
    {
        Genre,
        Suggestions
    }

    private const string InteractionTriggerName = "InteractionTrigger";

    [Header("NPC")]
    public string npcName = "Anime Guide";
    public AnimeListMode listMode = AnimeListMode.Genre;
    public string genre = "Action";
    public string promptOverride = "";

    [Header("Interaction")]
    public float interactionRadius = 2.4f;
    public UIManager uiManager;

    private void Reset()
    {
        EnsureInteractionTrigger();
    }

    private void Awake()
    {
        ResolveUiManager();
        EnsureInteractionTrigger();
    }

    private void OnValidate()
    {
        interactionRadius = Mathf.Max(0.25f, interactionRadius);
        var trigger = transform.Find(InteractionTriggerName);
        if (trigger == null) return;

        var sphere = trigger.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            sphere.isTrigger = true;
            sphere.radius = interactionRadius;
        }
    }

    public void Interact(PlayerInteractor interactor)
    {
        ResolveUiManager();
        if (uiManager == null)
        {
            Debug.LogWarning($"{name}: no UIManager found for anime NPC interaction.");
            return;
        }

        if (listMode == AnimeListMode.Suggestions)
        {
            uiManager.OpenAnimeSuggestionsPanel();
            return;
        }

        uiManager.OpenAnimeGenrePanel(genre);
    }

    public string GetPrompt()
    {
        if (!string.IsNullOrWhiteSpace(promptOverride))
        {
            return promptOverride.Trim();
        }

        return $"Talk to {npcName}";
    }

    private void ResolveUiManager()
    {
        if (uiManager != null) return;
        uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
    }

    private void EnsureInteractionTrigger()
    {
        Transform trigger = transform.Find(InteractionTriggerName);
        if (trigger == null)
        {
            var triggerObject = new GameObject(InteractionTriggerName);
            triggerObject.transform.SetParent(transform, false);
            trigger = triggerObject.transform;
        }

        var sphere = trigger.GetComponent<SphereCollider>();
        if (sphere == null)
        {
            sphere = trigger.gameObject.AddComponent<SphereCollider>();
        }

        sphere.isTrigger = true;
        sphere.center = Vector3.zero;
        sphere.radius = Mathf.Max(0.25f, interactionRadius);

        var body = trigger.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = trigger.gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;
    }
}
