using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenMemory {
 public static class Desktop {
  delegate bool EnumProc(IntPtr h,IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,EnumProc callback,IntPtr p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int max);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int max);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h,int flags,StringBuilder path,ref int size);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr h,ref int length,StringBuilder id);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string cmd,out int count);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
  public static string ProcessPath(int pid) {
   var h=OpenProcess(0x1000,false,(uint)pid); if(h==IntPtr.Zero) return "";
   try { var b=new StringBuilder(32768);int size=b.Capacity;return QueryFullProcessImageName(h,0,b,ref size)?b.ToString():""; } finally {CloseHandle(h);}
  }
  static string PackageId(int pid) {
   var h=OpenProcess(0x1000,false,(uint)pid);if(h==IntPtr.Zero)return "";
   try {int length=512;var b=new StringBuilder(length);return GetApplicationUserModelId(h,ref length,b)==0?b.ToString():"";}finally{CloseHandle(h);}
  }
  public static bool Chromium(string exe) {return new[]{"chrome.exe","msedge.exe","brave.exe","vivaldi.exe","whale.exe"}.Contains(Path.GetFileName(exe),StringComparer.OrdinalIgnoreCase);}
  public static bool Browser(string exe) {return Chromium(exe) || new[]{"firefox.exe","opera.exe"}.Contains(Path.GetFileName(exe),StringComparer.OrdinalIgnoreCase);}
  public static string Quote(string s) {
   if(s==null) return "\"\""; var b=new StringBuilder("\"");int slashes=0;
   foreach(char c in s) { if(c=='\\') {slashes++;continue;} if(c=='"') {b.Append('\\',slashes*2+1);b.Append(c);}else {b.Append('\\',slashes);b.Append(c);}slashes=0; }
   b.Append('\\',slashes*2);b.Append('"');return b.ToString();
  }
  public static string[] SplitArguments(string command) {
   int count;var block=CommandLineToArgvW(command,out count);if(block==IntPtr.Zero)return new string[0];
   try {var args=new string[count];for(int i=0;i<count;i++)args[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(block,i*IntPtr.Size));return args;}finally{LocalFree(block);}
  }
  public static string BrowserArguments(Entry e) {
   if(!Chromium(e.Path))return "";
   var result="--restore-last-session --no-first-run --no-default-browser-check";
   if(!String.IsNullOrEmpty(e.Profile)) {
    if(e.Profile=="." || e.Profile==".." || e.Profile.IndexOfAny(new[]{'\\','/','"','\r','\n',':'})>=0) throw new InvalidDataException("지원하지 않는 브라우저 프로필");
    result+=" --profile-directory="+Quote(e.Profile);
   }
   if(!String.IsNullOrEmpty(e.DataDirectory)) {if(Data.AbsolutePath(e.DataDirectory).Length==0)throw new InvalidDataException("브라우저 데이터 폴더 경로 확인 필요"); result+=" --user-data-dir="+Quote(e.DataDirectory);}
   return result;
  }
  static void ReadBrowserProfile(Entry entry,int pid) {
   try {
    using(var q=new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId="+pid)) using(var rows=q.Get()) foreach(ManagementObject row in rows) using(row) {
     var args=SplitArguments(Convert.ToString(row["CommandLine"]));
     for(int i=1;i<args.Length;i++) {
      if(args[i].StartsWith("--profile-directory=",StringComparison.Ordinal)) entry.Profile=args[i].Substring(20);
      if(args[i].StartsWith("--user-data-dir=",StringComparison.Ordinal)) entry.DataDirectory=args[i].Substring(16);
     }
    }
   } catch { /* Command lines are never replayed; only two allowlisted profile switches are retained. */ }
  }
  public static Session Windows() {
   var result=new Session();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   EnumWindows(delegate(IntPtr window,IntPtr ignored) {
    try {
     if(!IsWindowVisible(window))return true;
     var style=GetWindowLong(window,-20);if((style&0x80)!=0 || (GetWindow(window,4)!=IntPtr.Zero && (style&0x40000)==0))return true;
     int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0 && cloaked!=0)return true;
     var title=new StringBuilder(2048);GetWindowText(window,title,title.Capacity);if(title.Length==0)return true;
     var cls=new StringBuilder(256);GetClassName(window,cls,cls.Capacity);
     if(new[]{"Shell_TrayWnd","Progman","WorkerW","Shell_SecondaryTrayWnd"}.Contains(cls.ToString()))return true;
     uint pid;GetWindowThreadProcessId(window,out pid);if(pid==Process.GetCurrentProcess().Id)return true;
     var exe=ProcessPath((int)pid);var file=Path.GetFileName(exe);
     if(file.Equals("OpenMemory.exe",StringComparison.OrdinalIgnoreCase))return true;
     if(file.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase) && (cls.ToString()=="CabinetWClass" || cls.ToString()=="ExploreWClass"))return true;
     var aumid=PackageId((int)pid);
     if(file.Equals("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)) {
      EnumChildWindows(window,delegate(IntPtr child,IntPtr unused) {uint other;GetWindowThreadProcessId(child,out other);if(other!=pid){var id=PackageId((int)other);if(id.Length>0){aumid=id;exe=ProcessPath((int)other);return false;}}return true;},IntPtr.Zero);
     }
     var app=Path.GetFileNameWithoutExtension(exe);
     try {if(File.Exists(exe)){var desc=FileVersionInfo.GetVersionInfo(exe).FileDescription;if(!String.IsNullOrWhiteSpace(desc))app=desc;}}catch{}
     Entry entry;
     if(aumid.Length>0) entry=new Entry {Kind="PackagedApp",Path=aumid,Executable=exe,App=app,Title=title.ToString(),Source="작업표시줄",Note="프로그램 재실행 · 내부 문서/탭은 앱에 따라 다름"};
     else if(Data.IsExecutable(exe)) {
      if(new[]{"conhost.exe","dllhost.exe","rundll32.exe","ApplicationFrameHost.exe"}.Contains(Path.GetFileName(exe),StringComparer.OrdinalIgnoreCase)){result.Warnings.Add("실행 대상을 확인할 수 없는 호스트 창: "+title);return true;}
      entry=new Entry {Kind=Browser(exe)?"Browser":"Application",Path=exe,Executable=exe,App=app,Title=title.ToString(),Source="작업표시줄",Note="프로그램만 재실행 · 열린 문서/편집 상태는 별도"};
      if(Browser(exe)) {ReadBrowserProfile(entry,(int)pid);entry.Note=Chromium(exe)?"마지막 종료 세션 복원 요청 · 저장 시점의 탭 목록은 아님":"프로그램 재실행 · 탭은 브라우저의 이전 세션 설정에 따름";if(String.IsNullOrEmpty(entry.Profile))entry.Note+=" · 프로필 미지정";}
     } else {result.Warnings.Add("실행 경로 접근 불가: "+title+" (앱 권한 확인)");return true;}
     entry.WindowTitles.Add(title.ToString());
     if(Data.EntryPath(entry).Length>0) {
      var placement=Layout.Capture(window,entry);if(placement!=null)result.Windows.Add(placement);
      if(seen.Add(Data.Key(entry)))result.Files.Add(entry);
      else {var existing=result.Files.First(a=>Data.Key(a).Equals(Data.Key(entry),StringComparison.OrdinalIgnoreCase));if(!existing.WindowTitles.Contains(title.ToString()))existing.WindowTitles.Add(title.ToString());}
     }
    } catch(Exception e) {result.Warnings.Add("일부 창 감지 실패: "+e.Message);}
    return true;
   },IntPtr.Zero);
   return result;
  }
  static object Get(object obj,string name) {return obj.GetType().InvokeMember(name,BindingFlags.GetProperty,null,obj,null);}
  static object Call(object obj,string name,params object[] args) {return obj.GetType().InvokeMember(name,BindingFlags.InvokeMethod|BindingFlags.GetProperty,null,obj,args);}
  static void Release(object obj) {if(obj!=null && Marshal.IsComObject(obj))try{Marshal.ReleaseComObject(obj);}catch{}}
  public static Session Folders() {
   var result=new Session();object shell=null,windows=null;
   try {
    shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=Call(shell,"Windows");
    int count=Convert.ToInt32(Get(windows,"Count"));
    for(int i=0;i<count;i++) {
     object win=null,doc=null,folder=null,self=null;
     try {
      win=Call(windows,"Item",i);var exe=Convert.ToString(Get(win,"FullName"));if(!Path.GetFileName(exe).Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))continue;
      doc=Get(win,"Document");folder=Get(doc,"Folder");self=Get(folder,"Self");
      var path=Convert.ToString(Get(self,"Path"));var entry=new Entry {Kind=path.StartsWith("::{",StringComparison.Ordinal)?"ShellFolder":"Folder",Path=path,App="파일 탐색기",Title=Convert.ToString(Get(win,"LocationName")),Source="탐색기",Note="폴더 다시 열기 · 창/탭 배치는 별도"};
      if(Data.EntryPath(entry).Length>0){result.Files.Add(entry);entry.Note="폴더 다시 열기 · 창 위치·크기 포함 (탭 구성 제외)";var placement=Layout.Capture(new IntPtr(Convert.ToInt64(Get(win,"HWND"))),entry);if(placement!=null && !result.Windows.Any(w=>w.Handle==placement.Handle))result.Windows.Add(placement);}else result.Warnings.Add("탐색기 경로 자동 복원 미지원: "+entry.Title);
     }catch(Exception e){result.Warnings.Add("탐색기 창 일부 읽기 실패: "+e.GetBaseException().Message);}finally{Release(self);Release(folder);Release(doc);Release(win);}
    }
   }catch(Exception e){result.Warnings.Add("탐색기 감지: "+e.GetBaseException().Message);}finally{Release(windows);Release(shell);}
   result.Files=Data.Unique(result.Files);return result;
  }
  public static bool Running(string exe) {
   if(!Data.IsExecutable(exe))return false;
   foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))using(p) if(ProcessPath(p.Id).Equals(exe,StringComparison.OrdinalIgnoreCase))return true;
   return false;
  }
  public static bool BrowserRunning(Entry e) {
   if(String.IsNullOrEmpty(e.Profile) && String.IsNullOrEmpty(e.DataDirectory))return Running(e.Path);
   foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(e.Path)))using(p) {
    if(!ProcessPath(p.Id).Equals(e.Path,StringComparison.OrdinalIgnoreCase))continue;
    var running=new Entry {Path=e.Path};ReadBrowserProfile(running,p.Id);
    if(String.Equals(running.Profile,e.Profile,StringComparison.OrdinalIgnoreCase) && String.Equals(running.DataDirectory,e.DataDirectory,StringComparison.OrdinalIgnoreCase))return true;
   }
   return false;
  }
 }
}
