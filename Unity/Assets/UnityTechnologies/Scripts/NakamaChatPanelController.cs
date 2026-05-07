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
    private readonly List<string> _renderedMessageIds = new List<string>();
    private readonly List<ChatDisplayMessage> _pendingMessages = new List<ChatDisplayMessage>();
    private readonly object _pendingMessagesLock = new object();

    public string CurrentChannelKey => _activeChannelKey;
    public string GeneralChannelKey => BuildRoomChannelKey(defaultRoomName);

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

    private void OnEnable()
    {
        EnsureElements();
        SubscribeToSocket();

        if (_channel == null)
        {
            OpenGeneralChat();
        }
        else
        {
            RefreshSendState();
        }
    }

    private void OnDisable()
    {
    }

    private void OnDestroy()
    {
        UnsubscribeFromSocket();
    }

    private void Update()
    {
        FlushPendingMessages();
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
        if (!TryGetSessionAuth(out var auth) || auth.IsIncognitoSession)
        {
            return;
        }

        string channelKey = GeneralChannelKey;
        if (_channel != null && string.Equals(_activeChannelKey, channelKey, StringComparison.Ordinal))
        {
            return;
        }

        DozzleLogger.Action("Global chat auto-join requested", $"room={defaultRoomName}");
        _ = JoinChannel(defaultRoomName, ChannelType.Room, "General Chat", channelKey, hidden: false);
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
            SetStatus("Chat ready.");
            DozzleLogger.Action("Chat channel joined", $"channel={_channel.Id};type={type};target={target}");
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

            if (history != null && history.Messages != null)
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
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

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
            DozzleLogger.Action("Chat message sent", $"channel={_channel.Id}");
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
        if (NakamaAuthManager.Instance == null || NakamaAuthManager.Instance.Socket == null)
        {
            return;
        }

        var socket = NakamaAuthManager.Instance.Socket;
        if (_subscribedToSocket && ReferenceEquals(_subscribedSocket, socket))
        {
            return;
        }

        UnsubscribeFromSocket();
        socket.ReceivedChannelMessage += OnReceivedChannelMessage;
        _subscribedSocket = socket;
        _subscribedToSocket = true;
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
        if (message == null || string.IsNullOrWhiteSpace(_activeChannelId) || !string.Equals(message.ChannelId, _activeChannelId, StringComparison.Ordinal))
        {
            return;
        }

        if (!IsFromCurrentUser(message))
        {
            ChatMessageReceived?.Invoke(NormalizeChannelName(_activeChannelKey, message.ChannelId));
        }

        EnqueueMessage(ToDisplayMessage(message));
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
        if (string.IsNullOrWhiteSpace(text))
        {
            text = message.content ?? string.Empty;
        }

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

        var inputImage = inputObj.GetComponent<Image>();
        inputImage.color = ComposerSurfaceColor;

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
}
