namespace KeePassXCCardPrint.Services;

// One derived UI state. Worker lifetime and frame ownership stay in ScannerView;
// none of these presentation decisions can start a source implicitly.
internal enum ScannerWorkspaceState { Idle, LocalLive, NetworkConnecting, NetworkLive, NetworkFailed, Frozen, Image, Pdf, Text }
internal enum ScannerInputKind { Camera, File, Clipboard }
internal enum ScannerCameraCommand { Start, Stop, Cancel, Retry }
internal static class ScannerWorkspace
{
    // Input choice is independent of whether the source is currently running or populated.
    internal static (bool Camera,bool File,bool Clipboard) InputActions(ScannerInputKind kind)=>
        (kind==ScannerInputKind.Camera,kind==ScannerInputKind.File,kind==ScannerInputKind.Clipboard);
    internal static ScannerWorkspaceState Resolve(bool camera,bool network,bool ended,NetworkCameraState state,bool frozen,bool pdf,bool image,bool text)
    {
        if(network&&ended)return ScannerWorkspaceState.NetworkFailed;
        if(camera)
        {
            if(network&&(state is NetworkCameraState.Connecting or NetworkCameraState.Retrying))return ScannerWorkspaceState.NetworkConnecting;
            if(frozen)return ScannerWorkspaceState.Frozen;
            return network?ScannerWorkspaceState.NetworkLive:ScannerWorkspaceState.LocalLive;
        }
        if(pdf)return ScannerWorkspaceState.Pdf;
        if(image)return ScannerWorkspaceState.Image;
        if(text)return ScannerWorkspaceState.Text;
        return ScannerWorkspaceState.Idle;
    }
    internal static ScannerCameraCommand CameraCommand(ScannerWorkspaceState state)=>state switch
    {
        ScannerWorkspaceState.NetworkConnecting=>ScannerCameraCommand.Cancel,
        ScannerWorkspaceState.NetworkFailed=>ScannerCameraCommand.Retry,
        ScannerWorkspaceState.LocalLive or ScannerWorkspaceState.NetworkLive or ScannerWorkspaceState.Frozen=>ScannerCameraCommand.Stop,
        _=>ScannerCameraCommand.Start
    };
}
