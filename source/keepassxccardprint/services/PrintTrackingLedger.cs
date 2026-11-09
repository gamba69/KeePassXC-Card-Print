using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Services;

// No entry title, field value, TOTP, password or KDBX path enters these files.
public sealed class PrintEntryState
{
    public Guid EntryId {get;set;}
    public bool Tracked {get;set;}
    public int TotalParts {get;set;}=1;
    public List<int> PrintedParts {get;set;}=[];
}
public sealed class PrintJobHistory
{
    public DateTime Utc {get;set;}
    public string Printer {get;set;}="";
    public string Mode {get;set;}="";
    public string Result {get;set;}="";
    public int Sheets {get;set;}
    public int CardCount {get;set;}
    public List<Guid> EntryIds {get;set;}=[];
}
public sealed class PrintTrackingData
{
    public int Version {get;set;}=1;
    public List<PrintEntryState> Entries {get;set;}=[];
    public List<PrintJobHistory> History {get;set;}=[];
}
[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(PrintTrackingData))]
internal partial class PrintTrackingJsonContext : JsonSerializerContext { }

public sealed class PrintTrackingLedger
{
    private readonly string _path;
    private readonly PrintTrackingData _data;
    private readonly Dictionary<Guid,PrintEntryState> _entries;
    public IReadOnlyList<PrintJobHistory> History => _data.History;

    public PrintTrackingLedger(Guid databaseRoot)
    {
        if(databaseRoot==Guid.Empty)throw new ArgumentException("Missing KDBX root ID.");
        var name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databaseRoot.ToString("D")))).ToLowerInvariant();
        _path=Path.Combine(SettingsLocation.DirectoryPath,"print-tracking",name+".json");
        if(File.Exists(_path))
        {
            if(new FileInfo(_path).Length>2_000_000)throw new InvalidDataException("Слишком большой журнал печати.");
            _data=JsonSerializer.Deserialize(File.ReadAllBytes(_path),PrintTrackingJsonContext.Default.PrintTrackingData)
                ?? throw new InvalidDataException("Не удалось прочесть журнал печати.");
            if(_data.Version!=1)throw new InvalidDataException("Неизвестная версия журнала печати.");
        }
        else _data=new();
        _entries=new Dictionary<Guid,PrintEntryState>();
        foreach(var state in _data.Entries)
            if(state.EntryId!=Guid.Empty)_entries[state.EntryId]=state;
    }

    private PrintEntryState Entry(Guid id)
    {
        if(_entries.TryGetValue(id,out var state))return state;
        state=new PrintEntryState{EntryId=id};
        _entries.Add(id,state);
        _data.Entries.Add(state);
        return state;
    }

    public string Status(Guid id)
    {
        if(!_entries.TryGetValue(id,out var entry)||!entry.Tracked)return "—";
        if(entry.PrintedParts.Count==0)return "Не напечатана";
        return IsFullyPrinted(id) ? "Напечатана":"Частично";
    }
    public bool IsTracked(Guid id)=>_entries.TryGetValue(id,out var entry)&&entry.Tracked;
    // Typed status predicate; do not interpret translated/display text for filtering.
    public bool IsFullyPrinted(Guid id) => _entries.TryGetValue(id, out var entry) &&
        entry.Tracked && entry.PrintedParts.Count > 0 &&
        Enumerable.Range(1, Math.Max(1, entry.TotalParts)).All(entry.PrintedParts.Contains);

    public void Mark(Guid id,string action,int? partCount=null)
    {
        var entry=Entry(id);
        if(partCount is >=1 and <=64)entry.TotalParts=partCount.Value;
        switch(action)
        {
            case "exclude":entry.Tracked=false;break;
            case "unprinted":entry.Tracked=true;entry.PrintedParts.Clear();break;
            case "printed":entry.Tracked=true;
                entry.PrintedParts=Enumerable.Range(1,entry.TotalParts).ToList();break;
            default:throw new ArgumentOutOfRangeException(nameof(action));
        }
        Save();
    }
    public void Confirm(IReadOnlyList<CardPrintSheet> sheets,IReadOnlySet<int>? failures=null)
    {
        foreach(var sheet in sheets)
        foreach(var slot in sheet.Slots)
        {
            var card=slot.Card;
            var part=sheet.Reverse ? card.Continuation : card.Front;
            if(part is null || failures?.Contains(slot.CardIndex)==true)continue;
            var entry=Entry(part.Record.Uuid);
            entry.Tracked=true;
            entry.TotalParts=Math.Max(entry.TotalParts,part.Total);
            if(!entry.PrintedParts.Contains(part.Number))entry.PrintedParts.Add(part.Number);
        }
        Save();
    }
    public void AppendHistory(string printer,string mode,string result,IReadOnlyList<CardPrintSheet> sheets)
    {
        var ids=sheets.SelectMany(s=>s.Slots).Select(s=>s.Card.Front.Record.Uuid)
            .Distinct().ToList();
        _data.History.Insert(0,new PrintJobHistory
        {
            Utc=DateTime.UtcNow,Printer=printer,Mode=mode,Result=result,
            Sheets=sheets.Select(s=>s.SheetNumber).Distinct().Count(),
            CardCount=sheets.SelectMany(s=>s.Slots).Select(s=>s.CardIndex).Distinct().Count(),
            EntryIds=ids
        });
        if(_data.History.Count>150)_data.History.RemoveRange(150,_data.History.Count-150);
        Save();
    }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(_data,PrintTrackingJsonContext.Default.PrintTrackingData);
        string temp=_path+".tmp";
        try {File.WriteAllBytes(temp,json);File.Move(temp,_path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
