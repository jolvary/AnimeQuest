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
        if (_world == null) return;

        Type type = _world.GetType();
        _selectedCharacterKeyField = type.GetField("_selectedCharacterKey", BindingFlags.Instance | BindingFlags.NonPublic);
        _selectedRobotColorField = type.GetField("_selectedRobotColor", BindingFlags.Instance | BindingFlags.NonPublic);
        _remotePlayersField = type.GetField("_remotePlayers", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void ApplyLocalAppearance()
    {
        if (_world == null) return;

        string characterKey = ReadStringField(_world, _selectedCharacterKeyField, DefaultCharacterKey);
        string robotColor = ReadStringField(_world, _selectedRobotColorField, DefaultRobotColor);
        _localPlayer = ResolveLocalPlayer();
        if (_localPlayer == null) return;

        _localRobotVisual = _localRobotVisual != null ? _localRobotVisual : FindChildByName(_localPlayer, "RobotKyle") ?? FindRenderableVisualRoot(_localPlayer);

        if (IsRobotCharacter(characterKey))
        {
            DestroyLocalOverride();
            SetLocalRobotVisualEnabled(true);
            ApplyRobotColor(_localRobotVisual != null ? _localRobotVisual.gameObject : null, robotColor);
            _localAppliedKey = characterKey;
            return;
        }

        GameObject prefab = CharacterPrefabCatalog.ResolvePrefab(characterKey);
        if (prefab == null)
        {
            DestroyLocalOverride();
            SetLocalRobotVisualEnabled(true);
            ApplyRobotColor(_localRobotVisual != null ? _localRobotVisual.gameObject : null, robotColor);
            _localAppliedKey = DefaultCharacterKey;
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
                return;
            }

            try
            {
                GameObject replacement = InstantiateCharacterVisual(prefab, mount, $"SelectedCharacterVisual_{characterKey}", characterKey);
                if (!HasRenderableVisual(replacement))
                {
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
                SetVisualRenderersEnabled(visual, true);
                ApplyRobotColor(visual, robotColor);
                continue;
            }

            GameObject prefab = CharacterPrefabCatalog.ResolvePrefab(characterKey);
            if (prefab == null) continue;

            if (visual != null && visual.name.StartsWith($"RemoteCharacterVisual_{characterKey}", StringComparison.Ordinal))
            {
                animatorField?.SetValue(remote, visual.GetComponentInChildren<Animator>(true));
                continue;
            }

            if (visual != null)
            {
                Destroy(visual);
            }

            GameObject replacement = InstantiateCharacterVisual(prefab, root.transform, $"RemoteCharacterVisual_{characterKey}", characterKey);
            visualField?.SetValue(remote, replacement);
            var animator = replacement.GetComponentInChildren<Animator>(true);
            animatorField?.SetValue(remote, animator);
            DozzleLogger.Action("Remote character visual applied", $"remote={entry.Key};character={characterKey};prefab={prefab.name};animator={(animator != null ? "yes" : "no")}");
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
                Destroy(_localOverrideRoot.GetChild(i).gameObject);
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

            if (component is Collider || component is Rigidbody || component is CharacterController || component is Camera || component is AudioListener)
            {
                Destroy(component);
                continue;
            }

            if (component is MonoBehaviour behaviour && ShouldStripMonoBehaviour(behaviour))
            {
                behaviour.enabled = false;
                Destroy(component);
            }
        }
    }

    private static bool ShouldStripMonoBehaviour(MonoBehaviour behaviour)
    {
        if (behaviour == null) return false;
        if (behaviour is StarterAssetsInputs) return true;

        string name = behaviour.GetType().Name ?? string.Empty;
        string fullName = behaviour.GetType().FullName ?? name;
        if (string.Equals(name, "GhostScript", StringComparison.Ordinal) || string.Equals(fullName, "Sample.GhostScript", StringComparison.Ordinal)) return true;
        if (ContainsAny(name, "Animator", "Animation", "IK", "Rig")) return false;

        return ContainsAny(fullName,
            "Input",
            "Controller",
            "CharacterControl",
            "CharacterMovement",
            "MovementController",
            "MoveController",
            "ThirdPerson",
            "FirstPerson",
            "CameraController",
            "PlayerController");
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(value) || terms == null) return false;
        foreach (string term in terms)
        {
            if (!string.IsNullOrWhiteSpace(term) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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

    private static void SetVisualRenderersEnabled(GameObject visual, bool enabled)
    {
        if (visual == null) return;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Canvas>() != null) continue;
            renderer.enabled = enabled;
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

    public static GameObject ResolvePrefab(string characterKey)
    {
        string key = string.IsNullOrWhiteSpace(characterKey) ? "robot_kyle" : characterKey.Trim();
        if (IsRobotCharacter(key)) return null;

        var resource = Resources.Load<GameObject>($"CharacterPrefabs/{SlotNameForKey(key)}");
        if (resource != null) return resource;

#if UNITY_EDITOR
        var editorPrefab = ResolveFromAssetDatabase(key);
        if (editorPrefab != null) return editorPrefab;
#endif

        var direct = ResolveFromReferences(key);
        if (direct != null) return direct;

        return ResolveLoadedPrefab(key);
    }

    private static GameObject ResolveFromReferences(string key)
    {
        if (!_referencesLoadAttempted)
        {
            _referencesLoadAttempted = true;
            _references = Resources.Load<CharacterPrefabReferences>("CharacterPrefabReferences");
        }

        if (_references == null) return null;

        switch (key)
        {
            case "ghost_character": return _references.ghostCharacterPrefab;
            case "skeleton": return _references.skeletonPrefab;
            case "tiny_hero":
            case "tiny_hero_male": return _references.tinyHeroMalePbrPrefab != null ? _references.tinyHeroMalePbrPrefab : _references.tinyHeroPrefab;
            case "tiny_hero_female": return _references.tinyHeroFemalePbrPrefab != null ? _references.tinyHeroFemalePbrPrefab : _references.sampleHeroPrefab;
            case "robot_hero": return _references.robotHeroPrefab;
            case "scifi_hp_character": return _references.sciFiHpCharacterPrefab;
            case "scifi_pbr_character": return _references.sciFiPbrCharacterPrefab;
            case "scifi_polyart_character": return _references.sciFiPolyartCharacterPrefab;
            case "sample_hero": return _references.sampleHeroPrefab;
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
            case "sample_hero": return "CharacterPackSample";
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
            case "sample_hero":
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RPG Tiny Hero Duo/Prefab/FemaleCharacterPolyart.prefab");
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
