using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

public partial class SettingsView
{
    private bool _hyphenationBusy, _updatingHyphenationPicker, _updatingHyphenationAutoCheck;
    private readonly DispatcherTimer _hyphenationTimer = new() { Interval = TimeSpan.FromHours(1) };
    private string? _hyphenationCatalogRoot;
    private DateTime _hyphenationLastAttemptUtc;
    private HyphenationPackages.Language[] _hyphenationLanguages = [];
    private HyphenationPackages.PackageInfo[] _hyphenationInstalled = [];
    private int _hyphenationSort = -1;
    private bool _hyphenationDescending;
    private MainWindow? _hyphenationOwner;

    private void InitializeHyphenationSettings()
    {
        HyphenationPicker.SelectionChanged += (_, _) => { if (!_updatingHyphenationPicker) SetHyphenationActions(); };
        HyphenationSearch.TextChanged += (_, _) => ReloadHyphenationPicker();
        HyphenationAutoCheck.IsCheckedChanged += (_, _) =>
        {
            if (_updatingHyphenationAutoCheck) return;
            try { HyphenationPackages.SetAutoCheck(SettingsLocation.DirectoryPath, HyphenationAutoCheck.IsChecked == true); }
            catch (Exception ex) { HyphenationOperationStatus.Text = ex.Message; }
            if (HyphenationAutoCheck.IsChecked == true) _ = CheckHyphenationAutomaticallyAsync();
        };
        _hyphenationTimer.Tick += async (_, _) => await CheckHyphenationAutomaticallyAsync();
        AttachedToVisualTree += async (_, _) =>
        {
            _hyphenationOwner = Owner;
            if (_hyphenationOwner is not null) _hyphenationOwner.HyphenationPackagesChanged += RefreshHyphenationSettings;
            _hyphenationTimer.Start();
            RefreshHyphenationSettings();
            await CheckHyphenationAutomaticallyAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _hyphenationTimer.Stop();
            if (_hyphenationOwner is not null) _hyphenationOwner.HyphenationPackagesChanged -= RefreshHyphenationSettings;
            _hyphenationOwner = null;
        };
    }
    private async Task CheckHyphenationAutomaticallyAsync()
    {
        if (_hyphenationBusy || !HyphenationPackages.AutoCheck(SettingsLocation.DirectoryPath)) return;
        string root = SettingsLocation.DirectoryPath;
        if (_hyphenationCatalogRoot != root) { _hyphenationCatalogRoot = root; _hyphenationLastAttemptUtc = DateTime.MinValue; }
        var checkedAt = HyphenationPackages.ReadCatalogState(root)?.CheckedUtc ?? DateTime.MinValue;
        if (DateTime.UtcNow - checkedAt < TimeSpan.FromHours(24) ||
            DateTime.UtcNow - _hyphenationLastAttemptUtc < TimeSpan.FromHours(1)) return;
        await RefreshHyphenationCatalogAsync(automatic: true);
    }
    private void ReloadHyphenationPicker()
    {
        string query = HyphenationSearch.Text?.Trim() ?? "";
        var installed = _hyphenationInstalled.Select(x => x.Language.Code).ToHashSet(StringComparer.Ordinal);
        var languages = _hyphenationLanguages.Where(x => !installed.Contains(x.Code) &&
            (x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        string? selected = (HyphenationPicker.SelectedItem as HyphenationPackages.Language)?.Code;
        _updatingHyphenationPicker = true;
        try
        {
            HyphenationPicker.ItemsSource = languages;
            HyphenationPicker.SelectedItem = languages.FirstOrDefault(x => x.Code == selected);
        }
        finally { _updatingHyphenationPicker = false; }
        SetHyphenationActions();
    }
    private bool CanEditHyphenation => !_hyphenationBusy && !_cloudCommandRunning && Owner?.CanChangeHyphenation == true;
    private void SetHyphenationActions()
    {
        bool enabled = CanEditHyphenation;
        HyphenationCatalogRefresh.IsEnabled = !_hyphenationBusy;
        HyphenationSearch.IsEnabled = enabled;
        HyphenationPicker.IsEnabled = enabled;
        HyphenationAutoCheck.IsEnabled = !_hyphenationBusy;
        HyphenationInstall.IsEnabled = enabled && HyphenationPicker.SelectedItem is HyphenationPackages.Language;
        HyphenationUpdateAll.IsVisible = _hyphenationInstalled.Any(x => x.CanUpdate);
        HyphenationUpdateAll.IsEnabled = enabled && HyphenationUpdateAll.IsVisible;
        HyphenationRestore.IsVisible = _hyphenationInstalled.Any(x => x.NeedsRestore);
        HyphenationRestore.IsEnabled = enabled && HyphenationRestore.IsVisible;
    }
    private string SourceLabel(HyphenationPackages.PackageInfo p) => p.Pin is null ? L("Встроенный", "Вбудований", "Bundled") :
        p.Language.BuiltIn ? L("Обновлённый", "Оновлений", "Updated") : L("Загруженный", "Завантажений", "Downloaded");
    private string StateLabel(HyphenationPackages.PackageInfo p) => !p.Ready ? L("Требует загрузки", "Потребує завантаження", "Restore needed") :
        p.UpdateAvailable == true ? L("Есть обновление", "Є оновлення", "Update available") :
        p.UpdateAvailable == false ? L("Актуален", "Актуальний", "Up to date") :
        p.CheckedUtc is null ? L("Не проверен", "Не перевірений", "Not checked") : L("Нет в каталоге", "Немає в каталозі", "Not in catalog");
    private string UpdateLabel(HyphenationPackages.PackageInfo p) => p.UpdateAvailable == true ? "rev. " + p.LatestRevision![..12] :
        p.UpdateAvailable == false ? "—" : "?";
    private string SizeLabel(HyphenationPackages.PackageInfo p) => p.Ready ? $"{p.Size / 1024.0:0.#} " + L("КБ", "КБ", "KB") : "—";
    private string Detail(HyphenationPackages.PackageInfo p) =>
        L("Версия правил: ", "Версія правил: ", "Rules version: ") + p.Version + "\n" +
        L("Ревизия: ", "Ревізія: ", "Revision: ") + p.Revision + "\n" +
        (p.CanUpdate ? L("Новая ревизия: ", "Нова ревізія: ", "New revision: ") + p.LatestRevision + "\n" : "") +
        L("Лицензия: ", "Ліцензія: ", "License: ") + (p.License.Length == 0 ? "—" : p.License) + "\n" +
        L("Установлен: ", "Встановлено: ", "Installed: ") + (p.InstalledUtc?.ToLocalTime().ToString("g") ?? "—") + "\n" +
        L("Проверен: ", "Перевірено: ", "Checked: ") + (p.CheckedUtc?.ToLocalTime().ToString("g") ?? "—") + "\n" +
        "https://github.com/hyphenation/tex-hyphen" + (p.Pin is { } pin ? "\nSHA-256: " + pin.Pattern : "");
    // Same header and 25-DIP row geometry as both mapping views. A single
    // grid per row keeps columns aligned without individual header borders.
    private const string HyphenationColumns = "*,86,112,156,142,78,132,58";
    private void BuildHyphenationTable()
    {
        HyphenationTable.Children.Clear(); HyphenationTable.RowDefinitions.Clear();
        string[] headers = [L("Язык", "Мова", "Language"), L("Код", "Код", "Code"), L("Источник", "Джерело", "Source"),
            L("Версия правил", "Версія правил", "Rules version"), L("Обновление", "Оновлення", "Update"),
            L("Размер", "Розмір", "Size"), L("Состояние", "Стан", "Status"), L("Действия", "Дії", "Actions")];
        static Grid Cells() => new() { ColumnDefinitions = new ColumnDefinitions(HyphenationColumns) };
        static void Cell(Grid grid, Control control, int col)
        {
            Grid.SetColumn(control, col); grid.Children.Add(control);
        }
        void Row(Border border, int height)
        {
            Grid.SetRow(border, HyphenationTable.RowDefinitions.Count);
            HyphenationTable.RowDefinitions.Add(new RowDefinition(new GridLength(height)));
            HyphenationTable.Children.Add(border);
        }
        var heading = Cells();
        for (int col = 0; col < headers.Length; col++)
        {
            if (col == 7)
            {
                Cell(heading, new TextBlock { Text = headers[col], VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis }, col);
                continue;
            }
            int index = col;
            var label = new TextBlock { Text = headers[col] + (_hyphenationSort == col ? (_hyphenationDescending ? " ↓" : " ↑") : ""),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var header = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            header.Classes.Add("table-sort");
            ToolTip.SetTip(header, headers[col]);
            header.Click += (_, _) => { _hyphenationDescending = _hyphenationSort == index && !_hyphenationDescending; _hyphenationSort = index; BuildHyphenationTable(); };
            Cell(heading, header, col);
        }
        var headerBorder = new Border { Child = heading, Padding = new Thickness(5, 0) };
        headerBorder.Classes.Add("table-header");
        Row(headerBorder, 27);
        IEnumerable<HyphenationPackages.PackageInfo> rows = _hyphenationInstalled;
        // The default grouping is stable. Clicking a header sorts within each source group.
        var group = rows.OrderByDescending(x => x.Language.BuiltIn);
        if (_hyphenationSort == 5) rows = _hyphenationDescending ? group.ThenByDescending(x => x.Size) : group.ThenBy(x => x.Size);
        else
        {
            string Key(HyphenationPackages.PackageInfo p) => _hyphenationSort switch
            { 1 => p.Language.Code, 2 => SourceLabel(p), 3 => p.Version, 4 => UpdateLabel(p), 6 => StateLabel(p), _ => p.Language.Name };
            rows = _hyphenationDescending ? group.ThenByDescending(Key, StringComparer.CurrentCultureIgnoreCase) : group.ThenBy(Key, StringComparer.CurrentCultureIgnoreCase);
        }
        int row = 0;
        foreach (var package in rows)
        {
            var cells = Cells();
            string[] values = [package.Language.Name, package.Language.Code, SourceLabel(package), package.Version,
                UpdateLabel(package), SizeLabel(package), StateLabel(package)];
            string detail = Detail(package);
            for (int col = 0; col < values.Length; col++)
            {
                var cell = new TextBlock { Text = values[col], TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
                ToolTip.SetTip(cell, detail); Cell(cells, cell, col);
            }
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            Button Action(Icon icon, string tip, Func<Task> run)
            {
                var button = new Button { Content = ActionIconVectors.Create(icon, 14), Padding = new Thickness(2),
                    Width = 24, Height = 23, MinHeight = 0, MinWidth = 0, IsEnabled = CanEditHyphenation };
                ToolTip.SetTip(button, tip); button.Click += async (_, _) => await run(); return button;
            }
            // Restore and update are mutually exclusive. Unmodified bundled
            // packages have no destructive action; downloaded overrides have
            // either reset-to-bundled or remove, never both.
            if (package.NeedsRestore)
                actions.Children.Add(Action(Icon.ArrowDownload, L("Восстановить точную версию", "Відновити точну версію", "Restore exact version"),
                    () => RunHyphenationOperation(root => HyphenationPackages.RestoreAsync(root, package.Language.Code))));
            else if (package.CanUpdate)
                actions.Children.Add(Action(Icon.ArrowDownload, L("Обновить", "Оновити", "Update"),
                    () => RunHyphenationOperation(async root => { _ = await HyphenationPackages.InstallOrUpdateAsync(root, package.Language.Code); })));
            if (package.CanRemoveOverride)
                actions.Children.Add(Action(package.Language.BuiltIn ? Icon.ArrowSync : Icon.Delete,
                    package.Language.BuiltIn ? L("Вернуть встроенную версию", "Повернути вбудовану версію", "Reset to bundled") :
                    L("Удалить язык", "Видалити мову", "Remove language"),
                    () => RunHyphenationOperation(root => HyphenationPackages.RemoveAsync(root, package.Language.Code))));
            Cell(cells, actions, 7);
            var border = new Border { Child = cells, Padding = new Thickness(5, 0) };
            border.Classes.Add("hyphenation-row");
            if (row++ % 2 == 1) border.Classes.Add("stripe");
            Row(border, 25);
        }
    }
    private void RefreshHyphenationSettings()
    {
        if (_updatingHyphenationPicker) return;
        HyphenationCaption.Text = L("Правила переноса слов", "Правила переносу слів", "Hyphenation packages");
        HyphenationSearch.PlaceholderText = L("Поиск языка или кода…", "Пошук мови або коду…", "Find language or code…");
        HyphenationPicker.PlaceholderText = L("Выберите язык…", "Виберіть мову…", "Select language…");
        HyphenationCatalogLabel.Text = L("Проверить обновления", "Перевірити оновлення", "Check for updates");
        HyphenationInstallLabel.Text = L("Установить", "Встановити", "Install");
        HyphenationUpdateAllLabel.Text = L("Обновить все", "Оновити всі", "Update all");
        HyphenationRestoreLabel.Text = L("Восстановить пакеты", "Відновити пакети", "Restore packages");
        HyphenationAutoCheck.Content = L("Автоматически проверять обновления", "Автоматично перевіряти оновлення", "Check for updates automatically");
        try
        {
            string root = SettingsLocation.DirectoryPath;
            _updatingHyphenationAutoCheck = true;
            try { HyphenationAutoCheck.IsChecked = HyphenationPackages.AutoCheck(root); }
            finally { _updatingHyphenationAutoCheck = false; }
            if (!_hyphenationBusy)
            {
                _hyphenationLanguages = HyphenationPackages.CatalogFor(root);
                _hyphenationInstalled = HyphenationPackages.Installed(root);
            }
            int builtIn = _hyphenationInstalled.Count(p => p.Language.BuiltIn);
            HyphenationCatalogCount.Text = L("Встроенных: ", "Вбудованих: ", "Bundled: ") + builtIn + " · " +
                L("Дополнительных: ", "Додаткових: ", "Additional: ") + (_hyphenationInstalled.Length - builtIn) + " · " +
                L("В каталоге: ", "У каталозі: ", "In catalog: ") + _hyphenationLanguages.Length;
        }
        catch (Exception ex) { HyphenationOperationStatus.Text = L("Ошибка конфигурации правил: ", "Помилка конфігурації правил: ", "Invalid package configuration: ") + ex.Message; _hyphenationInstalled = []; _hyphenationLanguages = []; }
        ReloadHyphenationPicker(); BuildHyphenationTable(); SetHyphenationActions();
    }
    private async void OnHyphenationCatalogRefresh(object? sender, RoutedEventArgs e) => await RefreshHyphenationCatalogAsync(automatic: false);
    private async Task RefreshHyphenationCatalogAsync(bool automatic)
    {
        if (_hyphenationBusy) return;
        string root = SettingsLocation.DirectoryPath;
        _hyphenationLastAttemptUtc = DateTime.UtcNow;
        _hyphenationBusy = true; RefreshHyphenationSettings();
        HyphenationOperationStatus.Text = L("Проверка обновлений…", "Перевірка оновлень…", "Checking for updates…");
        try
        {
            await HyphenationPackages.RefreshCatalogAsync(root);
            HyphenationOperationStatus.Text = L("Каталог обновлён", "Каталог оновлено", "Catalog refreshed");
        }
        catch (Exception ex)
        {
            HyphenationOperationStatus.Text = L("Используется сохранённый каталог", "Використовується збережений каталог", "Using the saved catalog") + (automatic ? "" : ": " + ex.Message);
        }
        finally { _hyphenationBusy = false; RefreshHyphenationSettings(); }
    }
    private async void OnHyphenationInstall(object? sender, RoutedEventArgs e)
    {
        if (HyphenationPicker.SelectedItem is not HyphenationPackages.Language lang) return;
        await RunHyphenationOperation(async root => { _ = await HyphenationPackages.InstallOrUpdateAsync(root, lang.Code); });
    }
    private async void OnHyphenationUpdateAll(object? sender, RoutedEventArgs e) => await RunHyphenationOperation(root => HyphenationPackages.UpdateAllAsync(root));
    private async void OnHyphenationRestore(object? sender, RoutedEventArgs e) => await RunHyphenationOperation(root => HyphenationPackages.ReconcileAsync(root));
    private async Task RunHyphenationOperation(Func<string, Task> operation)
    {
        if (!CanEditHyphenation) return;
        string root = SettingsLocation.DirectoryPath;
        _hyphenationBusy = true; RefreshHyphenationSettings();
        HyphenationOperationStatus.Text = L("Выполняется…", "Виконується…", "Working…");
        try
        {
            await operation(root);
            HyphenationOperationStatus.Text = L("Готово", "Готово", "Completed");
        }
        catch (Exception ex) { HyphenationOperationStatus.Text = L("Не удалось выполнить операцию: ", "Не вдалося виконати операцію: ", "Package operation failed: ") + ex.Message; }
        finally
        {
            _hyphenationBusy = false;
            if (SettingsLocation.DirectoryPath == root) Owner?.HyphenationChanged();
            RefreshHyphenationSettings();
        }
    }
}
