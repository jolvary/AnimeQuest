using UnityEngine;
using UnityEngine.UI;

public class RoundedUiSurfaces : MonoBehaviour
{
    private const float ScanInterval = 0.25f;
    private float _timeUntilScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<RoundedUiSurfaces>() != null) return;

        var obj = new GameObject("RoundedUiSurfaces");
        DontDestroyOnLoad(obj);
        obj.AddComponent<RoundedUiSurfaces>();
    }

    private void Awake()
    {
        _timeUntilScan = 0f;
    }

    private void Update()
    {
        _timeUntilScan -= Time.unscaledDeltaTime;
        if (_timeUntilScan > 0f) return;

        _timeUntilScan = ScanInterval;
        HideGeneratedSurfaces();
    }

    private static void HideGeneratedSurfaces()
    {
        var images = Resources.FindObjectsOfTypeAll<Image>();
        foreach (var image in images)
        {
            if (image == null) continue;
            if (!image.gameObject.scene.IsValid()) continue;
            if (!ShouldBeInvisible(image)) continue;
            if (image.color.a <= 0.001f) continue;

            var color = image.color;
            color.a = 0f;
            image.color = color;
        }
    }

    private static bool ShouldBeInvisible(Image image)
    {
        string name = image.gameObject.name;
        return name.StartsWith("AnimeCard_") ||
               name == "AnimeSearchInput" ||
               name == "ExpandedDescription" ||
               name.StartsWith("Match_") ||
               name == "ChatMessage" ||
               name == "ChatMessageInput" ||
               name.StartsWith("Friend_") ||
               name == "FriendSearchInput";
    }
}
