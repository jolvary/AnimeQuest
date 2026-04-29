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

    [SerializeField] private string scheme = "http";
    [SerializeField] private string host = "localhost";
    [SerializeField] private int port = 7350;
    [SerializeField] private string serverKey = "defaultkey";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    public async Task LoginDeviceAsync()
    {
        EnsureClient();

        string deviceId = SystemInfo.deviceUniqueIdentifier;
        Session = await Client.AuthenticateDeviceAsync(deviceId, null, true);

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
        Session = await Client.AuthenticateEmailAsync(email, password, username, create: true);

        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();

        Socket = Client.NewSocket();
        await Socket.ConnectAsync(Session);
    }

    public async Task LoginAsync(string username, string password)
    {
        EnsureClient();

        string email = ToPseudoEmail(username);
        Session = await Client.AuthenticateEmailAsync(email, password, username, create: false);

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
}
