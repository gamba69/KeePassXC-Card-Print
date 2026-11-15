using System.IO.Compression;
using System.Text;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Offline tests; no network and no real vault. Run before managed/AOT release.
internal static class UpdateSelfTests
{
    internal static string Report {get;private set;}="";
    internal static int Run()
    {
        var reports=new List<string>();int failed=0;
        void Test(string name,Action action)
        {
            try {action();reports.Add("[PASS] "+name);}
            catch(Exception ex){failed++;reports.Add("[FAIL] "+name+": "+ex.Message);}
        }
        void Require(bool value,string message="Assertion failed")
        {if(!value)throw new InvalidDataException(message);}
        void Reject(Action action)
        {
            try{action();throw new InvalidOperationException("Expected rejection.");}
            catch(InvalidDataException){}
        }
        Test("semantic version / no downgrade",()=>
        {
            Require(CardPrintUpdatePolicy.IsEligible("v7.0.1","7.0.0",false));
            Require(CardPrintUpdatePolicy.IsEligible("7.0.0","7.0.0",true));
            Require(!CardPrintUpdatePolicy.IsEligible("7.0.0","7.0.0",false));
            Require(!CardPrintUpdatePolicy.IsEligible("6.9.9","7.0.0",true));
            Reject(()=>CardPrintUpdatePolicy.ParseVersion("v7.0"));
        });
        Test("owned nested files and traversal protection",()=>
        {
            foreach(var p in new[]{"KeePassXCCardPrint.exe","fonts/IBM-Plex.ttf",
                "fonts/licenses/IBM-Plex-OFL.txt","KpcXpsBridge.dll"})
                Require(CardPrintUpdatePolicy.IsOwnedFile(p),p);
            foreach(var p in new[]{"fonts/../../evil.dll","fonts//evil.ttf", "fonts/../evil.ttf",
                "fonts\\x.ttf","C:/evil.dll","fonts/licenses/.env","settings.json",
                "database.kdbx","fonts/CON.ttf","fonts/licenses/file.txt:stream",
                "fonts/licenses/evil.txt/other","fonts/x.ttf/","evil.ps1","fonts/shortcut.lnk"})
                Require(!CardPrintUpdatePolicy.IsOwnedFile(p),p);
        });
        Test("checksum / release naming",()=>
        {
            var name=CardPrintUpdatePolicy.ZipName("v7.0.0");
            Require(name=="KeePassXCCardPrint-7.0.0-win-x64.zip");
            Require(CardPrintUpdatePolicy.ChecksumName("7.0.0")==name+".sha256");
            Require(CardPrintUpdatePolicy.ParseChecksum(new string('a',64)+"  "+name+"\n",name)==
                new string('a',64));
            Reject(()=>CardPrintUpdatePolicy.ParseChecksum(new string('a',64)+"  other.zip\n",name));
        });
        Test("signed release binds repository, tag and archive",()=>
        {
            var tag="v7.0.0";
            var zip=CardPrintUpdatePolicy.ZipName(tag);
            var digest=new string('a',64);
            var commit=new string('b',40);
            var purl="pkg:github/gamba69/keepassxc-card-print@"+tag;
            var statement="{\"_type\":\"https://in-toto.io/Statement/v1\","+
                "\"predicateType\":\"https://in-toto.io/attestation/release/v0.2\","+
                "\"predicate\":{\"repository\":\"gamba69/KeePassXC-Card-Print\","+
                "\"repositoryId\":\"1451452767\",\"tag\":\""+tag+"\",\"purl\":\""+purl+"\"},"+
                "\"subject\":[{\"name\":\""+zip+"\",\"digest\":{\"sha256\":\""+digest+"\"}},"+
                "{\"uri\":\""+purl+"\",\"digest\":{\"sha1\":\""+commit+"\"}}]}";
            CardPrintUpdatePolicy.VerifySignedStatement(statement,tag,commit,zip,digest);
            Reject(()=>CardPrintUpdatePolicy.VerifySignedStatement(
                statement.Replace("1451452767","1454413194",StringComparison.Ordinal),
                tag,commit,zip,digest));
            Reject(()=>CardPrintUpdatePolicy.VerifySignedStatement(
                statement.Replace(digest,new string('c',64),StringComparison.Ordinal),
                tag,commit,zip,digest));
            Reject(()=>CardPrintUpdatePolicy.VerifySignedStatement(
                statement.Replace(purl,"pkg:github/other/project@"+tag,StringComparison.Ordinal),
                tag,commit,zip,digest));
        });
        Test("About language change notifies update controls",()=>
        {
            var settings=new KeePassXCCardPrint.Services.UiSettings {Language="ru"};
            var about=new KeePassXCCardPrint.ViewModels.AboutViewModel(settings);
            int raised=0;
            about.PropertyChanged+=(_,e)=>
            {
                if(e.PropertyName==nameof(about.UpdateLanguage))raised++;
            };
            settings.Language="uk";
            about.Relocalize();
            Require(raised==1,"Update language was not notified.");
            Require(about.UpdateLanguage=="uk","New language was not visible.");
        });
        var root=Path.Combine(Path.GetTempPath(),"kpc-update-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var target=Path.Combine(root,"install");Directory.CreateDirectory(target);
            var source=Path.Combine(root,"release.zip");
            var stage=Path.Combine(root,"stage");
            var updateRoot=Path.Combine(root,"updates");
            void Add(ZipArchive archive,string name,string value)
            {
                var entry=archive.CreateEntry(name);
                using var stream=new StreamWriter(entry.Open(),Encoding.UTF8);
                stream.Write(value);
            }
            Test("recursive extract / manifest validation",()=>
            {
                using(var archive=ZipFile.Open(source,ZipArchiveMode.Create))
                {
                    foreach(var f in new[]{"KeePassXCCardPrint.exe","KpcXpsBridge.dll","README.md",
                        "LICENSE","THIRD_PARTY_NOTICES.md"})Add(archive,f,"new "+f);
                    for(int i=0;i<8;i++)
                    {
                        Add(archive,$"fonts/test{i}.ttf","font "+i);
                        Add(archive,$"fonts/licenses/test{i}.txt","license "+i);
                    }
                }
                CardPrintUpdatePolicy.Extract(source,Path.Combine(stage,"payload"));
                Require(File.Exists(Path.Combine(stage,"payload","fonts","licenses","test0.txt")));
                File.WriteAllText(Path.Combine(stage,"verified.txt"),"7.0.0");
                WindowsUpdateService.WritePayloadManifest(stage);
                WindowsUpdateService.VerifyPayloadManifest(stage);
                Require(WindowsUpdateService.ReadManifest(Path.Combine(stage,"files.txt")).Count==21);
            });
            Test("result journal stays installation-specific and is consumed once",()=>
            {
                var other=Path.Combine(root,"another portable copy");
                Directory.CreateDirectory(other);
                var outcome=new UpdateOperationResult("installed","update","7.0.3","7.1.0");
                UpdateOperationResults.Save(updateRoot,target,outcome);
                Require(UpdateOperationResults.Read(updateRoot,target)==outcome);
                Require(UpdateOperationResults.Read(updateRoot,other) is null);
                Require(UpdateOperationResults.PathFor(updateRoot,target)!=
                    UpdateOperationResults.PathFor(updateRoot,other));
                UpdateOperationResults.Consume(updateRoot,target);
                Require(UpdateOperationResults.Read(updateRoot,target) is null);
                UpdateOperationResults.Save(updateRoot,target,new("rolled-back","repair",
                    "7.1.0","7.1.0","Injected failure"));
                Require(UpdateOperationResults.Read(updateRoot,target)?.Status=="rolled-back");
                UpdateOperationResults.Consume(updateRoot,target);
                Reject(()=>UpdateOperationResults.Save(updateRoot,target,
                    new("arbitrary","update","7.0.3","7.1.0")));
            });
            Test("7.0.x completion marker is migrated only for matching installation",()=>
            {
                var legacyStage=Path.Combine(updateRoot,"pending-7.1.0");
                Directory.CreateDirectory(legacyStage);
                File.WriteAllText(Path.Combine(legacyStage,"verified.txt"),"7.1.0");
                File.WriteAllText(Path.Combine(legacyStage,"target.txt"),target);
                Directory.CreateDirectory(updateRoot);
                File.WriteAllText(Path.Combine(updateRoot,"completed.txt"),legacyStage);
                UpdateOperationResults.CaptureLegacyCompleted(updateRoot,target,"7.1.0");
                Require(UpdateOperationResults.Read(updateRoot,target)?.Status=="installed");
                UpdateOperationResults.Consume(updateRoot,target);
                UpdateOperationResults.CaptureLegacyCompleted(updateRoot,target,"7.2.0");
                Require(UpdateOperationResults.Read(updateRoot,target) is null);
                File.Delete(Path.Combine(updateRoot,"completed.txt"));
            });
            Test("recursive apply / obsolete managed file / user data",()=>
            {
                File.WriteAllText(Path.Combine(target,"KeePassXCCardPrint.exe"),"old exe");
                File.WriteAllText(Path.Combine(target,"database.kdbx"),"SECRET");
                Directory.CreateDirectory(Path.Combine(target,"fonts","licenses"));
                File.WriteAllText(Path.Combine(target,"fonts","licenses","test0.txt"),"old license");
                var progress=new List<UpdateOperationProgress>();
                WindowsUpdateService.ApplyVerifiedFiles(stage,target,updateRoot,
                    onProgress:progress.Add);
                Require(progress.Count==21);
                Require(progress[^1].Percent==90);
                Require(progress.Any(x=>x.Detail.StartsWith("fonts/licenses/",StringComparison.Ordinal)));
                Require(File.ReadAllText(Path.Combine(target,"database.kdbx"))=="SECRET");
                Require(File.ReadAllText(Path.Combine(target,"fonts","licenses","test0.txt"))
                    .Contains("license 0",StringComparison.Ordinal));
                Require(File.Exists(WindowsUpdateService.OwnedManifestPath(updateRoot,target)));
            });
            Test("rollback after simulated replace failure",()=>
            {
                var old=File.ReadAllText(Path.Combine(target,"KeePassXCCardPrint.exe"));
                var nested=File.ReadAllText(Path.Combine(target,"fonts","licenses","test0.txt"));
                var before=File.ReadAllText(WindowsUpdateService.OwnedManifestPath(updateRoot,target));
                bool invoked=false;
                var progress=new List<UpdateOperationProgress>();
                try
                {
                    WindowsUpdateService.ApplyVerifiedFiles(stage,target,updateRoot,
                        beforeReplace:path=>{if(path=="fonts/licenses/test0.txt")
                        {invoked=true;throw new IOException("Injected failure");}},
                        onProgress:progress.Add);
                    throw new Exception("Failure injection not reached.");
                }
                catch(IOException){}
                Require(invoked);
                Require(progress.Any(x=>x.Phase=="rollback"));
                Require(File.ReadAllText(Path.Combine(target,"KeePassXCCardPrint.exe"))==old);
                Require(File.ReadAllText(Path.Combine(target,"fonts","licenses","test0.txt"))==nested);
                Require(File.ReadAllText(WindowsUpdateService.OwnedManifestPath(updateRoot,target))==before);
            });
            Test("obsolete managed fonts removed, customized fonts retained",()=>
            {
                var fonts=Path.Combine(target,"fonts");
                var obsolete=Path.Combine(fonts,"unused.ttf");
                var customized=Path.Combine(fonts,"custom.ttf");
                File.WriteAllText(obsolete,"owned original");
                File.WriteAllText(customized,"locally edited");
                var ownership=WindowsUpdateService.OwnedManifestPath(updateRoot,target);
                File.AppendAllText(ownership,
                    "fonts/unused.ttf\t"+CardPrintUpdatePolicy.HashFile(obsolete)+"\n"+
                    "fonts/custom.ttf\t"+CardPrintUpdatePolicy.HashFile(obsolete)+"\n");
                WindowsUpdateService.ApplyVerifiedFiles(stage,target,updateRoot);
                Require(!File.Exists(obsolete),"Unmodified obsolete font was retained.");
                Require(File.ReadAllText(customized)=="locally edited",
                    "Locally customized file was deleted.");
                Require(File.ReadAllText(Path.Combine(target,"database.kdbx"))=="SECRET");
            });
            Test("modified payload cannot be applied",()=>
            {
                var nested=Path.Combine(stage,"payload","fonts","licenses","test0.txt");
                File.AppendAllText(nested,"TAMPER");
                Reject(()=>WindowsUpdateService.VerifyPayloadManifest(stage));
            });
            Test("ZIP slip and duplicate filename refused",()=>
            {
                var bad=Path.Combine(root,"bad.zip");
                using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create))
                {Add(archive,"fonts/../../danger.dll","BAD");}
                Reject(()=>CardPrintUpdatePolicy.Extract(bad,Path.Combine(root,"bad-payload")));
                var duplicate=Path.Combine(root,"duplicate.zip");
                using(var archive=ZipFile.Open(duplicate,ZipArchiveMode.Create))
                {
                    Add(archive,"KeePassXCCardPrint.exe","one");
                    Add(archive,"KEEPASSXCCARDPRINT.EXE","two");
                }
                Reject(()=>CardPrintUpdatePolicy.Extract(duplicate,
                    Path.Combine(root,"duplicate-payload")));
            });
        }
        finally
        {
            try{Directory.Delete(root,true);}catch(IOException){}
        }
        Report=string.Join(Environment.NewLine,reports);
        return failed==0?0:7;
    }
}
