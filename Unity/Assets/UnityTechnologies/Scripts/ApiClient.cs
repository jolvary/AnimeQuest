using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance;

    [SerializeField] private string baseUrl = "http://localhost:3000";
    [SerializeField] private bool autoResolveLocalhost = true;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);

        if (autoResolveLocalhost)
        {
            baseUrl = ResolveBaseUrlForRuntime(baseUrl);
        }

        DozzleLogger.FlushPending();
    }

    private static string ResolveBaseUrlForRuntime(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return rawUrl;
        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return rawUrl;

#if UNITY_ANDROID && !UNITY_EDITOR
        string runtimeHost = "10.0.2.2";
#else
        string runtimeHost = "127.0.0.1";
#endif

        return string.Format(CultureInfo.InvariantCulture, "{0}://{1}:{2}", uri.Scheme, runtimeHost, uri.Port);
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
        var req = CreateRequest($"{baseUrl}/api/me/ensure", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnime(string q = "", int limit = 100, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime?q={UnityWebRequest.EscapeURL(q)}&limit={limit}&offset={offset}";
        bool includeAuth = NakamaAuthManager.Instance != null && NakamaAuthManager.Instance.IsAuthenticated;
        var req = CreateRequest(url, UnityWebRequest.kHttpVerbGET, includeAuth: includeAuth);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetUserAnime(string q = "", int limit = 100, int offset = 0)
    {
        string url = $"{baseUrl}/api/anime/user?q={UnityWebRequest.EscapeURL(q)}&limit={limit}&offset={offset}";
        var req = CreateRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnimeMatches(string q = "", int limit = 100)
    {
        string url = $"{baseUrl}/api/anime/matches?q={UnityWebRequest.EscapeURL(q)}&limit={limit}";
        var req = CreateRequest(url, UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetQuests()
    {
        var req = CreateRequest($"{baseUrl}/api/quests", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> AcceptQuest(string code)
    {
        var req = CreateRequest($"{baseUrl}/api/quests/{code}/accept", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetTable(string tableName, int limit = 50, int offset = 0)
    {
        var req = CreateRequest($"{baseUrl}/api/table/{tableName}?limit={limit}&offset={offset}", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> PatchWatching(string animeId, bool isWatching)
    {
        string body = JsonUtility.ToJson(new WatchingPatchBody { isWatching = isWatching });
        var req = CreateRequest($"{baseUrl}/api/anime/{animeId}/watching", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> PatchLists(string animeId, string[] add, string[] remove)
    {
        string body = JsonUtility.ToJson(new ListsPatchBody { add = add ?? Array.Empty<string>(), remove = remove ?? Array.Empty<string>() });
        var req = CreateRequest($"{baseUrl}/api/anime/{animeId}/lists", "PATCH", body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> StartMyAnimeListLink()
    {
        var req = CreateRequest($"{baseUrl}/api/mal/oauth/start", UnityWebRequest.kHttpVerbGET);
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
        var req = CreateRequest($"{baseUrl}/api/mal/oauth/status", UnityWebRequest.kHttpVerbGET);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return JsonUtility.FromJson<MalOAuthStatusResponse>(req.downloadHandler.text);
    }

    public async Task<string> ImportMyAnimeList()
    {
        var req = CreateRequest($"{baseUrl}/api/mal/import", UnityWebRequest.kHttpVerbPOST, "{}");
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
}
