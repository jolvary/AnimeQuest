using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

public class ApiClient : MonoBehaviour
{
    public const string SessionConflictMarker = "SESSION_CONFLICT";
    public static ApiClient Instance;

    [SerializeField] private string baseUrl = "http://localhost:3000";
    [SerializeField] private bool autoResolveLocalhost = true;

    private readonly string _clientInstanceId = Guid.NewGuid().ToString("N");

    public string ClientInstanceId => _clientInstanceId;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);

        if (autoResolveLocalhost)
        {
            baseUrl = ResolveBaseUrlForRuntime(baseUrl);
        }

        DozzleLogger.Action("API base URL resolved", baseUrl);
        DozzleLogger.FlushPending();
    }

    private static string ResolveBaseUrlForRuntime(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return rawUrl;
        if (!IsLocalhost(uri.Host)) return rawUrl;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (TryResolveHostedServiceUrl("api", out string hostedApiUrl))
        {
            return hostedApiUrl;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        string runtimeHost = "10.0.2.2";
#else
        string runtimeHost = "127.0.0.1";
#endif

        return string.Format(CultureInfo.InvariantCulture, "{0}://{1}:{2}", uri.Scheme, runtimeHost, uri.Port);
    }

    private static bool IsLocalhost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static bool TryResolveHostedServiceUrl(string serviceSubdomain, out string serviceUrl)
    {
        serviceUrl = null;
        if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var pageUri)) return false;
        if (IsLocalhost(pageUri.Host)) return false;

        string host = pageUri.Host;
        if (host.StartsWith("play.", StringComparison.OrdinalIgnoreCase))
        {
            host = serviceSubdomain + "." + host.Substring("play.".Length);
        }
        else if (!host.StartsWith(serviceSubdomain + ".", StringComparison.OrdinalIgnoreCase))
        {
            host = serviceSubdomain + "." + host;
        }

        serviceUrl = string.Format(CultureInfo.InvariantCulture, "{0}://{1}", pageUri.Scheme, host);
        return true;
    }
#endif

    public string BuildImageProxyUrl(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return imageUrl;
        return $"{baseUrl}/client/image?url={UnityWebRequest.EscapeURL(imageUrl)}";
    }

    private string AuthToken => NakamaAuthManager.Instance.Session?.AuthToken;

    private UnityWebRequest CreateRequest(string url, string method, string jsonBody = null, bool includeAuth = true)
    {
        var req = new UnityWebRequest(url, method);
        req.downloadHandler = new DownloadHandlerBuffer();

        if (!string.IsNullOrEmpty(jsonBody))
        {
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.SetRequestHeader("Content-Type", "application/json");
        }

        if (includeAuth && !string.IsNullOrWhiteSpace(AuthToken))
        {
            req.SetRequestHeader("Authorization", $"Bearer {AuthToken}");
        }
        return req;
    }

    private async Task EnsureApiSession()
    {
        var auth = NakamaAuthManager.Instance;
        if (auth == null)
        {
            throw new Exception("Auth manager unavailable");
        }

        if (!auth.IsAuthenticated)
        {
            await auth.EnsureIncognitoSessionAsync();
        }

        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(AuthToken))
        {
            throw new Exception("Unable to start an incognito session");
        }
    }

    private async Task<UnityWebRequest> CreateApiRequest(string url, string method, string jsonBody = null)
    {
        await EnsureApiSession();
        return CreateRequest(url, method, jsonBody);
    }

    public async Task PostClientLog(string level, string action, string details = null, string message = null, string timestamp = null)
    {
        var body = JsonUtility.ToJson(new ClientLogBody
        {
            level = level,
            action = action,
            details = details,
            message = message,
            timestamp = timestamp,
            platform = Application.platform.ToString(),
            unityVersion = Application.unityVersion,
        });

        var req = CreateRequest($"{baseUrl}/client/logs", UnityWebRequest.kHttpVerbPOST, body, includeAuth: false);
        await req.SendWebRequest();
    }

    public async Task<string> PostEnsureMe()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/me/ensure", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task AcquireSessionLease()
    {
        await SendSessionLeaseRequest("acquire", throwConflict: true);
    }

    public async Task HeartbeatSessionLease()
    {
        await SendSessionLeaseRequest("heartbeat", throwConflict: true);
    }

    public async Task ReleaseSessionLease()
    {
        await SendSessionLeaseRequest("release", throwConflict: false);
    }

    private async Task SendSessionLeaseRequest(string action, bool throwConflict)
    {
        string body = JsonUtility.ToJson(new SessionLeaseBody
        {
            clientId = _clientInstanceId,
            platform = Application.platform.ToString(),
            unityVersion = Application.unityVersion,
        });

        var req = CreateRequest($"{baseUrl}/client/session/{action}", UnityWebRequest.kHttpVerbPOST, body);
        await req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success) return;

        string responseText = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
        if (throwConflict && req.responseCode == 409)
        {
            throw new Exception($"{SessionConflictMarker}: This account is already logged in on another device.");
        }

        throw new Exception(req.error + " | " + responseText);
    }

    public async Task<string> GetAnime(string q = "", int limit = 100, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime?q={UnityWebRequest.EscapeURL(q)}&limit={limit}&offset={offset}";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetUserAnime(string q = "", int limit = 100, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime/user?q={UnityWebRequest.EscapeURL(q)}&limit={limit}&offset={offset}";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnimeByGenre(string genre, string q = "", int limit = 100, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime/genre/{UnityWebRequest.EscapeURL(genre)}?q={UnityWebRequest.EscapeURL(q)}&limit={limit}&offset={offset}";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnimeSuggestions(int limit = 20, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime/suggestions?limit={limit}&offset={offset}";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnimeMatches(string q = "", int limit = 100)
    {
        string url = $"{baseUrl}/api/anime/matches?q={UnityWebRequest.EscapeURL(q)}&limit={limit}";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnimeDetails(string animeId)
    {
        string url = $"{baseUrl}/api/anime/{UnityWebRequest.EscapeURL(animeId)}/details";
        var req = await CreateApiRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetQuests()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/quests", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> ClaimQuest(string code)
    {
        var req = await CreateApiRequest($"{baseUrl}/api/quests/{UnityWebRequest.EscapeURL(code)}/claim", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetCharacterProgression()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/characters", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> SelectCharacter(string characterKey, string robotColor = null)
    {
        string body = JsonUtility.ToJson(new CharacterSelectBody { characterKey = characterKey, robotColor = robotColor });
        var req = await CreateApiRequest($"{baseUrl}/api/characters/select", UnityWebRequest.kHttpVerbPOST, body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetTable(string tableName, int limit = 50, int offset = 0)
    {
        var req = await CreateApiRequest($"{baseUrl}/api/table/{tableName}?limit={limit}&offset={offset}", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> PatchWatching(string animeId, bool isWatching)
    {
        string body = JsonUtility.ToJson(new WatchingPatchBody { isWatching = isWatching });
        var req = await CreateApiRequest($"{baseUrl}/api/anime/{animeId}/watching", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> PatchLists(string animeId, string[] add, string[] remove)
    {
        string body = JsonUtility.ToJson(new ListsPatchBody { add = add ?? Array.Empty<string>(), remove = remove ?? Array.Empty<string>() });
        var req = await CreateApiRequest($"{baseUrl}/api/anime/{animeId}/lists", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> PatchAnimeProgress(string animeId, string status, int score, int episodesWatched)
    {
        string body = JsonUtility.ToJson(new AnimeProgressPatchBody
        {
            status = status,
            score = score,
            episodesWatched = episodesWatched,
        });
        var req = await CreateApiRequest($"{baseUrl}/api/anime/{animeId}/progress", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<PlayerStateResponse> GetPlayerState()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/player/state", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return JsonUtility.FromJson<PlayerStateResponse>(req.downloadHandler.text);
    }

    public async Task<string> PatchPlayerState(Vector3 position, float rotationY)
    {
        string body = JsonUtility.ToJson(new PlayerStatePatchBody
        {
            x = position.x,
            y = position.y,
            z = position.z,
            rotationY = rotationY,
        });
        var req = await CreateApiRequest($"{baseUrl}/api/player/state", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> StartMyAnimeListLink()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/mal/oauth/start", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        var response = JsonUtility.FromJson<MalOAuthStartResponse>(req.downloadHandler.text);
        if (response == null || string.IsNullOrWhiteSpace(response.url))
            throw new Exception("MAL authorization URL missing");

        return response.url;
    }

    public async Task<MalOAuthStatusResponse> GetMyAnimeListOAuthStatus()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/mal/oauth/status", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return JsonUtility.FromJson<MalOAuthStatusResponse>(req.downloadHandler.text);
    }

    public async Task<string> ImportMyAnimeList()
    {
        var req = await CreateApiRequest($"{baseUrl}/api/mal/import", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    [Serializable]
    public class MalOAuthStatusResponse
    {
        public bool configured = true;
        public bool linked;
        public string malUsername;
        public bool reconnectRequired;
    }

    [Serializable]
    public class CharacterProgressionResponse
    {
        public CharacterProfile profile;
        public CharacterItem[] characters;
    }

    [Serializable]
    public class CharacterProfile
    {
        public string userId;
        public string displayName;
        public int experiencePoints;
        public int level;
        public int nextLevelExperience;
        public int coins;
        public string selectedCharacterKey;
        public string robotColor;
    }

    [Serializable]
    public class CharacterItem
    {
        public string key;
        public string displayName;
        public string description;
        public string kind;
        public string robotColor;
        public string prefabSlot;
        public int unlockLevel;
        public string unlockQuestCode;
        public string assetStoreUrl;
        public bool unlocked;
        public bool selected;
    }

    [Serializable]
    public class PlayerStateResponse
    {
        public bool hasPosition;
        public float x;
        public float y;
        public float z;
        public float rotationY;
        public string updatedAt;
    }

    [Serializable]
    private class MalOAuthStartResponse
    {
        public string url;
    }

    [Serializable]
    private class ClientLogBody
    {
        public string level;
        public string action;
        public string details;
        public string message;
        public string timestamp;
        public string platform;
        public string unityVersion;
    }

    [Serializable]
    private class SessionLeaseBody
    {
        public string clientId;
        public string platform;
        public string unityVersion;
    }

    [Serializable]
    private class CharacterSelectBody
    {
        public string characterKey;
        public string robotColor;
    }

    [Serializable]
    private class PlayerStatePatchBody
    {
        public float x;
        public float y;
        public float z;
        public float rotationY;
    }

    [Serializable]
    private class WatchingPatchBody
    {
        public bool isWatching;
    }

    [Serializable]
    private class ListsPatchBody
    {
        public string[] add;
        public string[] remove;
    }

    [Serializable]
    private class AnimeProgressPatchBody
    {
        public string status;
        public int score;
        public int episodesWatched;
    }
}
