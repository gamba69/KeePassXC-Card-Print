using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KeePassXCCardPrint.Models;

public sealed class VaultRecord : INotifyPropertyChanged
{
    public Guid Uuid { get; init; }
    public string Group { get; init; } = "";
    public string Title { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Password { get; init; } = "";
    public string Url { get; init; } = "";
    // KeePassXC KP2A_URL attributes, kept separate from the primary URL.
    public string[] AdditionalUrls { get; init; } = [];
    // Counts original KP2A_URL entries; never includes their values in diagnostics.
    public int AdditionalUrlCandidateCount { get; init; }
    public string Notes { get; init; } = "";
    public int IconId { get; init; }
    public Guid CustomIconUuid { get; init; }
    public byte[]? CustomIconPng { get; init; }
    // Distinct images are displayed at the group and entry titles.
    // Standard icons are cached app-wide; decoded custom icons are owned by each record.
    public Bitmap? IconImage { get; init; }
    public bool IconImageOwned { get; init; }
    public int GroupIconId { get; init; }
    public Guid GroupCustomIconUuid { get; init; }
    public Bitmap? GroupIconImage { get; init; }
    public bool GroupIconImageOwned { get; init; }
    public Dictionary<string, string> AdditionalFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasAttachments { get; init; }
    private string _printStatus="—";
    public string PrintStatus {get=>_printStatus;set{if(_printStatus==value)return;_printStatus=value;Changed();}}
    private bool _selected;
    private bool _visible = true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
    public bool Visible { get => _visible; set { if (_visible == value) return; _visible = value; Changed(); } }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VaultSnapshot
{
    public Guid RootUuid { get; init; }
    public string DisplayName { get; init; } = "";
    public string CipherName { get; init; } = "?";
    public string KdfName { get; init; } = "?";
    public int GroupCount { get; init; }
    public string FormatVersion { get; init; } = "";
    public List<VaultRecord> Records { get; init; } = [];
}
