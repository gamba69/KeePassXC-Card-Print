using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class DomainsWindow : Window
{
    private readonly ObservableCollection<string> _prefixes = new();
    private ListBox _list = null!;
    private TextBox _input = null!;
    private TextBlock _status = null!;
    public DomainsWindow() : this("auto") { }

    public DomainsWindow(string language)
    {
        AvaloniaXamlLoader.Load(this);
        PrintWindowIcons.Attach(this);
        _list = this.FindControl<ListBox>("PrefixList")!;
        _input = this.FindControl<TextBox>("PrefixInput")!;
        _status = this.FindControl<TextBlock>("DomainStatus")!;
        _list.ItemsSource = _prefixes;
        string L(string key) => MappingLexicon.Text(key,language);
        Title=L("DomainTitle");
        this.FindControl<TextBlock>("DomainLead")!.Text=L("DomainLead");
        this.FindControl<TextBlock>("DomainHint")!.Text=L("DomainHint");
        _input.PlaceholderText=L("DomainPlaceholder");
        this.FindControl<TextBlock>("AddText")!.Text=L("DomainAdd");
        this.FindControl<TextBlock>("RemoveText")!.Text=L("DomainRemove");
        this.FindControl<TextBlock>("DefaultsText")!.Text=L("DomainDefaults");
        this.FindControl<TextBlock>("CancelText")!.Text=L("DomainCancel");
        this.FindControl<TextBlock>("SaveText")!.Text=L("DomainSave");
        try { foreach (var prefix in DomainPrefixStore.Load()) _prefixes.Add(prefix); }
        catch (InvalidDataException) { _status.Text="Не удалось прочитать настройки поддоменов; проверьте domains.json."; }
    }
    private void OnAdd(object? sender,RoutedEventArgs e) => TryAdd();
    private void TryAdd()
    {
        try
        {
            var label=DomainPrefixStore.Normalize([_input.Text??""])[0];
            if (!_prefixes.Contains(label)) _prefixes.Add(label);
            _input.Text="";_input.Focus();_status.Text="";
        }
        catch(ArgumentException e2) { _status.Text=e2.Message; }
    }
    private void OnInputKeyDown(object? sender,KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled=true;TryAdd(); }
    }
    private void OnRemove(object? sender,RoutedEventArgs e)
    {
        foreach(var item in _list.SelectedItems?.OfType<string>().ToArray()??Array.Empty<string>())_prefixes.Remove(item);
    }
    private void OnDefaults(object? sender,RoutedEventArgs e)
    {
        _prefixes.Clear();foreach(var item in DomainPrefixStore.Defaults)_prefixes.Add(item);
    }
    private void OnCancel(object? sender,RoutedEventArgs e) => Close();
    private void OnSave(object? sender,RoutedEventArgs e)
    {
        try { DomainPrefixStore.Save(_prefixes);Close(); }
        catch(Exception e2) when (e2 is IOException or UnauthorizedAccessException or ArgumentException)
        { _status.Text="Ошибка сохранения списка поддоменов ("+e2.GetType().Name+")."; }
    }
}
