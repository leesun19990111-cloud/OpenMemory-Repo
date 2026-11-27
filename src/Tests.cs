using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpenMemory {
 public static class Tests {
  static List<string> checks = new List<string>();
  static void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL: " + name); checks.Add("PASS: " + name); }
  public static int Run(string root) {
   Directory.CreateDirectory(root);
   try {
    Check(Data.Normalize(@"C:\설계 자료\1층 평면.dwg")==@"C:\설계 자료\1층 평면.dwg","Korean paths and spaces");
    Check(Data.Normalize(@"\\server\설계\프로젝트.rvt").StartsWith(@"\\server"),"UNC paths");
    Check(Data.Normalize("Book1.xlsx")=="","Unsaved/relative document rejected");
    Check(Data.Normalize(@"C:relative.xlsx")=="","Drive relative path rejected");
    Check(Data.Normalize("https://server/a.xlsx")=="","Cloud URL rejected");
    Check(Data.Normalize(@"C:\test.exe")=="" && Data.Normalize(@"C:\test.cmd")=="","Executable files rejected");
    Check(Data.Normalize(@"C:\x.txt:evil.xlsx")=="","Alternate stream rejected");
    Check(Data.Normalize(@"\\?\C:\test.xlsx")=="","Device paths rejected");
    var entries=Data.Unique(new[]{new Entry {Path=@"C:\작업\A.xlsx"},new Entry {Path=@"c:\작업\a.xlsx"},new Entry {Path="Book1"}});
    Check(entries.Count==1,"Case-insensitive deduplication and unsaved filtering");
    var path=Path.Combine(root,"roundtrip.xml");
    var s=new Session {Files=new List<Entry>{new Entry {Path=@"\\nas\팀 A\도면 & 계획.dwg",Executable=@"C:\Program Files\Autodesk\acad.exe",Selected=false}},Warnings=new List<string>{"저장 필요 <1>"}};
    Data.Write(path,s); var loaded=Data.Read<Session>(path);
    Check(loaded.Files[0].Path==s.Files[0].Path && !loaded.Files[0].Selected && loaded.Warnings[0]==s.Warnings[0],"XML Unicode, special characters and selection round trip");
    s.Files[0].Selected=true; Data.Write(path,s); Check(Data.Read<Session>(path).Files[0].Selected,"Atomic replacement");
    Check(Directory.GetFiles(root,"*.tmp").Length==0,"No leftover partial writes");
    var bad=Path.Combine(root,"malformed.xml");File.WriteAllText(bad,"<Session><broken>");bool rejected=false;try {Data.Read<Session>(bad);}catch {rejected=true;}Check(rejected,"Malformed session rejected");
    var dtd=Path.Combine(root,"dtd.xml");File.WriteAllText(dtd,"<!DOCTYPE Session [<!ENTITY x SYSTEM 'file:///C:/secret'>]><Session><Created>&x;</Created></Session>");rejected=false;try {Data.Read<Session>(dtd);}catch {rejected=true;}Check(rejected,"External entities prohibited");
    var missing=Engine.Restore(new Entry {Path=Path.Combine(root,"missing.xlsx")},CancellationToken.None).GetAwaiter().GetResult(); Check(missing.Contains("파일 없음"),"Missing file reported without launch");
    bool empty=false;try {Engine.Save(new Session());}catch(InvalidOperationException){empty=true;}Check(empty,"Empty capture cannot replace a saved session");
    var uncheckedSession=new Session {Files=new List<Entry>{new Entry {Path=@"C:\작업\제외.xlsx",Selected=false}}};
    try {Engine.Save(uncheckedSession);}catch(InvalidOperationException){}Check(uncheckedSession.Files.Count==1,"All-unchecked save preserves the working list");
    Check(!Data.IsLive(new BridgeState {Pid=System.Diagnostics.Process.GetCurrentProcess().Id,Started=0}),"Stale or foreign bridge process rejected");
    Check(Data.IsRevit(@"C:\a.RVT") && Data.AppFor(@"C:\a.hwpx")=="한글","File routing");
    var folderEntry=new Entry {Kind="Folder",Path=@"C:\"};Check(Data.EntryPath(folderEntry)==@"C:\","Drive root folder accepted");
    Check(Data.EntryPath(new Entry {Kind="Folder",Path=@"\\nas\shared\계획"})==@"\\nas\shared\계획","UNC folders accepted");
    Check(Data.EntryPath(new Entry {Kind="Application",Path=@"C:\Apps\editor.exe"})!= "","Typed application entry accepted");
    Check(Data.EntryPath(new Entry {Kind="Application",Path=@"C:\Apps\editor.cmd"})=="","Script not accepted as executable app");
    Check(Data.EntryPath(new Entry {Kind="PackagedApp",Path="Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"})!="","Packaged app ID accepted");
    Check(Data.EntryPath(new Entry {Kind="PackagedApp",Path="bad!App /evil"})=="","Invalid packaged app ID rejected");
    Check(Data.EntryPath(new Entry {Kind="ShellFolder",Path="::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"})!="","Explorer virtual folder accepted");
    Check(Data.EntryPath(new Entry {Kind="Unknown",Path=@"C:\a.txt"})=="","Unknown entry kind rejected");
    var oldXml=Path.Combine(root,"legacy.xml");File.WriteAllText(oldXml,"<Session><Schema>1</Schema><Files><Entry><Path>C:\\work\\a.xlsx</Path><Selected>true</Selected></Entry></Files><Warnings /></Session>");
    var legacy=Data.Read<Session>(oldXml);Check(Data.KindOf(legacy.Files[0])=="File" && legacy.ExcludedKeys.Count==0,"Version 1 sessions remain readable");
    var removable=new Entry {Path=@"C:\작업\제외.xlsx"};var remaining=new Entry {Kind="Folder",Path=@"C:\작업"};
    var edited=new Session {Files=new List<Entry>{removable,remaining}};Data.Exclude(edited,new[]{removable});
    Check(edited.Files.Count==1 && edited.Files[0]==remaining,"Exclude removes the row, leaving unrelated rows");
    var editedPath=Engine.SaveEdited(edited,Path.Combine(root,"edited"));var savedEdited=Data.Read<Session>(editedPath);
    var rescanned=new Session {Files=new List<Entry>{removable,remaining}};
    Check(savedEdited.Files.Count==1 && savedEdited.ExcludedKeys.Count==0 && rescanned.Files.Count==2,"Exclusion edits only this record; fresh capture remains independent");
    Data.Exclude(edited,new[]{remaining});var emptyPath=Engine.SaveEdited(edited,Path.Combine(root,"edited"));Check(Data.Read<Session>(emptyPath).Files.Count==0,"Removing the last row saves an empty edited session");
    var mixed=Data.Unique(new[]{new Entry{Path=@"C:\a.xlsx"},new Entry{Path=@"c:\A.xlsx"},new Entry{Kind="Folder",Path=@"C:\a.xlsx"}});Check(mixed.Count==2,"Deduplication includes item kind");
    var multiProfile=Data.Unique(new[]{new Entry{Kind="Browser",Path=@"C:\Apps\chrome.exe",Profile="Default"},new Entry{Kind="Browser",Path=@"C:\Apps\chrome.exe",Profile="Profile 1"}});Check(multiProfile.Count==2,"Browser profiles remain distinct");
    var quotePath=@"C:\설계 자료\";var quoteArgs=Desktop.SplitArguments("app.exe "+Desktop.Quote(quotePath));Check(quoteArgs.Length==2 && quoteArgs[1]==quotePath,"Windows argument quoting preserves trailing backslashes and spaces");
    Check(Desktop.BrowserArguments(new Entry{Path=@"C:\chrome.exe",Profile="Profile 1"}).Contains("--restore-last-session"),"Chromium session restore switch");
    bool badProfile=false;try{Desktop.BrowserArguments(new Entry{Path=@"C:\chrome.exe",Profile="..\\evil"});}catch(InvalidDataException){badProfile=true;}Check(badProfile,"Browser profile path traversal rejected");
    Check(Desktop.BrowserArguments(new Entry{Path=@"C:\firefox.exe"})=="","Unsupported browser switches not invented");
    var startupCmd=Startup.Command(@"C:\Program Files\OpenMemory\OpenMemory.exe");var startupArgs=Desktop.SplitArguments(startupCmd);Check(startupArgs.Length==2 && startupArgs[1]=="--startup","Login startup command correctly quoted");
    var resetRoot=Path.Combine(root,"reset-test","sessions");var recordName="20260101-010101-001-abcdef.xml";Directory.CreateDirectory(resetRoot);Data.Write(Path.Combine(resetRoot,recordName),new Session());File.WriteAllText(Path.Combine(resetRoot,"keep.txt"),"not a session");File.WriteAllText(Path.Combine(resetRoot,"unrelated.xml"),"not a session");File.WriteAllText(Path.Combine(resetRoot,"unexpected.DOCX"),"not a session");
    Check(Data.SessionFiles(resetRoot).Length==1,"History accepts only exact OpenMemory session filenames");
    Check(Startup.ResetSessions(resetRoot)==1 && !File.Exists(Path.Combine(resetRoot,recordName)) && File.Exists(Path.Combine(resetRoot,"keep.txt")) && File.Exists(Path.Combine(resetRoot,"unrelated.xml")),"Reset removes only stored session XMLs");
    Check(Directory.GetFiles(Path.Combine(root,"reset-test","reset-backups"),recordName,SearchOption.AllDirectories).Any(p=>Path.GetFileName(p)==recordName),"Reset keeps a recoverable backup");
    Check(Data.AppFor(@"C:\a.pptm")=="PowerPoint" && Data.Normalize(@"C:\a.ppsx").Length>0,"PowerPoint document formats routed correctly");
    Check(Data.AppFor(@"C:\a.aep")=="After Effects" && Data.AppFor(@"C:\a.prproj")=="Premiere Pro","Adobe project formats routed correctly");
    Check(!Adobe.Fresh(DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o")) && !Adobe.Fresh(DateTimeOffset.UtcNow.AddMinutes(1).ToString("o")),"Stale and future Adobe state rejected");
    var appOnly=new Entry {Kind="Application",Path=@"C:\Apps\Revit.exe",Executable=@"C:\Apps\Revit.exe",WindowTitles=new List<string>{"Model A","Model B"}};
    var coverage=new Session {Files=new List<Entry>{appOnly}};Engine.DescribeCoverage(coverage);Check(appOnly.Note.Contains("문서 연결 없음"),"Unconnected application clearly described as program-only restore");
    coverage.Files.Add(new Entry {Path=@"C:\Model.rvt",Executable=appOnly.Executable});Engine.DescribeCoverage(coverage);Check(appOnly.Note.Contains("문서 1개 복원"),"Connected application reports actual captured document count");
    var hintsPath=Path.Combine(root,"window-hints.xml");Data.Write(hintsPath,coverage);Check(Data.Read<Session>(hintsPath).Files[0].WindowTitles.SequenceEqual(appOnly.WindowTitles),"Unconnected document/window titles survive save and reload");
    var placementSession=new Session {Files=new List<Entry>{removable,remaining},Windows=new List<WindowLayout>{new WindowLayout {EntryKeys=new List<string>{Data.Key(removable)}},new WindowLayout {EntryKeys=new List<string>{Data.Key(remaining)}}}};
    Data.Exclude(placementSession,new[]{removable});Check(placementSession.Windows.Count==1 && placementSession.Windows[0].EntryKeys[0]==Data.Key(remaining),"Excluded document placement is removed while unrelated placement remains");
    File.WriteAllLines(Path.Combine(root,"results.txt"),checks);return 0;
   } catch(Exception e) {checks.Add(e.ToString());File.WriteAllLines(Path.Combine(root,"results.txt"),checks);return 1;}
  }
 }
}
