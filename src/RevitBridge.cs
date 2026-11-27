using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OpenMemory {
 public class RevitBridge : IExternalApplication {
  Timer timer; ExternalEvent signal; Handler handler;
  public Result OnStartup(UIControlledApplication app) {
   handler = new Handler(); signal = ExternalEvent.Create(handler);
   timer = new Timer(_=> { try { signal.Raise(); } catch {} },null,1000,3000);
   return Result.Succeeded;
  }
  public Result OnShutdown(UIControlledApplication app) {
   if(timer != null) timer.Dispose(); if(signal != null) signal.Dispose();
   try { File.Delete(handler.StatePath); } catch {} return Result.Succeeded;
  }
 }
 public class Handler : IExternalEventHandler {
  readonly int pid = Process.GetCurrentProcess().Id;
  readonly long started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
  public string StatePath { get { return Path.Combine(Data.BridgeRoot,"state-" + pid + ".xml"); } }
  public string GetName() { return "OpenMemory document session"; }
  public void Execute(UIApplication app) {
   try {
    Directory.CreateDirectory(Data.BridgeRoot);
    foreach(var path in Directory.GetFiles(Data.BridgeRoot,"open-" + pid + "-*.xml").Where(p=>!p.EndsWith(".result.xml"))) {
     var response = path.Substring(0,path.Length-4) + ".result.xml";
     var result = new OpenResult();
     try {
      var request = Data.Read<OpenRequest>(path); DateTimeOffset expiry;
      if(request.Pid != pid || request.Started != started || !DateTimeOffset.TryParse(request.Expires,out expiry) || expiry < DateTimeOffset.UtcNow) throw new InvalidOperationException("만료된 요청");
      if(Data.Normalize(request.Path).Length == 0 || !Data.IsRevit(request.Path) || !File.Exists(request.Path)) throw new InvalidOperationException("지원하지 않거나 찾을 수 없는 모델 경로");
      File.Delete(path); // Claim once; no replay if a modal dialog postpones processing.
      var already = app.Application.Documents.Cast<Document>().FirstOrDefault(d=>!d.IsLinked && d.PathName.Equals(request.Path,StringComparison.OrdinalIgnoreCase));
      if(already == null) app.OpenAndActivateDocument(request.Path);
      result.Success = true;
     } catch(Exception ex) { result.Message = ex.Message; }
     Data.Write(response,result); if(File.Exists(path)) File.Delete(path);
    }
    var s = new BridgeState { Pid=pid, Started=started, Executable=Process.GetCurrentProcess().MainModule.FileName, Version=app.Application.VersionNumber, Updated=DateTimeOffset.UtcNow.ToString("o") };
    foreach(Document doc in app.Application.Documents) {
     if(doc.IsLinked) continue;
     if(doc.IsModelInCloud) { s.Warnings.Add("Revit 클라우드 모델은 자동 복원 제외: " + doc.Title); continue; }
     if(Data.Normalize(doc.PathName).Length == 0) { s.Warnings.Add("Revit 저장되지 않았거나 지원하지 않는 경로: " + doc.Title); continue; }
     s.Files.Add(new Entry { Path=doc.PathName, App="Revit " + s.Version, Executable=s.Executable, Source="Revit 연결", Note=doc.IsModified ? "수정 내용은 Revit에서 저장·동기화 필요" : "" });
    }
    Data.Write(StatePath,s);
   } catch { /* Do not interrupt the user's modeling session for helper I/O errors. */ }
  }
 }
}
