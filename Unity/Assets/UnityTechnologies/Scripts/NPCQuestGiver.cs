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
        "Use the Tab wheel to open panels at any time. Anime shows the shared catalog, Matches compares favorites with other players, and recommendations use your linked MyAnimeList history to suggest what to watch next.";

    [TextArea(3, 6)]
    public string recommendationsUnavailableText =
        "I need a little anime history before I can recommend anything useful. Log in with an account and import or track a few shows first; then come back and I can suggest anime based on what you already like.";

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
                new DialoguePanelController.DialogueOption("Recommended Anime", OpenRecommendedAnime),
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
                new DialoguePanelController.DialogueOption("Recommended Anime", OpenRecommendedAnime),
                new DialoguePanelController.DialogueOption("Back", ShowIntroDialogue),
                new DialoguePanelController.DialogueOption("Leave", () => uiManager.CloseDialoguePanel()),
            }
        );
    }

    private void OpenRecommendedAnime()
    {
        if (NeedsAccountForRecommendations())
        {
            ShowRecommendationsUnavailableDialogue();
            return;
        }

        uiManager?.OpenAnimeSuggestionsPanel();
        Debug.Log($"{npcName}: opened recommended anime for quest code {questCode}");
    }

    private void ShowRecommendationsUnavailableDialogue()
    {
        uiManager.OpenDialoguePanel(
            npcName,
            recommendationsUnavailableText,
            new DialoguePanelController.DialogueOption[]
            {
                new DialoguePanelController.DialogueOption("Start / Login", OpenMainMenu),
                new DialoguePanelController.DialogueOption("Back", ShowIntroDialogue),
                new DialoguePanelController.DialogueOption("Leave", () => uiManager.CloseDialoguePanel()),
            }
        );
    }

    private static bool NeedsAccountForRecommendations()
    {
        var auth = NakamaAuthManager.Instance;
        return auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession;
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
