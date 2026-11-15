using System.Text;
using System.Text.Json;
using Sigstore;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Identical limited compatibility transformation as in Cloud Sync: the target
// MUST already have been authenticated by TUF before correcting GitHub DNS URIs.
internal static class GithubTrustRootCompatibility
{
    internal static TrustedRoot ParseVerifiedTarget(ReadOnlyMemory<byte> target)
    {
        using var json=JsonDocument.Parse(target);
        if(json.RootElement.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("GitHub trusted root must be a JSON object.");
        using var output=new MemoryStream();
        using(var writer=new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach(var property in json.RootElement.EnumerateObject())
            {
                if(property.Name is not ("certificateAuthorities" or "timestampAuthorities"))
                { property.WriteTo(writer);continue; }
                if(property.Value.ValueKind!=JsonValueKind.Array)
                    throw new InvalidDataException("Trusted-root authority list is invalid.");
                writer.WritePropertyName(property.Name);
                writer.WriteStartArray();
                foreach(var authority in property.Value.EnumerateArray())
                {
                    if(authority.ValueKind!=JsonValueKind.Object)
                        throw new InvalidDataException("Trusted-root authority is invalid.");
                    writer.WriteStartObject();
                    bool found=false;
                    foreach(var field in authority.EnumerateObject())
                    {
                        if(field.Name!="uri") {field.WriteTo(writer);continue;}
                        if(found || field.Value.ValueKind!=JsonValueKind.String)
                            throw new InvalidDataException("Duplicate/invalid authority URI.");
                        found=true;
                        var raw=field.Value.GetString()!;
                        var normalized=raw switch
                        {
                            "fulcio.githubapp.com"=>"https://fulcio.githubapp.com",
                            "timestamp.githubapp.com"=>"https://timestamp.githubapp.com",
                            _=>raw
                        };
                        if(!Uri.TryCreate(normalized,UriKind.Absolute,out var uri) ||
                            uri.Scheme!=Uri.UriSchemeHttps || uri.UserInfo.Length!=0)
                            throw new InvalidDataException("Untrusted authority URI.");
                        writer.WriteString("uri",normalized);
                    }
                    if(!found)throw new InvalidDataException("Authority URI missing.");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return TrustedRoot.Deserialize(Encoding.UTF8.GetString(output.ToArray()));
    }
}
