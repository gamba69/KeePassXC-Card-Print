using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public enum StorageSwitchAnswer { Cancel, Yes, No }

public partial class StorageSwitchDialog : Window
{
    public StorageSwitchDialog() : this("auto", ConfigurationStorage.AppData) { }

    public StorageSwitchDialog(string language, ConfigurationStorage target)
    {
        AvaloniaXamlLoader.Load(this);
        PrintWindowIcons.Attach(this);
        var lang = Localizer.ResolveLanguage(language);
        var dest = target == ConfigurationStorage.AppData ? "AppData" :
                   lang == "en" ? "Local (Data)" : lang == "uk" ? "Локально (Data)" : "Локально (Data)";
        var heading = lang switch
        {
            "uk" => "Змінити сховище налаштувань",
            "en" => "Switch settings storage",
            _ => "Переключить хранение настроек"
        };
        var question = lang switch
        {
            "uk" => $"Перейти на «{dest}»?\n\nТак — скопіювати поточні налаштування.\nНі — використати налаштування нової папки.\nСкасувати — залишити все як є.",
            "en" => $"Switch to {dest}?\n\nYes — copy current settings.\nNo — use settings already at the destination.\nCancel — keep the current storage.",
            _ => $"Переключить хранение на «{dest}»?\n\nДа — скопировать текущие настройки.\nНет — использовать настройки выбранной папки.\nОтмена — ничего не менять."
        };
        this.FindControl<TextBlock>("DialogHeading")!.Text = heading;
        this.FindControl<TextBlock>("DialogQuestion")!.Text = question;
        this.FindControl<TextBlock>("YesText")!.Text = lang switch { "uk" => "Так", "en" => "Yes", _ => "Да" };
        this.FindControl<TextBlock>("NoText")!.Text = lang switch { "uk" => "Ні", "en" => "No", _ => "Нет" };
        this.FindControl<TextBlock>("CancelText")!.Text = lang switch { "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена" };
    }
    private void OnYes(object? sender, RoutedEventArgs e) => Close(StorageSwitchAnswer.Yes);
    private void OnNo(object? sender, RoutedEventArgs e) => Close(StorageSwitchAnswer.No);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(StorageSwitchAnswer.Cancel);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(StorageSwitchAnswer.Cancel); }
        base.OnKeyDown(e);
    }
}
