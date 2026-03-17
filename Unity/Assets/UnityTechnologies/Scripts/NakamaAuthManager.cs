using UnityEngine;
using Nakama;
using System.Threading.Tasks;

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
        Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);

        string deviceId = SystemInfo.deviceUniqueIdentifier;
        Session = await Client.AuthenticateDeviceAsync(deviceId, null, true);

        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();

        Socket = Client.NewSocket();
        await Socket.ConnectAsync(Session);
    }
}