using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OpenMemory {
 public static class Engine {
  public static async Task<Session> Scan() {
   var jobs = new[]{"Desktop","Folders","Excel","AutoCAD","한글","Word","PowerPoint","Photoshop","Illustrator","InDesign","Adobe"}.Select(kind => Task.Run(() => {
    string output = Path.Combine(Data.Root,"scan", Guid.NewGuid().ToString("N") + ".xml");
    try {
     var start = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--collect " + kind + " \"" + output + "\"") { UseShellExecute = false, CreateNoWindow = true };
     using(var p = Process.Start(start)) {
      if (!p.WaitForExit(18000)) { p.Kill(); p.WaitForExit(3000); return new Session { Warnings = new List<string>{kind + " 응답 시간 초과: 열린 대화상자를 닫고 다시 시도하세요."} }; }
      if(p.ExitCode != 0) throw new IOException("문서 조회 도우미 실패");
     }
     return Data.Read<Session>(output);
    } catch(Exception e) { return new Session { Warnings = new List<string>{kind + ": " + e.Message} }; }
    finally { try { if(File.Exists(output)) File.Delete(output); } catch {} }
   })).ToArray();
   var results = await Task.WhenAll(jobs);
   var session = new Session();
   foreach(var s in results) { session.Files.AddRange(s.Files); session.Warnings.AddRange(s.Warnings); session.Windows.AddRange(s.Windows); }
   var states = Data.Bridges();
   foreach(var s in states) {
    DateTimeOffset time;
    if(!DateTimeOffset.TryParse(s.Updated, out time) || (DateTimeOffset.UtcNow-time).TotalSeconds > 15) { session.Warnings.Add("Revit " + s.Version + " 응답이 오래되었습니다. 대화상자를 닫고 다시 저장하세요."); continue; }
    session.Files.AddRange(s.Files); session.Warnings.AddRange(s.Warnings);
   }
   foreach(var p in Process.GetProcessesByName("Revit")) {
    if(!states.Any(s=>s.Pid == p.Id && Adobe.Fresh(s.Updated))) session.Warnings.Add("Revit 문서 연결 없음 · 확인 가능한 창 제목을 기록하고 프로그램만 실행합니다. [문서 연결 설정] 후 Revit을 다음에 재시작하면 모델을 자동 감지합니다. " + p.MainWindowTitle);
    p.Dispose();
   }
   session.Files = Data.Unique(session.Files.OrderBy(e=>e.Source=="Adobe 문서 연결"?0:1).ThenBy(e=>Data.KindOf(e)=="File"?0:1));
   foreach(var e in session.Files.Where(e=>Data.KindOf(e)=="File" && String.IsNullOrEmpty(e.Executable))) {
    var expected=e.App=="PowerPoint"?"POWERPNT":e.App=="Word"?"WINWORD":e.App=="AutoCAD"?"acad":e.App=="한글"?"Hwp":e.App;
    var app=session.Files.FirstOrDefault(a=>(Data.KindOf(a)=="Application" || Data.KindOf(a)=="PackagedApp") && Path.GetFileNameWithoutExtension(a.Executable).Equals(expected,StringComparison.OrdinalIgnoreCase));
    if(app!=null)e.Executable=app.Executable;
   }
   Layout.Associate(session);DescribeCoverage(session);
   session.Warnings = session.Warnings.Distinct().ToList();
   return session;
  }
  public static void DescribeCoverage(Session session) {
   foreach(var app in session.Files.Where(e=>Data.KindOf(e)=="Application" || Data.KindOf(e)=="PackagedApp")) {
    var documents=session.Files.Count(e=>Data.KindOf(e)=="File" && !String.IsNullOrEmpty(e.Executable) && e.Executable.Equals(app.Executable,StringComparison.OrdinalIgnoreCase));
    app.Note=documents>0?"연결된 문서 "+documents+"개 복원 (개별 문서 행 참조)":"문서 연결 없음 → 프로그램만 실행 · 제목 보관";
    if(app.App.IndexOf("Figma",StringComparison.OrdinalIgnoreCase)>=0)app.Note="문서 링크 연결 없음 · 확인 가능한 제목 보관 / Figma만 실행";
   }
  }
  public static string InstallConnections() {
   var messages=new List<string>();
   try{messages.Add(InstallRevit());}catch(Exception e){messages.Add("Revit 연결: "+e.Message);}
   try{messages.Add(Adobe.Install());}catch(Exception e){messages.Add("Adobe 연결: "+e.Message);}
   return String.Join(Environment.NewLine,messages.ToArray());
  }
  public static string Save(Session s) {
   var selected = Data.Unique(s.Files.Where(e=>e.Selected));
   if(selected.Count == 0) throw new InvalidOperationException("저장할 파일이 없습니다. 기존 기록은 유지됩니다.");
   s.Created = DateTimeOffset.Now.ToString("o");
   var path = Path.Combine(Data.SessionRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0,6) + ".xml");
   var snapshot = new Session { Created=s.Created, Files=selected, Windows=Layout.Included(s), Warnings=new List<string>(s.Warnings), ExcludedKeys=new List<string>(s.ExcludedKeys ?? new List<string>()) };
   Data.Write(path,snapshot); return path;
  }
  public static string[] History() { return Data.SessionFiles(Data.SessionRoot).OrderByDescending(p=>p,StringComparer.Ordinal).ToArray(); }
  public static async Task<Session> LayoutSnapshot(CancellationToken token) {
   var path=Path.Combine(Data.Root,"scan",Guid.NewGuid().ToString("N")+".xml");
   using(var p=Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--collect Layout "+Desktop.Quote(path)){UseShellExecute=false,CreateNoWindow=true})) {
    try {var wait=Stopwatch.StartNew();while(!p.HasExited && wait.ElapsedMilliseconds<6000){token.ThrowIfCancellationRequested();await Task.Delay(100,token);}if(!p.HasExited)return new Session();return p.ExitCode==0?Data.Read<Session>(path):new Session();}
    finally{try{if(!p.HasExited){p.Kill();p.WaitForExit(1000);}if(File.Exists(path))File.Delete(path);}catch{}}
   }
  }
  public static string SaveEdited(Session session,string folder=null) {
   session.Created=DateTimeOffset.Now.ToString("o");session.Schema=2;
   var path=Path.Combine(folder??Data.SessionRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".xml");
   Data.Write(path,session);return path;
  }
  public static async Task<string> Restore(Entry e, CancellationToken token) {
   if(Data.EntryPath(e).Length == 0) return "지원하지 않는 대상 경로";
   token.ThrowIfCancellationRequested();
   var kind=Data.KindOf(e);
   if(kind=="PackagedApp" || kind=="ShellFolder") {
    using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Desktop.Quote(kind=="PackagedApp" ? "shell:AppsFolder\\"+e.Path : e.Path)){UseShellExecute=true})){}
    return kind=="PackagedApp"?"스토어 앱 실행 요청 전달":"탐색기 위치 열기 요청 전달";
   }
   var exists=Task.Run(()=>kind=="Folder"?Directory.Exists(e.Path):File.Exists(e.Path));
   if(await Task.WhenAny(exists,Task.Delay(10000,token))!=exists) {token.ThrowIfCancellationRequested();return "경로 응답 시간 초과 / 공유폴더 연결 확인";}
   if(!await exists) return "파일 없음 / 공유폴더 연결 확인";
   token.ThrowIfCancellationRequested();
   if(kind=="Folder") {using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Desktop.Quote(e.Path)){UseShellExecute=true})){}return "폴더 열기 요청 전달";}
   if(kind=="Application" || kind=="Browser") {
    if(kind=="Browser" ? Desktop.BrowserRunning(e) : Desktop.Running(e.Path))return "이미 실행 중 · 프로그램 재실행 생략";
    using(var p=Process.Start(new ProcessStartInfo(e.Path,kind=="Browser"?Desktop.BrowserArguments(e):""){UseShellExecute=true})){}
    return kind=="Browser" && Desktop.Chromium(e.Path)?"브라우저의 마지막 종료 세션 복원 요청 전달":"프로그램만 실행 · 문서 제목·경로 기록은 보관됨 (파일 복원 아님)";
   }
   if(Data.IsRevit(e.Path)) return await RestoreRevit(e,token);
   if(Adobe.SupportedExecutable(e.Executable))return await Adobe.Restore(e,token);
   var start = new ProcessStartInfo(e.Path) { UseShellExecute = true };
   // AutoCAD's launcher/Office shell associations reuse an existing application when supported.
   using(var p = Process.Start(start)) {}
   return "열기 요청 전달 (문서 열림 여부는 앱에서 확인)";
  }
  static async Task<string> RestoreRevit(Entry e, CancellationToken token) {
   if(String.IsNullOrEmpty(e.Executable) || !Path.GetFileName(e.Executable).Equals("Revit.exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(e.Executable)) return "Revit 버전 지정 필요: 파일을 선택하고 [Revit 버전 지정]";
   Func<BridgeState> find = () => Data.Bridges().FirstOrDefault(s=>s.Executable.Equals(e.Executable,StringComparison.OrdinalIgnoreCase));
   var host = find();
   if(host == null) {
    // Do not fall back to the default RVT association: it can upgrade a model in the wrong version.
    var year = Directory.GetParent(e.Executable).Name.Replace("Revit ","");
    var manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins",year,"OpenMemory.addin");
    if(!File.Exists(manifest)) {
     using(var p=Process.Start(new ProcessStartInfo(e.Executable,Desktop.Quote(e.Path)){UseShellExecute=true})){}
     return "Revit 모델 열기 요청 전달 (연결 설정 없음 · 파일별 새 창이 열릴 수 있음)";
    }
    bool running = Process.GetProcessesByName("Revit").Any(p=> { try { using(p) return p.MainModule.FileName.Equals(e.Executable,StringComparison.OrdinalIgnoreCase); } catch { return false; } });
    if(!running) using(var p = Process.Start(new ProcessStartInfo(e.Executable) { UseShellExecute = true })) {}
    for(int i=0; i<120 && host==null; i++) { await Task.Delay(1000,token); host = find(); }
    if(host == null) return "Revit 연결 대기 초과: 시작 화면·추가 기능 허용 창을 확인하고 재시도";
   }
   token.ThrowIfCancellationRequested();
   var id = "open-" + host.Pid + "-" + Guid.NewGuid().ToString("N");
   var request = Path.Combine(Data.BridgeRoot,id + ".xml");
   var response = Path.Combine(Data.BridgeRoot,id + ".result.xml");
   Data.Write(request,new OpenRequest { Pid = host.Pid, Started = host.Started, Path = e.Path, Expires = DateTimeOffset.UtcNow.AddMinutes(3).ToString("o") });
   try {
    for(int i=0;i<180;i++) {
     if(File.Exists(response)) { var r = Data.Read<OpenResult>(response); return r.Success ? "Revit 열림 확인" : "Revit: " + r.Message; }
     await Task.Delay(1000,token);
    }
    return "열림 확인 시간 초과: Revit 대화상자 확인 (이미 처리 중일 수 있음)";
   } finally {
    // A request already being handled cannot be canceled safely; never kill Revit.
    try { if(File.Exists(request)) File.Delete(request); if(File.Exists(response)) File.Delete(response); } catch {}
   }
  }
  public static string InstallRevit() {
   var package = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"RevitBridge");
   if(!Directory.Exists(package)) throw new IOException("배포 폴더의 RevitBridge 폴더를 찾을 수 없습니다.");
   var installed = new List<string>();
   foreach(var dir in Directory.GetDirectories(package)) {
    var year = Path.GetFileName(dir); var dll = Path.Combine(dir,"OpenMemory.Revit.dll");
    if(!File.Exists(dll)) continue;
    var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","Revit","Addins",year);
    var library = dll;
    Directory.CreateDirectory(dest);
    var xml = new XDocument(new XElement("RevitAddIns",new XElement("AddIn",new XAttribute("Type","Application"),new XElement("Name","OpenMemory"),new XElement("Assembly",library),new XElement("AddInId","E508FC7D-B986-4209-A50B-57694EA14CC8"),new XElement("FullClassName","OpenMemory.RevitBridge"),new XElement("VendorId","OPMM"),new XElement("VendorDescription","OpenMemory local document session helper"))));
    xml.Save(Path.Combine(dest,"OpenMemory.addin")); installed.Add(year);
   }
   if(installed.Count == 0) throw new IOException("빌드된 Revit 추가 기능이 없습니다.");
   return String.Join(", ",installed.ToArray()) + " 연결 설치 완료. 해당 Revit을 다시 실행하고 OpenMemory 추가 기능을 허용하세요.";
  }
 }
}
