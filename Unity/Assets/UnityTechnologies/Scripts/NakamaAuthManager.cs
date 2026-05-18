using UnityEngine;
using Nakama;
using System.Threading.Tasks;
using System;

public class NakamaAuthManager : MonoBehaviour
{
    private const string WebGlDeviceIdKey = "animequest_webgl_device_id";
    private const int SocketConnectTimeoutMilliseconds = 5000;

    public static NakamaAuthManager Instance;

    public IClient Client { get; private set; }
    public ISession Session { get; private set; }
    public ISocket Socket { get; private set; }
    public bool IsAuthenticated => Session != null && !Session.IsExpired;
    public bool IsIncognitoSession { get; private set; }
    public bool IsConnectionReady { get; private set; }

    [SerializeField] private string scheme = "http";
    [SerializeField] private string host = "localhost";
    [SerializeField] private int port = 7350;
    [SerializeField] private string serverKey = "defaultkey";
    [SerializeField] private bool autoResolveLocalhost = true;
    [SerializeField] private string androidDeviceHostOverride = "";
    [SerializeField] private string androidPublicScheme = "https";
    [SerializeField] private string androidPublicHost = "";
    [SerializeField] private int androidPublicPort = 443;

    private Task<bool> _socketConnectTask;
    private TaskCompletionSource<bool> _socketConnectCompletion;
    private ISocket _pendingSocket;
    private int _socketConnectAttemptId;
    private float _socketConnectDeadlineAt;
    private string _socketConnectSource;
    private int _socketConnectTimeoutMs;
    private Task _incognitoLoginTask;
    private int _authAttemptId;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (autoResolveLocalhost)
        {
            ResolveEndpointForRuntime();
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        CheckSocketConnectDeadline();
    }

    public async Task LoginDeviceAsync()
    {
        EnsureClient();
        int authAttemptId = BeginNewAuthSession();

        try
        {
            string deviceId = ResolveDeviceId();
            var session = await Client.AuthenticateDeviceAsync(deviceId, null, true);
            if (!IsCurrentAuthAttempt(authAttemptId)) return;

            Session = session;
            IsIncognitoSession = true;
            PersistSession();
            StartSocketConnectInBackground("device");
            DozzleLogger.Action("Nakama device session authenticated", "socket=connecting");
        }
        catch
        {
            if (IsCurrentAuthAttempt(authAttemptId))
            {
                ClearSessionState();
            }
            throw;
        }
    }

    public async Task EnsureIncognitoSessionAsync()
    {
        if (IsAuthenticated)
        {
            return;
        }

        if (_incognitoLoginTask != null && !_incognitoLoginTask.IsCompleted)
        {
            await _incognitoLoginTask;
            return;
        }

        _incognitoLoginTask = LoginDeviceAsync();
        try
        {
            await _incognitoLoginTask;
        }
        finally
        {
            if (_incognitoLoginTask != null && _incognitoLoginTask.IsCompleted)
            {
                _incognitoLoginTask = null;
            }
        }
    }

    public async Task RegisterAsync(string username, string password)
    {
        EnsureClient();
        int authAttemptId = BeginNewAuthSession();

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

        try
        {
            var session = await Client.AuthenticateEmailAsync(email, password, username, create: true);
            if (!IsCurrentAuthAttempt(authAttemptId)) return;

            Session = session;
            IsIncognitoSession = false;
            PersistSession();
            StartSocketConnectInBackground("register");
            DozzleLogger.Action("Nakama register session authenticated", $"username={username};socket=connecting");
        }
        catch
        {
            if (IsCurrentAuthAttempt(authAttemptId))
            {
                ClearSessionState();
            }
            throw;
        }
    }


    public async Task LogoutAsync()
    {
        _authAttemptId++;
        _incognitoLoginTask = null;
        AbortPendingSocketConnect("logout", log: false);
        if (Socket != null)
        {
            await Socket.CloseAsync();
            Socket = null;
        }

        ClearSessionState();
    }

    public async Task LoginAsync(string username, string password)
    {
        EnsureClient();
        int authAttemptId = BeginNewAuthSession();

        string email = ToPseudoEmail(username);

        try
        {
            var session = await Client.AuthenticateEmailAsync(email, password, username, create: false);
            if (!IsCurrentAuthAttempt(authAttemptId)) return;

            Session = session;
            IsIncognitoSession = false;
            PersistSession();
            StartSocketConnectInBackground("login");
            DozzleLogger.Action("Nakama login session authenticated", $"username={username};socket=connecting");
        }
        catch
        {
            if (IsCurrentAuthAttempt(authAttemptId))
            {
                ClearSessionState();
            }
            throw;
        }
    }

    public async Task<bool> EnsureSocketConnectedAsync(int timeoutMs = SocketConnectTimeoutMilliseconds)
    {
        if (Session == null || Session.IsExpired)
        {
            return false;
        }

        EnsureClient();

        if (Socket != null && IsConnectionReady)
        {
            return true;
        }

        if (_socketConnectTask != null && !_socketConnectTask.IsCompleted)
        {
            return await _socketConnectTask;
        }

        int attemptId = ++_socketConnectAttemptId;
        _socketConnectTask = StartSocketConnectAttempt(timeoutMs, "ensure", attemptId);
        return await _socketConnectTask;
    }

    public void MarkSocketDisconnected(string source)
    {
        if (Socket != null)
        {
            TryCloseSocketInBackground(Socket);
        }

        Socket = null;
        _pendingSocket = null;
        _socketConnectTask = null;
        IsConnectionReady = false;
        ClearSocketConnectDeadline();

        string reason = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
        DozzleLogger.Error("Nakama socket marked disconnected", $"source={reason};endpoint={scheme}://{host}:{port}");
    }

    private void StartSocketConnectInBackground(string source)
    {
        if (_socketConnectTask != null && !_socketConnectTask.IsCompleted)
        {
            DozzleLogger.Action("Nakama socket connect already pending", $"source={source};endpoint={scheme}://{host}:{port}");
            return;
        }

        int attemptId = ++_socketConnectAttemptId;
        _socketConnectTask = StartSocketConnectAttempt(SocketConnectTimeoutMilliseconds, source, attemptId);
        _ = ObserveSocketConnectAsync(_socketConnectTask);
    }

    private static async Task ObserveSocketConnectAsync(Task<bool> connectTask)
    {
        try
        {
            await connectTask;
        }
        catch
        {
            // The connect task logs its own failure; this keeps fire-and-forget safe.
        }
    }

    private Task<bool> StartSocketConnectAttempt(int timeoutMs, string source, int attemptId)
    {
        var sessionAtStart = Session;
        if (sessionAtStart == null || sessionAtStart.IsExpired || Client == null)
        {
            return Task.FromResult(false);
        }

        IsConnectionReady = false;
        _socketConnectCompletion = new TaskCompletionSource<bool>();
        _socketConnectSource = source;
        _socketConnectTimeoutMs = Math.Max(1, timeoutMs);
        _socketConnectDeadlineAt = Time.realtimeSinceStartup + (_socketConnectTimeoutMs / 1000f);

        DozzleLogger.Action("Nakama socket connect started", $"source={source};endpoint={scheme}://{host}:{port};attempt={attemptId};timeoutMs={_socketConnectTimeoutMs}");
        BeginSocketConnectAsync(sessionAtStart, source, attemptId, _socketConnectCompletion);
        return _socketConnectCompletion.Task;
    }

    private async void BeginSocketConnectAsync(ISession sessionAtStart, string source, int attemptId, TaskCompletionSource<bool> completion)
    {
        ISocket nextSocket = null;
        try
        {
            if (Socket != null)
            {
                TryCloseSocketInBackground(Socket);
                Socket = null;
            }

            nextSocket = Client.NewSocket();
            _pendingSocket = nextSocket;
            await nextSocket.ConnectAsync(sessionAtStart);

#if UNITY_WEBGL && !UNITY_EDITOR
            await Task.Yield();
#endif

            if (!IsCurrentSocketAttempt(attemptId, completion) || !ReferenceEquals(Session, sessionAtStart))
            {
                TryCloseSocketInBackground(nextSocket);
                return;
            }

            _pendingSocket = null;
            Socket = nextSocket;
            IsConnectionReady = true;
            ClearSocketConnectDeadline();
            completion.TrySetResult(true);
            DozzleLogger.Action("Nakama socket connected", $"source={source};endpoint={scheme}://{host}:{port};attempt={attemptId}");
        }
        catch (Exception ex)
        {
            if (nextSocket != null)
            {
                TryCloseSocketInBackground(nextSocket);
            }

            if (IsCurrentSocketAttempt(attemptId, completion))
            {
                _pendingSocket = null;
                Socket = null;
                IsConnectionReady = false;
                _socketConnectTask = null;
                ClearSocketConnectDeadline();
                completion.TrySetResult(false);
            }

            DozzleLogger.Error("Nakama socket connect failed", ex);
        }
    }

    private void CheckSocketConnectDeadline()
    {
        if (_socketConnectCompletion == null || _socketConnectCompletion.Task.IsCompleted || _socketConnectDeadlineAt <= 0f)
        {
            return;
        }

        if (Time.realtimeSinceStartup < _socketConnectDeadlineAt)
        {
            return;
        }

        string source = string.IsNullOrWhiteSpace(_socketConnectSource) ? "unknown" : _socketConnectSource;
        int timeoutMs = _socketConnectTimeoutMs > 0 ? _socketConnectTimeoutMs : SocketConnectTimeoutMilliseconds;
        DozzleLogger.Error("Nakama socket connect watchdog timed out", $"source={source};endpoint={scheme}://{host}:{port};attempt={_socketConnectAttemptId};timeoutMs={timeoutMs}");
        AbortPendingSocketConnect("watchdog", log: false);
    }

    private bool IsCurrentSocketAttempt(int attemptId, TaskCompletionSource<bool> completion)
    {
        return attemptId == _socketConnectAttemptId && ReferenceEquals(_socketConnectCompletion, completion);
    }

    private int BeginNewAuthSession()
    {
        int authAttemptId = ++_authAttemptId;
        _incognitoLoginTask = null;
        AbortPendingSocketConnect("new-auth-session", log: false);
        IsConnectionReady = false;

        if (Socket != null)
        {
            TryCloseSocketInBackground(Socket);
            Socket = null;
        }

        return authAttemptId;
    }

    private bool IsCurrentAuthAttempt(int authAttemptId)
    {
        return authAttemptId == _authAttemptId;
    }

    private void AbortPendingSocketConnect(string source, bool log)
    {
        _socketConnectAttemptId++;

        var completion = _socketConnectCompletion;
        var pendingSocket = _pendingSocket;

        _socketConnectCompletion = null;
        _pendingSocket = null;
        _socketConnectTask = null;
        ClearSocketConnectDeadline();
        IsConnectionReady = false;

        if (pendingSocket != null)
        {
            TryCloseSocketInBackground(pendingSocket);
        }

        completion?.TrySetResult(false);

        if (log)
        {
            DozzleLogger.Error("Nakama socket connect abandoned", $"source={source};endpoint={scheme}://{host}:{port}");
        }
    }

    private void ClearSocketConnectDeadline()
    {
        _socketConnectDeadlineAt = 0f;
        _socketConnectSource = null;
        _socketConnectTimeoutMs = 0;
    }

    private static async void TryCloseSocketInBackground(ISocket socket)
    {
        if (socket == null)
        {
            return;
        }

        try
        {
            await socket.CloseAsync();
        }
        catch
        {
            // Best-effort cleanup for sockets that lost the connect race.
        }
    }

    private void EnsureClient()
    {
        if (Client == null)
        {
            Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);
            DozzleLogger.Action("Nakama endpoint resolved", $"{scheme}://{host}:{port}");
        }
    }

    private void PersistSession()
    {
        if (Session == null) return;
        PlayerPrefs.SetString("nakama_auth_token", Session.AuthToken);
        PlayerPrefs.SetString("nakama_refresh_token", Session.RefreshToken ?? "");
        PlayerPrefs.Save();
    }

    private void ClearSessionState()
    {
        AbortPendingSocketConnect("clear-session", log: false);
        Session = null;
        Socket = null;
        _socketConnectTask = null;
        IsIncognitoSession = false;
        IsConnectionReady = false;
        PlayerPrefs.DeleteKey("nakama_auth_token");
        PlayerPrefs.DeleteKey("nakama_refresh_token");
        PlayerPrefs.Save();
    }

    private static string ResolveDeviceId()
    {
        string deviceId = SystemInfo.deviceUniqueIdentifier;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!IsValidNakamaDeviceId(deviceId))
        {
            deviceId = PlayerPrefs.GetString(WebGlDeviceIdKey, string.Empty);
            if (!IsValidNakamaDeviceId(deviceId))
            {
                deviceId = "webgl-" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(WebGlDeviceIdKey, deviceId);
                PlayerPrefs.Save();
            }
        }
#endif

        if (!IsValidNakamaDeviceId(deviceId))
        {
            deviceId = "device-" + Guid.NewGuid().ToString("N");
        }

        deviceId = deviceId.Trim();
        return deviceId.Length <= 128 ? deviceId : deviceId.Substring(0, 128);
    }

    private static bool IsValidNakamaDeviceId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 10 && value.Trim().Length <= 128;
    }

    private static string ToPseudoEmail(string username)
    {
        string safe = username.Trim().ToLowerInvariant().Replace(" ", "_");
        if (string.IsNullOrEmpty(safe)) throw new Exception("Username cannot be empty.");
        return $"{safe}@animequest.local";
    }

    private void ResolveEndpointForRuntime()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (TryApplyAndroidPublicEndpoint())
        {
            return;
        }
#endif

        if (!IsLocalhost(host)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (TryResolveHostedServiceHost("nakama", out string hostedHost))
        {
            scheme = "https";
            host = hostedHost;
            port = 443;
            return;
        }
#endif

        host = ResolveHostForRuntime(host, androidDeviceHostOverride);
    }

    private bool TryApplyAndroidPublicEndpoint()
    {
        if (string.IsNullOrWhiteSpace(androidPublicHost)) return false;

        scheme = string.IsNullOrWhiteSpace(androidPublicScheme) ? "https" : androidPublicScheme.Trim();
        host = androidPublicHost.Trim();
        port = androidPublicPort > 0 ? androidPublicPort : 443;
        return true;
    }

    private static bool IsLocalhost(string configuredHost)
    {
        return string.Equals(configuredHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuredHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(configuredHost, "::1", StringComparison.OrdinalIgnoreCase);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static bool TryResolveHostedServiceHost(string serviceSubdomain, out string serviceHost)
    {
        serviceHost = null;
        if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var pageUri)) return false;
        if (IsLocalhost(pageUri.Host)) return false;

        string hostName = pageUri.Host;
        if (hostName.StartsWith("play.", StringComparison.OrdinalIgnoreCase))
        {
            serviceHost = serviceSubdomain + "." + hostName.Substring("play.".Length);
        }
        else if (hostName.StartsWith(serviceSubdomain + ".", StringComparison.OrdinalIgnoreCase))
        {
            serviceHost = hostName;
        }
        else
        {
            serviceHost = serviceSubdomain + "." + hostName;
        }

        return true;
    }
#endif

    private static string ResolveHostForRuntime(string configuredHost, string androidHostOverride)
    {
        if (!string.Equals(configuredHost, "localhost", StringComparison.OrdinalIgnoreCase)) return configuredHost;

#if UNITY_ANDROID && !UNITY_EDITOR
        string runtimeHost = string.IsNullOrWhiteSpace(androidHostOverride) ? null : androidHostOverride.Trim();
        return string.IsNullOrWhiteSpace(runtimeHost) ? "10.0.2.2" : runtimeHost;
#else
        return "127.0.0.1";
#endif
    }
}
