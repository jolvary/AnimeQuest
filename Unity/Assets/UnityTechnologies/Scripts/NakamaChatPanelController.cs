using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class NakamaChatPanelController : MonoBehaviour
{
    private const int MaxChatMessageLength = 900;
    private const int MaxChatPayloadBytes = 3500;
    private static readonly Color PanelRowSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.24f);
    private static readonly Color ComposerSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.36f);

    public Font preferredFont;
    public string defaultRoomName = "animequest-lobby";

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
    private bool _isJoiningChannel;
    private readonly List<string> _renderedMessageIds = new List<string>();
    private readonly List<ChatDisplayMessage> _pendingMessages = new List<ChatDisplayMessage>();
    private readonly object _pendingMessagesLock = new object();

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
        UnsubscribeFromSocket();
    }

    private void Update()
    {
        FlushPendingMessages();
    }

    public async void OpenGeneralChat()
    {
        string channelKey = $"room:{defaultRoomName}";
        if (_channel != null && string.Equals(_activeChannelKey, channelKey, StringComparison.Ordinal))
        {
            RefreshSendState();
            return;
        }

        await JoinChannel(defaultRoomName, ChannelType.Room, "General Chat", channelKey, hidden: false);
    }

    public async void OpenDirectMessage(string userId, string username)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            SetStatus("Choose a friend before opening direct chat.");
            return;
        }

        string channelKey = $"dm:{userId}";
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

        if (_isJoiningChannel)
        {
            return;
        }

        if (!TryGetAuth(out var auth))
        {
            _channel = null;
            _activeChannelId = null;
            _activeChannelKey = null;
            SetTitle("Chat");
            SetStatus("Log in to use chat.");
            RefreshSendState();
            return;
        }

        _isJoiningChannel = true;
        try
        {
            SubscribeToSocket();
            SetTitle(label);
            SetStatus("Connecting to chat...");
            RefreshSendState();

            _channel = await auth.Socket.JoinChatAsync(target, type, persistence: true, hidden: hidden);
            _activeChannelId = _channel.Id;
            _activeChannelKey = channelKey;
            DozzleLogger.Action("Chat channel joined", $"channel={_channel.Id};type={type}");
            await LoadHistory();
            RefreshSendState();
        }
        catch (Exception ex)
        {
            _channel = null;
            _activeChannelId = null;
            _activeChannelKey = null;
            SetStatus("Chat unavailable.");
            RefreshSendState();
            DozzleLogger.Error("Chat channel join failed", ex);
        }
        finally
        {
            _isJoiningChannel = false;
        }
    }

    private async Task LoadHistory()
    {
        ClearRows();
        _renderedMessageIds.Clear();

        if (_channel == null || !TryGetAuth(out var auth))
        {
            SetStatus("Chat unavailable.");
            return;
        }

        try
        {
            var history = await auth.Client.ListChannelMessagesAsync(auth.Session, _channel.Id, 50, true, null);
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
            SetStatus("Could not load previous messages.");
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
            SetStatus("Log in to send chat messages.");
            RefreshSendState();
            return;
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
        if (_subscribedToSocket || NakamaAuthManager.Instance == null || NakamaAuthManager.Instance.Socket == null)
        {
            return;
        }

        NakamaAuthManager.Instance.Socket.ReceivedChannelMessage += OnReceivedChannelMessage;
        _subscribedToSocket = true;
    }

    private void UnsubscribeFromSocket()
    {
        if (!_subscribedToSocket || NakamaAuthManager.Instance == null || NakamaAuthManager.Instance.Socket == null)
        {
            _subscribedToSocket = false;
            return;
        }

        NakamaAuthManager.Instance.Socket.ReceivedChannelMessage -= OnReceivedChannelMessage;
        _subscribedToSocket = false;
    }

    private void OnReceivedChannelMessage(IApiChannelMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(_activeChannelId) || !string.Equals(message.ChannelId, _activeChannelId, StringComparison.Ordinal))
        {
            return;
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
        bool canSend = _channel != null && hasAuth && !isIncognito;
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
        auth = NakamaAuthManager.Instance;
        return auth != null && auth.IsAuthenticated && auth.Client != null && auth.Session != null && auth.Socket != null;
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
