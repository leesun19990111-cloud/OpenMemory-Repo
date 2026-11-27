using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace OpenMemory {
 public class Entry {
  public string Kind = "File";
  public string Title = "";
  public string Profile = "";
  public string DataDirectory = "";
  public bool Selected = true;
  public string Path = "";
  public string App = "";
  public string Executable = "";
  public string Source = "";
  public string Note = "";
  public List<string> WindowTitles = new List<string>();
 }
 public class Session {
  public int Schema = 2;
  public string Created = DateTimeOffset.Now.ToString("o");
  public List<Entry> Files = new List<Entry>();
  public List<string> Warnings = new List<string>();
  public List<string> ExcludedKeys = new List<string>();
  public List<WindowLayout> Windows = new List<WindowLayout>();
 }
 public class WindowLayout {
  public string AppKey="", Executable="", Title="", ClassName="", Monitor="";
  public List<string> EntryKeys=new List<string>();
  public long Handle, Started;
  public int Pid, Left, Top, Width, Height, Show=1, Flags, WorkLeft, WorkTop, WorkWidth, WorkHeight, OffsetX, OffsetY;
 }
 public class BridgeState {
  public int Pid;
  public long Started;
  public string Updated = "";
  public string Executable = "";
  public string Version = "";
  public List<Entry> Files = new List<Entry>();
  public List<string> Warnings = new List<string>();
 }
 public class OpenRequest {
  public string Path = "";
  public int Pid;
  public long Started;
  public string Expires = "";
 }
 public class OpenResult {
  public bool Success;
  public string Message = "";
 }
 public static class Data {
  internal static string TestRoot=null;
  public static string Root { get { return TestRoot ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenMemory"); } }
  public static string BridgeRoot { get { return System.IO.Path.Combine(Root, "bridge"); } }
  public static string SessionRoot { get { return System.IO.Path.Combine(Root, "sessions"); } }
  public static string[] SessionFiles(string root) {
   if(!Directory.Exists(root))return new string[0];
   return Directory.GetFiles(root,"*.xml").Where(p=>System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileName(p),@"^\d{8}-\d{6}-\d{3}-[a-fA-F0-9]{6}\.xml$")).ToArray();
  }
  static readonly HashSet<string> Extensions = new HashSet<string>((".dwg .dxf .dwt .rvt .rfa .rte .xlsx .xls .xlsm .xlsb .csv .ods .hwp .hwpx .doc .docx .docm .ppt .pptx .pptm .pps .ppsx .ppsm .pot .potx .potm .pdf .txt .rtf .nwd .nwf .skp .3dm .psd .psb .ai .indd .indt .idml .aep .aepx .prproj .xd .eps .svg .tif .tiff .png .jpg .jpeg .blend .max .fbx .ifc").Split(' '), StringComparer.OrdinalIgnoreCase);
  public static string AbsolutePath(string path) {
   if (String.IsNullOrWhiteSpace(path)) return "";
   path = path.Trim();
   if (path.IndexOf('"') >= 0 || path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0) return "";
   if (!(path.StartsWith(@"\\") && !path.StartsWith(@"\\?\") && !path.StartsWith(@"\\.\")) && !(path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))) return "";
   try { var full = System.IO.Path.GetFullPath(path); if (full.Substring(2).Contains(":")) return ""; return full.Length > 3 ? full.TrimEnd('\\') : full; } catch { return ""; }
  }
  public static string Normalize(string path) { var full=AbsolutePath(path); return full.Length>0 && Extensions.Contains(System.IO.Path.GetExtension(full)) ? full : ""; }
  public static bool IsExecutable(string path) { return AbsolutePath(path).Length>0 && System.IO.Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase); }
  public static string KindOf(Entry e) { return String.IsNullOrEmpty(e.Kind) ? "File" : e.Kind; }
  public static string EntryPath(Entry e) {
   if(e==null) return "";
   switch(KindOf(e)) {
    case "File": return Normalize(e.Path);
    case "Folder": return AbsolutePath(e.Path);
    case "Application": case "Browser": return IsExecutable(e.Path) ? AbsolutePath(e.Path) : "";
    case "PackagedApp": return !String.IsNullOrEmpty(e.Path) && System.Text.RegularExpressions.Regex.IsMatch(e.Path,@"^[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$") ? e.Path : "";
    case "ShellFolder": return System.Text.RegularExpressions.Regex.IsMatch(e.Path ?? "",@"^::\{[A-Fa-f0-9-]{36}\}$") ? e.Path : "";
    default: return "";
   }
  }
  public static string Key(Entry e) { return KindOf(e)+"|"+EntryPath(e)+"|"+(KindOf(e)=="Browser" ? e.Profile+"|"+e.DataDirectory : ""); }
  public static string Label(Entry e) { return !String.IsNullOrWhiteSpace(e.Title) ? e.Title : (System.IO.Path.GetFileName(e.Path).Length>0 ? System.IO.Path.GetFileName(e.Path) : e.Path); }
  public static string KindLabel(Entry e) { switch(KindOf(e)) {case "File":return "문서";case "Folder":case "ShellFolder":return "폴더";case "Browser":return "브라우저";default:return "프로그램";} }
  public static void Exclude(Session session,IEnumerable<Entry> entries) {
   var keys=new HashSet<string>(entries.Select(Key),StringComparer.OrdinalIgnoreCase);
   session.ExcludedKeys=new List<string>();
   session.Files.RemoveAll(e=>keys.Contains(Key(e)));
   var remaining=new HashSet<string>(session.Files.Select(Key),StringComparer.OrdinalIgnoreCase);
   if(session.Windows!=null)session.Windows.RemoveAll(w=>w==null || w.EntryKeys==null || !w.EntryKeys.Any(remaining.Contains));
  }
  public static bool IsRevit(string path) { var ext = System.IO.Path.GetExtension(path); return new[]{".rvt", ".rfa", ".rte"}.Contains(ext, StringComparer.OrdinalIgnoreCase); }
  public static string AppFor(string path) {
   var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
   if (IsRevit(path)) return "Revit";
   if (new[]{".dwg",".dxf",".dwt"}.Contains(ext)) return "AutoCAD";
   if (new[]{".xlsx",".xls",".xlsm",".xlsb",".csv",".ods"}.Contains(ext)) return "Excel";
   if (new[]{".hwp",".hwpx"}.Contains(ext)) return "한글";
   if (new[]{".psd",".psb"}.Contains(ext)) return "Photoshop";
   if (new[]{".ai",".eps"}.Contains(ext)) return "Illustrator";
   if (new[]{".indd",".indt",".idml"}.Contains(ext)) return "InDesign";
   if (new[]{".ppt",".pptx",".pptm",".pps",".ppsx",".ppsm",".pot",".potx",".potm"}.Contains(ext)) return "PowerPoint";
   if (new[]{".doc",".docx",".docm",".rtf"}.Contains(ext)) return "Word";
   if (new[]{".aep",".aepx"}.Contains(ext)) return "After Effects";
   if (ext==".prproj") return "Premiere Pro";
   return "기본 앱";
  }
  public static List<Entry> Unique(IEnumerable<Entry> entries) {
   var result = new List<Entry>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach (var e in entries) { var p = EntryPath(e); if (p.Length == 0 || !seen.Add(Key(e))) continue; e.Path = p; result.Add(e); }
   return result;
  }
  public static T Read<T>(string path) {
   using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var r = XmlReader.Create(f, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 })) return (T)new XmlSerializer(typeof(T)).Deserialize(r);
  }
  public static void Write<T>(string path, T value) {
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
   var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
   try {
    using(var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { using(var writer=XmlWriter.Create(f,new XmlWriterSettings {Encoding=new System.Text.UTF8Encoding(false),Indent=true,CloseOutput=false})) new XmlSerializer(typeof(T)).Serialize(writer, value); f.Flush(true); }
    if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
   } finally { if (File.Exists(tmp)) File.Delete(tmp); }
  }
  public static bool IsLive(BridgeState s) {
   try { using(var p = Process.GetProcessById(s.Pid)) return p.ProcessName.Equals("Revit", StringComparison.OrdinalIgnoreCase) && p.StartTime.ToUniversalTime().Ticks == s.Started; } catch { return false; }
  }
  public static List<BridgeState> Bridges() {
   var states = new List<BridgeState>();
   if (!Directory.Exists(BridgeRoot)) return states;
   foreach (var path in Directory.GetFiles(BridgeRoot, "state-*.xml")) { try { var s = Read<BridgeState>(path); if (IsLive(s)) states.Add(s); } catch {} }
   return states;
  }
 }
}
