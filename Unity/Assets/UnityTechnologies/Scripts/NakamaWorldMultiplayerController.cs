using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

public class NakamaWorldMultiplayerController : MonoBehaviour
{
    private const string WorldRoomName = "animequest-world";
    private const string StateMessageType = "world_state";
    private const string DefaultCharacterKey = "robot_kyle";
    private const string DefaultRobotColor = "default";
    private const float BroadcastInterval = 0.18f;
    private const float RemoteLerpSpeed = 12f;
    private const float RemoteAnimationLerpSpeed = 10f;
    private const float RemoteTimeoutSeconds = 30f;
    private const float SocketConnectRetrySeconds = 4f;
    private const float JoinRetrySeconds = 4f;
    private const float CharacterRefreshIntervalSeconds = 10f;
    private const float MovingVelocityThreshold = 0.08f;
    private const float RunVelocityThreshold = 3.2f;
    private const float WalkAnimationSpeed = 2f;
    private const float RunAnimationSpeed = 6f;
    private const int SocketOperationTimeoutMilliseconds = 5000;
    private const float PlayerStateSaveIntervalSeconds = 3f;
    private const float PlayerStateLoadRetrySeconds = 2f;
    private const float PlayerStatePositionEpsilon = 0.08f;
    private const float PlayerStateRotationEpsilon = 2f;

    private readonly Dictionary<string, RemotePlayer> _remotePlayers = new Dictionary<string, RemotePlayer>();
    private readonly List<WorldStatePayload> _pendingStates = new List<WorldStatePayload>();
    private readonly object _pendingStatesLock = new object();
    private readonly string _fallbackClientInstanceId = Guid.NewGuid().ToString("N");

    private ISocket _socket;
    private IChannel _worldChannel;
    private string _worldChannelId;
    private Transform _localPlayer;
    private Transform _remoteVisualSource;
    private Vector3 _lastLocalPosition;
    private float _lastLocalPositionAt;
    private bool _hasLastLocalPosition;
    private float _nextBroadcastAt;
    private float _nextSocketConnectAttemptAt;
    private float _nextJoinAttemptAt;
    private float _nextCharacterRefreshAt;
    private bool _isConnectingSocket;
    private bool _isJoining;
    private bool _isSending;
    private bool _isLoadingCharacterProgression;
    private int _localPlayerMissingLogCount;
    private string _selectedCharacterKey = DefaultCharacterKey;
    private string _selectedRobotColor = DefaultRobotColor;
    private string _playerStateUserId;
    private bool _hasLoadedPlayerState;
    private bool _isLoadingPlayerState;
    private bool _isSavingPlayerState;
    private bool _hasLastSavedPlayerState;
    private Vector3 _lastSavedPlayerPosition;
    private float _lastSavedPlayerRotationY;
    private float _nextPlayerStateLoadAttemptAt;
    private float _nextPlayerStateSaveAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<NakamaWorldMultiplayerController>() != null) return;

        var obj = new GameObject("NakamaWorldMultiplayerController");
        DontDestroyOnLoad(obj);
        obj.AddComponent<NakamaWorldMultiplayerController>();
    }

    private void Update()
    {
        FlushPendingStates();
        MaintainWorldConnection();
        MaintainPlayerStatePersistence();
        RefreshCharacterSelection();
        BroadcastLocalState();
        UpdateRemotePlayers();
    }

    private void OnDestroy()
    {
        ResetWorldConnection(clearRemotes: true);
    }

    public void ForceCharacterProgressionRefresh()
    {
        _nextCharacterRefreshAt = 0f;
        RefreshCharacterSelection();
    }

    private void MaintainWorldConnection()
    {
        var auth = NakamaAuthManager.Instance;
        if (auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession)
        {
            if (_socket != null || _worldChannel != null || _remotePlayers.Count > 0)
            {
                ResetWorldConnection(clearRemotes: true);
            }
            return;
        }

        if (auth.Socket == null || !auth.IsConnectionReady)
        {
            if (_socket != null || _worldChannel != null)
            {
                ResetWorldConnection(clearRemotes: true);
            }

            EnsureSocketForWorld(auth);
            return;
        }

        if (_socket != auth.Socket)
        {
            ResetWorldConnection(clearRemotes: true);
            _socket = auth.Socket;
            _socket.ReceivedChannelMessage += OnReceivedChannelMessage;
            DozzleLogger.Action("World multiplayer socket ready", $"room={WorldRoomName};client={ShortClientId()}");
        }

        if (_worldChannel == null && !_isJoining && Time.unscaledTime >= _nextJoinAttemptAt)
        {
            JoinWorldRoom(auth);
        }
    }

    private async void EnsureSocketForWorld(NakamaAuthManager auth)
    {
        if (_isConnectingSocket || Time.unscaledTime < _nextSocketConnectAttemptAt)
        {
            return;
        }

        _isConnectingSocket = true;
        _nextSocketConnectAttemptAt = Time.unscaledTime + SocketConnectRetrySeconds;

        try
        {
            DozzleLogger.Action("World multiplayer socket connect requested", $"room={WorldRoomName};client={ShortClientId()}");
            bool connected = await auth.EnsureSocketConnectedAsync(SocketOperationTimeoutMilliseconds);
            if (connected)
            {
                DozzleLogger.Action("World multiplayer socket connect ready", $"room={WorldRoomName};client={ShortClientId()}");
            }
            else
            {
                DozzleLogger.Error("World multiplayer socket connect unavailable", "Will retry while logged in.");
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("World multiplayer socket connect failed", ex);
        }
        finally
        {
            _isConnectingSocket = false;
        }
    }

    private async void JoinWorldRoom(NakamaAuthManager auth)
    {
        if (auth == null || auth.Socket == null || !auth.IsConnectionReady || auth.IsIncognitoSession) return;

        _isJoining = true;
        _nextJoinAttemptAt = Time.unscaledTime + JoinRetrySeconds;
        try
        {
            var joinTask = auth.Socket.JoinChatAsync(WorldRoomName, ChannelType.Room, persistence: false, hidden: false);
            var completed = await Task.WhenAny(joinTask, Task.Delay(SocketOperationTimeoutMilliseconds));
            if (completed != joinTask)
            {
                ObserveBackgroundTask(joinTask);
                DozzleLogger.Error("World multiplayer join timed out", $"room={WorldRoomName};timeoutMs={SocketOperationTimeoutMilliseconds};client={ShortClientId()}");
                return;
            }

            _worldChannel = await joinTask;
            _worldChannelId = _worldChannel.Id;
            _nextBroadcastAt = 0f;
            DozzleLogger.Action("World multiplayer joined", $"channel={_worldChannelId};client={ShortClientId()}");
        }
        catch (Exception ex)
        {
            _worldChannel = null;
            _worldChannelId = null;
            DozzleLogger.Error("World multiplayer join failed", ex);
        }
        finally
        {
            _isJoining = false;
        }
    }

    private void MaintainPlayerStatePersistence()
    {
        var auth = NakamaAuthManager.Instance;
        string userId = auth != null && auth.Session != null ? auth.Session.UserId : null;
        if (auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession || string.IsNullOrWhiteSpace(userId))
        {
            ResetPlayerStateTracking();
            return;
        }

        if (!string.Equals(_playerStateUserId, userId, StringComparison.Ordinal))
        {
            _playerStateUserId = userId;
            _hasLoadedPlayerState = false;
            _isLoadingPlayerState = false;
            _isSavingPlayerState = false;
            _hasLastSavedPlayerState = false;
            _nextPlayerStateLoadAttemptAt = 0f;
            _nextPlayerStateSaveAt = Time.unscaledTime + PlayerStateSaveIntervalSeconds;
        }

        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null || ApiClient.Instance == null)
        {
            return;
        }

        if (!_hasLoadedPlayerState && !_isLoadingPlayerState && Time.unscaledTime >= _nextPlayerStateLoadAttemptAt)
        {
            LoadPlayerState(_localPlayer);
            return;
        }

        if (_hasLoadedPlayerState && !_isSavingPlayerState && Time.unscaledTime >= _nextPlayerStateSaveAt)
        {
            SavePlayerStateIfChanged(_localPlayer);
        }
    }

    private async void LoadPlayerState(Transform localPlayer)
    {
        _isLoadingPlayerState = true;
        _nextPlayerStateLoadAttemptAt = Time.unscaledTime + PlayerStateLoadRetrySeconds;

        try
        {
            var state = await ApiClient.Instance.GetPlayerState();
            if (localPlayer == null)
            {
                return;
            }

            if (state != null && state.hasPosition)
            {
                var position = new Vector3(state.x, state.y, state.z);
                ApplyPlayerState(localPlayer, position, state.rotationY);
                _lastSavedPlayerPosition = position;
                _lastSavedPlayerRotationY = state.rotationY;
                _hasLastSavedPlayerState = true;
                DozzleLogger.Action("Player position restored", $"user={ShortKey(_playerStateUserId)};x={position.x:0.##};z={position.z:0.##}");
            }
            else
            {
                CaptureLastSavedPlayerState(localPlayer);
            }

            _hasLoadedPlayerState = true;
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Player position restore failed", ex);
        }
        finally
        {
            _isLoadingPlayerState = false;
        }
    }

    private void ApplyPlayerState(Transform localPlayer, Vector3 position, float rotationY)
    {
        if (localPlayer == null) return;

        var characterController = localPlayer.GetComponent<CharacterController>();
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        localPlayer.position = position;
        localPlayer.rotation = Quaternion.Euler(0f, rotationY, 0f);

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        _lastLocalPosition = position;
        _lastLocalPositionAt = Time.unscaledTime;
        _hasLastLocalPosition = true;
    }

    private void SavePlayerStateIfChanged(Transform localPlayer)
    {
        _nextPlayerStateSaveAt = Time.unscaledTime + PlayerStateSaveIntervalSeconds;
        if (localPlayer == null || ApiClient.Instance == null)
        {
            return;
        }

        Vector3 position = localPlayer.position;
        float rotationY = localPlayer.eulerAngles.y;
        if (_hasLastSavedPlayerState &&
            !PositionChangedEnough(_lastSavedPlayerPosition, position) &&
            RotationDelta(_lastSavedPlayerRotationY, rotationY) < PlayerStateRotationEpsilon)
        {
            return;
        }

        SavePlayerState(position, rotationY);
    }

    private async void SavePlayerState(Vector3 position, float rotationY)
    {
        if (_isSavingPlayerState || ApiClient.Instance == null)
        {
            return;
        }

        _isSavingPlayerState = true;
        try
        {
            await ApiClient.Instance.PatchPlayerState(position, rotationY);
            _lastSavedPlayerPosition = position;
            _lastSavedPlayerRotationY = rotationY;
            _hasLastSavedPlayerState = true;
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Player position save failed", ex);
        }
        finally
        {
            _isSavingPlayerState = false;
        }
    }

    private void CaptureLastSavedPlayerState(Transform localPlayer)
    {
        if (localPlayer == null) return;

        _lastSavedPlayerPosition = localPlayer.position;
        _lastSavedPlayerRotationY = localPlayer.eulerAngles.y;
        _hasLastSavedPlayerState = true;
    }

    private static bool PositionChangedEnough(Vector3 previous, Vector3 current)
    {
        return Vector3.Distance(previous, current) >= PlayerStatePositionEpsilon;
    }

    private static float RotationDelta(float previous, float current)
    {
        return Mathf.Abs(Mathf.DeltaAngle(previous, current));
    }

    private void ResetPlayerStateTracking()
    {
        _playerStateUserId = null;
        _hasLoadedPlayerState = false;
        _isLoadingPlayerState = false;
        _isSavingPlayerState = false;
        _hasLastSavedPlayerState = false;
        _nextPlayerStateLoadAttemptAt = 0f;
        _nextPlayerStateSaveAt = 0f;
    }

    private async void RefreshCharacterSelection()
    {
        if (_isLoadingCharacterProgression || Time.unscaledTime < _nextCharacterRefreshAt) return;

        var auth = NakamaAuthManager.Instance;
        if (auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession || ApiClient.Instance == null)
        {
            _selectedCharacterKey = DefaultCharacterKey;
            _selectedRobotColor = DefaultRobotColor;
            return;
        }

        _isLoadingCharacterProgression = true;
        _nextCharacterRefreshAt = Time.unscaledTime + CharacterRefreshIntervalSeconds;
        try
        {
            string json = await ApiClient.Instance.GetCharacterProgression();
            var progression = JsonUtility.FromJson<ApiClient.CharacterProgressionResponse>(json);
            if (progression?.profile != null)
            {
                _selectedCharacterKey = NormalizeText(progression.profile.selectedCharacterKey, DefaultCharacterKey);
                _selectedRobotColor = NormalizeText(progression.profile.robotColor, DefaultRobotColor);
                ApplyLocalRobotColor(_selectedRobotColor);
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Character progression refresh failed", ex);
        }
        finally
        {
            _isLoadingCharacterProgression = false;
        }
    }

    private void BroadcastLocalState()
    {
        if (_worldChannel == null || _isSending || Time.unscaledTime < _nextBroadcastAt) return;

        var auth = NakamaAuthManager.Instance;
        if (auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession || auth.Socket == null || !auth.IsConnectionReady || auth.Session == null) return;

        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null)
        {
            if (_localPlayerMissingLogCount < 3)
            {
                DozzleLogger.Error("World multiplayer local player missing", "No StarterAssetsInputs, Player tag, or CharacterController found.");
                _localPlayerMissingLogCount++;
            }
            return;
        }

        LocalAnimationState animationState = BuildLocalAnimationState(_localPlayer);
        _nextBroadcastAt = Time.unscaledTime + BroadcastInterval;
        SendLocalState(auth, _localPlayer.position, _localPlayer.eulerAngles.y, animationState);
    }

    private async void SendLocalState(NakamaAuthManager auth, Vector3 position, float rotationY, LocalAnimationState animationState)
    {
        _isSending = true;
        try
        {
            var payload = new WorldStatePayload
            {
                type = StateMessageType,
                room = WorldRoomName,
                clientId = ResolveClientInstanceId(),
                userId = auth.Session.UserId,
                username = ResolveUsername(auth),
                characterKey = _selectedCharacterKey,
                robotColor = _selectedRobotColor,
                px = position.x,
                py = position.y,
                pz = position.z,
                ry = rotationY,
                animation = 1,
                speed = animationState.speed,
                motionSpeed = animationState.motionSpeed,
                grounded = animationState.grounded,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            var sendTask = auth.Socket.WriteChatMessageAsync(_worldChannel.Id, JsonUtility.ToJson(payload));
            var completed = await Task.WhenAny(sendTask, Task.Delay(SocketOperationTimeoutMilliseconds));
            if (completed != sendTask)
            {
                ObserveBackgroundTask(sendTask);
                DozzleLogger.Error("World multiplayer state send timed out", $"room={WorldRoomName};timeoutMs={SocketOperationTimeoutMilliseconds};client={ShortClientId()}");
                return;
            }

            await sendTask;
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("World multiplayer state send failed", ex);
        }
        finally
        {
            _isSending = false;
        }
    }

    private LocalAnimationState BuildLocalAnimationState(Transform localPlayer)
    {
        var state = new LocalAnimationState { grounded = true };
        if (localPlayer == null) return state;

        Vector3 velocity = Vector3.zero;
        var characterController = localPlayer.GetComponent<CharacterController>();
        if (characterController != null)
        {
            velocity = characterController.velocity;
            state.grounded = characterController.isGrounded;
        }
        else
        {
            float now = Time.unscaledTime;
            if (_hasLastLocalPosition)
            {
                float deltaTime = Mathf.Max(now - _lastLocalPositionAt, 0.001f);
                velocity = (localPlayer.position - _lastLocalPosition) / deltaTime;
            }

            _lastLocalPosition = localPlayer.position;
            _lastLocalPositionAt = now;
            _hasLastLocalPosition = true;
        }

        velocity.y = 0f;
        float horizontalSpeed = velocity.magnitude;
        bool isMoving = horizontalSpeed > MovingVelocityThreshold;
        bool isRunning = horizontalSpeed >= RunVelocityThreshold;

        state.speed = isMoving ? (isRunning ? RunAnimationSpeed : WalkAnimationSpeed) : 0f;
        state.motionSpeed = isMoving ? 1f : 0f;
        return state;
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

    private void OnReceivedChannelMessage(IApiChannelMessage message)
    {
        if (message == null) return;

        try
        {
            var payload = JsonUtility.FromJson<WorldStatePayload>(message.Content);
            if (payload == null || payload.type != StateMessageType) return;
            if (!string.IsNullOrWhiteSpace(payload.room) && !string.Equals(payload.room, WorldRoomName, StringComparison.Ordinal)) return;
            if (string.IsNullOrWhiteSpace(payload.clientId) && string.IsNullOrWhiteSpace(payload.userId)) return;

            bool channelMatches = !string.IsNullOrWhiteSpace(_worldChannelId) && string.Equals(message.ChannelId, _worldChannelId, StringComparison.Ordinal);
            bool roomTaggedPayload = string.Equals(payload.room, WorldRoomName, StringComparison.Ordinal);
            if (!channelMatches && !roomTaggedPayload) return;

            lock (_pendingStatesLock)
            {
                _pendingStates.Add(payload);
            }
        }
        catch
        {
            // This socket also carries normal chat messages; ignore anything outside the world-state shape.
        }
    }

    private void FlushPendingStates()
    {
        WorldStatePayload[] states;
        lock (_pendingStatesLock)
        {
            if (_pendingStates.Count == 0) return;
            states = _pendingStates.ToArray();
            _pendingStates.Clear();
        }

        string localClientId = ResolveClientInstanceId();
        string localUserId = NakamaAuthManager.Instance?.Session?.UserId;
        foreach (var state in states)
        {
            if (state == null) continue;
            if (IsOwnState(state, localClientId, localUserId)) continue;
            ApplyRemoteState(state);
        }
    }

    private static bool IsOwnState(WorldStatePayload state, string localClientId, string localUserId)
    {
        if (state == null) return true;
        if (!string.IsNullOrWhiteSpace(state.clientId))
        {
            return string.Equals(state.clientId, localClientId, StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(localUserId) && string.Equals(state.userId, localUserId, StringComparison.Ordinal);
    }

    private void ApplyRemoteState(WorldStatePayload state)
    {
        string remoteKey = ResolveRemoteKey(state);
        if (string.IsNullOrWhiteSpace(remoteKey)) return;

        if (!_remotePlayers.TryGetValue(remoteKey, out var remote))
        {
            remote = CreateRemotePlayer(remoteKey, state);
            _remotePlayers[remoteKey] = remote;
        }

        remote.targetPosition = new Vector3(state.px, state.py, state.pz);
        remote.targetRotation = Quaternion.Euler(0f, state.ry, 0f);
        remote.lastSeenAt = Time.unscaledTime;
        ApplyRemoteAppearanceState(remote, state);
        ApplyRemoteAnimationState(remote, state);
        if (remote.nameLabel != null)
        {
            remote.nameLabel.text = string.IsNullOrWhiteSpace(state.username) ? "Player" : state.username;
        }
    }

    private void ApplyRemoteAppearanceState(RemotePlayer remote, WorldStatePayload state)
    {
        if (remote == null || state == null) return;

        string nextCharacterKey = NormalizeText(state.characterKey, DefaultCharacterKey);
        string nextRobotColor = NormalizeText(state.robotColor, DefaultRobotColor);
        if (remote.characterKey == nextCharacterKey && remote.robotColor == nextRobotColor) return;

        remote.characterKey = nextCharacterKey;
        remote.robotColor = nextRobotColor;
        ApplyRobotColor(remote.visual, nextRobotColor);
    }

    private void ApplyRemoteAnimationState(RemotePlayer remote, WorldStatePayload state)
    {
        if (remote == null || state == null) return;

        if (state.animation == 1)
        {
            remote.targetAnimationSpeed = Mathf.Max(0f, state.speed);
            remote.targetMotionSpeed = Mathf.Max(0f, state.motionSpeed);
            remote.grounded = state.grounded;
            return;
        }

        float inferredSpeed = InferRemoteAnimationSpeed(remote, new Vector3(state.px, state.py, state.pz));
        remote.targetAnimationSpeed = inferredSpeed;
        remote.targetMotionSpeed = inferredSpeed > 0f ? 1f : 0f;
        remote.grounded = true;
    }

    private static float InferRemoteAnimationSpeed(RemotePlayer remote, Vector3 targetPosition)
    {
        if (remote == null || remote.transform == null) return 0f;

        Vector3 delta = targetPosition - remote.transform.position;
        delta.y = 0f;
        if (delta.magnitude < 0.02f) return 0f;

        float estimatedSpeed = delta.magnitude / Mathf.Max(BroadcastInterval, 0.001f);
        return estimatedSpeed >= RunVelocityThreshold ? RunAnimationSpeed : WalkAnimationSpeed;
    }

    private RemotePlayer CreateRemotePlayer(string remoteKey, WorldStatePayload state)
    {
        var root = new GameObject($"RemotePlayer_{ShortKey(remoteKey)}");
        root.transform.position = new Vector3(state.px, state.py, state.pz);
        root.transform.rotation = Quaternion.Euler(0f, state.ry, 0f);

        GameObject visual = CreateRemoteRobotVisual(root.transform, remoteKey);
        bool usedRobotVisual = visual != null;
        if (!usedRobotVisual)
        {
            visual = CreateFallbackCapsule(root.transform, remoteKey);
        }

        string robotColor = NormalizeText(state.robotColor, DefaultRobotColor);
        ApplyRobotColor(visual, robotColor);

        var animator = root.GetComponentInChildren<Animator>(true);
        ConfigureRemoteAnimator(animator);

        var label = CreateNamePlate(root.transform, string.IsNullOrWhiteSpace(state.username) ? "Player" : state.username);
        DozzleLogger.Action("World multiplayer remote spawned", $"remote={ShortKey(remoteKey)};user={ShortKey(state.userId)};name={label.text};visual={(usedRobotVisual ? "robot" : "capsule")};character={NormalizeText(state.characterKey, DefaultCharacterKey)};color={robotColor};animator={(animator != null ? "yes" : "no")}");
        return new RemotePlayer
        {
            root = root,
            transform = root.transform,
            visual = visual,
            namePlate = label.transform,
            nameLabel = label,
            animator = animator,
            characterKey = NormalizeText(state.characterKey, DefaultCharacterKey),
            robotColor = robotColor,
            grounded = true,
            targetPosition = root.transform.position,
            targetRotation = root.transform.rotation,
            lastSeenAt = Time.unscaledTime,
        };
    }

    private GameObject CreateRemoteRobotVisual(Transform parent, string remoteKey)
    {
        Transform source = ResolveRemoteVisualSource();
        if (source == null)
        {
            return null;
        }

        var visual = Instantiate(source.gameObject, parent, false);
        visual.name = "RemoteRobotKyleVisual";
        visual.transform.localPosition = source == _localPlayer ? Vector3.zero : source.localPosition;
        visual.transform.localRotation = source == _localPlayer ? Quaternion.identity : source.localRotation;
        visual.transform.localScale = source.localScale;
        StripRemoteVisualComponents(visual);
        return visual;
    }

    private Transform ResolveRemoteVisualSource()
    {
        if (_remoteVisualSource != null)
        {
            return _remoteVisualSource;
        }

        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null)
        {
            return null;
        }

        _remoteVisualSource = FindChildByName(_localPlayer, "RobotKyle");
        if (_remoteVisualSource != null)
        {
            return _remoteVisualSource;
        }

        _remoteVisualSource = FindRenderableVisualRoot(_localPlayer);
        return _remoteVisualSource;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name)) return null;

        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindRenderableVisualRoot(Transform localPlayer)
    {
        if (localPlayer == null) return null;

        var renderers = localPlayer.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;

            Transform candidate = renderer.transform;
            while (candidate.parent != null && candidate.parent != localPlayer)
            {
                if (candidate.GetComponent<Animator>() != null)
                {
                    return candidate;
                }
                candidate = candidate.parent;
            }

            return candidate;
        }

        return null;
    }

    private void ApplyLocalRobotColor(string colorKey)
    {
        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null) return;

        Transform robot = FindChildByName(_localPlayer, "RobotKyle") ?? FindRenderableVisualRoot(_localPlayer);
        if (robot != null)
        {
            ApplyRobotColor(robot.gameObject, colorKey);
        }
    }

    private static void ApplyRobotColor(GameObject visual, string colorKey)
    {
        if (visual == null) return;

        Color color = ColorForRobotColor(colorKey);
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        bool appliedNamedMaterial = false;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null || !material.HasProperty("_Color")) continue;

                string materialName = material.name ?? string.Empty;
                bool looksLikeRobotMaterial = materialName.IndexOf("Kyle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              materialName.IndexOf("Robot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              renderer.name.IndexOf("Robot", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeRobotMaterial) continue;

                material.color = color;
                appliedNamedMaterial = true;
            }
        }

        if (appliedNamedMaterial) return;

        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            foreach (var material in renderer.materials)
            {
                if (material != null && material.HasProperty("_Color"))
                {
                    material.color = color;
                }
            }
        }
    }

    private static Color ColorForRobotColor(string colorKey)
    {
        switch (NormalizeText(colorKey, DefaultRobotColor).ToLowerInvariant())
        {
            case "blue": return new Color(0.22f, 0.48f, 0.95f, 1f);
            case "green": return new Color(0.22f, 0.76f, 0.36f, 1f);
            case "red": return new Color(0.88f, 0.20f, 0.16f, 1f);
            case "gold": return new Color(0.95f, 0.74f, 0.28f, 1f);
            default: return Color.white;
        }
    }

    private static void StripRemoteVisualComponents(GameObject visual)
    {
        if (visual == null) return;

        var components = visual.GetComponentsInChildren<Component>(true);
        for (int i = components.Length - 1; i >= 0; i--)
        {
            var component = components[i];
            if (component == null || component is Transform || component is Renderer || component is MeshFilter || component is Animator)
            {
                continue;
            }

            if (component is Collider || component is Rigidbody || component is CharacterController || component is Camera || component is AudioListener || component is MonoBehaviour)
            {
                Destroy(component);
            }
        }
    }

    private static void ConfigureRemoteAnimator(Animator animator)
    {
        NativeCharacterAnimationAdapter.Configure(animator, DefaultCharacterKey);
    }

    private static GameObject CreateFallbackCapsule(Transform parent, string remoteKey)
    {
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "RemoteCapsuleFallback";
        capsule.transform.SetParent(parent, false);
        capsule.transform.localPosition = Vector3.up;
        capsule.transform.localRotation = Quaternion.identity;

        var collider = capsule.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        var renderer = capsule.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = ColorForRemote(remoteKey);
        }

        return capsule;
    }

    private Text CreateNamePlate(Transform parent, string username)
    {
        var canvasObj = new GameObject("NamePlate", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObj.transform.SetParent(parent, false);
        canvasObj.transform.localPosition = new Vector3(0f, 1.85f, 0f);
        canvasObj.transform.localScale = Vector3.one * 0.01f;

        var canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rect = canvasObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(180f, 42f);

        var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
        textObj.transform.SetParent(canvasObj.transform, false);
        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textObj.GetComponent<Text>();
        text.text = username;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        var shadow = textObj.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private void UpdateRemotePlayers()
    {
        Camera camera = Camera.main;
        var expired = new List<string>();

        foreach (var pair in _remotePlayers)
        {
            var remote = pair.Value;
            if (remote == null || remote.root == null)
            {
                expired.Add(pair.Key);
                continue;
            }

            if (Time.unscaledTime - remote.lastSeenAt > RemoteTimeoutSeconds)
            {
                expired.Add(pair.Key);
                continue;
            }

            remote.transform.position = Vector3.Lerp(remote.transform.position, remote.targetPosition, Time.deltaTime * RemoteLerpSpeed);
            remote.transform.rotation = Quaternion.Slerp(remote.transform.rotation, remote.targetRotation, Time.deltaTime * RemoteLerpSpeed);
            UpdateRemoteAnimation(remote);

            if (camera != null && remote.namePlate != null)
            {
                remote.namePlate.rotation = Quaternion.LookRotation(remote.namePlate.position - camera.transform.position, Vector3.up);
            }
        }

        foreach (string remoteKey in expired)
        {
            if (_remotePlayers.TryGetValue(remoteKey, out var remote) && remote?.root != null)
            {
                Destroy(remote.root);
                DozzleLogger.Action("World multiplayer remote expired", $"remote={ShortKey(remoteKey)};timeoutSeconds={RemoteTimeoutSeconds}");
            }
            _remotePlayers.Remove(remoteKey);
        }
    }

    private static void UpdateRemoteAnimation(RemotePlayer remote)
    {
        if (remote == null || remote.animator == null) return;

        remote.animationSpeed = Mathf.Lerp(remote.animationSpeed, remote.targetAnimationSpeed, Time.deltaTime * RemoteAnimationLerpSpeed);
        if (remote.targetAnimationSpeed <= 0f && remote.animationSpeed < 0.05f)
        {
            remote.animationSpeed = 0f;
        }

        NativeCharacterAnimationAdapter.Apply(remote.animator, remote.characterKey, remote.animationSpeed, remote.targetMotionSpeed, remote.grounded);
    }

    private Transform ResolveLocalPlayer()
    {
        if (_localPlayer != null) return _localPlayer;

        var inputs = FindFirstObjectByType<StarterAssetsInputs>();
        if (inputs != null) return inputs.transform;

        var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null) return taggedPlayer.transform;

        var characterController = FindFirstObjectByType<CharacterController>();
        return characterController != null ? characterController.transform : null;
    }

    private string ResolveClientInstanceId()
    {
        string apiClientId = ApiClient.Instance?.ClientInstanceId;
        return string.IsNullOrWhiteSpace(apiClientId) ? _fallbackClientInstanceId : apiClientId;
    }

    private static string ResolveUsername(NakamaAuthManager auth)
    {
        if (auth?.Session == null) return "Player";
        if (!string.IsNullOrWhiteSpace(auth.Session.Username)) return auth.Session.Username;
        string userId = auth.Session.UserId;
        return string.IsNullOrWhiteSpace(userId) ? "Player" : $"player_{userId.Substring(0, Math.Min(6, userId.Length))}";
    }

    private static string ResolveRemoteKey(WorldStatePayload state)
    {
        if (state == null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(state.clientId)) return state.clientId;
        return state.userId ?? string.Empty;
    }

    private string ShortClientId()
    {
        return ShortKey(ResolveClientInstanceId());
    }

    private static string ShortKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return value.Length <= 8 ? value : value.Substring(0, 8);
    }

    private static string NormalizeText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static Color ColorForRemote(string value)
    {
        uint hash = 2166136261u;
        if (!string.IsNullOrWhiteSpace(value))
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
        }

        float hue = (hash % 360u) / 360f;
        var color = Color.HSVToRGB(hue, 0.58f, 0.95f);
        color.a = 1f;
        return color;
    }

    private void ResetWorldConnection(bool clearRemotes)
    {
        if (_socket != null)
        {
            _socket.ReceivedChannelMessage -= OnReceivedChannelMessage;
        }

        _socket = null;
        _worldChannel = null;
        _worldChannelId = null;
        _isJoining = false;
        _isSending = false;

        lock (_pendingStatesLock)
        {
            _pendingStates.Clear();
        }

        if (!clearRemotes) return;

        foreach (var remote in _remotePlayers.Values)
        {
            if (remote?.root != null) Destroy(remote.root);
        }
        _remotePlayers.Clear();
    }

    private class RemotePlayer
    {
        public GameObject root;
        public GameObject visual;
        public Transform transform;
        public Transform namePlate;
        public Text nameLabel;
        public Animator animator;
        public string characterKey;
        public string robotColor;
        public Vector3 targetPosition;
        public Quaternion targetRotation;
        public float animationSpeed;
        public float targetAnimationSpeed;
        public float targetMotionSpeed;
        public bool grounded;
        public float lastSeenAt;
    }

    private class LocalAnimationState
    {
        public float speed;
        public float motionSpeed;
        public bool grounded;
    }

    [Serializable]
    private class WorldStatePayload
    {
        public string type;
        public string room;
        public string clientId;
        public string userId;
        public string username;
        public string characterKey;
        public string robotColor;
        public float px;
        public float py;
        public float pz;
        public float ry;
        public int animation;
        public float speed;
        public float motionSpeed;
        public bool grounded;
        public long timestamp;
    }
}
