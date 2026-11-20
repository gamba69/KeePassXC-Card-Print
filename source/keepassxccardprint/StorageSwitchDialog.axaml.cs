using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public enum StorageSwitchAnswer { Cancel, Yes, No }

// Pure description data: the transfer decision and all paths are explicit and testable
// without opening an Avalonia window. Yes/No are retained only for call-site compatibility.
internal sealed record StorageSwitchDescription(
    string Heading, string Intro,
    string FirstHeading, string FirstRoute, string FirstExplanation, string FirstAction,
    string SecondHeading, string SecondRoute, string SecondExplanation, string SecondAction,
    string CancelAction);

public partial class StorageSwitchDialog : Window
{
    public StorageSwitchDialog() : this("auto", ConfigurationStorage.AppData) { }

    public StorageSwitchDialog(string language, ConfigurationStorage target,
        ConfigurationStorage? source = null, bool changingCloudDestination = false)
    {
        AvaloniaXamlLoader.Load(this);
        PrintWindowIcons.Attach(this);
        var from = source ?? SettingsLocation.Mode;
        var remote = target == ConfigurationStorage.Cloud ? CloudLocation() : "";
        var description = Describe(language, from, target,
            SettingsLocation.RootFor(from), SettingsLocation.RootFor(target),
            remote, SettingsLocation.CloudRoot, changingCloudDestination);
        Title = "KeePassXC Card Print — " + description.Heading;
        this.FindControl<TextBlock>("DialogHeading")!.Text = description.Heading;
        this.FindControl<TextBlock>("DialogIntro")!.Text = description.Intro;
        this.FindControl<TextBlock>("YesHeading")!.Text = description.FirstHeading;
        this.FindControl<TextBlock>("YesRoute")!.Text = description.FirstRoute;
        this.FindControl<TextBlock>("YesExplanation")!.Text = description.FirstExplanation;
        this.FindControl<TextBlock>("YesText")!.Text = description.FirstAction;
        this.FindControl<TextBlock>("NoHeading")!.Text = description.SecondHeading;
        this.FindControl<TextBlock>("NoRoute")!.Text = description.SecondRoute;
        this.FindControl<TextBlock>("NoExplanation")!.Text = description.SecondExplanation;
        this.FindControl<TextBlock>("NoText")!.Text = description.SecondAction;
        this.FindControl<TextBlock>("CancelText")!.Text = description.CancelAction;
    }

    private static string CloudLocation()
    {
        var profile = CloudConnectionStore.Load();
        var provider = profile.Provider switch
        {
            "GoogleDrive" => "Google Drive",
            "ProtonDrive" => "Proton Drive",
            "CloudflareR2" => "S3 (Cloudflare R2)",
            "S3" => "S3",
            "WebDAV" => "WebDAV",
            _ => "Cloud"
        };
        return provider + " / " + CloudConnectionStore.RemoteFilePath(profile);
    }

    internal static StorageSwitchDescription Describe(string language, ConfigurationStorage source,
        ConfigurationStorage target, string sourcePath, string destinationPath,
        string remoteLocation, string cloudCachePath, bool changingCloudDestination = false)
    {
        var lang = Localizer.ResolveLanguage(language);
        string T(string ru, string uk, string en) => lang switch
        {
            "uk" => uk, "en" => en, _ => ru
        };
        string Name(ConfigurationStorage mode) => mode switch
        {
            ConfigurationStorage.AppData => T("В системе", "У системі", "System storage"),
            ConfigurationStorage.Local => T("Локально", "Локально", "Local storage"),
            _ => T("Локальный кэш облака", "Локальний кеш хмари", "Local cloud cache")
        };
        var heading = T("Выберите источник настроек", "Оберіть джерело налаштувань",
            "Choose settings source");
        var cancel = T("Отмена — ничего не менять", "Скасувати — нічого не змінювати",
            "Cancel — change nothing");
        if (target == ConfigurationStorage.Cloud)
        {
            var intro = changingCloudDestination
                ? T("Выбрана новая папка облака. Укажите, какую копию настроек оставить.",
                    "Обрано нову папку хмари. Вкажіть, яку копію налаштувань залишити.",
                    "A new cloud folder was selected. Choose which settings copy to keep.")
                : T("Переключение: «" + Name(source) + "» → «В облаке». Выберите направление переноса.",
                    "Перемикання: «" + Name(source) + "» → «У хмарі». Оберіть напрямок перенесення.",
                    "Switch: " + Name(source) + " → Cloud. Choose the transfer direction.");
            return new StorageSwitchDescription(
                heading, intro,
                T("С компьютера → в облако", "З комп’ютера → у хмару", "Computer → cloud"),
                T("Источник: ", "Джерело: ", "From: ") + Name(source) + " — " + sourcePath + "\n" +
                T("Назначение: ", "Призначення: ", "To: ") + remoteLocation,
                T("Переносимые настройки и история печати в облачном configuration.zip будут заменены текущими. Исходные локальные файлы останутся; пароли и локальные параметры принтера не отправляются.",
                    "Переносимі налаштування та історію друку в хмарному configuration.zip буде замінено поточними. Вихідні локальні файли залишаться; паролі й локальні параметри принтера не надсилаються.",
                    "The cloud configuration.zip will be replaced with current portable settings and print history. Original local files remain; passwords and local printer settings are not uploaded."),
                T("Отправить в облако", "Надіслати у хмару", "Upload to cloud"),
                T("Из облака → на компьютер", "Із хмари → на комп’ютер", "Cloud → computer"),
                T("Источник: ", "Джерело: ", "From: ") + remoteLocation + "\n" +
                T("Назначение: ", "Призначення: ", "To: ") +
                T("Локальный облачный кэш", "Локальний хмарний кеш", "Local cloud cache") + " — " + cloudCachePath,
                T("Из существующего configuration.zip загрузятся переносимые настройки и история печати. Они заменят соответствующие данные в локальном кэше; сам архив в облаке не изменится. Локальные параметры компьютера сохраняются.",
                    "Із наявного configuration.zip буде завантажено переносимі налаштування й історію друку. Вони замінять відповідні дані в локальному кеші; архів у хмарі не зміниться. Локальні параметри комп’ютера збережуться.",
                    "Portable settings and print history are downloaded from the existing configuration.zip, replacing matching local cache data. The cloud archive stays unchanged; machine-specific settings are preserved."),
                T("Загрузить из облака", "Завантажити із хмари", "Download from cloud"),
                cancel);
        }
        return new StorageSwitchDescription(
            heading,
            T("Переключение: «" + Name(source) + "» → «" + Name(target) + "». Выберите, какие настройки использовать.",
                "Перемикання: «" + Name(source) + "» → «" + Name(target) + "». Оберіть, які налаштування використовувати.",
                "Switch: " + Name(source) + " → " + Name(target) + ". Choose which settings to use."),
            T("Скопировать текущие настройки", "Скопіювати поточні налаштування", "Copy current settings"),
            T("Источник: ", "Джерело: ", "From: ") + Name(source) + " — " + sourcePath + "\n" +
            T("Назначение: ", "Призначення: ", "To: ") + Name(target) + " — " + destinationPath,
            T("Файлы настроек и история печати будут скопированы в целевую папку. Совпадающие файлы заменяются с резервной копией; исходные файлы не удаляются.",
                "Файли налаштувань та історію друку буде скопійовано до цільової папки. Однойменні файли замінюються зі створенням резервної копії; вихідні файли не видаляються.",
                "Settings files and print history are copied to the target folder. Matching files are replaced with backup; source files are not deleted."),
            T("Копировать настройки", "Копіювати налаштування", "Copy settings"),
            T("Использовать уже сохранённые настройки", "Використати вже збережені налаштування",
                "Use existing settings"),
            T("Папка назначения: ", "Тека призначення: ", "Destination folder: ") + destinationPath,
            T("Ничего не копируется и не удаляется. Программа использует файлы из выбранной папки. Если папка пуста, будут созданы настройки по умолчанию.",
                "Нічого не копіюється і не видаляється. Програма використовує файли з вибраної теки. Якщо тека порожня, буде створено типові налаштування.",
                "Nothing is copied or deleted. The app uses settings already in the destination folder. If empty, defaults are created."),
            T("Использовать существующие", "Використати наявні", "Use existing"),
            cancel);
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
