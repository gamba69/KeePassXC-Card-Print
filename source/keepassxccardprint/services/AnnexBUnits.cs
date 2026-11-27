namespace KeePassXCCardPrint.Services;

internal static class AnnexBUnits
{
    // Accept three/four byte start codes and Annex B leading/trailing zero bytes.
    // Never pass start codes to APIs that expect a complete raw NAL.
    internal static IReadOnlyList<ReadOnlyMemory<byte>> Split(byte[] data)
    {
        var result=new List<ReadOnlyMemory<byte>>();int begin=-1,zeros=0;
        for(int i=0;i<data.Length;i++)
        {
            if(data[i]==0){zeros++;continue;}
            if(data[i]==1&&zeros>=2)
            {
                int end=i-zeros;
                if(begin>=0&&end>begin)result.Add(data.AsMemory(begin,end-begin));
                begin=i+1;
            }
            zeros=0;
        }
        int tail=data.Length-zeros;
        if(begin>=0&&tail>begin)result.Add(data.AsMemory(begin,tail-begin));
        if(result.Count==0)throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,"Empty or malformed Annex B access unit");
        return result;
    }
}
