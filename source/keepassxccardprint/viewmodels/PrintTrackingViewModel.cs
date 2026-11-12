using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

public sealed partial class ShellViewModel
{
    private PrintTrackingLedger? _printLedger;
    public PrintTrackingLedger? CurrentPrintLedger => _printLedger;
    private void LoadPrintLedger()
    {
        _printLedger=null;
        if(_snapshot is null)return;
        _printLedger=new PrintTrackingLedger(_snapshot.RootUuid, inMemory: _isDemoMode);
        RefreshPrintStatuses();
    }
    public void RefreshPrintStatuses()
    {
        foreach(var record in VaultRecords)
            record.PrintStatus=_printLedger?.Status(record.Uuid)??"—";
        ApplyRecordFilters();
    }
    public void MarkPrintStatus(VaultRecord record,string action)
    {
        int? partCount=action=="printed"
            ? _cardParts.Where(card=>card.Front.Record.Uuid==record.Uuid)
                .Select(card=>(int?)card.Front.Total).FirstOrDefault()
            : null;
        _printLedger?.Mark(record.Uuid,action,partCount);
        record.PrintStatus=_printLedger?.Status(record.Uuid)??"—";
        ApplyRecordFilters();
    }
    public void ConfirmPrint(IReadOnlyList<CardPrintSheet> sheets,IReadOnlySet<int>? failed=null)
    {
        _printLedger?.Confirm(sheets,failed);
        RefreshPrintStatuses();
    }
    public void AddPrintHistory(string printer,string mode,string result,IReadOnlyList<CardPrintSheet> sheets)
        => _printLedger?.AppendHistory(printer,mode,result,sheets);
}
