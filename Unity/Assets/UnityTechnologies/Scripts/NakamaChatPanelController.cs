using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class NakamaChatPanelController : MonoBehaviour
{
    private const int MaxChatMessageLength = 900;
    private const int MaxChatPayloadBytes = 3500;
    private const int ChatOperationTimeoutMilliseconds = 5000;
    private const float NotificationRefreshIntervalSeconds = 12f;
    private static readonly Color PanelRowSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.24f);
    private static readonly Color ComposerSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.36f);

    public Font preferredFont;
    public string defaultRoomName = "animequest-lobby";

    public static event Action<string> ChatMessageReceived;

    private Text _titleText;
    private Text _statusText;
    private ScrollRect _scrollRect;
    private RectTransform _content;
    private InputField _messageInput;
    private Button _sendButton;
    private Text _sendButtonLabel;
    private IChannel _channel;
    private string _activeChannelId;
    private string _activeChannelKey;
    private bool _subscribedToSocket;
    private ISocket _subscribedSocket;
    private bool _isJoiningChannel;
    private int _joinGeneration;
    private float _nextNotificationRefreshAt;
    private bool _isRefreshingNotificationSubscriptions;
    private string _notificationUserId;
    private readonly HashSet<string> _joinedNotificationChannelKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _renderedMessageIds = new List<string>();
    private readonly List<ChatDisplayMessage> _pendingMessages = new List<ChatDisplayMessage>();
    private readonly object _pendingMessagesLock = new object();

    public string CurrentChannelKey => _activeChannelKey;
    public string GeneralChannelKey => BuildRoomChannelKey(defaultRoomName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapNotificationDriver()
    {
        if (FindFirstObjectByType<NakamaChatNotificationDriver>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        var obj = new GameObject("NakamaChatNotificationDriver");
        DontDestroyOnLoad(obj);
        obj.AddComponent<NakamaChatNotificationDriver>();
    }

    public static string BuildRoomChannelKey(string roomName)
    {
        return $"room:{NormalizeChannelName(roomName, "animequest-lobby")}";
    }

    public static string BuildDirectChannelKey(string userId)
    {
        return $"dm:{NormalizeChannelName(userId, "unknown")}";
    }

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    internal void TickBackgroundNotifications()
    {
        if (Time.unscaledTime < _nextNotificationRefreshAt)
        {
            return;
        }

        _nextNotificationRefreshAt = Time.unscaledTime + NotificationRefreshIntervalSeconds;

        if (!TryGetSessionAuth(out var auth) || auth.IsIncognitoSession)
        {
            ClearNotificationJoins();
            return;
        }

        string userId = auth.Session.UserId;
        if (!string.Equals(_notificationUserId, userId, StringComparison.Ordinal) || !ReferenceEquals(_subscribedSocket, auth.Socket))
        {
            _notificationUserId = userId;
            _joinedNotificationChannelKeys.Clear();
        }

        if (_isRefreshingNotificationSubscriptions)
        {
            return;
        }

        RefreshNotificationSubscriptions(auth);
    }

    private void OnEnable()
    {
        EnsureElements();
        SubscribeToSocket();
        _nextNotificationRefreshAt = 0f;
        TickBackgroundNotifications();

        if (_channel == null)
        {
            OpenGeneralChat();
        }
        else
        {
            RefreshSendState();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromSocket();
    }

    private void Update()
    {
        FlushPendingMessages();
        TickBackgroundNotifications();
    }

    public async void OpenGeneralChat()
    {
        string channelKey = GeneralChannelKey;
        if (_channel != null && string.Equals(_activeChannelKey, channelKey, StringComparison.Ordinal))
        {
            RefreshSendState();
            return;
        }

        await JoinChannel(defaultRoomName, ChannelType.Room, "General Chat", channelKey, hidden: false);
    }

    public void ConnectGlobalRoomInBackground()
    {
        _nextNotificationRefreshAt = 0f;
        TickBackgroundNotifications();
    }

    public async void OpenDirectMessage(string userId, string username)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            SetStatus("Choose a friend before opening direct chat.");
            return;
        }

        string channelKey = BuildDirectChannelKey(userId);
        string label = string.IsNullOrWhiteSpace(username) ? "Direct Chat" : $"Chat with {username}";

        if (_channel != null && string.Equals(_activeChannelKey, channelKey, StringComparison.Ordinal))
        {
            SetTitle(label);
            RefreshSendState();
            return;
        }

        await JoinChannel(userId, ChannelType.DirectMessage, label, channelKey, hidden: true);
    }

    private async void RefreshNotificationSubscriptions(NakamaAuthManager auth)
    {
        _isRefreshingNotificationSubscriptions = true;
        try
        {
            if (auth == null || auth.IsIncognitoSession || auth.Session == null)
            {
                ClearNotificationJoins();
                return;
            }

            if (auth.Socket == null || !auth.IsConnectionReady)
            {
                bool connected = await auth.EnsureSocketConnectedAsync(ChatOperationTimeoutMilliseconds);
                if (!connected)
                {
                    DozzleLogger.Error("Chat notification socket unavailable", "Background chat notification channels will retry.");
                    return;
                }
            }

            SubscribeToSocket();
            await JoinNotificationChannel(defaultRoomName, ChannelType.Room, GeneralChannelKey, hidden: false);

            var friendsTask = auth.Client.ListFriendsAsync(auth.Session, null, 100, null);
            var completed = await Task.WhenAny(friendsTask, Task.Delay(ChatOperationTimeoutMilliseconds));
            if (completed != friendsTask)
            {
                ObserveBackgroundTask(friendsTask);
                DozzleLogger.Error("Chat notification friends load timed out", $"timeoutMs={ChatOperationTimeoutMilliseconds}");
                return;
            }

            var friends = await friendsTask;
            int acceptedFriends = 0;
            int joinedChannels = _joinedNotificationChannelKeys.Count;

            if (friends?.Friends != null)
            {
                foreach (var friend in friends.Friends)
                {
                    if (friend?.User == null || string.IsNullOrWhiteSpace(friend.User.Id) || !IsAcceptedFriend(friend))
                    {
                        continue;
                    }

                    acceptedFriends++;
                    await JoinNotificationChannel(friend.User.Id, ChannelType.DirectMessage, BuildDirectChannelKey(friend.User.Id), hidden: true);
                }
            }

            DozzleLogger.Action("Chat notification refresh completed", $"friends={acceptedFriends};joined={_joinedNotificationChannelKeys.Count};new={Mathf.Max(0, _joinedNotificationChannelKeys.Count - joinedChannels)}");
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Chat notification refresh failed", ex);
        }
        finally
        {
            _isRefreshingNotificationSubscriptions = false;
        }
    }

    private async Task JoinNotificationChannel(string target, ChannelType type, string channelKey, bool hidden)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(channelKey))
        {
            return;
        }

        if (_joinedNotificationChannelKeys.Contains(channelKey))
        {
            return;
        }

        if (!TryGetAuth(out var auth))
        {
            return;
        }

        var joinTask = auth.Socket.JoinChatAsync(target, type, persistence: true, hidden: hidden);
        var completed = await Task.WhenAny(joinTask, Task.Delay(ChatOperationTimeoutMilliseconds));
        if (completed != joinTask)
        {
            ObserveBackgroundTask(joinTask);
            DozzleLogger.Error("Chat notification channel join timed out", $"target={target};key={channelKey};type={type};timeoutMs={ChatOperationTimeoutMilliseconds}");
            return;
        }

        var channel = await joinTask;
        _joinedNotificationChannelKeys.Add(channelKey);
        DozzleLogger.Action("Chat notification channel joined", $"channel={ShortChannel(channel?.Id)};key={channelKey};type={type};target={target}");
    }

    private static bool IsAcceptedFriend(IApiFriend friend)
    {
        if (friend == null) return false;

        try
        {
            return Convert.ToInt32(friend.State) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void ClearNotificationJoins()
    {
        _notificationUserId = null;
        _joinedNotificationChannelKeys.Clear();
    }

    private async Task JoinChannel(string target, ChannelType type, string label, string channelKey, bool hidden)
    {
        EnsureElements();

        int generation = ++_joinGeneration;
        _isJoiningChannel = true;

        try
        {
            SetTitle(label);

            if (!TryGetAuth(out var auth))
            {
                bool hasSession = TryGetSessionAuth(out var sessionAuth);
                if (!hasSession)
                {
                    if (generation != _joinGeneration) return;
                    _channel = null;
                    _activeChannelId = null;
                    _activeChannelKey = null;
                    SetTitle("Chat");
                    SetStatus("Log in to use chat.");
                    RefreshSendState();
                    return;
                }

                SetStatus("Connecting to chat...");
                RefreshSendState();
                bool connected = await sessionAuth.EnsureSocketConnectedAsync(ChatOperationTimeoutMilliseconds);
                if (generation != _joinGeneration) return;

                if (connected)
                {
                    SubscribeToSocket();
                }
            }

            if (!TryGetAuth(out auth))
            {
                if (generation != _joinGeneration) return;
                _channel = null;
                _activeChannelId = null;
                _activeChannelKey = null;
                SetStatus("Chat connection unavailable. Try again in a moment.");
                RefreshSendState();
                return;
            }

            SubscribeToSocket();
            SetStatus("Connecting to chat...");
            RefreshSendState();

            var joinTask = auth.Socket.JoinChatAsync(target, type, persistence: true, hidden: hidden);
            var completed = await Task.WhenAny(joinTask, Task.Delay(ChatOperationTimeoutMilliseconds));
            if (generation != _joinGeneration)
            {
                ObserveBackgroundTask(joinTask);
                return;
            }

            if (completed != joinTask)
            {
                ObserveBackgroundTask(joinTask);
                SetStatus("Chat connection timed out. Try again in a moment.");
                RefreshSendState();
                DozzleLogger.Error("Chat channel join timed out", $"target={target};type={type};timeoutMs={ChatOperationTimeoutMilliseconds}");
                return;
            }

            _channel = await joinTask;
            if (generation != _joinGeneration) return;

            _activeChannelId = _channel.Id;
            _activeChannelKey = channelKey;
            _joinedNotificationChannelKeys.Add(channelKey);
            SetStatus("Chat ready.");
            DozzleLogger.Action("Chat channel joined", $"channel={_channel.Id};key={channelKey};type={type};target={target}");
            await LoadHistory(generation);
            RefreshSendState();
        }
        catch (Exception ex)
        {
            if (generation != _joinGeneration) return;
            _channel = null;
            _activeChannelId = null;
            _activeChannelKey = null;
            SetStatus("Chat unavailable.");
            RefreshSendState();
            DozzleLogger.Error("Chat channel join failed", ex);
        }
        finally
        {
            if (generation == _joinGeneration)
            {
                _isJoiningChannel = false;
                RefreshSendState();
            }
        }
    }

    private async Task LoadHistory(int generation)
    {
        if (generation != _joinGeneration) return;

        ClearRows();
        _renderedMessageIds.Clear();

        if (_channel == null || !TryGetAuth(out var auth))
        {
            if (generation != _joinGeneration) return;
            SetStatus("Chat unavailable.");
            return;
        }

        string channelId = _channel.Id;
        try
        {
            var historyTask = auth.Client.ListChannelMessagesAsync(auth.Session, channelId, 50, true, null);
            var completed = await Task.WhenAny(historyTask, Task.Delay(ChatOperationTimeoutMilliseconds));
            if (generation != _joinGeneration || !string.Equals(channelId, _activeChannelId, StringComparison.Ordinal)) return;

            if (completed != historyTask)
            {
                ObserveBackgroundTask(historyTask);
                SetStatus("Chat ready. Previous messages unavailable.");
                DozzleLogger.Error("Chat history load timed out", $"channel={channelId};timeoutMs={ChatOperationTimeoutMilliseconds}");
                return;
            }

            var history = await historyTask;
            if (generation != _joinGeneration || !string.Equals(channelId, _activeChannelId, StringComparison.Ordinal)) return;

            if (history?.Messages != null)
            {
                foreach (var message in history.Messages)
                {
                    RenderMessage(ToDisplayMessage(message));
                }
            }

            SetStatus("Chat ready.");
            ResetScrollToBottom();
        }
        catch (Exception ex)
        {
            if (generation != _joinGeneration || !string.Equals(channelId, _activeChannelId, StringComparison.Ordinal)) return;
            SetStatus("Chat ready. Could not load previous messages.");
            DozzleLogger.Error("Chat history load failed", ex);
        }
    }

    private async void OnSendPressed()
    {
        await SendCurrentMessage();
    }

    private async Task SendCurrentMessage()
    {
        EnsureElements();

        if (_channel == null)
        {
            SetStatus("Open a chat first.");
            return;
        }

        if (!TryGetAuth(out var auth))
        {
            bool hasSession = TryGetSessionAuth(out var sessionAuth);
            if (hasSession)
            {
                SetStatus("Connecting to chat...");
                RefreshSendState();
                bool connected = await sessionAuth.EnsureSocketConnectedAsync(ChatOperationTimeoutMilliseconds);
                if (connected)
                {
                    SubscribeToSocket();
                }
            }

            if (!TryGetAuth(out auth))
            {
                SetStatus(hasSession ? "Chat connection unavailable. Try again in a moment." : "Log in to send chat messages.");
                RefreshSendState();
                return;
            }
        }

        if (auth.IsIncognitoSession)
        {
            SetStatus("Log in to send chat messages.");
            RefreshSendState();
            return;
        }

        string message = _messageInput != null ? _messageInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(message)) return;

        if (message.Length > MaxChatMessageLength)
        {
            SetStatus($"Messages are limited to {MaxChatMessageLength} characters.");
            return;
        }

        string payload = JsonUtility.ToJson(new ChatMessagePayload { content = message });
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > MaxChatPayloadBytes)
        {
            SetStatus("Message is too long to send safely.");
            return;
        }

        try
        {
            SetSendInteractable(false);
            var ack = await auth.Socket.WriteChatMessageAsync(_channel.Id, payload);
            _messageInput.text = string.Empty;
            RenderMessage(new ChatDisplayMessage
            {
                channelId = _activeChannelId,
                messageId = ack?.MessageId,
                username = !string.IsNullOrWhiteSpace(auth.Session.Username) ? auth.Session.Username : "You",
                content = payload,
            });
            SetStatus("Chat ready.");
            ResetScrollToBottom();
            DozzleLogger.Action("Chat message sent", $"channel={_channel.Id};key={NormalizeChannelName(_activeChannelKey, "-")}");
        }
        catch (Exception ex)
        {
            SetStatus("Message failed.");
            DozzleLogger.Error("Chat message send failed", ex);
        }
        finally
        {
            RefreshSendState();
        }
    }

    private void SubscribeToSocket()
    {
        if (NakamaAuthManager.Instance == null || NakamaAuthManager.Instance.Socket == null) return;

        var socket = NakamaAuthManager.Instance.Socket;
        if (_subscribedToSocket && ReferenceEquals(_subscribedSocket, socket)) return;

        UnsubscribeFromSocket();
        socket.ReceivedChannelMessage += OnReceivedChannelMessage;
        _subscribedSocket = socket;
        _subscribedToSocket = true;
        DozzleLogger.Action("Chat socket subscribed", $"socket=yes;active={ShortChannel(_activeChannelId)};key={NormalizeChannelName(_activeChannelKey, "-")}");
    }

    private void UnsubscribeFromSocket()
    {
        if (!_subscribedToSocket || _subscribedSocket == null)
        {
            _subscribedToSocket = false;
            _subscribedSocket = null;
            return;
        }

        _subscribedSocket.ReceivedChannelMessage -= OnReceivedChannelMessage;
        _subscribedSocket = null;
        _subscribedToSocket = false;
    }

    private void OnReceivedChannelMessage(IApiChannelMessage message)
    {
        if (message == null)
        {
            DozzleLogger.Action("Chat message received", "message=null");
            return;
        }

        if (IsWorldStateMessage(message.Content))
        {
            return;
        }

        bool isActiveChannel = !string.IsNullOrWhiteSpace(_activeChannelId) && string.Equals(message.ChannelId, _activeChannelId, StringComparison.Ordinal);
        bool fromCurrentUser = IsFromCurrentUser(message);
        string notificationKey = ResolveNotificationChannelKey(message);
        int subscriberCount = ChatMessageReceived?.GetInvocationList().Length ?? 0;

        DozzleLogger.Action(
            "Chat message received",
            $"channel={ShortChannel(message.ChannelId)};active={ShortChannel(_activeChannelId)};activeKey={NormalizeChannelName(_activeChannelKey, "-")};isActive={isActiveChannel};fromSelf={fromCurrentUser};notifyKey={notificationKey};subscribers={subscriberCount};message={ShortId(message.MessageId)};username={NormalizeChannelName(message.Username, "-")};contentBytes={(message.Content != null ? System.Text.Encoding.UTF8.GetByteCount(message.Content) : 0)}");

        if (!fromCurrentUser)
        {
            ChatMessageReceived?.Invoke(notificationKey);
            DozzleLogger.Action("Chat notification emitted", $"key={notificationKey};channel={ShortChannel(message.ChannelId)};active={isActiveChannel}");
        }

        if (!isActiveChannel)
        {
            DozzleLogger.Action("Chat message skipped for inactive channel", $"channel={ShortChannel(message.ChannelId)};active={ShortChannel(_activeChannelId)};key={notificationKey}");
            return;
        }

        EnqueueMessage(ToDisplayMessage(message));
    }

    private string ResolveNotificationChannelKey(IApiChannelMessage message)
    {
        if (message == null) return "chat";

        if (!string.IsNullOrWhiteSpace(_activeChannelId) && string.Equals(message.ChannelId, _activeChannelId, StringComparison.Ordinal))
        {
            return NormalizeChannelName(_activeChannelKey, message.ChannelId);
        }

        string peerId = ResolveDirectMessagePeerId(message.ChannelId);
        if (!string.IsNullOrWhiteSpace(peerId))
        {
            return BuildDirectChannelKey(peerId);
        }

        string roomName = ResolveRoomName(message.ChannelId);
        if (!string.IsNullOrWhiteSpace(roomName))
        {
            return BuildRoomChannelKey(roomName);
        }

        return BuildRoomChannelKey(message.ChannelId);
    }

    private static string ResolveDirectMessagePeerId(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || !TryGetSessionAuth(out var auth) || auth.Session == null) return null;

        string[] parts = channelId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "4", StringComparison.Ordinal)) return null;

        string currentUserId = auth.Session.UserId;
        if (string.Equals(parts[1], currentUserId, StringComparison.Ordinal)) return parts[2];
        if (string.Equals(parts[2], currentUserId, StringComparison.Ordinal)) return parts[1];
        return parts[1];
    }

    private static string ResolveRoomName(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return null;

        string[] parts = channelId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && string.Equals(parts[0], "1", StringComparison.Ordinal)) return parts[1];
        return null;
    }

    private static bool IsWorldStateMessage(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return false;
        if (rawContent.IndexOf("world_state", StringComparison.OrdinalIgnoreCase) < 0) return false;

        try
        {
            var probe = JsonUtility.FromJson<WorldStateProbe>(rawContent);
            return probe != null && string.Equals(probe.type, "world_state", StringComparison.Ordinal);
        }
        catch
        {
            return rawContent.IndexOf("\"type\":\"world_state\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private void EnqueueMessage(ChatDisplayMessage message)
    {
        lock (_pendingMessagesLock)
        {
            _pendingMessages.Add(message);
        }
    }

    private void FlushPendingMessages()
    {
        ChatDisplayMessage[] messages;
        lock (_pendingMessagesLock)
        {
            if (_pendingMessages.Count == 0) return;
            messages = _pendingMessages.ToArray();
            _pendingMessages.Clear();
        }

        EnsureElements();
        foreach (var message in messages)
        {
            RenderMessage(message);
        }
        ResetScrollToBottom();
    }

    private static async void ObserveBackgroundTask(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The foreground timeout path already logged enough context.
        }
    }

    private static ChatDisplayMessage ToDisplayMessage(IApiChannelMessage message)
    {
        return new ChatDisplayMessage
        {
            channelId = message.ChannelId,
            messageId = message.MessageId,
            username = message.Username,
            content = message.Content,
        };
    }

    private static bool IsFromCurrentUser(IApiChannelMessage message)
    {
        if (message == null || !TryGetSessionAuth(out var auth) || auth.Session == null) return false;

        string senderId = ReadStringProperty(message, "SenderId");
        if (!string.IsNullOrWhiteSpace(senderId) && !string.IsNullOrWhiteSpace(auth.Session.UserId))
        {
            return string.Equals(senderId, auth.Session.UserId, StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(message.Username) || string.IsNullOrWhiteSpace(auth.Session.Username)) return false;
        return string.Equals(message.Username, auth.Session.Username, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadStringProperty(object target, string propertyName)
    {
        if (target == null || string.IsNullOrWhiteSpace(propertyName)) return null;

        try
        {
            return target.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(target) as string;
        }
        catch
        {
            return null;
        }
    }

    private void RenderMessage(ChatDisplayMessage message)
    {
        if (message == null) return;

        if (!string.IsNullOrWhiteSpace(message.messageId))
        {
            if (_renderedMessageIds.Contains(message.messageId)) return;
            _renderedMessageIds.Add(message.messageId);
        }

        string username = string.IsNullOrWhiteSpace(message.username) ? "Player" : message.username;
        string text = ExtractMessageText(message.content);
        if (string.IsNullOrWhiteSpace(text)) text = message.content ?? string.Empty;

        CreateMessageRow(username, text);
    }

    private void EnsureElements()
    {
        if (_titleText == null)
        {
            _titleText = CreateText("ChatTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -100f), new Vector2(-48f, -48f), 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.text = "Chat";
        }

        if (_statusText == null)
        {
            _statusText = CreateText("ChatStatus", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -146f), new Vector2(-48f, -106f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            _statusText.text = "Log in to use chat.";
        }

        if (_scrollRect == null || _content == null)
        {
            CreateMessagesContainer();
        }

        if (_messageInput == null || _sendButton == null)
        {
            CreateComposer();
        }

        ApplyFonts();
        RefreshSendState();
    }

    private void CreateMessagesContainer()
    {
        var viewportObj = new GameObject("ChatViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 126f);
        viewportRect.offsetMax = new Vector2(-48f, -162f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        _scrollRect = viewportObj.GetComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 28f;
        _scrollRect.viewport = viewportRect;

        var contentObj = new GameObject("ChatContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _content = contentObj.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.offsetMin = Vector2.zero;
        _content.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 8f;
        layout.padding = new RectOffset(8, 8, 8, 8);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scrollRect.content = _content;
    }

    private void CreateComposer()
    {
        var inputObj = new GameObject("ChatMessageInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        inputObj.transform.SetParent(transform, false);
        var inputRect = inputObj.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 0f);
        inputRect.anchorMax = new Vector2(1f, 0f);
        inputRect.offsetMin = new Vector2(48f, 48f);
        inputRect.offsetMax = new Vector2(-220f, 108f);

        inputObj.GetComponent<Image>().color = ComposerSurfaceColor;

        _messageInput = inputObj.GetComponent<InputField>();
        _messageInput.lineType = InputField.LineType.SingleLine;
        _messageInput.contentType = InputField.ContentType.Standard;
        _messageInput.characterLimit = MaxChatMessageLength;
        _messageInput.onEndEdit.AddListener(value =>
        {
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                OnSendPressed();
            }
        });

        var inputText = CreateChildText(inputObj.transform, "Text", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.black);
        inputText.rectTransform.offsetMin = new Vector2(14f, 8f);
        inputText.rectTransform.offsetMax = new Vector2(-14f, -8f);
        var placeholder = CreateChildText(inputObj.transform, "Placeholder", "Message...", 18, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.45f, 0.45f, 0.45f, 1f));
        placeholder.rectTransform.offsetMin = new Vector2(14f, 8f);
        placeholder.rectTransform.offsetMax = new Vector2(-14f, -8f);

        _messageInput.textComponent = inputText;
        _messageInput.placeholder = placeholder;

        var buttonObj = new GameObject("SendButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObj.transform.SetParent(transform, false);
        var buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(1f, 0f);
        buttonRect.anchoredPosition = new Vector2(-48f, 48f);
        buttonRect.sizeDelta = new Vector2(154f, 60f);

        buttonObj.GetComponent<Image>().color = new Color(0.23f, 0.77f, 0.27f, 1f);
        _sendButton = buttonObj.GetComponent<Button>();
        _sendButton.onClick.AddListener(OnSendPressed);
        _sendButtonLabel = CreateChildText(buttonObj.transform, "Text", "Send", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black);
    }

    private void CreateMessageRow(string username, string message)
    {
        if (_content == null) return;

        var rowObj = new GameObject("ChatMessage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        rowObj.transform.SetParent(_content, false);
        rowObj.GetComponent<Image>().color = PanelRowSurfaceColor;

        int lineCount = Math.Max(1, Mathf.CeilToInt(message.Length / 88f));
        float bodyHeight = Mathf.Clamp(24f + lineCount * 18f, 42f, 220f);
        float rowHeight = 39f + bodyHeight;

        var layoutElement = rowObj.GetComponent<LayoutElement>();
        layoutElement.minHeight = rowHeight;
        layoutElement.preferredHeight = rowHeight;

        var layout = rowObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 7, 7);
        layout.spacing = 2f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        CreateRowLabel(rowObj.transform, username, 15, FontStyle.Bold);
        var messageLabel = CreateRowLabel(rowObj.transform, message, 16, FontStyle.Normal);
        var messageLayout = messageLabel.GetComponent<LayoutElement>();
        messageLayout.minHeight = bodyHeight;
        messageLayout.preferredHeight = bodyHeight;
    }

    private Text CreateRowLabel(Transform parent, string value, int fontSize, FontStyle style)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<LayoutElement>().minHeight = fontSize + 5f;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private Text CreateText(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, FontStyle style, TextAnchor alignment)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(transform, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        var text = obj.GetComponent<Text>();
        text.font = ResolveFont();
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = new Color(0.17f, 0.10f, 0.04f, 1f);
        return text;
    }

    private Text CreateChildText(Transform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = obj.GetComponent<Text>();
        text.text = value;
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        return text;
    }

    private void ClearRows()
    {
        if (_content == null) return;
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            Destroy(_content.GetChild(i).gameObject);
        }
    }

    private void ApplyFonts()
    {
        Font font = ResolveFont();
        if (font == null) return;
        foreach (var label in GetComponentsInChildren<Text>(true))
        {
            label.font = font;
        }
    }

    private Font ResolveFont()
    {
        if (preferredFont != null) return preferredFont;
        try
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch
        {
            return null;
        }
    }

    private void SetTitle(string value)
    {
        EnsureElements();
        if (_titleText != null) _titleText.text = value;
    }

    private void SetStatus(string value)
    {
        if (_statusText != null) _statusText.text = value;
    }

    private void RefreshSendState()
    {
        bool hasAuth = TryGetAuth(out var auth);
        bool isIncognito = hasAuth && auth.IsIncognitoSession;
        bool canSend = !_isJoiningChannel && _channel != null && hasAuth && !isIncognito;
        SetSendInteractable(canSend);

        if (_channel != null && isIncognito)
        {
            SetStatus("Log in to send chat messages.");
        }
    }

    private void SetSendInteractable(bool interactable)
    {
        if (_sendButton != null) _sendButton.interactable = interactable;
        if (_sendButtonLabel != null) _sendButtonLabel.color = interactable ? Color.black : new Color(0.35f, 0.35f, 0.35f, 1f);
        if (_messageInput != null) _messageInput.interactable = interactable;
    }

    private void ResetScrollToBottom()
    {
        if (_scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 0f;
    }

    private static bool TryGetAuth(out NakamaAuthManager auth)
    {
        return TryGetSessionAuth(out auth) && auth.Socket != null && auth.IsConnectionReady;
    }

    private static bool TryGetSessionAuth(out NakamaAuthManager auth)
    {
        auth = NakamaAuthManager.Instance;
        return auth != null && auth.IsAuthenticated && auth.Client != null && auth.Session != null;
    }

    private static string NormalizeChannelName(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ExtractMessageText(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent)) return string.Empty;

        try
        {
            var payload = JsonUtility.FromJson<ChatMessagePayload>(rawContent);
            if (payload != null)
            {
                if (!string.IsNullOrWhiteSpace(payload.content)) return payload.content;
                if (!string.IsNullOrWhiteSpace(payload.message)) return payload.message;
            }
        }
        catch
        {
            // Some external messages may not use the local payload shape.
        }

        return rawContent;
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";
        string text = value.Trim();
        return text.Length <= 8 ? text : text.Substring(0, 8);
    }

    private static string ShortChannel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "none";
        string text = value.Trim();
        return text.Length <= 36 ? text : $"{text.Substring(0, 16)}...{text.Substring(text.Length - 12)}";
    }

    private class NakamaChatNotificationDriver : MonoBehaviour
    {
        private NakamaChatPanelController _panel;

        private void Update()
        {
            if (_panel == null)
            {
                _panel = FindFirstObjectByType<NakamaChatPanelController>(FindObjectsInactive.Include);
            }

            if (_panel != null)
            {
                _panel.TickBackgroundNotifications();
            }
        }
    }

    private class ChatDisplayMessage
    {
        public string channelId;
        public string messageId;
        public string username;
        public string content;
    }

    [Serializable]
    private class ChatMessagePayload
    {
        public string content;
        public string message;
    }

    [Serializable]
    private class WorldStateProbe
    {
        public string type;
    }
}
