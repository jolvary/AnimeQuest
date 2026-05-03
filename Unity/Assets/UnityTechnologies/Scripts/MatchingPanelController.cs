using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.UI;

public class MatchingPanelController : MonoBehaviour
{
    private static readonly Color PanelRowSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.22f);

    public int defaultLimit = 100;
    public Font preferredFont;

    private Text _titleText;
    private Text _statusText;
    private ScrollRect _scrollRect;
    private RectTransform _content;

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public async void RefreshMatches()
    {
        EnsureElements();
        _statusText.text = "Loading friend anime matches...";

        if (!TryGetAuth(out _))
        {
            ClearRows();
            _statusText.text = "Log in to see friend anime matches.";
            return;
        }

        try
        {
            var friendIds = await LoadAcceptedFriendIds();
            if (friendIds.Count == 0)
            {
                ClearRows();
                _statusText.text = "No friends found yet.";
                return;
            }

            string json = await ApiClient.Instance.GetAnimeMatches(string.Empty, Math.Max(defaultLimit, 1));
            var response = JsonUtility.FromJson<MatchingResponse>(json);
            ClearRows();

            if (response == null || response.items == null || response.items.Length == 0)
            {
                _statusText.text = "No anime matches with friends yet.";
                return;
            }

            await ResolveMatchingUsernames(response.items);
            var friendMatches = FilterMatchesToFriends(response.items, friendIds);
            if (friendMatches.Length == 0)
            {
                _statusText.text = "No anime matches with friends yet.";
                return;
            }

            foreach (var item in friendMatches)
            {
                CreateMatchRow(item);
            }

            _statusText.text = $"Loaded {friendMatches.Length} friend anime matches.";
            ResetScrollToTop();
        }
        catch (Exception ex)
        {
            ClearRows();
            _statusText.text = "Failed to load friend anime matches.";
            DozzleLogger.Error("Friend anime matches load failed", ex);
        }
    }

    private async Task<HashSet<string>> LoadAcceptedFriendIds()
    {
        var ids = new HashSet<string>();
        if (!TryGetAuth(out var auth)) return ids;

        try
        {
            var friendList = await auth.Client.ListFriendsAsync(auth.Session, null, 100, null);
            if (friendList == null || friendList.Friends == null) return ids;

            foreach (var friend in friendList.Friends)
            {
                if (friend?.User == null || string.IsNullOrWhiteSpace(friend.User.Id)) continue;
                if (Convert.ToInt32(friend.State) == 0)
                {
                    ids.Add(friend.User.Id);
                }
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Matching friends lookup failed", ex);
        }

        return ids;
    }

    private static MatchingAnimeItem[] FilterMatchesToFriends(MatchingAnimeItem[] items, HashSet<string> friendIds)
    {
        var filteredItems = new List<MatchingAnimeItem>();
        if (items == null || friendIds == null || friendIds.Count == 0) return filteredItems.ToArray();

        foreach (var item in items)
        {
            if (item == null || item.matchingUsers == null) continue;

            var friendUsers = new List<MatchingUser>();
            foreach (var user in item.matchingUsers)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.userId)) continue;
                if (friendIds.Contains(user.userId)) friendUsers.Add(user);
            }

            if (friendUsers.Count == 0) continue;

            item.matchingUsers = friendUsers.ToArray();
            item.matchCount = friendUsers.Count;
            filteredItems.Add(item);
        }

        return filteredItems.ToArray();
    }

    private async Task ResolveMatchingUsernames(MatchingAnimeItem[] items)
    {
        if (!TryGetAuth(out var auth)) return;

        var ids = new List<string>();
        foreach (var item in items)
        {
            if (item?.matchingUsers == null) continue;
            foreach (var user in item.matchingUsers)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.userId)) continue;
                if (!ids.Contains(user.userId)) ids.Add(user.userId);
            }
        }

        if (ids.Count == 0) return;

        try
        {
            var result = await auth.Client.GetUsersAsync(auth.Session, ids.ToArray());
            var namesById = new Dictionary<string, string>();
            if (result != null && result.Users != null)
            {
                foreach (var user in result.Users)
                {
                    if (user == null || string.IsNullOrWhiteSpace(user.Id)) continue;
                    if (!string.IsNullOrWhiteSpace(user.Username)) namesById[user.Id] = user.Username;
                }
            }

            foreach (var item in items)
            {
                if (item?.matchingUsers == null) continue;
                foreach (var match in item.matchingUsers)
                {
                    if (match == null || string.IsNullOrWhiteSpace(match.userId)) continue;
                    if (namesById.TryGetValue(match.userId, out string username))
                    {
                        match.displayName = username;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Matching usernames lookup failed", ex);
        }
    }

    private void EnsureElements()
    {
        if (_titleText == null)
        {
            _titleText = CreateText("MatchingTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -104f), new Vector2(-48f, -48f), 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.text = "Matching Anime";
        }

        if (_statusText == null)
        {
            _statusText = CreateText("MatchingStatus", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -162f), new Vector2(-48f, -120f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
        }

        if (_scrollRect == null || _content == null)
        {
            CreateListContainer();
        }

        ApplyFonts();
    }

    private void CreateListContainer()
    {
        var viewportObj = new GameObject("MatchingViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 48f);
        viewportRect.offsetMax = new Vector2(-48f, -184f);

        var viewportImage = viewportObj.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        _scrollRect = viewportObj.GetComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 26f;
        _scrollRect.viewport = viewportRect;

        var contentObj = new GameObject("MatchingContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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
        layout.spacing = 10f;
        layout.padding = new RectOffset(8, 8, 8, 8);

        var fitter = contentObj.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scrollRect.content = _content;
    }

    private void CreateMatchRow(MatchingAnimeItem item)
    {
        var rowObj = new GameObject($"Match_{item.id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        rowObj.transform.SetParent(_content, false);

        var image = rowObj.GetComponent<Image>();
        image.color = PanelRowSurfaceColor;

        var layout = rowObj.GetComponent<LayoutElement>();
        layout.minHeight = 118f;
        layout.preferredHeight = 118f;

        var vLayout = rowObj.GetComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(14, 14, 10, 10);
        vLayout.spacing = 4f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandHeight = false;

        CreateRowLabel(rowObj.transform, item.title, 18, FontStyle.Bold);
        CreateRowLabel(rowObj.transform, $"Your status: {FormatStatus(item.watchStatus)}  |  Watched: {FormatWatched(item.episodesWatched, item.episodes)}  |  Score: {FormatScore(item.score)}", 13, FontStyle.Normal);
        CreateRowLabel(rowObj.transform, $"Friend matches: {Math.Max(item.matchCount, 0)}", 13, FontStyle.Bold);
        CreateRowLabel(rowObj.transform, BuildUsersText(item.matchingUsers), 13, FontStyle.Normal);
    }

    private Text CreateRowLabel(Transform parent, string value, int fontSize, FontStyle style)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        var layout = obj.GetComponent<LayoutElement>();
        layout.minHeight = fontSize + 6f;

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

    private void ResetScrollToTop()
    {
        if (_scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 1f;
    }

    private static string BuildUsersText(MatchingUser[] users)
    {
        if (users == null || users.Length == 0) return "-";
        int count = Math.Min(users.Length, 5);
        string[] labels = new string[count];
        for (int i = 0; i < count; i++)
        {
            labels[i] = FormatMatchingUser(users[i]);
        }
        return string.Join(", ", labels);
    }

    private static string FormatMatchingUser(MatchingUser user)
    {
        string scoreText = user != null && user.score > 0 ? $" | Score: {FormatScore(user.score)}" : string.Empty;
        return $"{FormatDisplayName(user)} ({FormatStatus(user?.status)}{scoreText})";
    }

    private static string FormatDisplayName(MatchingUser user)
    {
        if (user == null) return "Player";
        if (!string.IsNullOrWhiteSpace(user.displayName)) return user.displayName;
        return string.IsNullOrWhiteSpace(user.userId) ? "Player" : $"player_{user.userId.Substring(0, Math.Min(6, user.userId.Length))}";
    }

    private static string FormatStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Not in list";
        switch (status.Trim().ToLowerInvariant())
        {
            case "watching": return "Watching";
            case "completed": return "Completed";
            case "planned": return "Planned";
            case "dropped": return "Dropped";
            case "on_hold": return "On Hold";
            default: return status;
        }
    }

    private static string FormatWatched(int watched, int total)
    {
        return total > 0 ? $"{Math.Max(watched, 0)}/{total}" : Math.Max(watched, 0).ToString();
    }

    private static string FormatScore(int score)
    {
        return score <= 0 ? "-" : score.ToString();
    }

    private static bool TryGetAuth(out NakamaAuthManager auth)
    {
        auth = NakamaAuthManager.Instance;
        return auth != null && auth.IsAuthenticated && auth.Client != null && auth.Session != null;
    }

    [Serializable]
    private class MatchingResponse
    {
        public MatchingAnimeItem[] items;
    }

    [Serializable]
    private class MatchingAnimeItem
    {
        public string id;
        public string title;
        public int episodes;
        public string watchStatus;
        public int score;
        public int episodesWatched;
        public int matchCount;
        public MatchingUser[] matchingUsers;
    }

    [Serializable]
    private class MatchingUser
    {
        public string userId;
        public string displayName;
        public string status;
        public int score;
        public int episodesWatched;
    }
}
