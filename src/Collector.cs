using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace OpenMemory {
 public static class Collector {
  [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int r, out IRunningObjectTable table);
  [DllImport("ole32.dll")] static extern int CreateBindCtx(int r, out IBindCtx ctx);
  delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc callback, IntPtr param);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder b, int max);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
  static object Get(object o, string prop) { return o.GetType().InvokeMember(prop, BindingFlags.GetProperty, null, o, null); }
  static object Item(object o, int i) { return o.GetType().InvokeMember("Item", BindingFlags.GetProperty | BindingFlags.InvokeMethod, null, o, new object[]{i}); }
  static string Str(object o, string prop) { try { return Convert.ToString(Get(o, prop)); } catch { return ""; } }
  static void Release(object o) { if(o != null && Marshal.IsComObject(o)) { try { Marshal.ReleaseComObject(o); } catch {} } }
  static string ExeForWindow(long hwnd) { try { uint pid; GetWindowThreadProcessId(new IntPtr(hwnd), out pid); using(var p = Process.GetProcessById((int)pid)) return p.MainModule.FileName; } catch { return ""; } }
  static readonly HashSet<string> AppsSeen = new HashSet<string>();
  static Session result;
  static void ReadApp(object app, string kind) {
   if(app==null)return;
   object docs = null;
   try {
    var exe = kind == "AutoCAD" ? Str(app,"FullName") : "";
    var hwnd = Str(app, kind == "AutoCAD" ? "HWND" : "Hwnd");
    long h; if (exe.Length == 0 && Int64.TryParse(hwnd, out h)) exe = ExeForWindow(h);
    var key = kind + ":" + hwnd;
    if(hwnd.Length > 0 && !AppsSeen.Add(key)) return;
    docs = Get(app, kind == "Excel" ? "Workbooks" : kind == "한글" ? "XHwpDocuments" : kind == "PowerPoint" ? "Presentations" : "Documents");
    int count = Convert.ToInt32(Get(docs,"Count"));
    for (int n = 0; n < count; n++) {
     object doc = null;
     try {
      doc = Item(docs, kind == "AutoCAD" || kind == "한글" ? n : n + 1);
      var path = Str(doc, "FullName"); var name = Str(doc,"Name");
      if ((kind == "Excel" || kind == "Word" || kind=="PowerPoint") && String.IsNullOrEmpty(Str(doc,"Path"))) path = "";
      if (Data.Normalize(path).Length == 0) { result.Warnings.Add(kind + " / " + name + ": 저장되지 않았거나 로컬·공유폴더 경로를 읽을 수 없습니다."); continue; }
      var saved = Str(doc, "Saved"); var modified = kind == "한글" ? Str(doc,"Modified") : "";
      var dirty = saved.Equals("False",StringComparison.OrdinalIgnoreCase) || (kind=="PowerPoint" && saved=="0") || modified.Equals("True",StringComparison.OrdinalIgnoreCase);
      result.Files.Add(new Entry { Path = path, App = kind, Executable = exe, Source = "자동 감지", Note = dirty ? "수정 내용은 원본 앱에서 저장 필요" : "" });
     } catch(Exception e) { result.Warnings.Add(kind + " 문서 일부 읽기 실패: " + e.GetBaseException().Message); }
     finally { Release(doc); }
    }
   } catch(Exception e) { result.Warnings.Add(kind + " 연결 확인 필요: " + e.GetBaseException().Message); }
   finally { Release(docs); }
  }
  static void OfficeWindows(string kind, string processName, string className) {
   var ids = new HashSet<int>(Process.GetProcessesByName(processName).Select(p => p.Id));
   EnumWindows(delegate(IntPtr top, IntPtr unused) {
    uint pid; GetWindowThreadProcessId(top, out pid); if(!ids.Contains((int)pid)) return true;
    EnumChildWindows(top, delegate(IntPtr child, IntPtr ignored) {
     var b = new StringBuilder(256); GetClassName(child, b, b.Capacity); if(b.ToString() != className) return true;
     object window = null, app = null;
     try { var iid = new Guid("00020400-0000-0000-C000-000000000046"); if(AccessibleObjectFromWindow(child, 0xFFFFFFF0, ref iid, out window) == 0 && window != null) { app = Get(window,"Application"); ReadApp(app, kind); } }
     catch {} finally { Release(app); Release(window); }
     return true;
    }, IntPtr.Zero);
    return true;
   }, IntPtr.Zero);
  }
  static void Rot(string kind) {
   IRunningObjectTable rot = null; IBindCtx ctx = null; IEnumMoniker iter = null;
   try {
    GetRunningObjectTable(0, out rot); CreateBindCtx(0, out ctx); rot.EnumRunning(out iter);
    var monikers = new IMoniker[1];
    while(iter.Next(1, monikers, IntPtr.Zero) == 0) {
     object obj = null, app = null;
     try {
      string name; monikers[0].GetDisplayName(ctx, null, out name);
      bool match = kind == "한글" ? name.IndexOf("HwpObject", StringComparison.OrdinalIgnoreCase) >= 0 : kind == "AutoCAD" ? name.IndexOf("AutoCAD",StringComparison.OrdinalIgnoreCase) >= 0 || name.EndsWith(".dwg",StringComparison.OrdinalIgnoreCase) : false;
      if(!match) continue;
      rot.GetObject(monikers[0], out obj);
      if(kind == "한글") ReadApp(obj,kind);
      else { try { app = Get(obj,"Application"); } catch { app = obj; } ReadApp(app,kind); }
     } catch {} finally { if(!Object.ReferenceEquals(app,obj)) Release(app); Release(obj); Release(monikers[0]); }
    }
   } finally { Release(iter); Release(ctx); Release(rot); }
  }
  public static Session Run(string kind) {
   if(kind=="Layout"){var s=Desktop.Windows();var folders=Desktop.Folders();s.Windows.AddRange(folders.Windows);return s;}
   if(kind=="Desktop") return Desktop.Windows();
   if(kind=="Folders") return Desktop.Folders();
   if(kind=="Adobe") return Adobe.Read();
   result = new Session(); AppsSeen.Clear();
   try {
    if(kind == "Excel") OfficeWindows(kind,"EXCEL","EXCEL7");
    else if(kind == "Word") OfficeWindows(kind,"WINWORD","_WwG");
    else if(kind == "PowerPoint") {
     object app=null;try{app=Marshal.GetActiveObject("PowerPoint.Application");ReadApp(app,kind);}catch{}finally{Release(app);}
    }
    else if(kind == "AutoCAD") {
     Rot(kind);
     var names = new List<string>{"AutoCAD.Application"};
     using(var key = Registry.ClassesRoot) names.AddRange(key.GetSubKeyNames().Where(n => n.StartsWith("AutoCAD.Application.",StringComparison.OrdinalIgnoreCase)));
     foreach(var name in names) { object app = null; try { app = Marshal.GetActiveObject(name); ReadApp(app,kind); } catch {} finally { Release(app); } }
    } else if(kind == "한글") Rot(kind);
    else if(new[]{"Photoshop","Illustrator","InDesign"}.Contains(kind)) {
     var names=new List<string>{kind+".Application"};
     using(var key=Registry.ClassesRoot) names.AddRange(key.GetSubKeyNames().Where(n=>n.StartsWith(kind+".Application.",StringComparison.OrdinalIgnoreCase)));
     foreach(var name in names.Distinct()) {object app=null;try {app=Marshal.GetActiveObject(name);ReadApp(app,kind);}catch{}finally{Release(app);}}
    }
   } catch(Exception e) { result.Warnings.Add(kind + ": " + e.GetBaseException().Message); }
   result.Files = Data.Unique(result.Files);
   var proc = kind == "Excel" ? "EXCEL" : kind == "AutoCAD" ? "acad" : kind == "Word" ? "WINWORD" : kind=="PowerPoint" ? "POWERPNT" : kind=="한글" ? "Hwp" : kind;
   foreach(var p in Process.GetProcessesByName(proc)) {
    if(result.Files.Count == 0) result.Warnings.Add(kind + " 실행 중이나 저장 가능한 파일을 찾지 못했습니다. 권한·대화상자를 확인하거나 파일을 직접 추가하세요. " + p.MainWindowTitle);
    p.Dispose();
   }
   if(kind == "AutoCAD" && Process.GetProcessesByName("acad").Length>1) result.Warnings.Add("AutoCAD를 여러 프로세스로 실행한 경우 일부 인스턴스가 감지되지 않을 수 있습니다. 목록을 확인하세요.");
   return result;
  }
 }
}
