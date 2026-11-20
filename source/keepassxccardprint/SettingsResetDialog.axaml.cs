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
        AvaloniaXamlLoader.Load(this);
        PrintWindowIcons.Attach(this);
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
            "uk" => "Усі локальні сховища та налаштування підключення",
            "en" => "All local storages and connection settings",
            _ => "Все локальные хранилища и настройки подключения"
        };
        ClearHistory.Content = lang switch
        {
            "uk" => "Очистити історію друку", "en" => "Clear print history", _ => "Очистить историю печати"
        };
        HistoryHelp.Text = lang switch
        {
            "uk" => "За замовчуванням вимкнено. Історія та відмітки надрукованих карток залишаться без змін.",
            "en" => "Off by default. Print history and per-card printed states are preserved unless checked.",
            _ => "По умолчанию выключено. История печати и отметки напечатанных карточек сохранятся."
        };
        SafetyText.Text = lang switch
        {
            "uk" => "Бази KeePassXC, ключові файли та архів configuration.zip у хмарі не видаляються. Після скидання програму буде перезапущено.",
            "en" => "KeePassXC databases, key files and the remote configuration.zip are not deleted. The application will restart.",
            _ => "Базы KeePassXC, ключевые файлы и облачный configuration.zip не удаляются. Программа перезапустится."
        };
        ContinueText.Text = lang switch { "uk" => "Продовжити…", "en" => "Continue…", _ => "Продолжить…" };
        CancelText.Text = lang switch { "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена" };
    }

    private void OnContinue(object? sender, RoutedEventArgs e) =>
        Close(new SettingsResetOptions(AllOption.IsChecked == true, ClearHistory.IsChecked == true));
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
        base.OnKeyDown(e);
    }
}
