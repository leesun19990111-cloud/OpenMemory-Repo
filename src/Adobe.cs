using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMemory {
 public class AdobeState {
  public int CepPid;
  public string Instance="", Host="", Version="", Updated="";
  public List<Entry> Files=new List<Entry>();
  public List<string> Warnings=new List<string>();
 }
 public static class Adobe {
  public static string ProcessName(string host) {
   switch(host){case "PHXS":case "PHSP":return "Photoshop";case "ILST":return "Illustrator";case "PPRO":return "Adobe Premiere Pro";case "AEFT":return "AfterFX";case "IDSN":return "InDesign";default:return "";}
  }
  public static bool SupportedExecutable(string exe){return new[]{"Photoshop.exe","Illustrator.exe","Adobe Premiere Pro.exe","AfterFX.exe","InDesign.exe"}.Contains(Path.GetFileName(exe),StringComparer.OrdinalIgnoreCase);}
  public static bool Fresh(string updated){DateTimeOffset t;return DateTimeOffset.TryParse(updated,out t) && (DateTimeOffset.UtcNow-t).TotalSeconds>=-5 && (DateTimeOffset.UtcNow-t).TotalSeconds<15;}
  static string HostExecutable(AdobeState state) {
   var expected=ProcessName(state.Host);if(expected.Length==0)return "";
   int pid=state.CepPid;
   try {
    using(var cep=Process.GetProcessById(pid))if(!cep.ProcessName.Equals("CEPHtmlEngine",StringComparison.OrdinalIgnoreCase))return "";
    for(int depth=0;depth<6 && pid>0;depth++) {
     using(var current=Process.GetProcessById(pid)) {
      if(current.ProcessName.Equals(expected,StringComparison.OrdinalIgnoreCase))return Desktop.ProcessPath(pid);
      using(var row=new ManagementObject("Win32_Process.Handle='"+pid+"'")){row.Get();int parent=Convert.ToInt32(row["ParentProcessId"]);if(parent==pid)return "";pid=parent;}
     }
    }
   }catch{}
   return "";
  }
  public static Session Read() {
   var result=new Session();var root=Path.Combine(Data.Root,"adobe");if(!Directory.Exists(root))return result;
   foreach(var file in Directory.GetFiles(root,"state-*.xml"))try {
    var state=Data.Read<AdobeState>(file);
    if(!Fresh(state.Updated) || state.Files==null || state.Warnings==null)continue;
    var exe=HostExecutable(state);if(exe.Length==0)continue;
    foreach(var entry in state.Files) {
     if(entry==null || Data.Normalize(entry.Path).Length==0)continue;
     entry.Kind="File";entry.Executable=exe;entry.Source="Adobe 문서 연결";entry.Selected=true;result.Files.Add(entry);
    }
    result.Warnings.AddRange(state.Warnings.Select(w=>state.Host+": "+w));
   }catch{}
   result.Files=Data.Unique(result.Files);return result;
  }
  public static string Arguments(Entry entry){return Path.GetFileName(entry.Executable).Equals("AfterFX.exe",StringComparison.OrdinalIgnoreCase)?"-project "+Desktop.Quote(entry.Path):Desktop.Quote(entry.Path);}
  public static async Task<string> Restore(Entry entry,CancellationToken token) {
   if(!SupportedExecutable(entry.Executable) || !File.Exists(entry.Executable))return "Adobe 실행 파일 연결 없음 · 파일 경로는 기록에 보관됨";
   token.ThrowIfCancellationRequested();
   using(var p=Process.Start(new ProcessStartInfo(entry.Executable,Arguments(entry)){UseShellExecute=true})){}
   // A launch request is not proof that a project opened. Confirm from the live host.
   for(int i=0;i<15;i++) {
    await Task.Delay(1000,token);
    var found=await Task.Run(()=>Read().Files.Any(e=>Data.Key(e).Equals(Data.Key(entry),StringComparison.OrdinalIgnoreCase) && e.Executable.Equals(entry.Executable,StringComparison.OrdinalIgnoreCase)));
    if(found)return "Adobe 문서 열림 확인";
   }
   return "파일 열기 요청 전달 · 문서 열림 미확인 (시작·로그인·앱 대화상자 확인)";
  }
  public static string Install() {
   var source=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AdobeBridge");
   if(!Directory.Exists(source) || !File.Exists(Path.Combine(source,"META-INF","signatures.xml")))return "Adobe 연결 패키지 없음 · 프로그램만 실행";
   var target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Adobe","CEP","extensions","local.openmemory.documents");
   foreach(var relative in new[]{"mimetype","index.html","bridge.js","reader.jsx","CSXS/manifest.xml","META-INF/signatures.xml"}) {
    var file=Path.Combine(source,relative);var dest=Path.Combine(target,relative);
    if(File.Exists(dest) && File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(dest)))continue;
    Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);
   }
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"installed-adobe.flag"),"OpenMemory");
   return "Adobe 문서 연결 설치됨 · 이미 실행 중인 Adobe 앱은 저장 후 다음 재시작부터 연결";
  }
 }
}
