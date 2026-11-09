using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public enum UnsavedMappingChoice { Cancel, Save, Discard }

public partial class UnsavedMappingDialog : Window
{
    public UnsavedMappingDialog() : this("auto") { }
    public UnsavedMappingDialog(string language,bool exiting=false)
    {
        AvaloniaXamlLoader.Load(this);
        PrintWindowIcons.Attach(this);
        var lang=Localizer.ResolveLanguage(language);
        this.FindControl<TextBlock>("DialogHeading")!.Text=lang switch
        {
            "uk"=>"Незбережені зміни мапінгу",
            "en"=>"Unsaved field mapping",
            _=>"Несохранённые изменения маппинга"
        };
        this.FindControl<TextBlock>("DialogQuestion")!.Text=exiting? lang switch
        {
            "uk"=>"Є незбережені зміни мапінгу. Зберегти їх перед виходом?",
            "en"=>"Field mapping has unsaved changes. Save them before exiting?",
            _=>"В маппинге есть несохранённые изменения. Сохранить их перед выходом?"
        } : lang switch
        {
            "uk"=>"Зміни полів ще не записані на диск. Зберегти їх перед продовженням? Якщо введено некоректне число, поверніться до мапінгу та виправте його.",
            "en"=>"Field mapping changes have not been saved. Save before continuing? If a numeric value is invalid, return to Mapping and correct it.",
            _=>"Изменения полей ещё не записаны на диск. Сохранить перед продолжением? Если число введено неверно, вернитесь в «Маппинг» и исправьте его."
        };
        this.FindControl<TextBlock>("SaveText")!.Text=lang switch {"uk"=>"Зберегти", "en"=>"Save changes", _=>"Сохранить"};
        this.FindControl<TextBlock>("DiscardText")!.Text=lang switch {"uk"=>"Не зберігати", "en"=>"Don't save", _=>"Не сохранять"};
        this.FindControl<TextBlock>("CancelText")!.Text=lang switch {"uk"=>"Скасувати", "en"=>"Cancel", _=>"Отмена"};
    }
    private void OnSave(object? sender,RoutedEventArgs e) => Close(UnsavedMappingChoice.Save);
    private void OnDiscard(object? sender,RoutedEventArgs e) => Close(UnsavedMappingChoice.Discard);
    private void OnCancel(object? sender,RoutedEventArgs e) => Close(UnsavedMappingChoice.Cancel);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Key==Key.Escape){e.Handled=true;Close(UnsavedMappingChoice.Cancel);}
        base.OnKeyDown(e);
    }
}
