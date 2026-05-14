using System.Collections.Generic;
using System;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

public class MapPanelController : MonoBehaviour
{
    private const float MapWidth = 1080f;
    private const float MapHeight = 560f;
    private const float MapPadding = 34f;
    private const float CityBoundsMargin = 8f;
    private const int MaxMapFeatures = 280;
    private static readonly Color FriendMarkerColor = new Color(0.44f, 0.24f, 0.76f, 1f);

    private readonly List<MapEntry> _entries = new List<MapEntry>();
    private readonly HashSet<Transform> _seenTransforms = new HashSet<Transform>();
    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();
    private readonly List<MapFeature> _mapFeatures = new List<MapFeature>();
    private readonly HashSet<string> _friendUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private Font _font;
    private RectTransform _mapArea;
    private Text _statusText;
    private RectTransform _playerMarker;
    private Vector2 _worldMin;
    private Vector2 _worldMax;
    private float _nextPlayerRefreshAt;
    private bool _hasLoadedFriendIds;
    private bool _isLoadingFriendIds;

    private void OnEnable()
    {
        RefreshFriendIds();
        RefreshMap();
    }

    private void Update()
    {
        if (_playerMarker == null || Time.unscaledTime < _nextPlayerRefreshAt) return;

        _nextPlayerRefreshAt = Time.unscaledTime + 0.12f;
        UpdatePlayerMarker();
    }

    public void ConfigureFont(Font font)
    {
        _font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    public void RefreshMap()
    {
        EnsureBuilt();
        ClearMarkers();
        CollectEntries();
        CalculateBounds();
        DrawMapFrame();
        DrawNpcMarkers();
        DrawPlayerMarker();
        UpdateStatus();
    }

    private void EnsureBuilt()
    {
        if (_font == null)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        RemoveRefreshButtonIfPresent();
        if (_mapArea != null)
        {
            ConfigureTransparentMapBackground();
            return;
        }

        var titleObject = new GameObject("MapTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        titleObject.transform.SetParent(transform, false);
        var titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -74f);
        titleRect.sizeDelta = new Vector2(520f, 58f);

        var title = titleObject.GetComponent<Text>();
        title.text = "City Map";
        title.font = _font;
        title.fontSize = 36;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.15f, 0.08f, 0.03f, 1f);
        title.raycastTarget = false;

        var mapObject = new GameObject("MapArea", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        mapObject.transform.SetParent(transform, false);
        _mapArea = mapObject.GetComponent<RectTransform>();
        _mapArea.anchorMin = new Vector2(0.5f, 0.5f);
        _mapArea.anchorMax = new Vector2(0.5f, 0.5f);
        _mapArea.pivot = new Vector2(0.5f, 0.5f);
        _mapArea.anchoredPosition = new Vector2(0f, -22f);
        _mapArea.sizeDelta = new Vector2(MapWidth, MapHeight);

        var mapImage = mapObject.GetComponent<Image>();
        mapImage.color = Color.clear;
        mapImage.raycastTarget = false;

        var statusObject = new GameObject("MapStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        statusObject.transform.SetParent(transform, false);
        var statusRect = statusObject.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 0f);
        statusRect.anchorMax = new Vector2(0.5f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(0f, 70f);
        statusRect.sizeDelta = new Vector2(820f, 42f);

        _statusText = statusObject.GetComponent<Text>();
        _statusText.font = _font;
        _statusText.fontSize = 22;
        _statusText.alignment = TextAnchor.MiddleCenter;
        _statusText.color = new Color(0.16f, 0.09f, 0.04f, 1f);
        _statusText.raycastTarget = false;
    }

    private void ConfigureTransparentMapBackground()
    {
        if (_mapArea == null) return;

        var image = _mapArea.GetComponent<Image>();
        if (image == null) return;

        image.color = Color.clear;
        image.raycastTarget = false;
    }

    private void RemoveRefreshButtonIfPresent()
    {
        Transform button = transform.Find("RefreshMapButton");
        if (button != null)
        {
            Destroy(button.gameObject);
        }
    }

    private void CollectEntries()
    {
        _entries.Clear();
        _seenTransforms.Clear();

        foreach (var marker in FindObjectsByType<NpcMapMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (marker != null && marker.showOnMap)
            {
                AddEntry(marker.transform, marker.ResolveLabel(), marker.markerColor, "N");
            }
        }

        foreach (var questGiver in FindObjectsByType<NPCQuestGiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (questGiver != null)
            {
                AddEntry(questGiver.transform, questGiver.npcName, new Color(0.18f, 0.31f, 0.74f, 1f), "N");
            }
        }

        foreach (var animeGuide in FindObjectsByType<NpcAnimeCatalogInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (animeGuide != null)
            {
                AddEntry(animeGuide.transform, animeGuide.npcName, new Color(0.12f, 0.52f, 0.42f, 1f), "N");
            }
        }

        CollectFriendEntries();
    }

    private void CollectFriendEntries()
    {
        if (!_hasLoadedFriendIds || _friendUserIds.Count == 0) return;

        var world = FindFirstObjectByType<NakamaWorldMultiplayerController>(FindObjectsInactive.Exclude);
        if (world == null) return;

        var remotes = world.GetRemoteMapEntries();
        foreach (var remote in remotes)
        {
            if (remote.target == null || string.IsNullOrWhiteSpace(remote.userId)) continue;
            if (!_friendUserIds.Contains(remote.userId)) continue;

            AddEntry(remote.target, remote.username, FriendMarkerColor, "F");
        }
    }

    private async void RefreshFriendIds()
    {
        if (_isLoadingFriendIds) return;

        var auth = NakamaAuthManager.Instance;
        if (auth == null || !auth.IsAuthenticated || auth.IsIncognitoSession || auth.Client == null || auth.Session == null)
        {
            _friendUserIds.Clear();
            _hasLoadedFriendIds = true;
            return;
        }

        _isLoadingFriendIds = true;
        try
        {
            var friendList = await auth.Client.ListFriendsAsync(auth.Session, null, 100, null);
            _friendUserIds.Clear();
            if (friendList?.Friends != null)
            {
                foreach (var friend in friendList.Friends)
                {
                    if (friend?.User == null || string.IsNullOrWhiteSpace(friend.User.Id)) continue;
                    if (Convert.ToInt32(friend.State) == 0)
                    {
                        _friendUserIds.Add(friend.User.Id);
                    }
                }
            }

            _hasLoadedFriendIds = true;
            if (isActiveAndEnabled)
            {
                RefreshMap();
            }
        }
        catch (Exception ex)
        {
            _friendUserIds.Clear();
            _hasLoadedFriendIds = true;
            DozzleLogger.Error("Map friend marker load failed", ex);
        }
        finally
        {
            _isLoadingFriendIds = false;
        }
    }

    private void AddEntry(Transform target, string label, Color color, string symbol)
    {
        if (target == null || _seenTransforms.Contains(target)) return;

        _seenTransforms.Add(target);
        _entries.Add(new MapEntry
        {
            target = target,
            label = string.IsNullOrWhiteSpace(label) ? target.name : label.Trim(),
            color = color,
            symbol = string.IsNullOrWhiteSpace(symbol) ? "N" : symbol
        });
    }

    private void CalculateBounds()
    {
        _mapFeatures.Clear();

        if (TryCalculateCityBounds(out Vector2 cityMin, out Vector2 cityMax))
        {
            _worldMin = cityMin;
            _worldMax = cityMax;
            return;
        }

        bool hasPoint = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;

        foreach (var entry in _entries)
        {
            IncludeWorldPoint(ToMapPoint(entry.target.position), ref min, ref max, ref hasPoint);
        }

        Transform player = ResolvePlayer();
        if (player != null)
        {
            IncludeWorldPoint(ToMapPoint(player.position), ref min, ref max, ref hasPoint);
        }

        if (!hasPoint)
        {
            min = new Vector2(-10f, -10f);
            max = new Vector2(10f, 10f);
        }

        Vector2 span = max - min;
        if (span.x < 8f)
        {
            min.x -= 4f;
            max.x += 4f;
        }

        if (span.y < 8f)
        {
            min.y -= 4f;
            max.y += 4f;
        }

        Vector2 margin = new Vector2(Mathf.Max(3f, (max.x - min.x) * 0.12f), Mathf.Max(3f, (max.y - min.y) * 0.12f));
        _worldMin = min - margin;
        _worldMax = max + margin;
    }

    private bool TryCalculateCityBounds(out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;
        bool hasPoint = false;

        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!ShouldUseRendererForCityBounds(renderer, out MapFeatureKind kind)) continue;

            Bounds bounds = renderer.bounds;
            Vector2 boundsMin = new Vector2(bounds.min.x, bounds.min.z);
            Vector2 boundsMax = new Vector2(bounds.max.x, bounds.max.z);
            IncludeWorldPoint(boundsMin, ref min, ref max, ref hasPoint);
            IncludeWorldPoint(boundsMax, ref min, ref max, ref hasPoint);

            if (_mapFeatures.Count < MaxMapFeatures && ShouldDrawFeature(bounds, kind))
            {
                _mapFeatures.Add(new MapFeature
                {
                    worldMin = boundsMin,
                    worldMax = boundsMax,
                    kind = kind,
                    color = ResolveFeatureColor(kind),
                    order = ResolveFeatureOrder(kind)
                });
            }
        }

        if (!hasPoint)
        {
            foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (terrain == null || terrain.terrainData == null) continue;

                Vector3 position = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                IncludeWorldPoint(new Vector2(position.x, position.z), ref min, ref max, ref hasPoint);
                IncludeWorldPoint(new Vector2(position.x + size.x, position.z + size.z), ref min, ref max, ref hasPoint);
            }
        }

        if (!hasPoint) return false;

        Vector2 span = max - min;
        if (span.x < 12f || span.y < 12f) return false;

        Vector2 margin = new Vector2(
            Mathf.Max(CityBoundsMargin, span.x * 0.04f),
            Mathf.Max(CityBoundsMargin, span.y * 0.04f));

        min -= margin;
        max += margin;
        _mapFeatures.Sort((left, right) => left.order.CompareTo(right.order));
        return true;
    }

    private static bool ShouldUseRendererForCityBounds(Renderer renderer, out MapFeatureKind kind)
    {
        kind = MapFeatureKind.Unknown;
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
        if (renderer is SkinnedMeshRenderer) return false;
        if (renderer.GetComponentInParent<Canvas>() != null) return false;
        if (renderer.GetComponentInParent<StarterAssetsInputs>() != null) return false;
        if (renderer.GetComponentInParent<CharacterController>() != null) return false;
        if (renderer.GetComponentInParent<NPCQuestGiver>() != null) return false;
        if (renderer.GetComponentInParent<NpcAnimeCatalogInteractable>() != null) return false;
        if (renderer.GetComponentInParent<NpcMapMarker>() != null) return false;

        Bounds bounds = renderer.bounds;
        Vector3 size = bounds.size;
        if (!IsUsableCityBounds(size)) return false;

        string name = BuildHierarchyName(renderer.transform);
        string lowerName = name.ToLowerInvariant();
        if (lowerName.Contains("backdrop") || lowerName.Contains("camera")) return false;

        kind = ResolveFeatureKind(lowerName);
        return kind != MapFeatureKind.Unknown;
    }

    private static bool IsUsableCityBounds(Vector3 size)
    {
        if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z)) return false;
        if (float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)) return false;
        if (size.x < 0.08f && size.z < 0.08f) return false;
        if (size.x > 500f || size.z > 500f) return false;
        return true;
    }

    private static string BuildHierarchyName(Transform transform)
    {
        if (transform == null) return "";

        string name = transform.name;
        Transform current = transform.parent;
        int depth = 0;

        while (current != null && depth < 5)
        {
            name = current.name + "/" + name;
            current = current.parent;
            depth++;
        }

        return name;
    }

    private static MapFeatureKind ResolveFeatureKind(string lowerName)
    {
        if (lowerName.Contains("natures_grass") || lowerName.Contains("natures grass"))
        {
            return MapFeatureKind.Ground;
        }

        if (lowerName.Contains("building"))
        {
            return MapFeatureKind.Building;
        }

        if (lowerName.Contains("road"))
        {
            return MapFeatureKind.Road;
        }

        return MapFeatureKind.Unknown;
    }

    private static bool ShouldDrawFeature(Bounds bounds, MapFeatureKind kind)
    {
        return kind == MapFeatureKind.Building ||
               kind == MapFeatureKind.Ground ||
               kind == MapFeatureKind.Road;
    }

    private static Color ResolveFeatureColor(MapFeatureKind kind)
    {
        switch (kind)
        {
            case MapFeatureKind.Road:
                return new Color(0.31f, 0.25f, 0.21f, 0.58f);
            case MapFeatureKind.Building:
                return new Color(0.42f, 0.25f, 0.13f, 0.54f);
            case MapFeatureKind.Vehicle:
                return new Color(0.17f, 0.31f, 0.46f, 0.48f);
            case MapFeatureKind.Ground:
                return new Color(0.43f, 0.53f, 0.25f, 0.22f);
            case MapFeatureKind.Prop:
                return new Color(0.28f, 0.20f, 0.13f, 0.34f);
            default:
                return new Color(0.38f, 0.31f, 0.22f, 0.28f);
        }
    }

    private static int ResolveFeatureOrder(MapFeatureKind kind)
    {
        switch (kind)
        {
            case MapFeatureKind.Ground:
                return 0;
            case MapFeatureKind.Road:
                return 1;
            case MapFeatureKind.Building:
                return 2;
            case MapFeatureKind.Vehicle:
                return 3;
            case MapFeatureKind.Prop:
                return 4;
            default:
                return 5;
        }
    }

    private static void IncludeWorldPoint(Vector2 point, ref Vector2 min, ref Vector2 max, ref bool hasPoint)
    {
        if (!hasPoint)
        {
            min = point;
            max = point;
            hasPoint = true;
            return;
        }

        min = Vector2.Min(min, point);
        max = Vector2.Max(max, point);
    }

    private void DrawMapFrame()
    {
        if (_mapFeatures.Count > 0)
        {
            DrawCityFeatures();
            return;
        }

        CreateMapLine("NorthStreet", new Vector2(0f, MapHeight * 0.18f), new Vector2(MapWidth - 80f, 14f));
        CreateMapLine("SouthStreet", new Vector2(0f, -MapHeight * 0.18f), new Vector2(MapWidth - 100f, 14f));
        CreateMapLine("CrossStreet", new Vector2(-MapWidth * 0.18f, 0f), new Vector2(14f, MapHeight - 90f));
        CreateMapLine("SideStreet", new Vector2(MapWidth * 0.22f, 0f), new Vector2(12f, MapHeight - 120f));
    }

    private void DrawCityFeatures()
    {
        for (int i = 0; i < _mapFeatures.Count; i++)
        {
            MapFeature feature = _mapFeatures[i];
            Vector2 min = WorldToMapPosition(new Vector3(feature.worldMin.x, 0f, feature.worldMin.y));
            Vector2 max = WorldToMapPosition(new Vector3(feature.worldMax.x, 0f, feature.worldMax.y));
            Vector2 center = (min + max) * 0.5f;
            Vector2 size = new Vector2(
                Mathf.Max(4f, Mathf.Abs(max.x - min.x)),
                Mathf.Max(4f, Mathf.Abs(max.y - min.y)));

            CreateMapRect($"{feature.kind}_Feature_{i}", center, size, feature.color);
        }
    }

    private void CreateMapRect(string name, Vector2 position, Vector2 size, Color color)
    {
        var rectObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rectObject.transform.SetParent(_mapArea, false);
        _spawnedObjects.Add(rectObject);

        var rect = rectObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        rectObject.GetComponent<Image>().color = color;
    }

    private void CreateMapLine(string name, Vector2 position, Vector2 size)
    {
        var line = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        line.transform.SetParent(_mapArea, false);
        _spawnedObjects.Add(line);

        var rect = line.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        line.GetComponent<Image>().color = new Color(0.54f, 0.39f, 0.22f, 0.42f);
    }

    private void DrawNpcMarkers()
    {
        foreach (var entry in _entries)
        {
            CreateMarker(entry.label, entry.target.position, entry.color, entry.symbol);
        }
    }

    private void DrawPlayerMarker()
    {
        Transform player = ResolvePlayer();
        if (player == null) return;

        _playerMarker = CreateMarker("You", player.position, new Color(0.86f, 0.14f, 0.08f, 1f), "P");
        _nextPlayerRefreshAt = 0f;
        UpdatePlayerMarker();
    }

    private RectTransform CreateMarker(string label, Vector3 worldPosition, Color color, string symbol)
    {
        var markerObject = new GameObject($"{label}_Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        markerObject.transform.SetParent(_mapArea, false);
        _spawnedObjects.Add(markerObject);

        var rect = markerObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(28f, 28f);
        rect.anchoredPosition = WorldToMapPosition(worldPosition);

        markerObject.GetComponent<Image>().color = color;

        var symbolObject = new GameObject("Symbol", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        symbolObject.transform.SetParent(markerObject.transform, false);
        var symbolRect = symbolObject.GetComponent<RectTransform>();
        symbolRect.anchorMin = Vector2.zero;
        symbolRect.anchorMax = Vector2.one;
        symbolRect.offsetMin = Vector2.zero;
        symbolRect.offsetMax = Vector2.zero;

        var symbolText = symbolObject.GetComponent<Text>();
        symbolText.text = symbol;
        symbolText.font = _font;
        symbolText.fontSize = 16;
        symbolText.fontStyle = FontStyle.Bold;
        symbolText.alignment = TextAnchor.MiddleCenter;
        symbolText.color = Color.white;
        symbolText.raycastTarget = false;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(markerObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 5f);
        labelRect.sizeDelta = new Vector2(180f, 30f);

        var labelText = labelObject.GetComponent<Text>();
        labelText.text = label;
        labelText.font = _font;
        labelText.fontSize = 16;
        labelText.fontStyle = FontStyle.Bold;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = new Color(0.12f, 0.07f, 0.03f, 1f);
        labelText.raycastTarget = false;

        return rect;
    }

    private void UpdatePlayerMarker()
    {
        Transform player = ResolvePlayer();
        if (player == null || _playerMarker == null) return;

        _playerMarker.anchoredPosition = WorldToMapPosition(player.position);
    }

    private Vector2 WorldToMapPosition(Vector3 worldPosition)
    {
        Vector2 point = ToMapPoint(worldPosition);
        float normalizedX = Mathf.InverseLerp(_worldMin.x, _worldMax.x, point.x);
        float normalizedY = Mathf.InverseLerp(_worldMin.y, _worldMax.y, point.y);

        float width = Mathf.Max(1f, _mapArea.rect.width);
        float height = Mathf.Max(1f, _mapArea.rect.height);
        float x = Mathf.Lerp(-width * 0.5f + MapPadding, width * 0.5f - MapPadding, normalizedX);
        float y = Mathf.Lerp(-height * 0.5f + MapPadding, height * 0.5f - MapPadding, normalizedY);
        return new Vector2(x, y);
    }

    private static Vector2 ToMapPoint(Vector3 worldPosition)
    {
        return new Vector2(worldPosition.x, worldPosition.z);
    }

    private void UpdateStatus()
    {
        if (_statusText == null) return;

        if (_entries.Count == 0)
        {
            _statusText.text = "No NPCs or friends found on the map.";
            return;
        }

        int npcCount = 0;
        int friendCount = 0;
        foreach (var entry in _entries)
        {
            if (entry.symbol == "F") friendCount++;
            else npcCount++;
        }

        _statusText.text = $"Showing {npcCount} NPC marker{(npcCount == 1 ? "" : "s")} and {friendCount} friend marker{(friendCount == 1 ? "" : "s")}.";
    }

    private void ClearMarkers()
    {
        _playerMarker = null;
        for (int i = 0; i < _spawnedObjects.Count; i++)
        {
            if (_spawnedObjects[i] != null)
            {
                Destroy(_spawnedObjects[i]);
            }
        }

        _spawnedObjects.Clear();
    }

    private static Transform ResolvePlayer()
    {
        var inputs = FindFirstObjectByType<StarterAssetsInputs>(FindObjectsInactive.Exclude);
        if (inputs != null) return inputs.transform;

        try
        {
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null) return taggedPlayer.transform;
        }
        catch (UnityException)
        {
        }

        var characterController = FindFirstObjectByType<CharacterController>(FindObjectsInactive.Exclude);
        return characterController != null ? characterController.transform : null;
    }

    private class MapEntry
    {
        public Transform target;
        public string label;
        public Color color;
        public string symbol;
    }

    private class MapFeature
    {
        public Vector2 worldMin;
        public Vector2 worldMax;
        public MapFeatureKind kind;
        public Color color;
        public int order;
    }

    private enum MapFeatureKind
    {
        Unknown,
        Ground,
        Road,
        Building,
        Vehicle,
        Prop
    }
}
