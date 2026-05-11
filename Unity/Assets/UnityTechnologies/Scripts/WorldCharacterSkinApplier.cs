using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class WorldCharacterSkinApplier : MonoBehaviour
{
    private const string DefaultCharacterKey = "robot_kyle";
    private const string DefaultRobotColor = "default";
    private const string LocalOverrideRootName = "SelectedCharacterVisualRoot";
    private const string LocalSelectedCharacterVisualPrefix = "SelectedCharacterVisual_";
    private const string RemoteRobotVisualName = "RemoteRobotKyleVisual";
    private const string RemoteFallbackVisualName = "RemoteCapsuleFallback";
    private const string RemoteCharacterVisualPrefix = "RemoteCharacterVisual_";
    private const float RefreshIntervalSeconds = 0.25f;

    private NakamaWorldMultiplayerController _world;
    private FieldInfo _selectedCharacterKeyField;
    private FieldInfo _selectedRobotColorField;
    private FieldInfo _remotePlayersField;
    private Transform _localPlayer;
    private Transform _localRobotVisual;
    private Transform _localOverrideRoot;
    private GameObject _localOverrideVisual;
    private string _localAppliedKey;
    private float _nextRefreshAt;
    private readonly HashSet<string> _diagnosticLogs = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<WorldCharacterSkinApplier>(FindObjectsInactive.Include) != null) return;

        var obj = new GameObject("WorldCharacterSkinApplier");
        DontDestroyOnLoad(obj);
        obj.AddComponent<WorldCharacterSkinApplier>();
    }

    public void ForceApplyNow()
    {
        _nextRefreshAt = 0f;
        ResolveWorldController();
        ApplyLocalAppearance();
        ApplyRemoteAppearances();
    }

    public void ApplyLocalSelectionNow(string characterKey, string robotColor)
    {
        _nextRefreshAt = 0f;
        ResolveWorldController();
        ApplyLocalAppearance(NormalizeText(characterKey, DefaultCharacterKey), NormalizeText(robotColor, DefaultRobotColor));
        ApplyRemoteAppearances();
    }

    private void Update()
    {
        DriveLocalOverrideAnimation(_localAppliedKey);
        if (Time.unscaledTime < _nextRefreshAt) return;

        _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        ResolveWorldController();
        ApplyLocalAppearance();
        ApplyRemoteAppearances();
    }

    private void ResolveWorldController()
    {
        if (_world != null) return;

        _world = FindFirstObjectByType<NakamaWorldMultiplayerController>(FindObjectsInactive.Include);
        if (_world == null)
        {
            LogOnce("world-missing", "Character visual world missing", "No NakamaWorldMultiplayerController found.");
            return;
        }

        Type type = _world.GetType();
        _selectedCharacterKeyField = type.GetField("_selectedCharacterKey", BindingFlags.Instance | BindingFlags.NonPublic);
        _selectedRobotColorField = type.GetField("_selectedRobotColor", BindingFlags.Instance | BindingFlags.NonPublic);
        _remotePlayersField = type.GetField("_remotePlayers", BindingFlags.Instance | BindingFlags.NonPublic);
        DozzleLogger.Action("Character visual world resolved", $"selectedField={(_selectedCharacterKeyField != null ? "yes" : "no")};colorField={(_selectedRobotColorField != null ? "yes" : "no")};remotesField={(_remotePlayersField != null ? "yes" : "no")}");
    }

    private void ApplyLocalAppearance()
    {
        if (_world == null) return;

        string characterKey = ReadStringField(_world, _selectedCharacterKeyField, DefaultCharacterKey);
        string robotColor = ReadStringField(_world, _selectedRobotColorField, DefaultRobotColor);
        ApplyLocalAppearance(characterKey, robotColor);
    }

    private void ApplyLocalAppearance(string characterKey, string robotColor)
    {
        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null)
        {
            LogOnce("local-player-missing", "Local character visual skipped", $"reason=no-local-player;character={characterKey}");
            return;
        }

        _localRobotVisual = ResolveLocalRobotVisual();
        bool changed = !string.Equals(_localAppliedKey, characterKey, StringComparison.Ordinal);

        if (IsRobotCharacter(characterKey))
        {
            DestroyLocalOverride();
            SetLocalRobotVisualEnabled(true);
            ApplyRobotColor(_localRobotVisual != null ? _localRobotVisual.gameObject : null, robotColor);
            _localAppliedKey = characterKey;
            if (changed)
            {
                DozzleLogger.Action("Local character visual robot applied", $"character={characterKey};robotColor={robotColor};robotVisual={ObjectName(_localRobotVisual)}");
            }
            return;
        }

        GameObject prefab = CharacterPrefabCatalog.ResolvePrefab(characterKey);
        if (prefab == null)
        {
            DestroyLocalOverride();
            SetLocalRobotVisualEnabled(true);
            ApplyRobotColor(_localRobotVisual != null ? _localRobotVisual.gameObject : null, robotColor);
            _localAppliedKey = DefaultCharacterKey;
            DozzleLogger.Action("Local character visual fallback", $"reason=prefab-not-found;character={characterKey};robotColor={robotColor}");
            return;
        }

        if (_localOverrideVisual == null || !string.Equals(_localAppliedKey, characterKey, StringComparison.Ordinal))
        {
            DestroyLocalOverride();
            Transform mount = ResolveLocalOverrideRoot();
            if (mount == null)
            {
                SetLocalRobotVisualEnabled(true);
                _localAppliedKey = DefaultCharacterKey;
                DozzleLogger.Action("Local character visual fallback", $"reason=no-mount;character={characterKey};prefab={prefab.name}");
                return;
            }

            try
            {
                GameObject replacement = InstantiateCharacterVisual(prefab, mount, $"SelectedCharacterVisual_{characterKey}", characterKey);
                if (!HasRenderableVisual(replacement))
                {
                    SetVisualRenderersEnabled(replacement, false);
                    Destroy(replacement);
                    throw new InvalidOperationException($"Character prefab {prefab.name} has no renderers after setup.");
                }

                _localOverrideVisual = replacement;
                _localAppliedKey = characterKey;
                DozzleLogger.Action("Local character visual applied", $"character={characterKey};prefab={prefab.name}");
            }
            catch (Exception ex)
            {
                DestroyLocalOverride();
                SetLocalRobotVisualEnabled(true);
                _localAppliedKey = DefaultCharacterKey;
                DozzleLogger.Error("Local character visual apply failed", ex);
                return;
            }
        }

        SetLocalRobotVisualEnabled(_localOverrideVisual == null);
        DriveLocalOverrideAnimation(characterKey);
    }

    private void DriveLocalOverrideAnimation(string characterKey)
    {
        if (_localOverrideVisual == null || _localPlayer == null) return;

        Animator animator = _localOverrideVisual.GetComponentInChildren<Animator>(true);
        if (animator == null) return;

        float speed = 0f;
        float motionSpeed = 0f;
        bool grounded = true;
        var controller = _localPlayer.GetComponent<CharacterController>();
        if (controller != null)
        {
            Vector3 horizontalVelocity = controller.velocity;
            horizontalVelocity.y = 0f;
            speed = horizontalVelocity.magnitude;
            motionSpeed = speed > 0.08f ? 1f : 0f;
            grounded = controller.isGrounded;
        }

        NativeCharacterAnimationAdapter.Apply(animator, characterKey, speed, motionSpeed, grounded);
    }

    private void ApplyRemoteAppearances()
    {
        if (_world == null || _remotePlayersField == null) return;
        if (!(_remotePlayersField.GetValue(_world) is IDictionary remotes)) return;

        foreach (DictionaryEntry entry in remotes)
        {
            object remote = entry.Value;
            if (remote == null) continue;

            Type remoteType = remote.GetType();
            FieldInfo rootField = remoteType.GetField("root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo visualField = remoteType.GetField("visual", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo animatorField = remoteType.GetField("animator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo characterKeyField = remoteType.GetField("characterKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo robotColorField = remoteType.GetField("robotColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var root = rootField?.GetValue(remote) as GameObject;
            var visual = visualField?.GetValue(remote) as GameObject;
            string characterKey = ReadStringField(remote, characterKeyField, DefaultCharacterKey);
            string robotColor = ReadStringField(remote, robotColorField, DefaultRobotColor);
            if (root == null) continue;

            if (IsRobotCharacter(characterKey))
            {
                GameObject robotVisual = EnsureRemoteRobotVisual(root, visual, robotColor, entry.Key);
                visualField?.SetValue(remote, robotVisual);
                var robotAnimator = robotVisual != null ? robotVisual.GetComponentInChildren<Animator>(true) : null;
                NativeCharacterAnimationAdapter.Configure(robotAnimator, DefaultCharacterKey);
                animatorField?.SetValue(remote, robotAnimator);
                continue;
            }

            GameObject prefab = CharacterPrefabCatalog.ResolvePrefab(characterKey);
            if (prefab == null)
            {
                DozzleLogger.Action("Remote character visual fallback", $"remote={entry.Key};reason=prefab-not-found;character={characterKey};robotColor={robotColor}");
                continue;
            }

            if (visual != null && visual.name.StartsWith($"{RemoteCharacterVisualPrefix}{characterKey}", StringComparison.Ordinal))
            {
                SetVisualRenderersEnabled(visual, true);
                RemoveRemoteVisualSiblings(root.transform, visual.transform);
                animatorField?.SetValue(remote, visual.GetComponentInChildren<Animator>(true));
                continue;
            }

            if (visual != null)
            {
                DisableAndDestroyVisual(visual);
            }

            GameObject replacement = InstantiateCharacterVisual(prefab, root.transform, $"{RemoteCharacterVisualPrefix}{characterKey}", characterKey);
            RemoveRemoteVisualSiblings(root.transform, replacement.transform);
            visualField?.SetValue(remote, replacement);
            var animator = replacement.GetComponentInChildren<Animator>(true);
            animatorField?.SetValue(remote, animator);
            DozzleLogger.Action("Remote character visual applied", $"remote={entry.Key};character={characterKey};prefab={prefab.name};animator={(animator != null ? "yes" : "no")}");
        }
    }

    private GameObject EnsureRemoteRobotVisual(GameObject root, GameObject currentVisual, string robotColor, object remoteKey)
    {
        if (root == null) return currentVisual;

        GameObject robotVisual = null;
        bool created = false;

        if (IsRemoteRobotVisual(currentVisual))
        {
            robotVisual = currentVisual;
        }
        else if (currentVisual != null)
        {
            DisableAndDestroyVisual(currentVisual);
        }

        if (robotVisual == null)
        {
            RemoveRemoteVisualSiblings(root.transform, null);
            robotVisual = CreateRemoteRobotVisual(root.transform);
            if (robotVisual == null)
            {
                robotVisual = CreateRemoteFallbackCapsule(root.transform);
            }
            created = true;
        }

        if (robotVisual != null)
        {
            robotVisual.SetActive(true);
            StripGameplayComponents(robotVisual);
            SetUntaggedRecursively(robotVisual);
            SetLayerRecursively(robotVisual, root.layer);
            SetVisualRenderersEnabled(robotVisual, true);
            ApplyRobotColor(robotVisual, robotColor);
            RemoveRemoteVisualSiblings(root.transform, robotVisual.transform);
        }

        if (created)
        {
            DozzleLogger.Action("Remote robot visual restored", $"remote={remoteKey};robotColor={robotColor};visual={(robotVisual != null ? robotVisual.name : "none")}");
        }
        return robotVisual;
    }

    private GameObject CreateRemoteRobotVisual(Transform parent)
    {
        if (parent == null) return null;

        Transform source = ResolveRemoteRobotVisualSource();
        if (source == null) return null;

        var visual = Instantiate(source.gameObject, parent, false);
        visual.name = RemoteRobotVisualName;
        visual.transform.localPosition = source == _localPlayer ? Vector3.zero : source.localPosition;
        visual.transform.localRotation = source == _localPlayer ? Quaternion.identity : source.localRotation;
        visual.transform.localScale = source.localScale;
        visual.SetActive(true);
        RemoveRuntimeCharacterCloneChildren(visual.transform);
        StripGameplayComponents(visual);
        SetUntaggedRecursively(visual);
        SetLayerRecursively(visual, parent != null ? parent.gameObject.layer : visual.layer);
        SetVisualRenderersEnabled(visual, true);
        return visual;
    }

    private Transform ResolveRemoteRobotVisualSource()
    {
        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null)
        {
            return IsRobotVisualSource(_localRobotVisual) ? _localRobotVisual : null;
        }

        Transform explicitRobot = FindChildByName(_localPlayer, "RobotKyle");
        if (IsRobotVisualSource(explicitRobot))
        {
            _localRobotVisual = explicitRobot;
            return _localRobotVisual;
        }

        return IsRobotVisualSource(_localRobotVisual) ? _localRobotVisual : null;
    }

    private static bool IsRobotVisualSource(Transform source)
    {
        if (source == null) return false;

        string name = source.name ?? string.Empty;
        return string.Equals(name, "RobotKyle", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("RobotKyle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Robot Kyle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("KyleRobot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GameObject CreateRemoteFallbackCapsule(Transform parent)
    {
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = RemoteFallbackVisualName;
        capsule.transform.SetParent(parent, false);
        capsule.transform.localPosition = Vector3.up;
        capsule.transform.localRotation = Quaternion.identity;
        capsule.transform.localScale = Vector3.one;

        var collider = capsule.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        SetUntaggedRecursively(capsule);
        SetLayerRecursively(capsule, parent != null ? parent.gameObject.layer : capsule.layer);
        return capsule;
    }

    private static bool IsRemoteCharacterOverride(GameObject visual)
    {
        return visual != null && visual.name.StartsWith(RemoteCharacterVisualPrefix, StringComparison.Ordinal);
    }

    private static bool IsRemoteRobotVisual(GameObject visual)
    {
        if (visual == null) return false;

        string name = visual.name ?? string.Empty;
        return string.Equals(name, RemoteRobotVisualName, StringComparison.Ordinal) ||
               string.Equals(name, RemoteFallbackVisualName, StringComparison.Ordinal);
    }

    private static void RemoveRemoteVisualSiblings(Transform root, Transform activeVisual)
    {
        if (root == null) return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null || child == activeVisual || (activeVisual != null && child.IsChildOf(activeVisual))) continue;
            if (!IsRemoteVisualRoot(child)) continue;
            DisableAndDestroyVisual(child.gameObject);
        }
    }

    private static bool IsRemoteVisualRoot(Transform child)
    {
        if (child == null) return false;

        string name = child.name ?? string.Empty;
        if (name.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (child.GetComponentInChildren<Text>(true) != null) return false;
        if (child.GetComponentInChildren<Canvas>(true) != null) return false;

        return string.Equals(name, RemoteRobotVisualName, StringComparison.Ordinal) ||
               string.Equals(name, RemoteFallbackVisualName, StringComparison.Ordinal) ||
               name.StartsWith(RemoteCharacterVisualPrefix, StringComparison.Ordinal) ||
               HasRenderableVisual(child.gameObject);
    }

    private static void DisableAndDestroyVisual(GameObject visual)
    {
        if (visual == null) return;

        SetVisualRenderersEnabled(visual, false);
        foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
        {
            if (animator != null) animator.enabled = false;
        }
        foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null) behaviour.enabled = false;
        }
        visual.SetActive(false);
        Destroy(visual);
    }

    private static void RemoveRuntimeCharacterCloneChildren(Transform visualRoot)
    {
        if (visualRoot == null) return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            if (child == null) continue;

            string name = child.name ?? string.Empty;
            bool isRuntimeCharacterVisual =
                string.Equals(name, LocalOverrideRootName, StringComparison.Ordinal) ||
                name.StartsWith(LocalSelectedCharacterVisualPrefix, StringComparison.Ordinal) ||
                name.StartsWith(RemoteCharacterVisualPrefix, StringComparison.Ordinal);

            if (isRuntimeCharacterVisual)
            {
                DisableAndDestroyVisual(child.gameObject);
            }
        }
    }

    private GameObject InstantiateCharacterVisual(GameObject prefab, Transform parent, string name, string characterKey)
    {
        var visual = Instantiate(prefab, parent, false);
        visual.name = name;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        visual.SetActive(true);
        StripGameplayComponents(visual);
        SetUntaggedRecursively(visual);
        SetLayerRecursively(visual, parent != null ? parent.gameObject.layer : visual.layer);
        SetVisualRenderersEnabled(visual, true);
        CharacterPrefabCatalog.ConfigureAnimatorForCharacter(characterKey, visual.GetComponentInChildren<Animator>(true));
        return visual;
    }

    private Transform ResolveLocalOverrideRoot()
    {
        if (_localPlayer == null) return null;
        if (_localOverrideRoot != null && _localOverrideRoot.parent == _localPlayer) return _localOverrideRoot;

        var existing = _localPlayer.Find(LocalOverrideRootName);
        if (existing != null)
        {
            _localOverrideRoot = existing;
            return _localOverrideRoot;
        }

        var root = new GameObject(LocalOverrideRootName);
        root.transform.SetParent(_localPlayer, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        _localOverrideRoot = root.transform;
        return _localOverrideRoot;
    }

    private Transform ResolveLocalRobotVisual()
    {
        if (_localPlayer == null) return _localRobotVisual;

        Transform explicitRobot = FindChildByName(_localPlayer, "RobotKyle");
        if (IsRobotVisualSource(explicitRobot))
        {
            _localRobotVisual = explicitRobot;
            return _localRobotVisual;
        }

        if (IsRobotVisualSource(_localRobotVisual))
        {
            return _localRobotVisual;
        }

        _localRobotVisual = null;
        return null;
    }

    private void SetLocalRobotVisualEnabled(bool enabled)
    {
        if (_localRobotVisual == null) return;
        foreach (var renderer in _localRobotVisual.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            if (_localOverrideRoot != null && renderer.transform.IsChildOf(_localOverrideRoot)) continue;
            renderer.enabled = enabled;
        }
    }

    private void DestroyLocalOverride()
    {
        if (_localOverrideRoot != null)
        {
            for (int i = _localOverrideRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = _localOverrideRoot.GetChild(i).gameObject;
                SetVisualRenderersEnabled(child, false);
                Destroy(child);
            }
        }

        _localOverrideVisual = null;
    }

    private static void StripGameplayComponents(GameObject visual)
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
                continue;
            }
        }
    }

    private Transform ResolveLocalPlayer()
    {
        if (_localPlayer != null) return _localPlayer;

        var inputs = FindFirstObjectByType<StarterAssetsInputs>(FindObjectsInactive.Include);
        if (inputs != null) return inputs.transform;

        var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null) return taggedPlayer.transform;

        var characterController = FindFirstObjectByType<CharacterController>(FindObjectsInactive.Include);
        return characterController != null ? characterController.transform : null;
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

    private static void SetVisualRenderersEnabled(GameObject visual, bool enabled)
    {
        if (visual == null) return;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            renderer.enabled = enabled;
        }
    }

    private static void SetUntaggedRecursively(GameObject obj)
    {
        if (obj == null) return;

        try
        {
            obj.tag = "Untagged";
        }
        catch
        {
            // Unusual tag setups should not block render-only isolation.
        }

        foreach (Transform child in obj.transform)
        {
            if (child != null)
            {
                SetUntaggedRecursively(child.gameObject);
            }
        }
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;

        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            if (child != null)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }

    private static bool HasRenderableVisual(GameObject visual)
    {
        if (visual == null) return false;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            return true;
        }

        return false;
    }

    private static void ApplyRobotColor(GameObject visual, string colorKey)
    {
        if (visual == null) return;

        Color color = ColorForRobotColor(colorKey);
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
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

    private static bool IsRobotCharacter(string characterKey)
    {
        string normalized = NormalizeText(characterKey, DefaultCharacterKey).ToLowerInvariant();
        return normalized == "robot_kyle" || normalized == "robot_blue" || normalized == "robot_green" || normalized == "robot_red";
    }

    private static string ReadStringField(object target, FieldInfo field, string fallback)
    {
        if (target == null || field == null) return fallback;
        return NormalizeText(field.GetValue(target) as string, fallback);
    }

    private void LogOnce(string key, string action, string details)
    {
        if (_diagnosticLogs.Contains(key)) return;

        _diagnosticLogs.Add(key);
        DozzleLogger.Action(action, details);
    }

    private static string ObjectName(UnityEngine.Object obj)
    {
        return obj != null ? obj.name : "none";
    }

    private static string NormalizeText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public static class NativeCharacterAnimationAdapter
{
    private const float RunVelocityThreshold = 3.2f;

    private static readonly string[] SpeedFloatNames = { "Speed", "Velocity", "MoveSpeed", "MovementSpeed", "ForwardSpeed", "Blend" };
    private static readonly string[] MotionFloatNames = { "MotionSpeed", "Motion", "InputMagnitude" };
    private static readonly string[] MovingBoolNames = { "IsMoving", "Moving", "Move" };
    private static readonly string[] RunBoolNames = { "Run", "Running", "IsRunning" };
    private static readonly string[] GroundedBoolNames = { "Grounded", "IsGrounded" };
    private static readonly string[] JumpBoolNames = { "Jump", "Jumping", "IsJumping" };
    private static readonly string[] FreeFallBoolNames = { "FreeFall", "Falling", "IsFalling" };

    private static readonly Dictionary<int, ParameterCache> Caches = new Dictionary<int, ParameterCache>();

    public static void Configure(Animator animator, string characterKey)
    {
        ConfigureBaseAnimator(animator);
        if (animator == null) return;

        GetCache(animator);
        Apply(animator, characterKey, 0f, 0f, true);
    }

    public static void Apply(Animator animator, string characterKey, float speed, float motionSpeed, bool grounded)
    {
        if (animator == null) return;

        var cache = GetCache(animator);
        float rawSpeed = Mathf.Max(0f, speed);
        float normalizedMotion = Mathf.Clamp01(Mathf.Max(motionSpeed, rawSpeed > 0.08f ? 1f : 0f));
        bool moving = rawSpeed > 0.08f || normalizedMotion > 0.08f;
        bool running = rawSpeed >= RunVelocityThreshold;

        SetFloat(animator, cache.SpeedFloatHashes, rawSpeed);
        SetFloat(animator, cache.MotionFloatHashes, normalizedMotion);
        SetBool(animator, cache.MovingBoolHashes, moving);
        SetBool(animator, cache.RunBoolHashes, moving || running);
        SetBool(animator, cache.GroundedBoolHashes, grounded);
        SetBool(animator, cache.JumpBoolHashes, false);
        SetBool(animator, cache.FreeFallBoolHashes, !grounded);
    }

    private static void ConfigureBaseAnimator(Animator animator)
    {
        if (animator == null) return;
        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private static ParameterCache GetCache(Animator animator)
    {
        int id = animator.GetInstanceID();
        if (Caches.TryGetValue(id, out var cache)) return cache;

        cache = new ParameterCache();
        foreach (var parameter in animator.parameters)
        {
            if (parameter == null) continue;
            if (parameter.type == AnimatorControllerParameterType.Float)
            {
                if (Matches(parameter.name, SpeedFloatNames)) cache.SpeedFloatHashes.Add(parameter.nameHash);
                if (Matches(parameter.name, MotionFloatNames)) cache.MotionFloatHashes.Add(parameter.nameHash);
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                if (Matches(parameter.name, MovingBoolNames)) cache.MovingBoolHashes.Add(parameter.nameHash);
                if (Matches(parameter.name, RunBoolNames)) cache.RunBoolHashes.Add(parameter.nameHash);
                if (Matches(parameter.name, GroundedBoolNames)) cache.GroundedBoolHashes.Add(parameter.nameHash);
                if (Matches(parameter.name, JumpBoolNames)) cache.JumpBoolHashes.Add(parameter.nameHash);
                if (Matches(parameter.name, FreeFallBoolNames)) cache.FreeFallBoolHashes.Add(parameter.nameHash);
            }
        }

        Caches[id] = cache;
        return cache;
    }

    private static bool Matches(string value, string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static void SetFloat(Animator animator, List<int> hashes, float value)
    {
        for (int i = 0; i < hashes.Count; i++)
        {
            try { animator.SetFloat(hashes[i], value); } catch { }
        }
    }

    private static void SetBool(Animator animator, List<int> hashes, bool value)
    {
        for (int i = 0; i < hashes.Count; i++)
        {
            try { animator.SetBool(hashes[i], value); } catch { }
        }
    }

    private class ParameterCache
    {
        public readonly List<int> SpeedFloatHashes = new List<int>();
        public readonly List<int> MotionFloatHashes = new List<int>();
        public readonly List<int> MovingBoolHashes = new List<int>();
        public readonly List<int> RunBoolHashes = new List<int>();
        public readonly List<int> GroundedBoolHashes = new List<int>();
        public readonly List<int> JumpBoolHashes = new List<int>();
        public readonly List<int> FreeFallBoolHashes = new List<int>();
    }
}

public static class CharacterPrefabCatalog
{
    private static CharacterPrefabReferences _references;
    private static bool _referencesLoadAttempted;
    private static bool _referencesLoadLogged;
    private static readonly HashSet<string> ResolutionLogs = new HashSet<string>();

    public static GameObject ResolvePrefab(string characterKey)
    {
        string key = string.IsNullOrWhiteSpace(characterKey) ? "robot_kyle" : characterKey.Trim();
        if (IsRobotCharacter(key)) return null;

        var direct = ResolveFromReferences(key);
        if (direct != null)
        {
            LogResolution(key, "references", direct);
            return direct;
        }

        var resource = Resources.Load<GameObject>($"CharacterPrefabs/{SlotNameForKey(key)}");
        if (resource != null)
        {
            LogResolution(key, "resources", resource);
            return resource;
        }

#if UNITY_EDITOR
        var editorPrefab = ResolveFromAssetDatabase(key);
        if (editorPrefab != null)
        {
            LogResolution(key, "editor", editorPrefab);
            return editorPrefab;
        }
#endif

        var loaded = ResolveLoadedPrefab(key);
        if (loaded != null)
        {
            LogResolution(key, "loaded", loaded);
            return loaded;
        }

        LogResolution(key, "failed", null);
        return null;
    }

    private static GameObject ResolveFromReferences(string key)
    {
        if (!_referencesLoadAttempted)
        {
            _referencesLoadAttempted = true;
            _references = Resources.Load<CharacterPrefabReferences>("CharacterPrefabReferences");
            LogReferenceLoad();
        }

        if (_references == null) return null;

        switch (key)
        {
            case "ghost_character": return _references.ghostCharacterPrefab;
            case "skeleton": return _references.skeletonPrefab;
            case "tiny_hero":
            case "tiny_hero_male": return _references.tinyHeroMalePbrPrefab != null ? _references.tinyHeroMalePbrPrefab : _references.tinyHeroPrefab;
            case "tiny_hero_female": return _references.tinyHeroFemalePbrPrefab != null ? _references.tinyHeroFemalePbrPrefab : _references.tinyHeroPrefab;
            case "robot_hero": return _references.robotHeroPrefab;
            case "scifi_hp_character": return _references.sciFiHpCharacterPrefab;
            case "scifi_pbr_character": return _references.sciFiPbrCharacterPrefab;
            case "scifi_polyart_character": return _references.sciFiPolyartCharacterPrefab;
            default: return null;
        }
    }

    private static string SlotNameForKey(string key)
    {
        switch (key)
        {
            case "ghost_character": return "GhostCharacter";
            case "skeleton": return "StylizedLowPolySkeleton";
            case "tiny_hero":
            case "tiny_hero_male": return "MaleCharacterPBR";
            case "tiny_hero_female": return "FemaleCharacterPBR";
            case "robot_hero": return "RobotHero";
            case "scifi_hp_character": return "HPCharacter";
            case "scifi_pbr_character": return "PBRCharacter";
            case "scifi_polyart_character": return "PolyartCharacter";
            default: return key;
        }
    }

    private static GameObject ResolveLoadedPrefab(string key)
    {
        foreach (string slot in CandidateNamesForKey(key))
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null) continue;
                if (!candidate.scene.IsValid() && candidate.name.IndexOf(slot, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void LogResolution(string key, string source, GameObject prefab)
    {
        string logKey = $"{key}:{source}";
        if (ResolutionLogs.Contains(logKey)) return;

        ResolutionLogs.Add(logKey);
        DozzleLogger.Action("Character prefab resolution", $"character={key};source={source};prefab={(prefab != null ? prefab.name : "none")}");
    }

    private static void LogReferenceLoad()
    {
        if (_referencesLoadLogged) return;

        _referencesLoadLogged = true;
        if (_references == null)
        {
            DozzleLogger.Action("Character prefab references loaded", "loaded=no;resource=CharacterPrefabReferences");
            return;
        }

        DozzleLogger.Action("Character prefab references loaded",
            $"loaded=yes;ghost={PrefabState(_references.ghostCharacterPrefab)};skeleton={PrefabState(_references.skeletonPrefab)};tinyMale={PrefabState(_references.tinyHeroMalePbrPrefab)};tinyFemale={PrefabState(_references.tinyHeroFemalePbrPrefab)};sciFiHp={PrefabState(_references.sciFiHpCharacterPrefab)};sciFiPbr={PrefabState(_references.sciFiPbrCharacterPrefab)};sciFiPolyart={PrefabState(_references.sciFiPolyartCharacterPrefab)}");
    }

    private static string PrefabState(GameObject prefab)
    {
        return prefab != null ? prefab.name : "null";
    }

    private static bool IsRobotCharacter(string characterKey)
    {
        string normalized = string.IsNullOrWhiteSpace(characterKey) ? "robot_kyle" : characterKey.Trim().ToLowerInvariant();
        return normalized == "robot_kyle" || normalized == "robot_blue" || normalized == "robot_green" || normalized == "robot_red";
    }

    private static string[] CandidateNamesForKey(string key)
    {
        switch (key)
        {
            case "tiny_hero":
            case "tiny_hero_male": return new[] { "MaleCharacterPBR", "MaleCharacterPolyart", "RpgTinyHero" };
            case "tiny_hero_female": return new[] { "FemaleCharacterPBR", "FemaleCharacterPolyart" };
            case "skeleton": return new[] { "StylizedLowPolySkeleton", "Skeleton" };
            case "robot_hero": return new[] { "RobotHero", "Robot Hero" };
            case "scifi_hp_character": return new[] { "HPCharacter" };
            case "scifi_pbr_character": return new[] { "PBRCharacter" };
            case "scifi_polyart_character": return new[] { "PolyartCharacter" };
            default: return new[] { SlotNameForKey(key) };
        }
    }

    public static void ConfigureAnimatorForCharacter(string characterKey, Animator animator)
    {
        NativeCharacterAnimationAdapter.Configure(animator, characterKey);
    }

#if UNITY_EDITOR
    private static GameObject ResolveFromAssetDatabase(string key)
    {
        switch (key)
        {
            case "ghost_character":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GhostCharacter_Free/Prefabs/Ghost.prefab");
            case "tiny_hero":
            case "tiny_hero_male":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab")
                    ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPolyart.prefab");
            case "tiny_hero_female":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Hero Duo/Prefab/FemaleCharacterPBR.prefab")
                    ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Hero Duo/Prefab/FemaleCharacterPolyart.prefab");
            case "skeleton":
                return FindPrefabBySearch("Skeleton", new[] { "Assets/SazenGames/Skeleton", "Assets" });
            case "robot_hero":
                return FindPrefabBySearch("Robot Hero", new[] { "Assets" });
            case "scifi_hp_character":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/HPCharacter.prefab")
                    ?? FindPrefabBySearch("HPCharacter", new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });
            case "scifi_pbr_character":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab")
                    ?? FindPrefabBySearch("PBRCharacter", new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });
            case "scifi_polyart_character":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SciFiWarriorPBRHPPolyart/Prefabs/PolyartCharacter.prefab")
                    ?? FindPrefabBySearch("PolyartCharacter", new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });
            default:
                return null;
        }
    }

    private static GameObject FindPrefabBySearch(string query, string[] folders)
    {
        string[] guids = AssetDatabase.FindAssets($"{query} t:Prefab", folders);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
        }

        return null;
    }
#endif
}
