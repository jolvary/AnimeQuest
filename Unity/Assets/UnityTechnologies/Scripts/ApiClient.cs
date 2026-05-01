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

    public async Task<string> PostEnsureMe()
    {
        var req = CreateRequest($"{baseUrl}/api/me/ensure", UnityWebRequest.kHttpVerbPOST, "{}");
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
    }

    public async Task<string> GetAnime(string q = "", int limit = 20)
    {
        string url = $"{baseUrl}/api/anime?q={UnityWebRequest.EscapeURL(q)}&limit={limit}";
        bool includeAuth = NakamaAuthManager.Instance != null && NakamaAuthManager.Instance.IsAuthenticated;
        var req = CreateRequest(url, UnityWebRequest.kHttpVerbGET, includeAuth: includeAuth);
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


    public async Task<string> ImportMyAnimeList(string username)
    {
        string body = JsonUtility.ToJson(new MalImportBody { username = username });
        var req = CreateRequest($"{baseUrl}/api/mal/import", UnityWebRequest.kHttpVerbPOST, body);
        await req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception(req.error + " | " + req.downloadHandler.text);

        return req.downloadHandler.text;
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
    private class MalImportBody
    {
        public string username;
    }
}
