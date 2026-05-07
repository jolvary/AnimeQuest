using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class NativeCharacterClipMotionDriver : MonoBehaviour
{
    private const string LocalVisualRootName = "SelectedCharacterVisualRoot";
    private const string LocalCharacterVisualPrefix = "SelectedCharacterVisual_";
    private const string RemotePlayerPrefix = "RemotePlayer_";
    private const string RemoteCharacterVisualPrefix = "RemoteCharacterVisual_";
    private const float MovingThreshold = 0.08f;
    private const float RunningThreshold = 3.2f;
    private const float WalkReferenceSpeed = 2f;
    private const float RunReferenceSpeed = 6f;
    private const float JumpVerticalSpeedThreshold = 0.12f;
    private const float JumpPositionDeltaThreshold = 0.015f;

    private readonly Dictionary<int, DrivenAnimator> _drivenAnimators = new Dictionary<int, DrivenAnimator>();
    private readonly Dictionary<int, MotionSourceState> _motionSources = new Dictionary<int, MotionSourceState>();
    private readonly HashSet<int> _unsupportedAnimators = new HashSet<int>();
    private float _nextDiscoveryAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<NativeCharacterClipMotionDriver>(FindObjectsInactive.Include) != null) return;

        var obj = new GameObject("NativeCharacterClipMotionDriver");
        DontDestroyOnLoad(obj);
        obj.AddComponent<NativeCharacterClipMotionDriver>();
    }

    private void Update()
    {
        CleanupDestroyedAnimators();
        DriveLocalSelectedCharacter();
        DriveRemoteCharacters();
    }

    private void OnDestroy()
    {
        foreach (var item in _drivenAnimators.Values)
        {
            item.DestroyGraph();
        }

        _drivenAnimators.Clear();
        _motionSources.Clear();
    }

    private void DriveLocalSelectedCharacter()
    {
        var root = GameObject.Find(LocalVisualRootName);
        if (root == null || root.transform.childCount == 0) return;

        var animator = root.GetComponentInChildren<Animator>(true);
        if (animator == null) return;

        Transform movementSource = root.transform.parent != null ? root.transform.parent : root.transform;
        DriveAnimator(animator, movementSource, ResolveLocalMotion(movementSource), localSource: true);
    }

    private void DriveRemoteCharacters()
    {
        if (Time.unscaledTime < _nextDiscoveryAt)
        {
            foreach (var item in _drivenAnimators.Values)
            {
                if (item == null || !item.IsRemote || item.Animator == null || item.MovementSource == null) continue;
                DriveAnimator(item.Animator, item.MovementSource, ResolveRemoteMotion(item), localSource: false);
            }
            return;
        }

        _nextDiscoveryAt = Time.unscaledTime + 0.25f;
        foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (transform == null || !transform.name.StartsWith(RemotePlayerPrefix, StringComparison.Ordinal)) continue;

            Animator animator = FindRemoteSelectedAnimator(transform);
            if (animator == null) continue;

            var item = EnsureDrivenAnimator(animator, transform, isRemote: true);
            if (item == null) continue;

            DriveAnimator(animator, transform, ResolveRemoteMotion(item), localSource: false);
        }
    }

    private static Animator FindRemoteSelectedAnimator(Transform remoteRoot)
    {
        if (remoteRoot == null) return null;

        foreach (var animator in remoteRoot.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null) continue;
            Transform current = animator.transform;
            while (current != null && current != remoteRoot)
            {
                if (current.name.StartsWith(RemoteCharacterVisualPrefix, StringComparison.Ordinal))
                {
                    return animator;
                }
                current = current.parent;
            }
        }

        return null;
    }

    private MotionState ResolveLocalMotion(Transform movementSource)
    {
        var state = new MotionState { Grounded = true };
        if (movementSource == null) return state;

        var characterController = movementSource.GetComponent<CharacterController>();
        if (characterController != null)
        {
            Vector3 velocity = characterController.velocity;
            state.VerticalSpeed = velocity.y;
            velocity.y = 0f;
            state.Speed = velocity.magnitude;
            state.Grounded = characterController.isGrounded;
            state.Jumping = !state.Grounded || state.VerticalSpeed > JumpVerticalSpeedThreshold;
            return state;
        }

        int key = movementSource.GetInstanceID();
        if (!_motionSources.TryGetValue(key, out var sourceState))
        {
            sourceState = new MotionSourceState();
            _motionSources[key] = sourceState;
        }

        return ResolveTransformMotion(movementSource, sourceState);
    }

    private static MotionState ResolveRemoteMotion(DrivenAnimator item)
    {
        if (item == null || item.MovementSource == null)
        {
            return new MotionState { Grounded = true };
        }

        return ResolveTransformMotion(item.MovementSource, item);
    }

    private static MotionState ResolveTransformMotion(Transform movementSource, MotionSourceState item)
    {
        var state = new MotionState { Grounded = true };
        if (movementSource == null || item == null) return state;

        float now = Time.unscaledTime;
        Vector3 position = movementSource.position;
        if (!item.HasLastPosition)
        {
            item.LastPosition = position;
            item.LastPositionAt = now;
            item.HasLastPosition = true;
            return state;
        }

        float deltaTime = Mathf.Max(now - item.LastPositionAt, 0.001f);
        Vector3 delta = position - item.LastPosition;
        item.LastPosition = position;
        item.LastPositionAt = now;

        state.VerticalSpeed = delta.y / deltaTime;
        state.Jumping = Mathf.Abs(delta.y) > JumpPositionDeltaThreshold && Mathf.Abs(state.VerticalSpeed) > JumpVerticalSpeedThreshold;
        state.Grounded = !state.Jumping;
        delta.y = 0f;
        state.Speed = delta.magnitude / deltaTime;
        return state;
    }

    private void DriveAnimator(Animator animator, Transform movementSource, MotionState motion, bool localSource)
    {
        var item = EnsureDrivenAnimator(animator, movementSource, isRemote: !localSource);
        if (item == null || !item.HasAnyDrivenClip) return;

        bool moving = motion.Speed > MovingThreshold;
        bool jumping = motion.Jumping && item.JumpClip != null;
        AnimationClip targetClip = jumping
            ? item.JumpClip
            : moving
                ? (motion.Speed >= RunningThreshold ? item.RunClip ?? item.WalkClip : item.WalkClip ?? item.RunClip)
                : item.IdleClip;

        if (targetClip == null)
        {
            StopGraph(item);
            return;
        }

        EnsureGraph(item);
        if (item.CurrentClip != targetClip)
        {
            if (item.CurrentPlayable.IsValid())
            {
                item.CurrentPlayable.Destroy();
            }

            targetClip.wrapMode = targetClip == item.JumpClip ? WrapMode.Once : WrapMode.Loop;
            item.CurrentPlayable = AnimationClipPlayable.Create(item.Graph, targetClip);
            item.CurrentPlayable.SetApplyFootIK(true);
            item.CurrentPlayable.SetApplyPlayableIK(false);
            item.Output.SetSourcePlayable(item.CurrentPlayable);
            item.CurrentClip = targetClip;
        }

        double playbackSpeed = 1.0;
        if (moving && targetClip != item.JumpClip)
        {
            float referenceSpeed = targetClip == item.RunClip ? RunReferenceSpeed : WalkReferenceSpeed;
            playbackSpeed = Mathf.Clamp(motion.Speed / referenceSpeed, 0.75f, 1.35f);
        }

        item.CurrentPlayable.SetSpeed(playbackSpeed);
    }

    private DrivenAnimator EnsureDrivenAnimator(Animator animator, Transform movementSource, bool isRemote)
    {
        if (animator == null) return null;

        int key = animator.GetInstanceID();
        if (_drivenAnimators.TryGetValue(key, out var item))
        {
            item.MovementSource = movementSource;
            item.IsRemote = isRemote;
            return item;
        }

        if (_unsupportedAnimators.Contains(key)) return null;

        item = BuildDrivenAnimator(animator, movementSource, isRemote);
        if (item == null)
        {
            _unsupportedAnimators.Add(key);
            return null;
        }

        _drivenAnimators[key] = item;
        DozzleLogger.Action(
            "Native character clip driver active",
            $"character={item.CharacterKey};animator={animator.name};idle={ClipName(item.IdleClip)};walk={ClipName(item.WalkClip)};run={ClipName(item.RunClip)};jump={ClipName(item.JumpClip)}"
        );
        return item;
    }

    private static DrivenAnimator BuildDrivenAnimator(Animator animator, Transform movementSource, bool isRemote)
    {
        var controller = animator.runtimeAnimatorController;
        var clips = controller != null ? controller.animationClips : null;
        if (clips == null || clips.Length == 0) return null;

        string characterKey = ResolveCharacterKey(animator.transform);
        var item = new DrivenAnimator
        {
            Animator = animator,
            MovementSource = movementSource,
            IsRemote = isRemote,
            CharacterKey = string.IsNullOrWhiteSpace(characterKey) ? "unknown" : characterKey,
        };

        ApplyExplicitClipMapping(clips, item.CharacterKey, item);
        item.IdleClip = item.IdleClip != null ? item.IdleClip : PickClip(clips, ClipKind.Idle);
        item.WalkClip = item.WalkClip != null ? item.WalkClip : PickClip(clips, ClipKind.Walk);
        item.RunClip = item.RunClip != null ? item.RunClip : PickClip(clips, ClipKind.Run);
        item.JumpClip = item.JumpClip != null ? item.JumpClip : PickClip(clips, ClipKind.Jump);

        if (!item.HasAnyDrivenClip) return null;

        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return item;
    }

    private static void ApplyExplicitClipMapping(AnimationClip[] clips, string characterKey, DrivenAnimator item)
    {
        if (clips == null || item == null) return;

        switch (NormalizeKey(characterKey))
        {
            case "tiny_hero":
            case "tiny_hero_male":
            case "tiny_hero_female":
                item.WalkClip = FindClip(clips, "MoveFWD_Normal_RM_SwordAndShield");
                item.RunClip = FindClip(clips, "SprintFWD_Battle_RM_SwordAndShield");
                item.JumpClip = FindClip(clips, "JumpFull_Spin_RM_SwordAndShield");
                break;
            case "skeleton":
                item.WalkClip = FindClip(clips, "Skeleton_walk_forward");
                item.RunClip = FindClip(clips, "Skeleton_run_forward");
                item.JumpClip = FindClip(clips, "Skeleton_jump");
                break;
            case "ghost_character":
                var ghostRun = FindClip(clips, "ghost_run");
                item.WalkClip = ghostRun;
                item.RunClip = ghostRun;
                break;
        }
    }

    private static AnimationClip FindClip(AnimationClip[] clips, string expectedName)
    {
        if (clips == null || string.IsNullOrWhiteSpace(expectedName)) return null;

        foreach (var clip in clips)
        {
            if (clip != null && string.Equals(clip.name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }

        foreach (var clip in clips)
        {
            if (clip != null && clip.name.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return clip;
            }
        }

        return null;
    }

    private static AnimationClip PickClip(AnimationClip[] clips, ClipKind kind)
    {
        AnimationClip best = null;
        int bestScore = 0;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            int score = ScoreClip(clip.name, kind);
            if (score > bestScore)
            {
                bestScore = score;
                best = clip;
            }
        }

        return best;
    }

    private static int ScoreClip(string clipName, ClipKind kind)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return 0;

        string name = clipName.ToLowerInvariant();
        if (kind == ClipKind.Jump)
        {
            if (ContainsAny(name, "attack", "atk", "hit", "hurt", "damage", "death", "die", "turn", "strafe", "cast")) return 0;
        }
        else if (ContainsAny(name, "attack", "atk", "hit", "hurt", "damage", "death", "die", "jump", "fall", "turn", "strafe", "cast"))
        {
            return 0;
        }

        int score = 0;
        switch (kind)
        {
            case ClipKind.Idle:
                if (name.Contains("idle")) score += 100;
                if (name.Contains("stand")) score += 70;
                break;
            case ClipKind.Walk:
                if (name.Contains("walk")) score += 100;
                if (name.Contains("locomotion")) score += 60;
                if (name.Contains("move")) score += 45;
                break;
            case ClipKind.Run:
                if (name.Contains("sprint")) score += 120;
                if (name.Contains("run")) score += 110;
                if (name.Contains("jog")) score += 80;
                break;
            case ClipKind.Jump:
                if (name.Contains("jump")) score += 120;
                if (name.Contains("leap")) score += 90;
                if (name.Contains("air")) score += 60;
                break;
        }

        if (score > 0 && ContainsAny(name, "forward", "fwd", "root", "rootmotion")) score += 10;
        return score;
    }

    private void EnsureGraph(DrivenAnimator item)
    {
        if (item == null || item.Animator == null) return;
        if (item.Graph.IsValid()) return;

        item.Graph = PlayableGraph.Create($"NativeCharacterClip_{item.Animator.GetInstanceID()}");
        item.Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        item.Output = AnimationPlayableOutput.Create(item.Graph, "Animation", item.Animator);
        item.Graph.Play();
    }

    private static void StopGraph(DrivenAnimator item)
    {
        if (item == null) return;
        item.DestroyGraph();
        item.CurrentClip = null;
    }

    private void CleanupDestroyedAnimators()
    {
        List<int> remove = null;
        foreach (var pair in _drivenAnimators)
        {
            var item = pair.Value;
            if (item == null || item.Animator == null)
            {
                if (remove == null) remove = new List<int>();
                remove.Add(pair.Key);
                item?.DestroyGraph();
            }
        }

        if (remove == null) return;
        foreach (int key in remove)
        {
            _drivenAnimators.Remove(key);
            _unsupportedAnimators.Remove(key);
        }
    }

    private static string ResolveCharacterKey(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string key = ExtractCharacterKey(current.name, LocalCharacterVisualPrefix);
            if (!string.IsNullOrWhiteSpace(key)) return key;

            key = ExtractCharacterKey(current.name, RemoteCharacterVisualPrefix);
            if (!string.IsNullOrWhiteSpace(key)) return key;

            current = current.parent;
        }

        return string.Empty;
    }

    private static string ExtractCharacterKey(string name, string prefix)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prefix)) return string.Empty;
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return string.Empty;
        return name.Substring(prefix.Length).Trim();
    }

    private static string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
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

    private static string ClipName(AnimationClip clip)
    {
        return clip == null ? "none" : clip.name;
    }

    private enum ClipKind
    {
        Idle,
        Walk,
        Run,
        Jump,
    }

    private class MotionState
    {
        public float Speed;
        public float VerticalSpeed;
        public bool Grounded;
        public bool Jumping;
    }

    private class MotionSourceState
    {
        public bool HasLastPosition;
        public Vector3 LastPosition;
        public float LastPositionAt;
    }

    private class DrivenAnimator : MotionSourceState
    {
        public Animator Animator;
        public Transform MovementSource;
        public bool IsRemote;
        public string CharacterKey;
        public AnimationClip IdleClip;
        public AnimationClip WalkClip;
        public AnimationClip RunClip;
        public AnimationClip JumpClip;
        public PlayableGraph Graph;
        public AnimationPlayableOutput Output;
        public AnimationClipPlayable CurrentPlayable;
        public AnimationClip CurrentClip;

        public bool HasAnyDrivenClip => IdleClip != null || WalkClip != null || RunClip != null || JumpClip != null;

        public void DestroyGraph()
        {
            if (CurrentPlayable.IsValid())
            {
                CurrentPlayable.Destroy();
            }

            if (Graph.IsValid())
            {
                Graph.Destroy();
            }

            CurrentClip = null;
        }
    }
}
