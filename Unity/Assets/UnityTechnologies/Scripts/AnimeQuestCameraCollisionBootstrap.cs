using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AnimeQuestCameraCollisionBootstrap
{
    private const string TargetSceneName = "AnimeQuestGame";
    private const string ThirdPersonFollowTypeName = "Unity.Cinemachine.CinemachineThirdPersonFollow";
    private static bool _loggedApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        ApplyForActiveScene();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private static void OnActiveSceneChanged(Scene previous, Scene current)
    {
        ApplyForActiveScene();
    }

    private static void ApplyForActiveScene()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, TargetSceneName, StringComparison.Ordinal))
        {
            return;
        }

        bool applied = false;
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null) continue;

            Type type = behaviour.GetType();
            if (!string.Equals(type.FullName, ThirdPersonFollowTypeName, StringComparison.Ordinal) &&
                !string.Equals(type.Name, "CinemachineThirdPersonFollow", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryApplyAvoidObstacles(type, behaviour))
            {
                applied = true;
            }
        }

        if (applied && !_loggedApplied)
        {
            _loggedApplied = true;
            DozzleLogger.Action("AnimeQuest camera collision enabled", "scene=AnimeQuestGame;radius=0.3;ignoreTag=Player");
        }
    }

    private static bool TryApplyAvoidObstacles(Type followType, object follow)
    {
        object avoidObstacles = GetMemberValue(followType, follow, "AvoidObstacles", out var avoidMember);
        if (avoidObstacles == null || avoidMember == null) return false;

        Type avoidType = avoidObstacles.GetType();
        SetMemberValue(avoidType, avoidObstacles, "Enabled", true);
        SetMemberValue(avoidType, avoidObstacles, "CameraRadius", 0.3f);
        SetMemberValue(avoidType, avoidObstacles, "DampingIntoCollision", 0.05f);
        SetMemberValue(avoidType, avoidObstacles, "DampingFromCollision", 0.5f);
        SetMemberValue(avoidType, avoidObstacles, "IgnoreTag", "Player");
        SetMemberValue(avoidType, avoidObstacles, "CollisionFilter", new LayerMask { value = 1 });

        SetExistingMemberValue(followType, follow, avoidMember, avoidObstacles);
        return true;
    }

    private static object GetMemberValue(Type type, object target, string name, out MemberInfo member)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            member = field;
            return field.GetValue(target);
        }

        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
        {
            member = property;
            return property.GetValue(target, null);
        }

        member = null;
        return null;
    }

    private static void SetMemberValue(Type type, object target, string name, object value)
    {
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(target, ConvertValue(value, field.FieldType));
            return;
        }

        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(value, property.PropertyType), null);
        }
    }

    private static void SetExistingMemberValue(Type type, object target, MemberInfo member, object value)
    {
        if (member is FieldInfo field)
        {
            field.SetValue(target, ConvertValue(value, field.FieldType));
        }
        else if (member is PropertyInfo property && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(value, property.PropertyType), null);
        }
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (value == null || targetType.IsInstanceOfType(value)) return value;
        if (targetType == typeof(float)) return Convert.ToSingle(value);
        if (targetType == typeof(bool)) return Convert.ToBoolean(value);
        if (targetType == typeof(string)) return Convert.ToString(value);
        if (targetType == typeof(LayerMask) && value is int maskValue) return new LayerMask { value = maskValue };
        return value;
    }
}
