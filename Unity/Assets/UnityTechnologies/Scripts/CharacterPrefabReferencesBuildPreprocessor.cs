#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

internal sealed class CharacterPrefabReferencesBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        CharacterPrefabReferencesEditorBinding.Rebind("pre-build");
    }
}

internal static class CharacterPrefabReferencesEditorBinding
{
    private const string ReferencesPath = "Assets/Resources/CharacterPrefabReferences.asset";

    [MenuItem("AnimeQuest/Rebind Character Prefab References")]
    public static void RebindFromMenu()
    {
        Rebind("menu");
    }

    public static void Rebind(string reason)
    {
        var references = AssetDatabase.LoadAssetAtPath<CharacterPrefabReferences>(ReferencesPath);
        if (references == null)
        {
            Debug.LogError($"[AnimeQuest] Character prefab references missing at {ReferencesPath}; reason={reason}");
            return;
        }

        references.ghostCharacterPrefab = LoadPrefab("ghost", references.ghostCharacterPrefab,
            new[] { "Assets/GhostCharacter_Free/Prefabs/Ghost.prefab" },
            new[] { "Ghost" },
            new[] { "Assets/GhostCharacter_Free", "Assets" });

        references.skeletonPrefab = LoadPrefab("skeleton", references.skeletonPrefab,
            new[]
            {
                "Assets/SazenGames/Skeleton/Prefabs/Skeleton_110.prefab",
                "Assets/SazenGames/Stylized Low Poly Skeleton/Prefabs/Skeleton_110.prefab",
                "Assets/Feyloom/Skeleton_Necromancer/Renders/URP/Prefab/SKM_Skeleton_Necromancer.prefab",
                "Assets/Feyloom/Skeleton_Necromancer/Renders/Built-in/Prefab/SKM_Skeleton_Necromancer.prefab"
            },
            new[] { "Skeleton_110", "Skeleton", "SKM_Skeleton_Necromancer" },
            new[] { "Assets/SazenGames", "Assets/Feyloom", "Assets" });

        references.tinyHeroPrefab = LoadPrefab("tinyHero", references.tinyHeroPrefab,
            new[]
            {
                "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab",
                "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPolyart.prefab"
            },
            new[] { "MaleCharacterPBR", "MaleCharacterPolyart" },
            new[] { "Assets/RPG Tiny Hero Duo", "Assets" });

        references.tinyHeroMalePbrPrefab = LoadPrefab("tinyHeroMale", references.tinyHeroMalePbrPrefab,
            new[]
            {
                "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab",
                "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPolyart.prefab"
            },
            new[] { "MaleCharacterPBR", "MaleCharacterPolyart" },
            new[] { "Assets/RPG Tiny Hero Duo", "Assets" });

        references.tinyHeroFemalePbrPrefab = LoadPrefab("tinyHeroFemale", references.tinyHeroFemalePbrPrefab,
            new[]
            {
                "Assets/RPG Tiny Hero Duo/Prefab/FemaleCharacterPBR.prefab",
                "Assets/RPG Tiny Hero Duo/Prefab/FemaleCharacterPolyart.prefab"
            },
            new[] { "FemaleCharacterPBR", "FemaleCharacterPolyart" },
            new[] { "Assets/RPG Tiny Hero Duo", "Assets" });

        references.robotHeroPrefab = LoadPrefab("robotHero", references.robotHeroPrefab,
            new[]
            {
                "Assets/Robot Hero PBR HP Polyart/Prefabs/RobotHero.prefab",
                "Assets/Robot Hero PBR HP Polyart/Prefabs/Robot Hero.prefab",
                "Assets/RobotHeroPBRHPPolyart/Prefabs/RobotHero.prefab"
            },
            new[] { "RobotHero", "Robot Hero" },
            new[] { "Assets" });

        references.sciFiHpCharacterPrefab = LoadPrefab("sciFiHp", references.sciFiHpCharacterPrefab,
            new[] { "Assets/SciFiWarriorPBRHPPolyart/Prefabs/HPCharacter.prefab" },
            new[] { "HPCharacter" },
            new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });

        references.sciFiPbrCharacterPrefab = LoadPrefab("sciFiPbr", references.sciFiPbrCharacterPrefab,
            new[] { "Assets/SciFiWarriorPBRHPPolyart/Prefabs/PBRCharacter.prefab" },
            new[] { "PBRCharacter" },
            new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });

        references.sciFiPolyartCharacterPrefab = LoadPrefab("sciFiPolyart", references.sciFiPolyartCharacterPrefab,
            new[] { "Assets/SciFiWarriorPBRHPPolyart/Prefabs/PolyartCharacter.prefab" },
            new[] { "PolyartCharacter" },
            new[] { "Assets/SciFiWarriorPBRHPPolyart", "Assets" });

        EditorUtility.SetDirty(references);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AnimeQuest] Character prefab references rebound; reason={reason}; {Describe(references)}");
    }

    private static GameObject LoadPrefab(
        string label,
        GameObject existing,
        IReadOnlyList<string> candidatePaths,
        IReadOnlyList<string> searchNames,
        string[] searchFolders)
    {
        foreach (string path in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                return prefab;
            }
        }

        foreach (string searchName in searchNames)
        {
            var prefab = FindPrefabBySearch(searchName, searchFolders);
            if (prefab != null)
            {
                return prefab;
            }
        }

        if (existing != null)
        {
            Debug.LogWarning($"[AnimeQuest] Character prefab path missing for {label}; keeping existing reference {existing.name}.");
            return existing;
        }

        Debug.LogWarning($"[AnimeQuest] Character prefab reference remains empty for {label}.");
        return null;
    }

    private static GameObject FindPrefabBySearch(string query, string[] folders)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        string[] guids;
        try
        {
            guids = AssetDatabase.FindAssets($"{query} t:Prefab", folders);
        }
        catch (Exception)
        {
            guids = AssetDatabase.FindAssets($"{query} t:Prefab");
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
        }

        return null;
    }

    private static string Describe(CharacterPrefabReferences references)
    {
        return string.Join(";",
            $"ghost={Name(references.ghostCharacterPrefab)}",
            $"skeleton={Name(references.skeletonPrefab)}",
            $"tinyMale={Name(references.tinyHeroMalePbrPrefab)}",
            $"tinyFemale={Name(references.tinyHeroFemalePbrPrefab)}",
            $"robotHero={Name(references.robotHeroPrefab)}",
            $"sciFiHp={Name(references.sciFiHpCharacterPrefab)}",
            $"sciFiPbr={Name(references.sciFiPbrCharacterPrefab)}",
            $"sciFiPolyart={Name(references.sciFiPolyartCharacterPrefab)}");
    }

    private static string Name(UnityEngine.Object value)
    {
        return value != null ? value.name : "null";
    }
}
#endif
