using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.UI;

public class NakamaWatchPartyPanelController : MonoBehaviour
{
    private const int WatchPartyOperationTimeoutMilliseconds = 5000;
    private const int MaxWatchPartyPayloadBytes = 3600;
    private const int MaxWatchRoomNameLength = 64;
    private const int MaxNoteLength = 280;
    private const int MaxAnimeResults = 8;
    private const int MaxEventRows = 80;
    private static readonly Color SurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.24f);
    private static readonly Color InputColor = new Color(0.96f, 0.90f, 0.78f, 0.36f);
    private static readonly Color PrimaryButtonColor = new Color(0.23f, 0.77f, 0.27f, 1f);
    private static readonly Color SecondaryButtonColor = new Color(0.48f, 0.28f, 0.12f, 0.86f);

    public Font preferredFont;
    public string defaultRoomName = "animequest-watch";

    private Text _titleText;
    private Text _statusText;
    private Text _selectionText;
    private Text _playbackText;
    private InputField _roomInput;
    private InputField _searchInput;
    private InputField _episodeInput;
    private InputField _linkInput;
    private InputField _noteInput;
    private Button _joinButton;
    private Button _searchButton;
    private Button _shareButton;
    private Button _playButton;
    private Button _pauseButton;
    private Button _seekBackButton;
    private Button _seekForwardButton;
    private Button _openLinkButton;
    private Button _sendNoteButton;
    private ScrollRect _resultsScrollRect;
    private RectTransform _resultsContent;
    private ScrollRect _eventsScrollRect;
    private RectTransform _eventsContent;

    private IChannel _channel;
    private string _activeChannelId;
    private string _activeRoomName;
    private bool _subscribedToSocket;
    private ISocket _subscribedSocket;
    private bool _isJoining;
    private bool _isSearching;
    private bool _isSending;
    private int _joinGeneration;
    private float _stateReceivedAt;
    private WatchPartyPayload _state = new WatchPartyPayload
    {
        type = "watch_party",
        action = "state",
        room = "animequest-watch",
        animeTitle = "No anime selected",
        episode = 1,
        positionSeconds = 0f,
        playing = false
    };

    private readonly List<WatchPartyPayload> _pendingPayloads = new List<WatchPartyPayload>();
    private readonly object _pendingPayloadsLock = new object();

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    private void OnEnable()
    {
        EnsureElements();
        SubscribeToSocket();
        RefreshInteractableState();
    }

    private void OnDestroy()
    {
        UnsubscribeFromSocket();
    }

    private void Update()
    {
        FlushPendingPayloads();
        RefreshPlaybackText();
    }

    public void OpenWatchRoom()
    {
        EnsureElements();
        if (_isJoining) return;

        string roomName = _roomInput != null ? _roomInput.text : defaultRoomName;
        JoinRoom(roomName);
    }

    private async void JoinRoom(string rawRoomName)
    {
        EnsureElements();
        string roomName = NormalizeRoomName(rawRoomName);
        if (_channel != null && string.Equals(roomName, _activeRoomName, StringComparison.Ordinal))
        {
            SetStatus($"Watch room ready: {roomName}");
            RefreshInteractableState();
            return;
        }

        int generation = ++_joinGeneration;
        _isJoining = true;
        RefreshInteractableState();

        try
        {
            SetStatus("Connecting to watch room...");

            if (!TryGetReadyAuth(out var auth))
            {
                bool hasSession = TryGetSessionAuth(out var sessionAuth);
                if (!hasSession)
                {
                    _channel = null;
                    _activeChannelId = null;
                    _activeRoomName = null;
                    SetStatus("Log in to use watch rooms.");
                    RefreshInteractableState();
                    return;
                }

                bool connected = await sessionAuth.EnsureSocketConnectedAsync(WatchPartyOperationTimeoutMilliseconds);
                if (generation != _joinGeneration) return;
                if (connected)
                {
                    SubscribeToSocket();
                }
            }

            if (!TryGetReadyAuth(out auth))
            {
                _channel = null;
                _activeChannelId = null;
                _activeRoomName = null;
                SetStatus("Watch room connection unavailable.");
                RefreshInteractableState();
                return;
            }

            SubscribeToSocket();
            var joinTask = auth.Socket.JoinChatAsync(roomName, ChannelType.Room, persistence: true, hidden: false);
            var completed = await Task.WhenAny(joinTask, Task.Delay(WatchPartyOperationTimeoutMilliseconds));
            if (generation != _joinGeneration)
            {
                ObserveBackgroundTask(joinTask);
                return;
            }

            if (completed != joinTask)
            {
                ObserveBackgroundTask(joinTask);
                SetStatus("Watch room connection timed out.");
                DozzleLogger.Error("Watch party room join timed out", $"room={roomName};timeoutMs={WatchPartyOperationTimeoutMilliseconds}");
                return;
            }

            _channel = await joinTask;
            _activeChannelId = _channel.Id;
            _activeRoomName = roomName;
            _state.room = roomName;
            if (_roomInput != null) _roomInput.text = roomName;

            SetStatus($"Watch room ready: {roomName}");
            DozzleLogger.Action("Watch party room joined", $"room={roomName};channel={ShortChannel(_activeChannelId)}");
            AddEventRow("System", $"Joined watch room {roomName}.");
            await LoadLatestRoomState(generation);
        }
        catch (Exception ex)
        {
            if (generation != _joinGeneration) return;
            _channel = null;
            _activeChannelId = null;
            _activeRoomName = null;
            SetStatus("Watch room unavailable.");
            DozzleLogger.Error("Watch party room join failed", ex);
        }
        finally
        {
            if (generation == _joinGeneration)
            {
                _isJoining = false;
                RefreshInteractableState();
            }
        }
    }

    private async Task LoadLatestRoomState(int generation)
    {
        if (generation != _joinGeneration || _channel == null || !TryGetReadyAuth(out var auth)) return;

        try
        {
            var historyTask = auth.Client.ListChannelMessagesAsync(auth.Session, _channel.Id, 30, true, null);
            var completed = await Task.WhenAny(historyTask, Task.Delay(WatchPartyOperationTimeoutMilliseconds));
            if (generation != _joinGeneration || _channel == null) return;

            if (completed != historyTask)
            {
                ObserveBackgroundTask(historyTask);
                AddEventRow("System", "Previous watch state was unavailable.");
                return;
            }

            var history = await historyTask;
            WatchPartyPayload latestState = null;
            if (history?.Messages != null)
            {
                foreach (var message in history.Messages)
                {
                    if (!TryParsePayload(message.Content, out var payload)) continue;
                    if (payload.action == "note") continue;

                    if (latestState == null || payload.sentAtUnixMs >= latestState.sentAtUnixMs)
                    {
                        latestState = payload;
                    }
                }
            }

            if (latestState != null)
            {
                ApplyPayload(latestState, "history");
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Watch party history load failed", ex);
        }
    }

    private async void SearchAnime()
    {
        EnsureElements();
        if (ApiClient.Instance == null)
        {
            SetStatus("Anime API unavailable.");
            return;
        }

        _isSearching = true;
        RefreshInteractableState();
        SetStatus("Searching anime...");

        try
        {
            string query = _searchInput != null ? _searchInput.text.Trim() : string.Empty;
            string json = await ApiClient.Instance.GetAnime(query, MaxAnimeResults, 0);
            var response = JsonUtility.FromJson<AnimeDeckResponse>(json);
            RenderAnimeResults(response?.items);
            int count = response?.items != null ? response.items.Length : 0;
            SetStatus(count > 0 ? $"Found {count} anime. Select one to share." : "No anime found.");
            DozzleLogger.Action("Watch party anime search completed", $"query={SafeLogValue(query)};count={count}");
        }
        catch (Exception ex)
        {
            ClearResults();
            SetStatus("Anime search failed.");
            DozzleLogger.Error("Watch party anime search failed", ex);
        }
        finally
        {
            _isSearching = false;
            RefreshInteractableState();
        }
    }

    private void SelectAnime(AnimeDeckItem item)
    {
        if (item == null) return;

        _state.animeId = item.id;
        _state.animeTitle = string.IsNullOrWhiteSpace(item.title) ? "Untitled anime" : item.title;
        _state.totalEpisodes = Mathf.Max(0, item.episodes);
        _state.episode = ClampEpisode(ReadEpisodeInput(), _state.totalEpisodes);
        _state.positionSeconds = 0f;
        _state.playing = false;
        _state.action = "select";
        _stateReceivedAt = Time.unscaledTime;

        if (_episodeInput != null) _episodeInput.text = _state.episode.ToString();
        RefreshSelectionText();
        RefreshPlaybackText();
        SetStatus("Anime selected. Share it with the room when ready.");
        AddEventRow("You", $"Selected {_state.animeTitle} episode {_state.episode}.");
    }

    private void ShareState()
    {
        SendState("state", keepPlayingState: true);
    }

    private void Play()
    {
        SendState("play", keepPlayingState: false, playing: true);
    }

    private void Pause()
    {
        SendState("pause", keepPlayingState: false, playing: false);
    }

    private void Seek(float deltaSeconds)
    {
        float nextPosition = Mathf.Max(0f, CurrentPositionSeconds() + deltaSeconds);
        _state.positionSeconds = nextPosition;
        _stateReceivedAt = Time.unscaledTime;
        SendState("seek", keepPlayingState: true);
    }

    private async void SendState(string action, bool keepPlayingState, bool playing = false)
    {
        _state.action = action;
        _state.episode = ClampEpisode(ReadEpisodeInput(), _state.totalEpisodes);
        _state.watchUrl = _linkInput != null ? _linkInput.text.Trim() : string.Empty;
        _state.positionSeconds = CurrentPositionSeconds();
        if (!keepPlayingState)
        {
            _state.playing = playing;
        }

        _stateReceivedAt = Time.unscaledTime;
        await SendPayload(CloneStateForSend());
    }

    private async void SendNote()
    {
        string note = _noteInput != null ? _noteInput.text.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(note)) return;
        if (note.Length > MaxNoteLength)
        {
            SetStatus($"Room notes are limited to {MaxNoteLength} characters.");
            return;
        }

        var payload = CloneStateForSend();
        payload.action = "note";
        payload.note = note;
        await SendPayload(payload);
        if (_noteInput != null) _noteInput.text = string.Empty;
    }

    private async Task SendPayload(WatchPartyPayload payload)
    {
        EnsureElements();
        if (_channel == null)
        {
            SetStatus("Join a watch room first.");
            return;
        }

        if (!TryGetReadyAuth(out var auth))
        {
            bool hasSession = TryGetSessionAuth(out var sessionAuth);
            if (hasSession)
            {
                SetStatus("Connecting to watch room...");
                bool connected = await sessionAuth.EnsureSocketConnectedAsync(WatchPartyOperationTimeoutMilliseconds);
                if (connected)
                {
                    SubscribeToSocket();
                }
            }

            if (!TryGetReadyAuth(out auth))
            {
                SetStatus(hasSession ? "Watch room connection unavailable." : "Log in to use watch rooms.");
                RefreshInteractableState();
                return;
            }
        }

        if (auth.IsIncognitoSession)
        {
            SetStatus("Log in to share a watch room.");
            RefreshInteractableState();
            return;
        }

        payload.type = "watch_party";
        payload.room = _activeRoomName ?? NormalizeRoomName(defaultRoomName);
        payload.senderId = auth.Session.UserId;
        payload.senderName = string.IsNullOrWhiteSpace(auth.Session.Username) ? "Player" : auth.Session.Username;
        payload.sentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string json = JsonUtility.ToJson(payload);
        if (Encoding.UTF8.GetByteCount(json) > MaxWatchPartyPayloadBytes)
        {
            SetStatus("Watch state is too large to send.");
            return;
        }

        _isSending = true;
        RefreshInteractableState();

        try
        {
            await auth.Socket.WriteChatMessageAsync(_channel.Id, json);
            ApplyPayload(payload, "local");
            DozzleLogger.Action("Watch party payload sent", $"action={payload.action};room={payload.room};channel={ShortChannel(_channel.Id)};anime={SafeLogValue(payload.animeTitle)};episode={payload.episode}");
        }
        catch (Exception ex)
        {
            SetStatus("Watch update failed.");
            DozzleLogger.Error("Watch party payload send failed", ex);
        }
        finally
        {
            _isSending = false;
            RefreshInteractableState();
        }
    }

    private void OpenWatchLink()
    {
        string url = _linkInput != null ? _linkInput.text.Trim() : _state.watchUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("Add a watch link first.");
            return;
        }

        Application.OpenURL(url);
        AddEventRow("System", "Opened watch link.");
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
        if (message == null || string.IsNullOrWhiteSpace(_activeChannelId)) return;
        if (!string.Equals(message.ChannelId, _activeChannelId, StringComparison.Ordinal)) return;
        if (!TryParsePayload(message.Content, out var payload)) return;
        if (IsFromCurrentUser(payload)) return;

        lock (_pendingPayloadsLock)
        {
            _pendingPayloads.Add(payload);
        }
    }

    private void FlushPendingPayloads()
    {
        WatchPartyPayload[] payloads;
        lock (_pendingPayloadsLock)
        {
            if (_pendingPayloads.Count == 0) return;
            payloads = _pendingPayloads.ToArray();
            _pendingPayloads.Clear();
        }

        EnsureElements();
        foreach (var payload in payloads)
        {
            ApplyPayload(payload, "remote");
        }
    }

    private void ApplyPayload(WatchPartyPayload payload, string source)
    {
        if (payload == null) return;

        if (!string.Equals(payload.action, "note", StringComparison.Ordinal))
        {
            _state = payload;
            _state.type = "watch_party";
            _state.room = string.IsNullOrWhiteSpace(payload.room) ? _activeRoomName : payload.room;
            _state.episode = ClampEpisode(payload.episode, payload.totalEpisodes);
            _state.positionSeconds = Mathf.Max(0f, payload.positionSeconds);
            _stateReceivedAt = Time.unscaledTime;

            if (_episodeInput != null) _episodeInput.text = _state.episode.ToString();
            if (_linkInput != null && !string.Equals(_linkInput.text, _state.watchUrl, StringComparison.Ordinal))
            {
                _linkInput.text = _state.watchUrl ?? string.Empty;
            }

            RefreshSelectionText();
            RefreshPlaybackText();
        }

        AddEventRow(DisplaySender(payload, source), FormatEvent(payload));
        SetStatus($"Watch room ready: {_activeRoomName ?? NormalizeRoomName(defaultRoomName)}");
    }

    private WatchPartyPayload CloneStateForSend()
    {
        return new WatchPartyPayload
        {
            type = "watch_party",
            action = _state.action,
            room = _activeRoomName ?? NormalizeRoomName(defaultRoomName),
            animeId = _state.animeId,
            animeTitle = _state.animeTitle,
            totalEpisodes = _state.totalEpisodes,
            episode = _state.episode,
            watchUrl = _state.watchUrl,
            playing = _state.playing,
            positionSeconds = _state.positionSeconds,
            sentAtUnixMs = _state.sentAtUnixMs,
            senderId = _state.senderId,
            senderName = _state.senderName,
            note = _state.note
        };
    }

    private float CurrentPositionSeconds()
    {
        if (_state == null) return 0f;
        float basePosition = Mathf.Max(0f, _state.positionSeconds);
        if (!_state.playing) return basePosition;
        return basePosition + Mathf.Max(0f, Time.unscaledTime - _stateReceivedAt);
    }

    private int ReadEpisodeInput()
    {
        if (_episodeInput == null || !int.TryParse(_episodeInput.text, out int episode))
        {
            return Mathf.Max(1, _state != null ? _state.episode : 1);
        }

        return Mathf.Max(1, episode);
    }

    private static int ClampEpisode(int episode, int totalEpisodes)
    {
        if (totalEpisodes > 0)
        {
            return Mathf.Clamp(Mathf.Max(1, episode), 1, totalEpisodes);
        }

        return Mathf.Max(1, episode);
    }

    private void EnsureElements()
    {
        if (_titleText == null)
        {
            _titleText = CreateText("WatchPartyTitle", transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -96f), new Vector2(-48f, -42f), 30, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.text = "Watch Together";
        }

        if (_statusText == null)
        {
            _statusText = CreateText("WatchPartyStatus", transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -136f), new Vector2(-48f, -98f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            _statusText.text = "Join a watch room to sync anime.";
        }

        if (_roomInput == null)
        {
            _roomInput = CreateInput("WatchRoomInput", transform, "Room name", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -194f), new Vector2(328f, -150f), 18);
            _roomInput.characterLimit = MaxWatchRoomNameLength;
            _roomInput.text = NormalizeRoomName(defaultRoomName);
        }

        if (_joinButton == null)
        {
            _joinButton = CreateButton("JoinWatchRoomButton", transform, "Join", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -194f), new Vector2(450f, -150f), 18, PrimaryButtonColor, () => JoinRoom(_roomInput != null ? _roomInput.text : defaultRoomName));
        }

        if (_searchInput == null)
        {
            _searchInput = CreateInput("WatchAnimeSearchInput", transform, "Search anime", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -250f), new Vector2(328f, -206f), 18);
            _searchInput.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    SearchAnime();
                }
            });
        }

        if (_searchButton == null)
        {
            _searchButton = CreateButton("WatchAnimeSearchButton", transform, "Search", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -250f), new Vector2(450f, -206f), 18, SecondaryButtonColor, SearchAnime);
        }

        if (_resultsScrollRect == null || _resultsContent == null)
        {
            CreateResultsContainer();
        }

        if (_selectionText == null)
        {
            _selectionText = CreateText("WatchSelectionText", transform, new Vector2(0.46f, 1f), new Vector2(1f, 1f), new Vector2(22f, -204f), new Vector2(-48f, -150f), 20, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        if (_playbackText == null)
        {
            _playbackText = CreateText("WatchPlaybackText", transform, new Vector2(0.46f, 1f), new Vector2(1f, 1f), new Vector2(22f, -246f), new Vector2(-48f, -206f), 17, FontStyle.Normal, TextAnchor.MiddleLeft);
        }

        if (_episodeInput == null)
        {
            _episodeInput = CreateInput("WatchEpisodeInput", transform, "Episode", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(22f, -306f), new Vector2(132f, -262f), 18);
            _episodeInput.contentType = InputField.ContentType.IntegerNumber;
            _episodeInput.text = "1";
        }

        if (_shareButton == null)
        {
            _shareButton = CreateButton("ShareWatchStateButton", transform, "Share", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(146f, -306f), new Vector2(256f, -262f), 18, PrimaryButtonColor, ShareState);
        }

        if (_playButton == null)
        {
            _playButton = CreateButton("WatchPlayButton", transform, "Play", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(270f, -306f), new Vector2(366f, -262f), 18, PrimaryButtonColor, Play);
        }

        if (_pauseButton == null)
        {
            _pauseButton = CreateButton("WatchPauseButton", transform, "Pause", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(380f, -306f), new Vector2(490f, -262f), 18, SecondaryButtonColor, Pause);
        }

        if (_seekBackButton == null)
        {
            _seekBackButton = CreateButton("WatchSeekBackButton", transform, "-30s", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(504f, -306f), new Vector2(600f, -262f), 18, SecondaryButtonColor, () => Seek(-30f));
        }

        if (_seekForwardButton == null)
        {
            _seekForwardButton = CreateButton("WatchSeekForwardButton", transform, "+30s", new Vector2(0.46f, 1f), new Vector2(0.46f, 1f), new Vector2(614f, -306f), new Vector2(710f, -262f), 18, SecondaryButtonColor, () => Seek(30f));
        }

        if (_linkInput == null)
        {
            _linkInput = CreateInput("WatchLinkInput", transform, "Legal watch link or source URL", new Vector2(0.46f, 1f), new Vector2(1f, 1f), new Vector2(22f, -362f), new Vector2(-180f, -318f), 16);
            _linkInput.onValueChanged.AddListener(_ => RefreshInteractableState());
        }

        if (_openLinkButton == null)
        {
            _openLinkButton = CreateButton("OpenWatchLinkButton", transform, "Open", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-168f, -362f), new Vector2(-48f, -318f), 18, SecondaryButtonColor, OpenWatchLink);
        }

        if (_eventsScrollRect == null || _eventsContent == null)
        {
            CreateEventsContainer();
        }

        if (_noteInput == null)
        {
            _noteInput = CreateInput("WatchNoteInput", transform, "Room note", new Vector2(0.46f, 0f), new Vector2(1f, 0f), new Vector2(22f, 48f), new Vector2(-180f, 94f), 16);
            _noteInput.characterLimit = MaxNoteLength;
        }

        if (_sendNoteButton == null)
        {
            _sendNoteButton = CreateButton("SendWatchNoteButton", transform, "Send", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-168f, 48f), new Vector2(-48f, 94f), 18, PrimaryButtonColor, SendNote);
        }

        ApplyFonts();
        RefreshSelectionText();
        RefreshPlaybackText();
        RefreshInteractableState();
    }

    private void CreateResultsContainer()
    {
        var viewportObj = new GameObject("WatchAnimeResultsViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(0.45f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 48f);
        viewportRect.offsetMax = new Vector2(-18f, -272f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        _resultsScrollRect = viewportObj.GetComponent<ScrollRect>();
        _resultsScrollRect.horizontal = false;
        _resultsScrollRect.vertical = true;
        _resultsScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _resultsScrollRect.scrollSensitivity = 24f;
        _resultsScrollRect.viewport = viewportRect;

        var contentObj = new GameObject("WatchAnimeResultsContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _resultsContent = contentObj.GetComponent<RectTransform>();
        _resultsContent.anchorMin = new Vector2(0f, 1f);
        _resultsContent.anchorMax = new Vector2(1f, 1f);
        _resultsContent.pivot = new Vector2(0.5f, 1f);
        _resultsContent.offsetMin = Vector2.zero;
        _resultsContent.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 8f;
        layout.padding = new RectOffset(6, 6, 6, 6);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _resultsScrollRect.content = _resultsContent;
    }

    private void CreateEventsContainer()
    {
        var viewportObj = new GameObject("WatchEventsViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0.46f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(22f, 110f);
        viewportRect.offsetMax = new Vector2(-48f, -382f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        _eventsScrollRect = viewportObj.GetComponent<ScrollRect>();
        _eventsScrollRect.horizontal = false;
        _eventsScrollRect.vertical = true;
        _eventsScrollRect.movementType = ScrollRect.MovementType.Clamped;
        _eventsScrollRect.scrollSensitivity = 24f;
        _eventsScrollRect.viewport = viewportRect;

        var contentObj = new GameObject("WatchEventsContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        _eventsContent = contentObj.GetComponent<RectTransform>();
        _eventsContent.anchorMin = new Vector2(0f, 1f);
        _eventsContent.anchorMax = new Vector2(1f, 1f);
        _eventsContent.pivot = new Vector2(0.5f, 1f);
        _eventsContent.offsetMin = Vector2.zero;
        _eventsContent.offsetMax = Vector2.zero;

        var layout = contentObj.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 8f;
        layout.padding = new RectOffset(6, 6, 6, 6);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _eventsScrollRect.content = _eventsContent;
    }

    private void RenderAnimeResults(AnimeDeckItem[] items)
    {
        ClearResults();
        if (_resultsContent == null) return;

        if (items == null || items.Length == 0)
        {
            AddResultRow("No anime found", "Try a different search.", null);
            return;
        }

        foreach (var item in items)
        {
            if (item == null) continue;
            string title = string.IsNullOrWhiteSpace(item.title) ? "Untitled anime" : item.title;
            string meta = item.episodes > 0 ? $"{item.episodes} episodes" : "Episodes unknown";
            if (!string.IsNullOrWhiteSpace(item.releaseDate))
            {
                meta += $" | {item.releaseDate}";
            }

            var capturedItem = item;
            AddResultRow(title, meta, () => SelectAnime(capturedItem));
        }

        ResetScroll(_resultsScrollRect);
    }

    private void AddResultRow(string title, string meta, Action onSelect)
    {
        var row = CreateRow(_resultsContent, "WatchAnimeResult", 84f, SurfaceColor);
        var titleText = CreateRowText(row.transform, "Title", title, 17, FontStyle.Bold, new Vector2(12f, -8f), new Vector2(onSelect == null ? -12f : -116f, -36f), TextAnchor.MiddleLeft);
        titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        CreateRowText(row.transform, "Meta", meta, 14, FontStyle.Normal, new Vector2(12f, -42f), new Vector2(onSelect == null ? -12f : -116f, -8f), TextAnchor.MiddleLeft);

        if (onSelect != null)
        {
            CreateButton("SelectButton", row.transform, "Select", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-104f, -20f), new Vector2(-12f, 20f), 15, PrimaryButtonColor, onSelect);
        }
    }

    private void AddEventRow(string sender, string message)
    {
        if (_eventsContent == null || string.IsNullOrWhiteSpace(message)) return;

        while (_eventsContent.childCount >= MaxEventRows)
        {
            Destroy(_eventsContent.GetChild(0).gameObject);
        }

        string senderText = string.IsNullOrWhiteSpace(sender) ? "Player" : sender;
        int lineCount = Mathf.Max(1, Mathf.CeilToInt(message.Length / 74f));
        float rowHeight = Mathf.Clamp(46f + lineCount * 18f, 68f, 160f);
        var row = CreateRow(_eventsContent, "WatchEvent", rowHeight, SurfaceColor);
        CreateRowText(row.transform, "Sender", senderText, 14, FontStyle.Bold, new Vector2(12f, -8f), new Vector2(-12f, -30f), TextAnchor.MiddleLeft);
        CreateRowText(row.transform, "Message", message, 15, FontStyle.Normal, new Vector2(12f, -34f), new Vector2(-12f, -8f), TextAnchor.UpperLeft);
        ResetScrollToBottom(_eventsScrollRect);
    }

    private GameObject CreateRow(Transform parent, string name, float height, Color color)
    {
        var rowObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        rowObj.transform.SetParent(parent, false);
        rowObj.GetComponent<Image>().color = color;

        var layout = rowObj.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        return rowObj;
    }

    private Text CreateRowText(Transform parent, string name, string value, int size, FontStyle style, Vector2 offsetMin, Vector2 offsetMax, TextAnchor alignment)
    {
        var text = CreateText(name, parent, Vector2.zero, Vector2.one, offsetMin, offsetMax, size, style, alignment);
        text.text = value ?? string.Empty;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int size, FontStyle style, TextAnchor alignment)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);

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
        text.raycastTarget = false;
        return text;
    }

    private InputField CreateInput(string name, Transform parent, string placeholderText, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int fontSize)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        obj.transform.SetParent(parent, false);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        obj.GetComponent<Image>().color = InputColor;

        var input = obj.GetComponent<InputField>();
        input.lineType = InputField.LineType.SingleLine;
        input.contentType = InputField.ContentType.Standard;

        var text = CreateText("Text", obj.transform, Vector2.zero, Vector2.one, new Vector2(12f, 6f), new Vector2(-12f, -6f), fontSize, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.color = Color.black;
        text.raycastTarget = true;

        var placeholder = CreateText("Placeholder", obj.transform, Vector2.zero, Vector2.one, new Vector2(12f, 6f), new Vector2(-12f, -6f), fontSize, FontStyle.Italic, TextAnchor.MiddleLeft);
        placeholder.text = placeholderText ?? string.Empty;
        placeholder.color = new Color(0.45f, 0.45f, 0.45f, 1f);

        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, int fontSize, Color color, Action onClick)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);

        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        obj.GetComponent<Image>().color = color;
        var button = obj.GetComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        var text = CreateText("Text", obj.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.text = label;
        text.color = Color.black;
        return button;
    }

    private void RefreshSelectionText()
    {
        if (_selectionText == null) return;
        string title = _state != null && !string.IsNullOrWhiteSpace(_state.animeTitle) ? _state.animeTitle : "No anime selected";
        int episode = _state != null ? Mathf.Max(1, _state.episode) : 1;
        int total = _state != null ? Mathf.Max(0, _state.totalEpisodes) : 0;
        string episodeText = total > 0 ? $"Episode {episode}/{total}" : $"Episode {episode}";
        _selectionText.text = $"{title} | {episodeText}";
    }

    private void RefreshPlaybackText()
    {
        if (_playbackText == null) return;
        string stateText = _state != null && _state.playing ? "Playing" : "Paused";
        string positionText = FormatTime(CurrentPositionSeconds());
        string roomText = string.IsNullOrWhiteSpace(_activeRoomName) ? NormalizeRoomName(defaultRoomName) : _activeRoomName;
        _playbackText.text = $"{stateText} at {positionText} | Room: {roomText}";
    }

    private void RefreshInteractableState()
    {
        bool connected = _channel != null && !_isJoining;
        bool busy = _isSearching || _isSending || _isJoining;
        bool canSend = connected && !busy;

        if (_joinButton != null) _joinButton.interactable = !_isJoining;
        if (_searchButton != null) _searchButton.interactable = !_isSearching;
        if (_shareButton != null) _shareButton.interactable = canSend;
        if (_playButton != null) _playButton.interactable = canSend;
        if (_pauseButton != null) _pauseButton.interactable = canSend;
        if (_seekBackButton != null) _seekBackButton.interactable = canSend;
        if (_seekForwardButton != null) _seekForwardButton.interactable = canSend;
        if (_openLinkButton != null) _openLinkButton.interactable = !string.IsNullOrWhiteSpace(_linkInput != null ? _linkInput.text : _state?.watchUrl);
        if (_sendNoteButton != null) _sendNoteButton.interactable = canSend;
    }

    private void SetStatus(string value)
    {
        if (_statusText != null)
        {
            _statusText.text = value ?? string.Empty;
        }
    }

    private void ClearResults()
    {
        if (_resultsContent == null) return;
        for (int i = _resultsContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_resultsContent.GetChild(i).gameObject);
        }
    }

    private void ApplyFonts()
    {
        Font font = ResolveFont();
        var texts = GetComponentsInChildren<Text>(true);
        foreach (var text in texts)
        {
            if (text != null) text.font = font;
        }
    }

    private Font ResolveFont()
    {
        return preferredFont != null ? preferredFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static string NormalizeRoomName(string roomName)
    {
        string value = string.IsNullOrWhiteSpace(roomName) ? "animequest-watch" : roomName.Trim();
        if (value.Length > MaxWatchRoomNameLength) value = value.Substring(0, MaxWatchRoomNameLength);
        return value;
    }

    private static bool TryGetSessionAuth(out NakamaAuthManager auth)
    {
        auth = NakamaAuthManager.Instance;
        return auth != null && auth.Session != null && !auth.Session.IsExpired;
    }

    private static bool TryGetReadyAuth(out NakamaAuthManager auth)
    {
        return TryGetSessionAuth(out auth) && auth.Socket != null && auth.IsConnectionReady;
    }

    private static bool TryParsePayload(string rawContent, out WatchPartyPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(rawContent)) return false;
        if (rawContent.IndexOf("watch_party", StringComparison.OrdinalIgnoreCase) < 0) return false;

        try
        {
            payload = JsonUtility.FromJson<WatchPartyPayload>(rawContent);
            return payload != null && string.Equals(payload.type, "watch_party", StringComparison.Ordinal);
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    private static bool IsFromCurrentUser(WatchPartyPayload payload)
    {
        if (payload == null || !TryGetSessionAuth(out var auth) || auth.Session == null) return false;
        if (!string.IsNullOrWhiteSpace(payload.senderId) && !string.IsNullOrWhiteSpace(auth.Session.UserId))
        {
            return string.Equals(payload.senderId, auth.Session.UserId, StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(payload.senderName) || string.IsNullOrWhiteSpace(auth.Session.Username)) return false;
        return string.Equals(payload.senderName, auth.Session.Username, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplaySender(WatchPartyPayload payload, string source)
    {
        if (string.Equals(source, "local", StringComparison.Ordinal)) return "You";
        if (string.Equals(source, "history", StringComparison.Ordinal)) return "Latest state";
        return string.IsNullOrWhiteSpace(payload.senderName) ? "Player" : payload.senderName;
    }

    private static string FormatEvent(WatchPartyPayload payload)
    {
        if (payload == null) return string.Empty;
        if (string.Equals(payload.action, "note", StringComparison.Ordinal))
        {
            return payload.note ?? string.Empty;
        }

        string title = string.IsNullOrWhiteSpace(payload.animeTitle) ? "the selected anime" : payload.animeTitle;
        string time = FormatTime(Mathf.Max(0f, payload.positionSeconds));
        switch (payload.action)
        {
            case "play":
                return $"Started {title} episode {Mathf.Max(1, payload.episode)} at {time}.";
            case "pause":
                return $"Paused {title} episode {Mathf.Max(1, payload.episode)} at {time}.";
            case "seek":
                return $"Synced {title} episode {Mathf.Max(1, payload.episode)} to {time}.";
            case "select":
                return $"Selected {title} episode {Mathf.Max(1, payload.episode)}.";
            default:
                return $"Shared {title} episode {Mathf.Max(1, payload.episode)} at {time}.";
        }
    }

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int secs = totalSeconds % 60;
        return hours > 0 ? $"{hours:00}:{minutes:00}:{secs:00}" : $"{minutes:00}:{secs:00}";
    }

    private static string ShortChannel(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return "-";
        return channelId.Length <= 12 ? channelId : channelId.Substring(0, 12);
    }

    private static string SafeLogValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        string normalized = value.Replace(";", ",").Replace("\n", " ").Replace("\r", " ").Trim();
        return normalized.Length > 64 ? normalized.Substring(0, 64) : normalized;
    }

    private static void ResetScroll(ScrollRect scrollRect)
    {
        if (scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 1f;
    }

    private static void ResetScrollToBottom(ScrollRect scrollRect)
    {
        if (scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    private static async void ObserveBackgroundTask(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Foreground timeout paths already report the relevant context.
        }
    }

    [Serializable]
    private class WatchPartyPayload
    {
        public string type;
        public string action;
        public string room;
        public string animeId;
        public string animeTitle;
        public int totalEpisodes;
        public int episode;
        public string watchUrl;
        public bool playing;
        public float positionSeconds;
        public long sentAtUnixMs;
        public string senderId;
        public string senderName;
        public string note;
    }

    [Serializable]
    private class AnimeDeckResponse
    {
        public AnimeDeckItem[] items;
        public int limit;
        public int offset;
        public bool hasMore;
    }

    [Serializable]
    private class AnimeDeckItem
    {
        public string id;
        public string title;
        public int episodes;
        public string releaseDate;
    }
}
