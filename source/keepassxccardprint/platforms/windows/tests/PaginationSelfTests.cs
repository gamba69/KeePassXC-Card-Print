using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;
using System.Text.Json;

namespace KeePassXCCardPrint.Rendering;

internal static class PaginationSelfTests
{
    internal static void Run(Action<bool,string> check)
    {
        var map=new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase);
        var a=new CardAppearance { ContinuationRepeatLabel=true };
        static CardLine V(string field,float height=4) => new(CardLineKind.Value,"fixture",height,Field:field);
        static CardLine L(string field,float height=2) => new(CardLineKind.Label,"caption",height,Field:field);
        static CardLine Q(string field,float height=8) => new(CardLineKind.Qr,"",height,Field:field,QrUri:"fixture",QrStrict:true);
        static CardLine R(string field,int number) => new(CardLineKind.Recovery,"",3,Field:field,Cells:[number.ToString()]);
        List<List<CardLine>> Run(float height,params CardLine[] rows)=>CardPagination.Paginate(rows,height,a,map);
        bool Has(List<CardLine> page,string field)=>page.Any(x=>x.Field==field && x.Kind==CardLineKind.Value);
        void Fits(List<List<CardLine>> pages,float height)
        {
            check(pages.All(p=>p.Sum(l=>l.HeightMm)<=height+.001f && CardLayoutEngine.HasPayload(p)),"pagination has no overflow or decoration-only page");
        }
        var plain=Run(10,V("A"),V("B"),V("B"),V("B"));
        check(plain.Count==2 && plain[0].Count==2,"independent text fills current side");
        map["B"]=new FieldRule {KeepWithNext="field:C"};
        var linked=Run(10,V("A",6),V("B"),V("C"));
        check(linked.Count==2 && Has(linked[1],"B") && Has(linked[1],"C"),"named linked pair moves intact");
        var through=Run(12,V("A",6),V("B"),V("X"),V("C"));
        check(through.Count==2 && through[1].Select(l=>l.Field).SequenceEqual(new[]{"B","X","C"}),"named target includes intervening fields in original order");
        var absent=Run(10,V("A",6),V("B"),V("X"));
        check(absent.Count==2 && Has(absent[0],"B"),"missing named target does not fall back to next");
        map["B"].KeepWithNext="next";
        var next=Run(10,V("A",6),V("B"),V("X"));
        check(Has(next[1],"B") && Has(next[1],"X"),"next links actual printed fields");
        map["X"]=new FieldRule {KeepWithNext="field:C"};
        var chain=Run(12,V("A",6),V("B"),V("X"),V("C"));
        check(chain.Count==2 && chain[1].Count==3,"overlapping chains stay together");
        map["X"].BreakBefore=true;
        var forced=Run(15,V("A"),V("B"),V("X"),V("C"));
        check(forced.Count==2 && Has(forced[0],"B") && Has(forced[1],"X"),"forced break overrides link");
        map.Clear();
        map["Codes"]=new FieldRule {Role="codes",KeepWithNext="field:QR"};
        var codes=Run(14,L("Codes"),R("Codes",1),R("Codes",2),R("Codes",3),R("Codes",4),L("QR",1),Q("QR",7));
        Fits(codes,14);
        check(codes.Count==2 && codes[1].Any(x=>x.Kind==CardLineKind.Recovery) && codes[1].Any(x=>x.Kind==CardLineKind.Qr),"oversized chain keeps a recovery tail with QR when possible");
        check(codes.SelectMany(p=>p).Where(x=>x.Kind==CardLineKind.Recovery).SelectMany(x=>x.Cells!).SequenceEqual(new[]{"1","2","3","4"}),"recovery rows retained exactly once");
        var labels=Run(10,V("A",6),new CardLine(CardLineKind.Section,"section",1,Field:"B"),L("B"),L("B"),V("B"));
        Fits(labels,10);
        check(labels.Count==2 && labels[1].Count==4,"all section and caption lines move with the first payload");
        var repeat=Run(10,L("Text",3),V("Text"),V("Text"),V("Text"));
        Fits(repeat,10);
        check(repeat.Count==3 && repeat.All(p=>p[0].Kind==CardLineKind.Label),"continuation captions counted before fitting");
        map["B"]=new FieldRule{PaginationMode="whole"};
        var whole=Run(10,V("A",6),V("B"),V("B"));
        check(whole.Count==2 && whole[1].Count==2,"whole-field placement is mandatory");
        bool rejected=false;
        try { _=Run(7,V("B"),V("B")); } catch(InvalidOperationException ex) { rejected=ex.Message.Contains("«B»"); }
        check(rejected,"oversized whole field reports its name");
        map.Clear();
        var qr=Run(10,V("A",4),L("QR"),Q("QR"));
        check(qr.Count==2 && qr[1].Last().HeightMm==8 && qr[1].First().Kind==CardLineKind.Label,"QR and caption never shrink or separate");
        rejected=false;
        try { _=Run(9,L("QR"),Q("QR")); } catch(InvalidOperationException ex) { rejected=ex.Message.Contains("«QR»"); }
        check(rejected,"oversized QR and caption fail without clipping");
        check(Run(10,L("Empty")).Count==0,"empty decorations cannot generate a card");
        map["First"]=new FieldRule{BreakBefore=true};
        check(Run(10,V("First")).Count==1,"forced first field does not create blank side");
        var editor=new MappingEditor("en");
        editor.Load("USERNAME",new FieldRule{PaginationMode="whole",KeepWithNext="field:PASSWORD",BreakBefore=true});
        editor.RefreshLinkTargets(["PASSWORD"]);
        var rule=editor.ToRule();
        check(rule.PaginationMode=="whole" && rule.KeepWithNext=="field:PASSWORD" && rule.BreakBefore,"mapping editor retains all pagination settings");
        editor.RefreshLinkTargets([]);
        check(editor.ToRule().KeepWithNext=="field:PASSWORD","missing link is retained, not silently redirected");
        using var old=JsonDocument.Parse("{\"mapping\":{\"USERNAME\":{\"Role\":\"secret\"}}}");
        var oldRule=CardAppearanceStore.ParseMapping(old.RootElement)["username"];
        check(oldRule.PaginationMode=="auto" && oldRule.KeepWithNext=="none" && !oldRule.BreakBefore,"old custom mappings retain no explicit link");
        using var updated=JsonDocument.Parse("{\"mapping\":{\"USERNAME\":{\"PaginationMode\":\"whole\",\"KeepWithNext\":\"field:PASSWORD\",\"BreakBefore\":true}}}");
        var stored=CardAppearanceStore.ParseMapping(updated.RootElement)["username"];
        check(stored.PaginationMode=="whole" && stored.KeepWithNext=="field:PASSWORD" && stored.BreakBefore,"mapping JSON retains pagination settings");
        check(CardAppearancePresets.CreateMapping("archive")["USERNAME"].KeepWithNext=="field:PASSWORD","built-in login targets password explicitly");
    }
}
