using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Pagination over measured, immutable layout rows. A cut is selected
/// only at a legal boundary; no value, QR or recovery row is reformatted here.</summary>
internal static class CardPagination
{
    private const float Epsilon=.001f;
    private sealed record Unit(List<CardLine> Lines,int Field,float Height,float TrailingGap);
    private sealed record Block(string Name,FieldRule Rule,int Start,int End,List<CardLine> Captions);
    private readonly record struct Cost(long Links,long Preferences,int Pages)
    {
        public bool BetterThan(Cost other) => Links<other.Links || Links==other.Links &&
            (Preferences<other.Preferences || Preferences==other.Preferences && Pages<other.Pages);
    }

    internal static List<List<CardLine>> Paginate(IReadOnlyList<CardLine> rows,float capacity,
        CardAppearance appearance,IReadOnlyDictionary<string,FieldRule> mapping)
    {
        var units=new List<Unit>();
        var blocks=new List<Block>();
        for(int first=0;first<rows.Count;)
        {
            int end=first+1;
            string name=rows[first].Field;
            while(end<rows.Count && string.Equals(rows[end].Field,name,StringComparison.OrdinalIgnoreCase))end++;
            var lines=rows.Skip(first).Take(end-first).ToList();
            first=end;
            if(!CardLayoutEngine.HasPayload(lines))continue;
            var rule=mapping.TryGetValue(name,out var configured)?configured:FieldMappingDefaults.Infer(name);
            int start=units.Count;
            var pending=new List<CardLine>();
            var captions=new List<CardLine>();
            bool seenPayload=false;
            foreach(var line in lines)
            {
                if(!seenPayload && line.Kind==CardLineKind.Label)captions.Add(line);
                if(line.Kind==CardLineKind.Gap)continue;
                pending.Add(line);
                // Leading headings, all caption lines, and blank lines must
                // accompany the next actual payload. Each recovery row and QR
                // is already physically indivisible in the rendering model.
                if(!CardLayoutEngine.HasPayload([line]))continue;
                seenPayload=true;
                units.Add(new Unit(pending,blocks.Count,pending.Sum(x=>x.HeightMm),0));
                pending=[];
            }
            if(units.Count==start)continue;
            // Terminal decoration/blank lines cannot create a new page. Keep
            // the field gap only between printed fields, never at a page end.
            float gap=lines.Where(x=>x.Kind==CardLineKind.Gap).Sum(x=>x.HeightMm);
            if(gap>0)
            {
                var last=units[^1];
                last.Lines.Add(new CardLine(CardLineKind.Gap,"",gap,Field:name));
                units[^1]=last with {Height=last.Height+gap,TrailingGap=gap};
            }
            blocks.Add(new Block(name,rule,start,units.Count-1,captions));
        }
        int n=units.Count;
        if(n==0)return [];
        var prefix=new double[n+1];
        for(int i=0;i<n;i++)prefix[i+1]=prefix[i]+units[i].Height;
        double Height(int start,int end)=>prefix[end+1]-prefix[start]-units[end].TrailingGap;
        var allowed=Enumerable.Repeat(true,n+1).ToArray();
        var force=new bool[n+1];
        var preferences=new int[n+1];
        var links=new int[n+2];
        var forcedPrefix=new int[blocks.Count+1];
        for(int b=0;b<blocks.Count;b++)
        {
            var block=blocks[b];
            force[block.Start]=block.Rule.BreakBefore && block.Start>0;
            forcedPrefix[b+1]=forcedPrefix[b]+(force[block.Start]?1:0);
            string mode=FieldMappingDefaults.NormalizePagination(block.Rule.PaginationMode);
            bool otp=block.Rule.Role=="totp";
            bool qr=Enumerable.Range(block.Start,block.End-block.Start+1)
                .Any(i=>units[i].Lines.Any(x=>x.Kind==CardLineKind.Qr));
            bool hard=mode=="whole" || otp && (mode=="auto" || qr);
            bool soft=mode=="auto" && (block.Name.Equals("USERNAME",StringComparison.OrdinalIgnoreCase) ||
                block.Name.Equals("PASSWORD",StringComparison.OrdinalIgnoreCase));
            if(hard && Height(block.Start,block.End)>capacity+Epsilon)
                throw Failure(block.Name);
            for(int i=block.Start+1;i<=block.End;i++)
            {
                if(hard)allowed[i]=false;
                if(soft && Height(block.Start,block.End)<=capacity+Epsilon)preferences[i]++;
            }
        }
        // Resolve links against actual printed fields, after hidden/empty and
        // compacted OTP metadata have been removed. Explicit breaks win.
        var intervals=new List<(int First,int Last)>();
        var positions=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(int b=0;b<blocks.Count;b++)positions.TryAdd(blocks[b].Name,b);
        void Penalize(int first,int last)
        {
            if(first>last)return;
            links[first]++;links[last+1]--;
        }
        for(int b=0;b<blocks.Count;b++)
        {
            string link=FieldMappingDefaults.NormalizeLink(blocks[b].Rule.KeepWithNext);
            int target=-1;
            if(link=="next")target=b+1;
            else if(link.StartsWith("field:",StringComparison.Ordinal) && positions.TryGetValue(link[6..],out int found))target=found;
            if(target<=b || target>=blocks.Count)continue;
            // A forced break terminates links, not the target or field itself.
            if(forcedPrefix[target+1]!=forcedPrefix[b+1])continue;
            intervals.Add((b,target));
            // On oversized chains, preserve the tail of the source and start
            // of the target when possible; a split within the source is legal.
            Penalize(blocks[b].End+1,blocks[target].Start);
        }
        // Connected intervals that fit an empty side are preferred as one
        // complete block, including intermediate fields and internal rows.
        for(int k=0;k<intervals.Count;)
        {
            var (first,last)=intervals[k++];
            while(k<intervals.Count && intervals[k].First<=last)
                last=Math.Max(last,intervals[k++].Last);
            int start=blocks[first].Start,end=blocks[last].End;
            if(Height(start,end)<=capacity+Epsilon)Penalize(start+1,end);
        }
        for(int i=1;i<=n;i++)links[i]+=links[i-1];

        List<CardLine>? Repeat(int start)
        {
            if(!appearance.ContinuationRepeatLabel || start==0 || units[start-1].Field!=units[start].Field ||
                units[start].Lines.Any(l=>l.Kind==CardLineKind.Label))return null;
            return blocks[units[start].Field].Captions;
        }
        // Shortest path in an acyclic graph of legal page boundaries. The
        // objective is lexicographic: preserve links, then preferred whole
        // fields, then minimize pages. Equal costs fill the earlier side.
        var cost=new Cost[n+1];
        var next=new int[n+1];
        Array.Fill(next,-1);next[n]=n;
        for(int start=n-1;start>=0;start--)
        {
            if(!allowed[start])continue;
            double captionHeight=Repeat(start)?.Sum(x=>x.HeightMm)??0;
            for(int end=start;end<n;end++)
            {
                if(end>start && force[end])break;
                if(Height(start,end)+captionHeight>capacity+Epsilon)break;
                int cut=end+1;
                if(!allowed[cut] || next[cut]<0)continue;
                var candidate=new Cost(cost[cut].Links+links[cut],
                    cost[cut].Preferences+preferences[cut],cost[cut].Pages+1);
                if(next[start]<0 || candidate.BetterThan(cost[start]) || candidate==cost[start])
                { cost[start]=candidate;next[start]=cut; }
            }
        }
        if(next[0]<0)
        {
            // Identify a physically impossible atom or mandatory continuation
            // caption; never include its actual secret in the error message.
            for(int i=0;i<n;i++)
                if(Height(i,i)>capacity+Epsilon || allowed[i] &&
                    Height(i,i)+(Repeat(i)?.Sum(l=>l.HeightMm)??0)>capacity+Epsilon)
                    throw Failure(blocks[units[i].Field].Name);
            throw Failure(blocks[units[0].Field].Name);
        }
        var result=new List<List<CardLine>>();
        for(int start=0;start<n;start=next[start])
        {
            var page=new List<CardLine>();
            if(Repeat(start) is { } captions)page.AddRange(captions);
            for(int i=start;i<next[start];i++)page.AddRange(units[i].Lines);
            while(page.Count>0 && page[^1].Kind==CardLineKind.Gap)page.RemoveAt(page.Count-1);
            result.Add(page);
        }
        return result;
    }

    private static InvalidOperationException Failure(string name) =>
        new($"Поле «{name}»: неделимый блок или заголовок продолжения не помещается на стороне карточки.");
}
