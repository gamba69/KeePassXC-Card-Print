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
        Services.AdaptiveViewport.LimitDialog(this);
        var lang=Localizer.ResolveLanguage(language);
        this.FindControl<TextBlock>("DialogHeading")!.Text=lang switch
        {
            "uk"=>"Незбережені зміни шаблона",
            "en"=>"Unsaved print template",
            _=>"Несохранённые изменения шаблона"
        };
        this.FindControl<TextBlock>("DialogQuestion")!.Text=exiting? lang switch
        {
            "uk"=>"Є незбережені зміни шаблона. Зберегти його перед виходом?",
            "en"=>"The print template has unsaved changes. Save it before exiting?",
            _=>"В шаблоне есть несохранённые изменения. Сохранить его перед выходом?"
        } : lang switch
        {
            "uk"=>"Зберегти весь шаблон, включно з мапінгом і оформленням обох боків?",
            "en"=>"Save the entire template, including field mapping and both sides?",
            _=>"Сохранить весь шаблон, включая маппинг и оформление обеих сторон?"
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
