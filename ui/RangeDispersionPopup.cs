using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class RangeDispersionPopup : CanvasLayer
{
    private const string DraftSessionMetadata = "__draft__";

    private PanelContainer _panel;
    private Label _titleLabel;
    private Button _closeButton;
    private OptionButton _sessionOption;
    private Button _newButton;
    private Button _deleteButton;
    private Button _compareButton;

    private HBoxContainer _compareControlsRow;
    private OptionButton _compareLeftOption;
    private OptionButton _compareRightOption;
    private Button _compareDoneButton;

    private VBoxContainer _singleView;
    private Label _singleSummaryLabel;
    private RangeDispersionPlot _singlePlot;
    private ItemList _singleLegend;

    private HBoxContainer _compareView;
    private Label _compareLeftSummaryLabel;
    private RangeDispersionPlot _compareLeftPlot;
    private ItemList _compareLeftLegend;
    private Label _compareRightSummaryLabel;
    private RangeDispersionPlot _compareRightPlot;
    private ItemList _compareRightLegend;

    private readonly List<RangeDispersionSession> _sessions = new();
    private readonly Dictionary<string, HashSet<string>> _singleViewDisabledClubsBySession =
        new(System.StringComparer.OrdinalIgnoreCase);
    private bool _isRangeMode;
    private bool _isCompareMode;
    private bool _isSyncingSelectors;
    private string _activeSessionFileName = string.Empty;
    private string _singleViewLegendSessionKey = string.Empty;
    private RangeDispersionSession _activeDraftSession;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Root/Panel");
        _titleLabel = GetNode<Label>("Root/Panel/Margin/Content/HeaderRow/HeaderMargin/HeaderHBox/TitleLabel");
        _closeButton = GetNode<Button>("Root/Panel/Margin/Content/HeaderRow/HeaderMargin/HeaderHBox/CloseButton");
        _sessionOption = GetNode<OptionButton>("Root/Panel/Margin/Content/SessionControls/SessionOption");
        _newButton = GetNode<Button>("Root/Panel/Margin/Content/SessionControls/NewButton");
        _deleteButton = GetNode<Button>("Root/Panel/Margin/Content/SessionControls/DeleteButton");
        _compareButton = GetNode<Button>("Root/Panel/Margin/Content/SessionControls/CompareButton");
        _compareControlsRow = GetNode<HBoxContainer>("Root/Panel/Margin/Content/CompareControls");
        _compareLeftOption = GetNode<OptionButton>("Root/Panel/Margin/Content/CompareControls/LeftOption");
        _compareRightOption = GetNode<OptionButton>("Root/Panel/Margin/Content/CompareControls/RightOption");
        _compareDoneButton = GetNode<Button>("Root/Panel/Margin/Content/CompareControls/DoneButton");

        _singleView = GetNode<VBoxContainer>("Root/Panel/Margin/Content/SingleView");
        _singleSummaryLabel = GetNode<Label>("Root/Panel/Margin/Content/SingleView/SummaryLabel");
        _singlePlot = GetNode<RangeDispersionPlot>("Root/Panel/Margin/Content/SingleView/ChartRow/Plot");
        _singleLegend = GetNode<ItemList>("Root/Panel/Margin/Content/SingleView/ChartRow/LegendContainer/Legend");

        _compareView = GetNode<HBoxContainer>("Root/Panel/Margin/Content/CompareView");
        _compareLeftSummaryLabel = GetNode<Label>("Root/Panel/Margin/Content/CompareView/LeftColumn/LeftSummary");
        _compareLeftPlot = GetNode<RangeDispersionPlot>("Root/Panel/Margin/Content/CompareView/LeftColumn/LeftPlot");
        _compareLeftLegend = GetNode<ItemList>("Root/Panel/Margin/Content/CompareView/LeftColumn/LeftLegend");
        _compareRightSummaryLabel = GetNode<Label>("Root/Panel/Margin/Content/CompareView/RightColumn/RightSummary");
        _compareRightPlot = GetNode<RangeDispersionPlot>("Root/Panel/Margin/Content/CompareView/RightColumn/RightPlot");
        _compareRightLegend = GetNode<ItemList>("Root/Panel/Margin/Content/CompareView/RightColumn/RightLegend");

        _closeButton.Pressed += HidePanel;
        _newButton.Pressed += OnNewPressed;
        _deleteButton.Pressed += OnDeletePressed;
        _compareButton.Pressed += OnComparePressed;
        _compareDoneButton.Pressed += OnCompareDonePressed;
        _sessionOption.ItemSelected += OnSessionSelected;
        _compareLeftOption.ItemSelected += OnCompareOptionChanged;
        _compareRightOption.ItemSelected += OnCompareOptionChanged;
        _singleLegend.GuiInput += OnSingleLegendGuiInput;

        _titleLabel.Text = "Shot Dispersion";
        Visible = false;
        _isRangeMode = false;
        _isCompareMode = false;
        ReloadSessions();
        ApplyViewState();
    }

    public override void _ExitTree()
    {
        if (_closeButton != null)
            _closeButton.Pressed -= HidePanel;
        if (_newButton != null)
            _newButton.Pressed -= OnNewPressed;
        if (_deleteButton != null)
            _deleteButton.Pressed -= OnDeletePressed;
        if (_compareButton != null)
            _compareButton.Pressed -= OnComparePressed;
        if (_compareDoneButton != null)
            _compareDoneButton.Pressed -= OnCompareDonePressed;
        if (_sessionOption != null)
            _sessionOption.ItemSelected -= OnSessionSelected;
        if (_compareLeftOption != null)
            _compareLeftOption.ItemSelected -= OnCompareOptionChanged;
        if (_compareRightOption != null)
            _compareRightOption.ItemSelected -= OnCompareOptionChanged;
        if (_singleLegend != null)
            _singleLegend.GuiInput -= OnSingleLegendGuiInput;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible)
            return;

        if (@event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            HidePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    public void SetRangeMode(bool enabled)
    {
        bool wasRangeMode = _isRangeMode;
        _isRangeMode = enabled;
        if (_isRangeMode)
        {
            if (!wasRangeMode)
            {
                StartFreshSessionForRangeOpen();
                return;
            }

            ReloadSessions();
            return;
        }

        if (!_isRangeMode)
            HidePanel();
    }

    public void TogglePanel()
    {
        if (!_isRangeMode)
            return;

        if (Visible)
            HidePanel();
        else
            ShowPanel();
    }

    public void ShowPanel()
    {
        if (!_isRangeMode)
            return;

        ReloadSessions();
        Visible = true;
        ApplyViewState();
    }

    public void HidePanel()
    {
        Visible = false;
        _isCompareMode = false;
        ApplyViewState();
    }

    public void RecordShot(
        string clubLabel,
        float distanceYards,
        float carryYards,
        float offlineYards,
        float? hlaDeg = null,
        float? totalSpinRpm = null)
    {
        if (!_isRangeMode)
            return;

        if (distanceYards <= 0.01f)
            return;

        EnsureActiveSession();
        bool recordingIntoDraft = IsDraftActive();
        string targetFileName = recordingIntoDraft
            ? _activeDraftSession?.FileName
            : _activeSessionFileName;
        if (string.IsNullOrWhiteSpace(targetFileName))
            return;

        var shot = new RangeDispersionShot(clubLabel, distanceYards, carryYards, offlineYards, hlaDeg, totalSpinRpm);
        if (!RangeDispersionStore.AppendShot(targetFileName, shot, out RangeDispersionSession updatedSession))
            return;

        if (recordingIntoDraft)
        {
            _activeDraftSession = null;
            _activeSessionFileName = updatedSession.FileName;
        }

        ReplaceOrAddSession(updatedSession);
        SortSessionsNewestFirst();
        PopulateSelectors();

        if (Visible)
            ApplyViewState();
    }

    private void OnNewPressed()
    {
        StartDraftSession();
        ReloadSessions();
        _isCompareMode = false;
        ApplyViewState();
    }

    private void OnDeletePressed()
    {
        if (string.IsNullOrWhiteSpace(_activeSessionFileName))
            return;

        RangeDispersionStore.DeleteSession(_activeSessionFileName);
        ReloadSessions();

        if (_sessions.Count <= 1)
            _isCompareMode = false;

        ApplyViewState();
    }

    private void OnComparePressed()
    {
        if (_sessions.Count <= 1)
            return;

        _isCompareMode = true;
        PopulateCompareDefaults();
        ApplyViewState();
    }

    private void OnCompareDonePressed()
    {
        _isCompareMode = false;
        ApplyViewState();
    }

    private void OnSessionSelected(long index)
    {
        if (_isSyncingSelectors)
            return;

        string selectedFile = GetOptionMetadata(_sessionOption, index);
        if (selectedFile == DraftSessionMetadata)
        {
            _activeSessionFileName = string.Empty;
            ApplyViewState();
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedFile))
            return;

        _activeSessionFileName = selectedFile;
        if (_isCompareMode)
            PopulateCompareDefaults();
        ApplyViewState();
    }

    private void OnCompareOptionChanged(long _index)
    {
        if (_isSyncingSelectors)
            return;

        ApplyViewState();
    }

    private void ReloadSessions()
    {
        List<RangeDispersionSession> loaded = RangeDispersionStore.LoadAllSessions();
        _sessions.Clear();
        _sessions.AddRange(loaded);

        SortSessionsNewestFirst();
        PruneSingleViewLegendState();
        if (!IsDraftActive() && !HasSession(_activeSessionFileName))
            _activeSessionFileName = _sessions.Count > 0 ? _sessions[0].FileName : string.Empty;

        PopulateSelectors();
    }

    private void PopulateSelectors()
    {
        _isSyncingSelectors = true;
        PopulateOptionFromSessions(_sessionOption, _activeSessionFileName, includeDraftOption: true);
        PopulateOptionFromSessions(_compareLeftOption, _activeSessionFileName, includeDraftOption: false);
        PopulateOptionFromSessions(_compareRightOption, _activeSessionFileName, includeDraftOption: false);
        _isSyncingSelectors = false;
    }

    private void PopulateOptionFromSessions(OptionButton option, string selectedFileName, bool includeDraftOption)
    {
        if (option == null)
            return;

        option.Clear();
        int selectedIndex = -1;
        if (includeDraftOption && _activeDraftSession != null)
        {
            option.AddItem(RangeDispersionStore.BuildSessionLabel(_activeDraftSession));
            option.SetItemMetadata(0, DraftSessionMetadata);
            if (IsDraftActive())
                selectedIndex = 0;
        }

        for (int i = 0; i < _sessions.Count; i++)
        {
            RangeDispersionSession session = _sessions[i];
            int optionIndex = option.ItemCount;
            option.AddItem(RangeDispersionStore.BuildSessionLabel(session));
            option.SetItemMetadata(optionIndex, session.FileName);
            if (session.FileName == selectedFileName)
                selectedIndex = optionIndex;
        }

        if (selectedIndex < 0 && option.ItemCount > 0)
            selectedIndex = 0;

        if (selectedIndex >= 0)
            option.Select(selectedIndex);

        option.Disabled = option.ItemCount == 0;
    }

    private void PopulateCompareDefaults()
    {
        if (_sessions.Count == 0)
            return;

        int activeIndex = FindSessionIndex(_activeSessionFileName);
        if (activeIndex < 0)
            activeIndex = 0;

        int rightIndex = activeIndex + 1;
        if (rightIndex >= _sessions.Count)
            rightIndex = activeIndex == 0 ? 1 : 0;

        _isSyncingSelectors = true;
        _compareLeftOption.Select(activeIndex);
        if (_sessions.Count > 1)
            _compareRightOption.Select(rightIndex);
        _isSyncingSelectors = false;
    }

    private void ApplyViewState()
    {
        bool hasMultiSession = _sessions.Count > 1;
        _compareButton.Visible = !_isCompareMode && hasMultiSession;
        _compareButton.Disabled = !hasMultiSession;

        _compareControlsRow.Visible = _isCompareMode && hasMultiSession;
        _singleView.Visible = !_isCompareMode;
        _compareView.Visible = _isCompareMode && hasMultiSession;

        _deleteButton.Disabled = string.IsNullOrWhiteSpace(_activeSessionFileName) || !HasSession(_activeSessionFileName);
        _panel.Visible = Visible;

        if (_isCompareMode && hasMultiSession)
            RefreshCompareView();
        else
            RefreshSingleView();
    }

    private void OnSingleLegendGuiInput(InputEvent @event)
    {
        if (_isCompareMode || !Visible || _singleLegend == null)
            return;

        if (@event is not InputEventMouseButton mouseEvent)
            return;

        if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        int itemIndex = _singleLegend.GetItemAtPosition(_singleLegend.GetLocalMousePosition(), false);
        if (itemIndex < 0)
            return;

        string clubLabel = GetLegendClubLabel(_singleLegend, itemIndex);
        if (string.IsNullOrWhiteSpace(clubLabel))
            return;

        if (!_singleViewDisabledClubsBySession.TryGetValue(_singleViewLegendSessionKey, out HashSet<string> disabledClubs))
            return;

        if (!disabledClubs.Add(clubLabel))
            disabledClubs.Remove(clubLabel);

        RefreshSingleView();
        GetViewport().SetInputAsHandled();
    }

    private void RefreshSingleView()
    {
        RangeDispersionSession session = IsDraftActive()
            ? _activeDraftSession
            : GetSessionByFileName(_activeSessionFileName);
        if (session == null)
        {
            _singleSummaryLabel.Text = "No dispersion sessions found.";
            _singleSummaryLabel.Visible = true;
            _singleViewLegendSessionKey = string.Empty;
            _singlePlot.SetVisibleClubs(null);
            _singlePlot.SetShots(null);
            PopulateLegend(_singleLegend, null);
            return;
        }

        string sessionKey = BuildSingleViewSessionKey(session, IsDraftActive());
        HashSet<string> disabledClubs = GetOrCreateDisabledClubs(sessionKey, session.Shots);
        _singleViewLegendSessionKey = sessionKey;

        _singleSummaryLabel.Text = string.Empty;
        _singleSummaryLabel.Visible = false;
        _singlePlot.SetClubOverlayEnabled(true);
        _singlePlot.SetVisibleClubs(BuildVisibleClubs(session.Shots, disabledClubs));
        _singlePlot.SetShots(session.Shots);
        PopulateLegend(_singleLegend, session.Shots, disabledClubs, interactive: true);
    }

    private void RefreshCompareView()
    {
        string leftFile = GetSelectedOptionFileName(_compareLeftOption);
        string rightFile = GetSelectedOptionFileName(_compareRightOption);

        RangeDispersionSession leftSession = GetSessionByFileName(leftFile);
        RangeDispersionSession rightSession = GetSessionByFileName(rightFile);

        if (leftSession == null && _sessions.Count > 0)
            leftSession = _sessions[0];

        if (rightSession == null && _sessions.Count > 1)
            rightSession = _sessions[1];

        _compareLeftSummaryLabel.Text = leftSession != null
            ? RangeDispersionStore.BuildSessionLabel(leftSession)
            : "No data";
        _compareRightSummaryLabel.Text = rightSession != null
            ? RangeDispersionStore.BuildSessionLabel(rightSession)
            : "No data";

        _compareLeftPlot.SetClubOverlayEnabled(false);
        _compareRightPlot.SetClubOverlayEnabled(false);
        _compareLeftPlot.SetVisibleClubs(null);
        _compareRightPlot.SetVisibleClubs(null);
        _compareLeftPlot.SetShots(leftSession?.Shots);
        _compareRightPlot.SetShots(rightSession?.Shots);
        PopulateLegend(_compareLeftLegend, leftSession?.Shots);
        PopulateLegend(_compareRightLegend, rightSession?.Shots);
    }

    private static void PopulateLegend(
        ItemList legend,
        IReadOnlyList<RangeDispersionShot> shots,
        ISet<string> disabledClubs = null,
        bool interactive = false)
    {
        if (legend == null)
            return;

        legend.Clear();
        if (shots == null || shots.Count == 0)
        {
            legend.AddItem("No clubs");
            legend.SetItemDisabled(0, true);
            return;
        }

        IEnumerable<IGrouping<string, RangeDispersionShot>> groups = shots
            .Where(shot => shot != null)
            .GroupBy(shot => RangeClubCatalog.NormalizeLabel(shot.ClubLabel))
            .OrderBy(group => ResolveClubSortOrder(group.Key));

        foreach (IGrouping<string, RangeDispersionShot> group in groups)
        {
            int itemIndex = legend.ItemCount;
            bool isDisabled = disabledClubs != null && disabledClubs.Contains(group.Key);
            string label = $"{group.Key} ({group.Count()})";
            if (interactive && isDisabled)
                label += " (off)";

            legend.AddItem(label);
            legend.SetItemMetadata(itemIndex, group.Key);

            Color baseColor = RangeDispersionPlot.ResolveClubColor(group.Key);
            Color finalColor = isDisabled
                ? baseColor.Lerp(new Color(0.68f, 0.72f, 0.80f, 0.82f), 0.72f)
                : baseColor;

            legend.SetItemCustomFgColor(itemIndex, finalColor);
        }
    }

    private static int ResolveClubSortOrder(string clubLabel)
    {
        string normalized = RangeClubCatalog.NormalizeLabel(clubLabel);
        for (int i = 0; i < RangeClubCatalog.Labels.Count; i++)
        {
            if (RangeClubCatalog.Labels[i] == normalized)
                return i;
        }

        return int.MaxValue;
    }

    private static string BuildSingleViewSessionKey(RangeDispersionSession session, bool isDraft)
    {
        if (isDraft)
            return DraftSessionMetadata;

        return session?.FileName ?? string.Empty;
    }

    private HashSet<string> GetOrCreateDisabledClubs(string sessionKey, IReadOnlyList<RangeDispersionShot> shots)
    {
        if (!_singleViewDisabledClubsBySession.TryGetValue(sessionKey, out HashSet<string> disabledClubs))
        {
            disabledClubs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            _singleViewDisabledClubsBySession[sessionKey] = disabledClubs;
        }

        HashSet<string> clubsInSession = CollectSessionClubs(shots);
        disabledClubs.RemoveWhere(club => !clubsInSession.Contains(club));
        return disabledClubs;
    }

    private static HashSet<string> BuildVisibleClubs(
        IReadOnlyList<RangeDispersionShot> shots,
        HashSet<string> disabledClubs)
    {
        var visibleClubs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (shots == null || shots.Count == 0)
            return visibleClubs;

        foreach (RangeDispersionShot shot in shots)
        {
            if (shot == null)
                continue;

            string clubLabel = RangeClubCatalog.NormalizeLabel(shot.ClubLabel);
            if (disabledClubs.Contains(clubLabel))
                continue;

            visibleClubs.Add(clubLabel);
        }

        return visibleClubs;
    }

    private static HashSet<string> CollectSessionClubs(IReadOnlyList<RangeDispersionShot> shots)
    {
        var clubs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (shots == null)
            return clubs;

        foreach (RangeDispersionShot shot in shots)
        {
            if (shot == null)
                continue;

            clubs.Add(RangeClubCatalog.NormalizeLabel(shot.ClubLabel));
        }

        return clubs;
    }

    private static string GetLegendClubLabel(ItemList legend, int itemIndex)
    {
        if (legend == null || itemIndex < 0 || itemIndex >= legend.ItemCount)
            return string.Empty;

        Variant metadata = legend.GetItemMetadata(itemIndex);
        string clubLabel = metadata.VariantType == Variant.Type.String
            ? (string)metadata
            : metadata.ToString();

        return RangeClubCatalog.NormalizeLabel(clubLabel);
    }

    private void PruneSingleViewLegendState()
    {
        var activeSessionKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            DraftSessionMetadata
        };

        foreach (RangeDispersionSession session in _sessions)
        {
            if (!string.IsNullOrWhiteSpace(session?.FileName))
                activeSessionKeys.Add(session.FileName);
        }

        string[] staleKeys = _singleViewDisabledClubsBySession.Keys
            .Where(key => !activeSessionKeys.Contains(key))
            .ToArray();

        foreach (string staleKey in staleKeys)
            _singleViewDisabledClubsBySession.Remove(staleKey);
    }

    private void ReplaceOrAddSession(RangeDispersionSession updatedSession)
    {
        if (updatedSession == null)
            return;

        int existingIndex = FindSessionIndex(updatedSession.FileName);
        if (existingIndex >= 0)
            _sessions[existingIndex] = updatedSession;
        else
            _sessions.Add(updatedSession);
    }

    private void SortSessionsNewestFirst()
    {
        _sessions.Sort((a, b) => string.CompareOrdinal(b.FileName, a.FileName));
    }

    private void EnsureActiveSession()
    {
        if (HasSession(_activeSessionFileName) || IsDraftActive())
            return;

        ReloadSessions();
        if (HasSession(_activeSessionFileName) || IsDraftActive())
            return;

        StartDraftSession();
        PopulateSelectors();
    }

    private bool IsDraftActive()
    {
        return _activeDraftSession != null && string.IsNullOrWhiteSpace(_activeSessionFileName);
    }

    private bool HasSession(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return _sessions.Any(session => session.FileName == fileName);
    }

    private int FindSessionIndex(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return -1;

        for (int i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].FileName == fileName)
                return i;
        }

        return -1;
    }

    private RangeDispersionSession GetSessionByFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        return _sessions.FirstOrDefault(session => session.FileName == fileName);
    }

    private static string GetOptionMetadata(OptionButton option, long index)
    {
        if (option == null || index < 0 || index >= option.ItemCount)
            return string.Empty;

        Variant metadata = option.GetItemMetadata((int)index);
        return metadata.VariantType == Variant.Type.String ? (string)metadata : metadata.ToString();
    }

    private static string GetSelectedOptionFileName(OptionButton option)
    {
        if (option == null || option.ItemCount == 0)
            return string.Empty;

        int selected = option.Selected;
        if (selected < 0 || selected >= option.ItemCount)
            selected = 0;

        return GetOptionMetadata(option, selected);
    }

    private void StartDraftSession()
    {
        _activeDraftSession = RangeDispersionStore.CreateNewSession(persist: false);
        _activeSessionFileName = string.Empty;
    }

    private void StartFreshSessionForRangeOpen()
    {
        StartDraftSession();
        ReloadSessions();
        _isCompareMode = false;
        ApplyViewState();
    }
}
