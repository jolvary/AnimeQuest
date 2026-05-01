using UnityEngine;
using Nakama;
using System.Threading.Tasks;
using System;

public class NakamaAuthManager : MonoBehaviour
{
    public static NakamaAuthManager Instance;

    public IClient Client { get; private set; }
    public ISession Session { get; private set; }
    public ISocket Socket { get; private set; }
    public bool IsAuthenticated => Session != null && !Session.IsExpired;
    public bool IsIncognitoSession { get; private set; }

    [SerializeField] private string scheme = "http";
    [SerializeField] private string host = "localhost";
    [SerializeField] private int port = 7350;
    [SerializeField] private string serverKey = "defaultkey";
    [SerializeField] private bool autoResolveLocalhost = true;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (autoResolveLocalhost)
        {
            host = ResolveHostForRuntime(host);
        }

        DontDestroyOnLoad(gameObject);
    }

    public async Task LoginDeviceAsync()
    {
        EnsureClient();

        string deviceId = SystemInfo.deviceUniqueIdentifier;
        Session = await Client.AuthenticateDeviceAsync(deviceId, null, true);
        IsIncognitoSession = true;

        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();

        Socket = Client.NewSocket();
        await Socket.ConnectAsync(Session);
    }

    public async Task RegisterAsync(string username, string password)
    {
        EnsureClient();

        string email = ToPseudoEmail(username);

        try
        {
            await Client.AuthenticateEmailAsync(email, password, username, create: false);
            throw new Exception("Username already exists.");
        }
        catch (ApiResponseException ex) when (ex.StatusCode == 404)
        {
            // Account does not exist yet; continue with registration.
        }

        Session = await Client.AuthenticateEmailAsync(email, password, username, create: true);
        IsIncognitoSession = false;

        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();

        Socket = Client.NewSocket();
        await Socket.ConnectAsync(Session);
    }


    public async Task LogoutAsync()
    {
        if (Socket != null)
        {
            await Socket.CloseAsync();
            Socket = null;
        }

        Session = null;
        IsIncognitoSession = false;
        PlayerPrefs.DeleteKey("nakama_auth_token");
        PlayerPrefs.DeleteKey("nakama_refresh_token");
        PlayerPrefs.Save();
    }

    public async Task LoginAsync(string username, string password)
    {
        EnsureClient();

        string email = ToPseudoEmail(username);
        Session = await Client.AuthenticateEmailAsync(email, password, username, create: false);
        IsIncognitoSession = false;

        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();

        Socket = Client.NewSocket();
        await Socket.ConnectAsync(Session);
    }

    private void EnsureClient()
    {
        if (Client == null)
        {
            Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);
        }
    }

    private static string ToPseudoEmail(string username)
    {
        string safe = username.Trim().ToLowerInvariant().Replace(" ", "_");
        if (string.IsNullOrEmpty(safe)) throw new Exception("Username cannot be empty.");
        return $"{safe}@animequest.local";
    }

    private static string ResolveHostForRuntime(string configuredHost)
    {
        if (!string.Equals(configuredHost, "localhost", StringComparison.OrdinalIgnoreCase)) return configuredHost;

#if UNITY_ANDROID && !UNITY_EDITOR
        return "10.0.2.2";
#else
        return "127.0.0.1";
#endif
    }
}
