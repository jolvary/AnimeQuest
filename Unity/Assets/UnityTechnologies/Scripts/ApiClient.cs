using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Threading.Tasks;

public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance;

    [SerializeField] private string baseUrl = "http://localhost:3000";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    private string AuthToken => NakamaAuthManager.Instance.Session?.AuthToken;

    private UnityWebRequest CreateRequest(string url, string method, string jsonBody = null)
    {
        var req = new UnityWebRequest(url, method);
        req.downloadHandler = new DownloadHandlerBuffer();

        if (!string.IsNullOrEmpty(jsonBody))
        {
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.SetRequestHeader("Content-Type", "application/json");
        }

        req.SetRequestHeader("Authorization", $"Bearer {AuthToken}");
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
