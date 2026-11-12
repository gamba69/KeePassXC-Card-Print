using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace KeePassXCCardPrint;

/// <summary>Application-wide modal confirmation. False is the default for Esc,
/// closing with the title-bar X, or any nonaffirmative result. Never logs secrets.</summary>
internal static class AppConfirmations
{
    internal static Task<bool> AskAsync(Window owner, string title, string description,
        string affirmative, bool sensitive=false, string actionIcon="check", string? cancelText=null)
    {
        var win=new Window
        {
            Title=title,
            Width=470, MinWidth=390, MaxHeight=360,
            SizeToContent=SizeToContent.Height,
            CanResize=false, ShowInTaskbar=false,
            WindowDecorations=WindowDecorations.Full,
            WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        PrintWindowIcons.Attach(win);
        var root=new DockPanel{LastChildFill=true};
        var actions=new StackPanel
        {
            Orientation=Orientation.Horizontal, Spacing=7,
            HorizontalAlignment=HorizontalAlignment.Right,
            VerticalAlignment=VerticalAlignment.Center
        };
        var approve=PrintDialogButtons.Create(affirmative,actionIcon);
        if(sensitive)
        {
            approve.Classes.Add("danger-action");
            // TextBlock's application-wide style would otherwise force a pale
            // foreground over the red background, ignoring Button.Foreground.
            if(approve.Content is StackPanel contents)
                foreach(var child in contents.Children)
                {
                    if(child is TextBlock text)text.Foreground=Brushes.White;
                    if(child is PathIcon path)path.Foreground=Brushes.White;
                }
        }
        var cancel=PrintDialogButtons.Create(cancelText??"Отмена","cancel");
        approve.Click+=(_,_)=>win.Close(true);
        cancel.Click+=(_,_)=>win.Close(false);
        actions.Children.Add(approve);
        actions.Children.Add(cancel); // Cancel always on the far right.
        var footer=new Border{Child=actions};
        footer.Classes.Add("app-footer");
        DockPanel.SetDock(footer,Dock.Bottom);
        root.Children.Add(footer);

        var card=new Border{Margin=new Thickness(12),Padding=new Thickness(12)};
        card.Classes.Add("settings-card");
        var stack=new StackPanel{Spacing=10};
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=9};
        var icon=ActionIconVectors.Create(sensitive?"warning":actionIcon);
        icon.Width=19; icon.Height=19;
        if(sensitive) icon.Foreground=Brushes.Firebrick;
        heading.Children.Add(icon);
        var caption=new TextBlock{Text=title,FontSize=14,FontWeight=FontWeight.SemiBold,
            VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        heading.Children.Add(caption);
        stack.Children.Add(heading);
        var descriptionText=new TextBlock{Text=description,FontSize=13,
            TextWrapping=TextWrapping.Wrap};
        stack.Children.Add(descriptionText);
        card.Child=stack;
        root.Children.Add(card);
        win.Content=root;
        Services.UiText.Enable(win, translateText: true);
        win.KeyDown+=(s,e)=>{if(e.Key==Key.Escape){e.Handled=true;win.Close(false);}};
        return win.ShowDialog<bool>(owner);
    }

    internal static Task<bool> ConfirmSecretFileAsync(Window owner, string language, bool virtualPrinter)
    {
        string lang=ViewModels.Localizer.ResolveLanguage(language);
        string title=lang switch
        {
            "uk"=>"Незашифровані секретні дані",
            "en"=>"Unencrypted secrets",
            _=>"Незашифрованные секретные данные"
        };
        string message=virtualPrinter ? lang switch
        {
            "uk"=>"Вибраний віртуальний принтер може зберегти паролі, TOTP та інші секрети у незашифрованому файлі. Продовжити друк?",
            "en"=>"The selected virtual printer may save passwords, TOTP and other secrets in an unencrypted file. Continue printing?",
            _=>"Виртуальный принтер может сохранить пароли, TOTP и другие секреты в незашифрованном файле. Продолжить печать?"
        } : lang switch
        {
            "uk"=>"PDF або архів PNG міститиме паролі, TOTP та інші секрети без шифрування. Кожен, хто отримає файл, зможе їх прочитати.",
            "en"=>"The PDF or PNG archive will contain passwords, TOTP and other secrets without encryption. Anyone with access to the file can read them.",
            _=>"PDF или архив PNG будет содержать пароли, TOTP и другие секреты без шифрования. Любой, у кого окажется файл, сможет их прочитать."
        };
        string affirmative=virtualPrinter ? lang switch
        {
            "uk"=>"Продовжити друк", "en"=>"Continue printing", _=>"Продолжить печать"
        } : lang switch
        {
            "uk"=>"Зберегти файл", "en"=>"Save file", _=>"Сохранить файл"
        };
        string cancel=lang switch{"uk"=>"Скасувати","en"=>"Cancel",_=>"Отмена"};
        return AskAsync(owner,title,message,affirmative,true,virtualPrinter?"print":"save",cancel);
    }

    internal static Task<bool> ConfirmExitAsync(Window owner, string language, bool exportInProgress)
    {
        string lang=ViewModels.Localizer.ResolveLanguage(language);
        var title=lang switch{"uk"=>"Завершити роботу?","en"=>"Exit application?",_=>"Завершить работу?"};
        var message=exportInProgress ? lang switch
        {
            "uk"=>"Експорт ще триває. Вихід перерве його. Завершити роботу?",
            "en"=>"An export is still running. Exiting will interrupt it. Exit now?",
            _=>"Экспорт ещё выполняется. Выход прервёт его. Завершить работу?"
        } : lang switch
        {
            "uk"=>"Закрити KeePassXC Card Print?",
            "en"=>"Close KeePassXC Card Print?",
            _=>"Закрыть KeePassXC Card Print?"
        };
        var action=lang switch{"uk"=>"Вийти","en"=>"Exit",_=>"Выйти"};
        var cancel=lang switch{"uk"=>"Скасувати","en"=>"Cancel",_=>"Отмена"};
        return AskAsync(owner,title,message,action,exportInProgress,"exit",cancel);
    }
}
