using UnityEngine;

public class NPCQuestGiver : MonoBehaviour, IInteractable
{
    private const string InteractionTriggerName = "InteractionTrigger";

    [Header("NPC")]
    public string npcName = "Quest NPC";
    public string questCode = "starter-quest";
    public bool useIntroDialogue = true;

    [TextArea(4, 8)]
    public string introText =
        "Welcome to AnimeQuest. This world is your anime hub: link your MyAnimeList account, import your list, browse the synced catalog, track episodes, discover suggestions, compare shows with friends, and chat while you explore.";

    [TextArea(4, 8)]
    public string helpText =
        "Use the Tab wheel to open panels at any time. Anime shows the shared catalog, My List shows your imported or tracked anime, Matches compares favorites with other players, and guide NPCs can show genres or personalized MyAnimeList suggestions.";

    [Header("References")]
    public UIManager uiManager;
    public QuestPanelController questPanelController;
    public MainMenuAuthController mainMenuAuthController;

    [Header("Interaction")]
    public float interactionRadius = 2.4f;

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
        if (useIntroDialogue && uiManager != null)
        {
            ShowIntroDialogue();
            Debug.Log($"{npcName}: opened intro dialogue for quest code {questCode}");
            return;
        }

        OpenMainMenu();
    }

    private void ShowIntroDialogue()
    {
        uiManager.OpenDialoguePanel(
            npcName,
            introText,
            new DialoguePanelController.DialogueOption[]
            {
                new DialoguePanelController.DialogueOption("Start / Login", OpenMainMenu),
                new DialoguePanelController.DialogueOption("Browse Anime Catalog", () => uiManager.OpenAnimePanel()),
                new DialoguePanelController.DialogueOption("Open My List", () => uiManager.OpenUserCatalogPanel()),
                new DialoguePanelController.DialogueOption("How does this work?", ShowHelpDialogue),
                new DialoguePanelController.DialogueOption("Leave", () => uiManager.CloseDialoguePanel()),
            }
        );
    }

    private void ShowHelpDialogue()
    {
        uiManager.OpenDialoguePanel(
            npcName,
            helpText,
            new DialoguePanelController.DialogueOption[]
            {
                new DialoguePanelController.DialogueOption("Start / Login", OpenMainMenu),
                new DialoguePanelController.DialogueOption("Back", ShowIntroDialogue),
                new DialoguePanelController.DialogueOption("Leave", () => uiManager.CloseDialoguePanel()),
            }
        );
    }

    private void OpenMainMenu()
    {
        uiManager?.HideAll();

        var menuController = GetOrCreateMainMenuAuthController();
        if (menuController != null)
        {
            menuController.gameObject.SetActive(true);
            menuController.ShowLoginPanel();
        }

        Debug.Log($"{npcName}: opened Main Menu for quest code {questCode}");
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

    private MainMenuAuthController GetOrCreateMainMenuAuthController()
    {
        if (mainMenuAuthController != null)
        {
            return mainMenuAuthController;
        }

        mainMenuAuthController = FindFirstObjectByType<MainMenuAuthController>(FindObjectsInactive.Include);
        return mainMenuAuthController;
    }


    public string GetPrompt()
    {
        return $"Talk to {npcName}";
    }
}
