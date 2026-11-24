using System.Collections.ObjectModel;
using Avalonia.Threading;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

public sealed partial class ShellViewModel
{
    public ObservableCollection<TemplateEditorSection> TemplatePreviewModes { get; } = new();
    private TemplateEditorSection? _templatePreviewMode;
    private SkiaPreviewScene? _templatePreviewScene;
    private CancellationTokenSource? _templatePreviewCancellation;
    private bool _templatePreviewBusy, _templatePreviewDemo;
    private string _templatePreviewError = "";
    private string _templateHighlightGroup = "";
    private PreviewHitRegion? _templateHoverRegion, _templateSelectedRegion;
    public bool TemplatePreviewForced => _templatePreviewMode is { Id: not "actual" } && TemplateEditorActive;
    public TemplateEditorSection? TemplatePreviewMode
    {
        get => _templatePreviewMode;
        set
        {
            if (value is null || ReferenceEquals(value, _templatePreviewMode)) return;
            bool changedMode = value.Id != _templatePreviewMode?.Id;
            _templatePreviewMode = value;
            Changed();
            if (changedMode)
            {
                ClearTemplatePreviewSelection();
                ScheduleTemplatePreview();
            }
            else NotifyTemplatePreview();
        }
    }
    public string TemplateHighlightSection => TemplateEditorActive && _templateHighlightGroup.Length>0 ? _selectedTemplateEditorSection?.Id ?? "" : "";
    public string TemplateHighlightGroup => TemplateEditorActive ? _templateHighlightGroup : "";
    public PreviewHitRegion? TemplateHoverRegion => _templateHoverRegion;
    public PreviewHitRegion? TemplateSelectedRegion => TemplateEditorActive ? _templateSelectedRegion : null;
    public string TemplateHighlightCaption
    {
        get
        {
            var scene=PreviewScene;
            if(PreviewUpdating || scene is null || (PreviewPreviousScene is not null && PreviewOldOpacity>.1))return "";
            var hover=TemplateHoverRegion;
            var group=hover is not null && scene.HitRegions.Contains(hover) ? hover.Group : TemplateHighlightGroup;
            return scene.HitRegions.Any(h=>h.Group==group) ? TemplateEditorLayout.GroupCaption(group,_settings.Language) : "";
        }
    }
    public bool TemplateHighlightCaptionVisible => TemplateHighlightCaption.Length>0;
    private void NotifyTemplateHighlight()
    {
        Changed(nameof(TemplateHighlightSection)); Changed(nameof(TemplateHighlightGroup));
        Changed(nameof(TemplateHoverRegion)); Changed(nameof(TemplateSelectedRegion));
        Changed(nameof(TemplateHighlightCaption)); Changed(nameof(TemplateHighlightCaptionVisible));
    }
    public void SetTemplatePreviewHover(PreviewHitRegion? hit)
    {
        if(Equals(_templateHoverRegion,hit))return;
        _templateHoverRegion=hit;
        NotifyTemplateHighlight();
    }
    // Clear only the preview overlay, not the selected section or template data.
    public void ClearTemplatePreviewSelection()
    {
        if(_templateSelectedRegion is null && _templateHighlightGroup.Length==0)return;
        _templateSelectedRegion=null;
        _templateHighlightGroup="";
        NotifyTemplateHighlight();
    }
    public void FixTemplatePreviewRegion(PreviewHitRegion hit)
    {
        _templateSelectedRegion=hit;
        _templateHighlightGroup=hit.Group;
        NotifyTemplateHighlight();
    }
    public void HighlightTemplateGroup(string group)
    {
        _templateHighlightGroup=group;
        _templateSelectedRegion=null;
        NotifyTemplateHighlight();
    }
    private void RelocalizeTemplatePreview()
    {
        string id = _templatePreviewMode?.Id ?? "actual";
        TemplatePreviewModes.Clear();
        void Add(string key,string ru,string uk,string en) => TemplatePreviewModes.Add(new(key,TemplateEditorLayout.L(_settings.Language,ru,uk,en)));
        Add("actual","По данным","За даними","Actual");
        Add("front","Лицо","Лице","Front");
        Add("back","Оборот с данными","Зворот з даними","Data back");
        Add("empty","Оборот без данных","Зворот без даних","Empty back");
        TemplatePreviewMode = TemplatePreviewModes.First(m => m.Id == id);
    }
    private string ForcedPreviewCaption => _templatePreviewDemo
        ? TemplateEditorLayout.L(_settings.Language,"Демо","Демо","Demo") : "";
    private void NotifyTemplatePreview()
    {
        _templateHoverRegion=null;
        NotifyTemplateHighlight();
        foreach(var property in new[]{nameof(TemplatePreviewForced),nameof(PreviewScene),nameof(PreviewPreviousScene),
            nameof(PreviewDisplayWidth),nameof(PreviewDisplayHeight),nameof(PreviewUpdating),nameof(PreviewEmpty),
            nameof(PreviewError),nameof(PreviewFaceLabel),nameof(PreviewCardCount),nameof(TemplateHighlightSection),nameof(TemplateHighlightGroup),nameof(PrintReady)})
            Changed(property);
    }
    private void ResetTemplatePreview()
    {
        if (_templatePreviewMode?.Id != "actual" && TemplatePreviewModes.FirstOrDefault(m=>m.Id=="actual") is { } actual)
        {
            _templatePreviewMode = actual;
            Changed(nameof(TemplatePreviewMode));
        }
        CancelTemplatePreview();
        NotifyTemplatePreview();
    }
    private void CancelTemplatePreview()
    {
        _templatePreviewCancellation?.Cancel();
        _templatePreviewCancellation = null;
        var old = _templatePreviewScene;
        _templatePreviewScene = null;
        _templatePreviewBusy = false;
        _templatePreviewDemo = false;
        _templatePreviewError = "";
        old?.Dispose();
    }
    private void ScheduleTemplatePreview()
    {
        CancelTemplatePreview();
        if (!TemplatePreviewForced) { NotifyTemplatePreview(); return; }
        _templatePreviewBusy = true;
        NotifyTemplatePreview();
        var cancellation = new CancellationTokenSource();
        _templatePreviewCancellation = cancellation;
        var mode = _templatePreviewMode!.Id;
        var appearance = SnapshotAppearance(_appearance);
        var mapping = CloneMap(_map);
        var anchor = _cardParts.Count > _cardIndex ? _cardParts[_cardIndex].Front : null;
        var chosen = VaultRecords.FirstOrDefault(r=>r.Selected && r.Uuid == anchor?.Record.Uuid)
            ?? VaultRecords.FirstOrDefault(r=>r.Selected);
        var record = chosen is null ? null : SnapshotRecord(chosen);
        var partnerId = _cardParts.Count > _cardIndex && anchor?.Record.Uuid == chosen?.Uuid
            ? _cardParts[_cardIndex].Continuation?.Record.Uuid : null;
        var partner = partnerId is Guid id && id != chosen?.Uuid
            ? VaultRecords.FirstOrDefault(r=>r.Selected && r.Uuid==id) : null;
        _ = BuildTemplatePreviewAsync(cancellation, mode, appearance, mapping, record,
            partner is null ? null : SnapshotRecord(partner), anchor?.Number ?? 1);
    }
    private async Task BuildTemplatePreviewAsync(CancellationTokenSource cancellation,string mode,CardAppearance appearance,
        Dictionary<string,FieldRule> mapping,VaultRecord? record,VaultRecord? partner,int partNumber)
    {
        var token = cancellation.Token;
        try
        {
            // Debounce rapidly edited values and share the existing worker limit.
            await Task.Delay(85,token);
            var result = await Task.Run(async () =>
            {
                await _previewRenderGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    PhysicalCard? card = null;
                    if(record is not null)
                    {
                        var parts=CardLayoutEngine.Build(partner is null ? [record] : [record,partner],appearance,mapping,cancellationToken:token);
                        var current=parts.FirstOrDefault(c=>c.Front.Record.Uuid==record.Uuid && c.Front.Number==partNumber)
                            ?? parts.FirstOrDefault(c=>c.Front.Record.Uuid==record.Uuid);
                        if(current is not null && (mode=="front" || (mode=="back" && current.Continuation is not null) ||
                            (mode=="empty" && current.Continuation is null))) card=current;
                    }
                    bool demo=card is null;
                    if(card is null)
                    {
                        var example=new VaultRecord
                        {
                            Uuid=Guid.Parse("00000000-0000-0000-0000-000000000001"),Title="Demo account",Group="DEMO",
                            UserName="demo@example.invalid",Password="TEST-0000-DEMO",Url="https://example.invalid",
                            Notes="Synthetic preview only",IconId=10
                        };
                        var parts=CardLayoutEngine.Build([example],appearance,mapping,cancellationToken:token);
                        var front=parts.FirstOrDefault()?.Front ?? new CardPart(example,[],1,1);
                        card=new PhysicalCard(front,mode=="back"?parts.FirstOrDefault()?.Continuation ?? front:null);
                    }
                    var scene=CardSkiaRenderer.Record(card,appearance,mode!="front");
                    return (Scene:scene,Demo:demo);
                }
                finally { _previewRenderGate.Release(); }
            },token);
            if(token.IsCancellationRequested || !ReferenceEquals(_templatePreviewCancellation,cancellation))
            { result.Scene.Dispose(); return; }
            _templatePreviewScene=result.Scene;
            _templatePreviewDemo=result.Demo;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception)
        {
            if(ReferenceEquals(_templatePreviewCancellation,cancellation))
                _templatePreviewError=TemplateEditorLayout.L(_settings.Language,"Не удалось построить образец.","Не вдалося побудувати зразок.","Unable to render the sample.");
        }
        finally
        {
            if(ReferenceEquals(_templatePreviewCancellation,cancellation))
            {
                _templatePreviewCancellation=null;
                _templatePreviewBusy=false;
                NotifyTemplatePreview();
            }
            cancellation.Dispose();
        }
    }
}
