using KeePassXCCardPrint.Platforms;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia.Threading;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

// View-only modes: they never mutate selection or print tracking.
public enum RecordQuickFilter { All, Selected, Remaining, Reprint }

public sealed partial class ShellViewModel
{
    private readonly IKdbxReader _reader = new KdbxVaultReader();
    private readonly VaultPreferences _prefs = PrivateUiState.Load();
    private readonly Dictionary<string, FieldRule> _map = FieldMappingStore.Load();
    private string _passwordInput = "";
    private string _filterText = "";
    private RecordQuickFilter _recordQuickFilter;
    // Remember only whether the successful unlock used a password, NEVER the password itself.
    private bool _lastVaultUsedPassword;
    private bool _isDemoMode;
    public bool IsDemoMode => _isDemoMode;
    public bool IsRealMode => !_isDemoMode;
    private string _vaultStatus = "База не открыта";
    private bool _busy;
    private bool _bulkSelection;
    private readonly DispatcherTimer _selectionSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _showPassword;
    private int _selectedCount;
    private VaultSnapshot? _snapshot;
    public ObservableCollection<VaultRecord> VaultRecords { get; } = new();
    public ObservableCollection<string> YubiDevices { get; } = new();

    private void InitializeVault()
    {
        _selectionSaveTimer.Tick += (_, _) =>
        {
            _selectionSaveTimer.Stop();
            SaveVaultPreferences();
        };
        InitializeMapping();
    }

    public string DatabasePath { get => _prefs.DatabasePath; set { _prefs.DatabasePath = value ?? ""; Changed(); } }
    public string KeyFilePath { get => _prefs.KeyFilePath; set { _prefs.KeyFilePath = value ?? ""; Changed(); } }
    public string PasswordInput { get => _passwordInput; set { _passwordInput = value ?? ""; Changed(); } }
    public bool ShowPassword { get => _showPassword; set { _showPassword = value; Changed(); } }
    public bool RememberPaths { get => _prefs.RememberPaths; set { _prefs.RememberPaths = value; Changed(); SaveVaultPreferences(); } }
    public bool RememberSelection { get => _prefs.RememberSelection; set { _prefs.RememberSelection = value; Changed(); SaveVaultPreferences(); } }
    public bool UseYubiKey { get => _prefs.UseYubiKey; set { _prefs.UseYubiKey = value; Changed(); SaveVaultPreferences(); } }
    public bool YubiShortChallenge { get => _prefs.YubiShortChallenge; set { _prefs.YubiShortChallenge = value; Changed(); SaveVaultPreferences(); } }
    // Radio buttons send both an unchecked and a checked event. Only the
    // checked event selects the slot; do not let an uncheck overwrite it.
    public bool YubiSlotOne { get => _prefs.YubiSlot != 2; set { if (value) SelectYubiSlot(1); } }
    public bool YubiSlotTwo { get => _prefs.YubiSlot == 2; set { if (value) SelectYubiSlot(2); } }
    private void SelectYubiSlot(int slot)
    {
        if (_prefs.YubiSlot == slot) return;
        _prefs.YubiSlot = slot;
        Changed(nameof(YubiSlotOne));
        Changed(nameof(YubiSlotTwo));
        SaveVaultPreferences();
    }
    public string YubiSerial { get => _prefs.YubiSerial; set { _prefs.YubiSerial = value ?? ""; Changed(); SaveVaultPreferences(); } }
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == (value ?? "")) return;
            _filterText = value ?? "";
            Changed();
            ApplyRecordFilters();
        }
    }
    public RecordQuickFilter ActiveRecordFilter => _recordQuickFilter;
    public bool FilterSelectedActive => _recordQuickFilter == RecordQuickFilter.Selected;
    public bool FilterRemainingActive => _recordQuickFilter == RecordQuickFilter.Remaining;
    public bool FilterReprintActive => _recordQuickFilter == RecordQuickFilter.Reprint;
    public bool HasRecords => !_busy && _snapshot is not null;
    public bool CanRefreshRecords => HasRecords && !_isDemoMode;
    public bool RequiresPasswordForRefresh => _lastVaultUsedPassword;
    public string FilterSelectedTip => Localizer.Text(_settings.Language, "QuickFilterSelected");
    public string FilterRemainingTip => Localizer.Text(_settings.Language, "QuickFilterRemaining");
    public string FilterReprintTip => Localizer.Text(_settings.Language, "QuickFilterReprint");
    public string RefreshRecordsTip => Localizer.Text(_settings.Language, "QuickRefreshRecords");
    public string ResetRecordFiltersTip => Localizer.Text(_settings.Language, "QuickResetFilters");

    public void ToggleRecordQuickFilter(RecordQuickFilter filter)
    {
        if (filter is RecordQuickFilter.All) return;
        SetRecordQuickFilter(_recordQuickFilter == filter ? RecordQuickFilter.All : filter);
    }

    private void SetRecordQuickFilter(RecordQuickFilter filter)
    {
        if (_recordQuickFilter == filter) return;
        _recordQuickFilter = filter;
        NotifyRecordFilterState();
        ApplyRecordFilters();
    }

    private void NotifyRecordFilterState()
    {
        Changed(nameof(ActiveRecordFilter));
        Changed(nameof(FilterSelectedActive));
        Changed(nameof(FilterRemainingActive));
        Changed(nameof(FilterReprintActive));
    }

    public void ResetRecordFilters()
    {
        bool changed = _recordQuickFilter != RecordQuickFilter.All || _filterText.Length != 0;
        if (!changed) return;
        _filterText = "";
        _recordQuickFilter = RecordQuickFilter.All;
        Changed(nameof(FilterText));
        NotifyRecordFilterState();
        ApplyRecordFilters();
    }

    private void ApplyRecordFilters()
    {
        // Text search is an AND condition on top of ONE mutually exclusive quick mode.
        // A missing/unknown print status is NOT proof that the card was printed.
        var query = _filterText.Trim();
        foreach (var record in VaultRecords)
        {
            bool matchesText = query.Length == 0 ||
                record.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                record.UserName.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool fullyPrinted = _printLedger?.IsFullyPrinted(record.Uuid) == true;
            bool matchesMode = _recordQuickFilter switch
            {
                RecordQuickFilter.Selected => record.Selected,
                RecordQuickFilter.Remaining => record.Selected && !fullyPrinted,
                RecordQuickFilter.Reprint => fullyPrinted,
                _ => true
            };
            record.Visible = matchesText && matchesMode;
        }
        Changed(nameof(Status));
        if (MappingScopeCurrent) RebuildMapping();
        ScheduleCardPreview();
    }
    public string VaultStatus { get => _vaultStatus; set { _vaultStatus = value; Changed(); Changed(nameof(Status)); } }
    public bool Busy { get => _busy; set { _busy = value; Changed(); Changed(nameof(CanOpen)); Changed(nameof(HasRecords)); Changed(nameof(CanRefreshRecords)); Changed(nameof(Status)); Changed(nameof(PrintReady)); } }
    public bool CanOpen => !_busy;
    public string SelectedCountText => Localizer.ResolveLanguage(_settings.Language) switch
    {
        "uk" => "Обрано: " + _selectedCount,
        "en" => "Selected: " + _selectedCount,
        _ => "Выбрано: " + _selectedCount
    };

    // A separate, trimmable database title keeps the operational counters
    // visible in a narrow window. Both pieces use the same status source.
    public bool HasLoadedStatus => !Busy && _snapshot is not null &&
        (_isDemoMode || (!VaultStatus.StartsWith('[') &&
                         VaultStatus.StartsWith("KDBX ", StringComparison.Ordinal)));

    public bool ShowOperationalStatus => !HasLoadedStatus;
    public string StatusLead => HasLoadedStatus ? _snapshot!.DisplayName : UiText.Tr(BuildOperationalStatus(),_settings.Language);
    public string StatusTail => HasLoadedStatus ? " · " + BuildVaultDetails() : "";

    private string BuildVaultDetails()
    {
        if (_snapshot is null) return "";
        var security = _isDemoMode ? "DEMO" : $"KDBX {_snapshot.FormatVersion} / {_snapshot.CipherName} / {_snapshot.KdfName}";
        var visible = VaultRecords.Count(r => r.Visible);
        return Localizer.ResolveLanguage(_settings.Language) switch
        {
            "uk" => $"{security} · Груп: {_snapshot.GroupCount} · Записів: {VaultRecords.Count} · У списку: {visible} · Для друку: {_selectedCount}",
            "en" => $"{security} · Groups: {_snapshot.GroupCount} · Entries: {VaultRecords.Count} · Shown: {visible} · For printing: {_selectedCount}",
            _ => $"{security} · Групп: {_snapshot.GroupCount} · Записей: {VaultRecords.Count} · В списке: {visible} · Для печати: {_selectedCount}"
        };
    }

    private string BuildOperationalStatus()
    {
        if (Busy) return VaultStatus;
        if (_isDemoMode && _snapshot is not null)
            return _snapshot.DisplayName + " · " + BuildVaultDetails();
        if (VaultStatus.StartsWith('[') ||
            (VaultStatus != "База закрыта" && VaultStatus != "База не открыта" &&
             !VaultStatus.StartsWith("KDBX ", StringComparison.Ordinal)))
            return VaultStatus;
        if (_snapshot is null)
            return Localizer.ResolveLanguage(_settings.Language) switch
            {
                "uk" => "Базу не відкрито",
                "en" => "Database closed",
                _ => "База не открыта"
            };
        return _snapshot.DisplayName + " · " + BuildVaultDetails();
    }

    public void RefreshDeviceList()
    {
        YubiDevices.Clear();
        foreach (var serial in PlatformServices.Current.SecurityKeys.AvailableSerials()) YubiDevices.Add(serial);
        VaultStatus = YubiDevices.Count == 0 && UseYubiKey ? "YubiKey не обнаружен" : VaultStatus;
    }

    public async Task OpenVaultAsync(bool preserveCurrentView = false)
    {
        if (Busy) return;
        Busy = true;
        VaultStatus = "Открываю KDBX…";
        var password = PasswordInput;
        // Snapshot the current, possibly unsaved, checkbox state before a reload.
        HashSet<Guid>? retainedSelection = preserveCurrentView
            ? VaultRecords.Where(r => r.Selected).Select(r => r.Uuid).ToHashSet()
            : null;
        var retainedText = _filterText;
        var retainedMode = _recordQuickFilter;
        try
        {
            var input = new VaultCredentials(DatabasePath, password, string.IsNullOrWhiteSpace(KeyFilePath) ? null : KeyFilePath,
                                             UseYubiKey, _prefs.YubiSlot, _prefs.YubiShortChallenge,
                                             string.IsNullOrWhiteSpace(YubiSerial) || YubiSerial == "без номера" ? null : YubiSerial);
            var snapshot = await _reader.ReadAsync(input,
                message => Dispatcher.UIThread.Post(() => { if (Busy) VaultStatus = message; }));
            ClearVault();
            _snapshot = snapshot;
            _lastVaultUsedPassword = !string.IsNullOrEmpty(password);
            if (preserveCurrentView && retainedSelection is not null)
            {
                foreach (var record in snapshot.Records)
                    record.Selected = retainedSelection.Contains(record.Uuid);
                _filterText = retainedText;
                _recordQuickFilter = retainedMode;
            }
            else
            {
                PrivateUiState.RestoreSelection(_prefs, DatabasePath, snapshot.Records);
                _filterText = "";
                _recordQuickFilter = RecordQuickFilter.All;
            }
            Changed(nameof(FilterText));
            NotifyRecordFilterState();
            foreach (var record in snapshot.Records)
            {
                record.PropertyChanged += RecordChanged;
                VaultRecords.Add(record);
            }
            bool ledgerFailed=false;
            try { LoadPrintLedger(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { _printLedger=null; ledgerFailed=true; }
            ApplyRecordFilters();
            UpdateSelectedCount();
            RebuildMapping();
            ScheduleCardPreview();
            SaveVaultPreferences();
            VaultStatus = ledgerFailed
                ? "Журнал печати недоступен; прежние статусы не менялись. Проверьте резервную копию."
                : $"KDBX {snapshot.FormatVersion} · Записей: {VaultRecords.Count}";
        }
        catch (Exception e)
        {
            // Never display exception.ToString(), file paths, decrypted data, or SDK diagnostics.
            // Messages from third-party KDBX/YubiKey libraries can contain file
            // paths or other diagnostics; only messages authored here may be displayed.
            VaultStatus = e switch
            {
                VaultOpenException failure => failure.Message,
                UnauthorizedAccessException => "Нет разрешения на чтение базы или файла-ключа.",
                IOException => "Ошибка чтения базы или файла-ключа.",
                CryptographicException => KdbxOpenErrorClassifier.CredentialsRejected().Message,
                _ => "Не удалось открыть базу KeePassXC. Проверьте файл и параметры доступа."
            };
        }
        finally
        {
            PasswordInput = "";
            Busy = false;
        }
    }

    public void LoadDemo()
    {
        if (Busy) return;
        // No KDBX reader or credential source is involved. The real database
        // path/key-file remain intact for the next ordinary unlock.
        ClearVault();
        _snapshot = DemoVaultFactory.Create();
        _isDemoMode = true;
        Changed(nameof(IsDemoMode));
        Changed(nameof(IsRealMode));
        Changed(nameof(HasRecords));
        Changed(nameof(CanRefreshRecords));
        foreach (var record in _snapshot.Records)
        {
            record.PropertyChanged += RecordChanged;
            VaultRecords.Add(record);
        }
        LoadPrintLedger(); // In-memory ledger; never reads or writes a real journal.
        ApplyRecordFilters();
        UpdateSelectedCount();
        RebuildMapping();
        ScheduleCardPreview();
        VaultStatus = Localizer.Text(_settings.Language, "DemoActive");
    }

    private void RecordChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VaultRecord.Selected)) return;
        if (_bulkSelection) return;
        UpdateSelectedCount();
        if (_recordQuickFilter is RecordQuickFilter.Selected or RecordQuickFilter.Remaining)
            ApplyRecordFilters();
        else
        {
            if (MappingScopeCurrent) RebuildMapping();
            ScheduleCardPreview();
        }
        if (RememberSelection && !_isDemoMode)
        {
            _selectionSaveTimer.Stop();
            _selectionSaveTimer.Start();
        }
    }

    private void UpdateSelectedCount()
    {
        _selectedCount = VaultRecords.Count(r => r.Selected);
        Changed(nameof(SelectedCountText));
        Changed(nameof(Status));
        Changed(nameof(PrintReady));
    }

    public void SetAll(bool selected)
    {
        _bulkSelection = true;
        try
        {
            foreach (var r in VaultRecords.Where(r => r.Visible)) r.Selected = selected;
        }
        finally { _bulkSelection = false; }
        UpdateSelectedCount();
        if (_recordQuickFilter is RecordQuickFilter.Selected or RecordQuickFilter.Remaining)
            ApplyRecordFilters();
        else
        {
            if (MappingScopeCurrent) RebuildMapping();
            ScheduleCardPreview();
        }
        if (!_isDemoMode) SaveVaultPreferences();
    }

    public void ClearVault()
    {
        _selectionSaveTimer.Stop();
        CloseCardPreview();
        foreach (var r in VaultRecords)
        {
            r.PropertyChanged -= RecordChanged;
            if (r.CustomIconPng is { } bytes) Array.Clear(bytes);
            if (r.IconImageOwned) r.IconImage?.Dispose();
            if (r.GroupIconImageOwned) r.GroupIconImage?.Dispose();
            r.AdditionalFields.Clear();
        }
        VaultRecords.Clear();
        _printLedger=null;
        _snapshot = null;
        _isDemoMode = false;
        Changed(nameof(IsDemoMode));
        Changed(nameof(IsRealMode));
        _lastVaultUsedPassword = false;
        // Reset modes upon closing/opening another database. During successful
        // refresh OpenVaultAsync restores the previous mode and search text.
        _recordQuickFilter = RecordQuickFilter.All;
        _filterText = "";
        Changed(nameof(FilterText));
        NotifyRecordFilterState();
        Changed(nameof(HasRecords));
        Changed(nameof(CanRefreshRecords));
        UpdateSelectedCount();
        RebuildMapping();
        VaultStatus = "База закрыта";
        PasswordInput = "";
    }

    public void SaveVaultPreferences()
    {
        try
        {
            if (_snapshot is not null && !_isDemoMode)
                PrivateUiState.CaptureSelection(_prefs, DatabasePath, VaultRecords.ToList());
            PrivateUiState.Save(_prefs);
        }
        catch (IOException) { VaultStatus = "Не удалось сохранить настройки."; }
        catch (UnauthorizedAccessException) { VaultStatus = "Нет доступа для сохранения настроек."; }
    }

}
