using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.UI;

public class NakamaFriendsPanelController : MonoBehaviour
{
    private static readonly Color PanelRowSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.22f);
    private static readonly Color SearchSurfaceColor = new Color(0.96f, 0.90f, 0.78f, 0.36f);

    public Font preferredFont;
    public UIManager uiManager;

    private Text _titleText;
    private Text _statusText;
    private InputField _searchInput;
    private ScrollRect _scrollRect;
    private RectTransform _content;
    private int _refreshGeneration;
    private readonly List<FriendDisplayRow> _allRows = new List<FriendDisplayRow>();

    public void ConfigureFont(Font font)
    {
        preferredFont = font;
        ApplyFonts();
    }

    public void Configure(UIManager manager, Font font)
    {
        uiManager = manager;
        ConfigureFont(font);
    }

    private void OnEnable()
    {
        EnsureElements();
        RefreshFriends();
    }

    public async void RefreshFriends()
    {
        EnsureElements();
        int generation = ++_refreshGeneration;

        if (!TryGetAuth(out var auth))
        {
            if (generation != _refreshGeneration) return;
            _allRows.Clear();
            ClearRows();
            SetStatus(IsIncognitoSessionActive()
                ? "Log in with an account to see and manage friends."
                : "Log in to manage friends.");
            SetInputsInteractable(false);
            return;
        }

        try
        {
            SetInputsInteractable(false);
            SetStatus("Loading friends and players...");
            ClearRows();
            _allRows.Clear();

            var rows = new List<FriendDisplayRow>();
            var rowsById = new Dictionary<string, FriendDisplayRow>();
            var friendList = await auth.Client.ListFriendsAsync(auth.Session, null, 100, null);

            if (generation != _refreshGeneration) return;

            if (friendList != null && friendList.Friends != null)
            {
                foreach (var friend in friendList.Friends)
                {
                    if (friend?.User == null || string.IsNullOrWhiteSpace(friend.User.Id)) continue;
                    var row = new FriendDisplayRow
                    {
                        userId = friend.User.Id,
                        displayName = string.IsNullOrWhiteSpace(friend.User.Username) ? friend.User.Id : friend.User.Username,
                        state = Convert.ToInt32(friend.State),
                    };
                    rows.Add(row);
                    rowsById[row.userId] = row;
                }
            }

            var directory = await LoadDirectoryUsers(auth);
            if (generation != _refreshGeneration) return;

            foreach (var user in directory)
            {
                if (rowsById.TryGetValue(user.userId, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(user.displayName) && IsGeneratedName(existing.displayName, existing.userId))
                    {
                        existing.displayName = user.displayName;
                    }
                    continue;
                }

                var row = new FriendDisplayRow
                {
                    userId = user.userId,
                    displayName = string.IsNullOrWhiteSpace(user.displayName) ? user.userId : user.displayName,
                    state = -1,
                };
                rows.Add(row);
                rowsById[row.userId] = row;
            }

            rows.RemoveAll(ShouldHideFromDirectory);
            rows.Sort(CompareFriendRows);
            _allRows.AddRange(rows);
            RenderFilteredRows();
            DozzleLogger.Action("Friends and players loaded", $"count={rows.Count}");
        }
        catch (Exception ex)
        {
            if (generation != _refreshGeneration) return;
            _allRows.Clear();
            ClearRows();
            SetStatus("Could not load friends.");
            DozzleLogger.Error("Friends list load failed", ex);
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                SetInputsInteractable(true);
            }
        }
    }

    private async Task<List<UserDirectoryEntry>> LoadDirectoryUsers(NakamaAuthManager auth)
    {
        var users = new List<UserDirectoryEntry>();
        if (ApiClient.Instance == null) return users;

        try
        {
            string json = await ApiClient.Instance.GetTable("users", 200, 0);
            var response = JsonUtility.FromJson<UsersTableResponse>(json);
            if (response?.rows == null) return users;

            string currentUserId = auth.Session?.UserId;
            foreach (var row in response.rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.user_id)) continue;
                if (!string.IsNullOrWhiteSpace(currentUserId) && string.Equals(row.user_id, currentUserId, StringComparison.OrdinalIgnoreCase)) continue;

                users.Add(new UserDirectoryEntry
                {
                    userId = row.user_id,
                    displayName = !string.IsNullOrWhiteSpace(row.display_name) ? row.display_name : row.user_id,
                });
            }

            await ResolveDirectoryUsernames(users, auth);
            users.RemoveAll(user => user != null && IsLikelyIncognitoName(user.displayName, user.userId));
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Player directory load failed", ex);
        }

        return users;
    }

    private async Task ResolveDirectoryUsernames(List<UserDirectoryEntry> users, NakamaAuthManager auth)
    {
        if (users == null || users.Count == 0 || auth?.Client == null || auth.Session == null) return;

        var ids = new List<string>();
        foreach (var user in users)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.userId)) continue;
            if (!ids.Contains(user.userId)) ids.Add(user.userId);
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
                    if (user == null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Username)) continue;
                    namesById[user.Id] = user.Username;
                }
            }

            foreach (var user in users)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.userId)) continue;
                if (namesById.TryGetValue(user.userId, out string username) && !IsLikelyIncognitoName(username, user.userId))
                {
                    user.displayName = username;
                }
            }
        }
        catch (Exception ex)
        {
            DozzleLogger.Error("Player directory usernames lookup failed", ex);
        }
    }

    private async void SendFriendRequest(string userId, string username)
    {
        if (string.IsNullOrWhiteSpace(userId) || !TryGetAuth(out var auth)) return;

        try
        {
            SetInputsInteractable(false);
            SetStatus("Sending friend request...");
            await auth.Client.AddFriendsAsync(auth.Session, new[] { userId });
            DozzleLogger.Action("Friend request sent", $"userId={userId};username={username}");
            RefreshFriends();
        }
        catch (Exception ex)
        {
            SetStatus("Friend request failed.");
            DozzleLogger.Error("Friend request failed", ex);
            SetInputsInteractable(true);
        }
    }

    private async void AcceptFriend(string userId, string username)
    {
        if (string.IsNullOrWhiteSpace(userId) || !TryGetAuth(out var auth)) return;

        try
        {
            SetStatus("Accepting friend request...");
            await auth.Client.AddFriendsAsync(auth.Session, new[] { userId });
            DozzleLogger.Action("Friend request accepted", $"userId={userId};username={username}");
            RefreshFriends();
        }
        catch (Exception ex)
        {
            SetStatus("Could not accept friend request.");
            DozzleLogger.Error("Friend request accept failed", ex);
        }
    }

    private async void RemoveFriend(string userId, string username, string actionLabel)
    {
        if (string.IsNullOrWhiteSpace(userId) || !TryGetAuth(out var auth)) return;

        try
        {
            SetStatus($"{actionLabel}...");
            await auth.Client.DeleteFriendsAsync(auth.Session, new[] { userId });
            DozzleLogger.Action("Friend relationship removed", $"action={actionLabel};userId={userId};username={username}");
            RefreshFriends();
        }
        catch (Exception ex)
        {
            SetStatus("Friend update failed.");
            DozzleLogger.Error("Friend relationship remove failed", ex);
        }
    }

    private void OpenChat(string userId, string username)
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        DozzleLogger.Action("Friend chat opened", $"userId={userId};username={username}");
        uiManager?.OpenChatPanelForUser(userId, username);
    }

    private void EnsureElements()
    {
        if (_titleText == null)
        {
            _titleText = CreateText("FriendsTitle", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -100f), new Vector2(-48f, -48f), 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.text = "Friends";
        }

        if (_statusText == null)
        {
            _statusText = CreateText("FriendsStatus", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -150f), new Vector2(-48f, -110f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            _statusText.text = "Log in to manage friends.";
        }

        if (_searchInput == null)
        {
            CreateToolbar();
        }

        if (_scrollRect == null || _content == null)
        {
            CreateListContainer();
        }

        ApplyFonts();
    }

    private void CreateToolbar()
    {
        var inputObj = new GameObject("FriendSearchInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        inputObj.transform.SetParent(transform, false);
        var inputRect = inputObj.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0f, 1f);
        inputRect.anchorMax = new Vector2(1f, 1f);
        inputRect.offsetMin = new Vector2(48f, -222f);
        inputRect.offsetMax = new Vector2(-48f, -164f);

        inputObj.GetComponent<Image>().color = SearchSurfaceColor;
        _searchInput = inputObj.GetComponent<InputField>();
        _searchInput.lineType = InputField.LineType.SingleLine;
        _searchInput.contentType = InputField.ContentType.Standard;
        _searchInput.onValueChanged.AddListener(_ => RenderFilteredRows());

        var inputText = CreateChildText(inputObj.transform, "Text", string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.black);
        inputText.rectTransform.offsetMin = new Vector2(14f, 8f);
        inputText.rectTransform.offsetMax = new Vector2(-14f, -8f);
        var placeholder = CreateChildText(inputObj.transform, "Placeholder", "Search players...", 18, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.45f, 0.45f, 0.45f, 1f));
        placeholder.rectTransform.offsetMin = new Vector2(14f, 8f);
        placeholder.rectTransform.offsetMax = new Vector2(-14f, -8f);
        _searchInput.textComponent = inputText;
        _searchInput.placeholder = placeholder;
    }

    private void CreateListContainer()
    {
        var viewportObj = new GameObject("FriendsViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObj.transform.SetParent(transform, false);

        var viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(48f, 48f);
        viewportRect.offsetMax = new Vector2(-48f, -242f);

        viewportObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        viewportObj.GetComponent<Mask>().showMaskGraphic = false;

        _scrollRect = viewportObj.GetComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 26f;
        _scrollRect.viewport = viewportRect;

        var contentObj = new GameObject("FriendsContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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

    private void RenderFilteredRows()
    {
        if (_content == null) return;

        ClearRows();
        string filter = _searchInput != null ? _searchInput.text.Trim() : string.Empty;
        int shown = 0;

        foreach (var row in _allRows)
        {
            if (!MatchesFilter(row, filter)) continue;
            CreateFriendRow(row);
            shown++;
        }

        if (_allRows.Count == 0)
        {
            SetStatus("No other players found yet.");
        }
        else if (shown == 0)
        {
            SetStatus("No players match that search.");
        }
        else if (string.IsNullOrWhiteSpace(filter))
        {
            SetStatus($"Loaded {shown} players and friend states.");
        }
        else
        {
            SetStatus($"Showing {shown} matching players.");
        }

        ResetScrollToTop();
    }

    private void CreateFriendRow(FriendDisplayRow row)
    {
        if (row == null || _content == null) return;

        string userId = row.userId;
        string username = string.IsNullOrWhiteSpace(row.displayName) ? userId : row.displayName;

        var rowObj = new GameObject($"Friend_{userId}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        rowObj.transform.SetParent(_content, false);
        rowObj.GetComponent<Image>().color = PanelRowSurfaceColor;
        var layoutElement = rowObj.GetComponent<LayoutElement>();
        layoutElement.minHeight = 92f;
        layoutElement.preferredHeight = 92f;

        var layout = rowObj.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateRowLabel(rowObj.transform, $"{username} - {FormatFriendState(row.state)}", 18, FontStyle.Bold);

        var actionsObj = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        actionsObj.transform.SetParent(rowObj.transform, false);
        actionsObj.GetComponent<LayoutElement>().minHeight = 34f;
        var actions = actionsObj.GetComponent<HorizontalLayoutGroup>();
        actions.spacing = 8f;
        actions.childControlWidth = false;
        actions.childControlHeight = true;
        actions.childForceExpandWidth = false;
        actions.childForceExpandHeight = false;

        if (row.state == 0)
        {
            CreateActionButton(actionsObj.transform, "Chat", () => OpenChat(userId, username));
            CreateActionButton(actionsObj.transform, "Remove", () => RemoveFriend(userId, username, "Removing friend"));
        }
        else if (row.state == 1)
        {
            CreateActionButton(actionsObj.transform, "Cancel", () => RemoveFriend(userId, username, "Cancelling request"));
        }
        else if (row.state == 2)
        {
            CreateActionButton(actionsObj.transform, "Accept", () => AcceptFriend(userId, username));
            CreateActionButton(actionsObj.transform, "Decline", () => RemoveFriend(userId, username, "Declining request"));
        }
        else if (row.state == 3)
        {
            CreateActionButton(actionsObj.transform, "Remove", () => RemoveFriend(userId, username, "Removing blocked user"));
        }
        else
        {
            CreateActionButton(actionsObj.transform, "Add", () => SendFriendRequest(userId, username));
        }
    }

    private Text CreateRowLabel(Transform parent, string value, int fontSize, FontStyle style)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<LayoutElement>().minHeight = fontSize + 6f;

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

    private Button CreateActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var buttonObj = new GameObject(label + "Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObj.transform.SetParent(parent, false);
        var layout = buttonObj.GetComponent<LayoutElement>();
        layout.minWidth = 104f;
        layout.preferredWidth = 118f;
        layout.minHeight = 34f;
        layout.preferredHeight = 34f;
        buttonObj.GetComponent<Image>().color = new Color(0.86f, 0.76f, 0.50f, 1f);

        var button = buttonObj.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        CreateChildText(buttonObj.transform, "Text", label, 15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black);
        return button;
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

    private void SetStatus(string value)
    {
        if (_statusText != null) _statusText.text = value;
    }

    private void SetInputsInteractable(bool interactable)
    {
        if (_searchInput != null) _searchInput.interactable = interactable;
    }

    private void ResetScrollToTop()
    {
        if (_scrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 1f;
    }

    private static bool MatchesFilter(FriendDisplayRow row, string filter)
    {
        if (row == null) return false;
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return (!string.IsNullOrWhiteSpace(row.displayName) && row.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrWhiteSpace(row.userId) && row.userId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static int CompareFriendRows(FriendDisplayRow a, FriendDisplayRow b)
    {
        int rankCompare = FriendStateRank(a.state).CompareTo(FriendStateRank(b.state));
        if (rankCompare != 0) return rankCompare;
        return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
    }

    private static int FriendStateRank(int state)
    {
        switch (state)
        {
            case 2: return 0;
            case 0: return 1;
            case 1: return 2;
            case -1: return 3;
            case 3: return 4;
            default: return 5;
        }
    }

    private static string FormatFriendState(int state)
    {
        switch (state)
        {
            case -1: return "Not friends";
            case 0: return "Friend";
            case 1: return "Request sent";
            case 2: return "Incoming request";
            case 3: return "Blocked";
            default: return "Unknown";
        }
    }

    private static bool ShouldHideFromDirectory(FriendDisplayRow row)
    {
        return row != null && row.state == -1 && IsLikelyIncognitoName(row.displayName, row.userId);
    }

    private static bool IsGeneratedName(string displayName, string userId)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(userId)) return false;
        return string.Equals(displayName, $"player_{userId.Substring(0, Math.Min(6, userId.Length))}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyIncognitoName(string displayName, string userId)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;
        if (IsGeneratedName(displayName, userId)) return true;

        string value = displayName.Trim();
        if (value.Length < 10 || value.Length > 18) return false;

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsLetterOrDigit(c)) return false;
            hasUpper |= char.IsUpper(c);
            hasLower |= char.IsLower(c);
            hasDigit |= char.IsDigit(c);
        }

        return hasUpper && hasLower && !hasDigit;
    }

    private static bool TryGetAuth(out NakamaAuthManager auth)
    {
        auth = NakamaAuthManager.Instance;
        return auth != null && auth.IsAuthenticated && !auth.IsIncognitoSession && auth.Client != null && auth.Session != null;
    }

    private static bool IsIncognitoSessionActive()
    {
        var auth = NakamaAuthManager.Instance;
        return auth != null && auth.IsAuthenticated && auth.IsIncognitoSession;
    }

    private class FriendDisplayRow
    {
        public string userId;
        public string displayName;
        public int state;
    }

    private class UserDirectoryEntry
    {
        public string userId;
        public string displayName;
    }

    [Serializable]
    private class UsersTableResponse
    {
        public UserTableRow[] rows;
    }

    [Serializable]
    private class UserTableRow
    {
        public string user_id;
        public string display_name;
    }
}
