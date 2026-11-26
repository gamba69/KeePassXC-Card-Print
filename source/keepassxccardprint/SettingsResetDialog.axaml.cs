using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class SettingsResetDialog : Window
{
    public SettingsResetDialog() : this("auto") { }

    public SettingsResetDialog(string language)
    {
        InitializeComponent();
        PrintWindowIcons.Attach(this);
        AdaptiveViewport.LimitDialog(this);
        var lang = Localizer.ResolveLanguage(language);
        Title = lang switch
        {
            "uk" => "KeePassXC Card Print — Скидання налаштувань",
            "en" => "KeePassXC Card Print — Reset settings",
            _ => "KeePassXC Card Print — Сброс настроек"
        };
        TitleText.Text = lang switch { "uk" => "Скидання налаштувань", "en" => "Reset settings", _ => "Сброс настроек" };
        PromptText.Text = lang switch
        {
            "uk" => "Виберіть, які локальні налаштування скинути. Всі файли будуть збережені в резервній копії до очищення.",
            "en" => "Choose which local settings to reset. A verified backup is created before anything is removed.",
            _ => "Выберите, какие локальные настройки сбросить. До очистки будет создана проверенная резервная копия."
        };
        CurrentOption.IsVisible = false;
        AllOption.IsChecked = true;
        CurrentOption.Content = SettingsLocation.Mode == ConfigurationStorage.Cloud ? lang switch
        {
            "uk" => "Поточне сховище (хмарний кеш і системні налаштування запуску)",
            "en" => "Current storage (cloud cache and system startup settings)",
            _ => "Текущее хранилище (облачный кэш и системные настройки запуска)"
        } : lang switch
        {
            "uk" => "Лише поточне сховище", "en" => "Current storage only", _ => "Только текущее хранилище"
        };
        AllOption.Content = lang switch
        {
            "uk" => "Усі сховища, кеш і віддалена хмара",
            "en" => "All storages, cache and remote cloud",
            _ => "Все хранилища, кэш и удалённое облако"
        };
        ClearHistory.Content = lang switch
        {
            "uk" => "Видалити всю папку print-tracking", "en" => "Delete entire print-tracking folder", _ => "Удалить всю папку print-tracking"
        };
        HistoryHelp.Text = lang switch
        {
            "uk" => "За замовчуванням зберігається вся папка: історія і всі статуси друку.",
            "en" => "By default the whole folder is kept, including history and every card status.",
            _ => "По умолчанию сохраняется вся папка: история и все статусы карточек."
        };
        SafetyText.Text = lang switch
        {
            "uk" => "Віддалений configuration.zip буде замінено чистим. Бази KeePassXC та ключові файли не зачіпаються. Програму буде перезапущено.",
            "en" => "Remote configuration.zip is replaced with a clean snapshot. KeePassXC databases and key files are untouched. The application will restart.",
            _ => "Облачный configuration.zip заменяется чистым. Базы KeePassXC и файлы ключей не затрагиваются. Программа перезапустится."
        };
        ContinueText.Text = lang switch { "uk" => "Продовжити…", "en" => "Continue…", _ => "Продолжить…" };
        CancelText.Text = lang switch { "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена" };
    }

    private void OnContinue(object? sender, RoutedEventArgs e) =>
        Close(new SettingsResetOptions(true, ClearHistory.IsChecked == true));
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
        base.OnKeyDown(e);
    }
}
