using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenMemory {
 public static class Layout {
  [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct Placement { public int Length,Flags,Show;public Point Min,Max;public Rect Normal; }
  [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h,ref Placement p);
  [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h,ref Placement p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
  static Placement NewPlacement(){return new Placement {Length=Marshal.SizeOf(typeof(Placement))};}
  public static WindowLayout Capture(IntPtr h,Entry e) {
   var p=NewPlacement();if(!GetWindowPlacement(h,ref p))return null;
   uint pid;GetWindowThreadProcessId(h,out pid);long started;
   try{using(var process=Process.GetProcessById((int)pid))started=process.StartTime.ToUniversalTime().Ticks;}catch{return null;}
   var screen=Screen.FromHandle(h);var work=screen.WorkingArea;var cls=new StringBuilder(256);GetClassName(h,cls,cls.Capacity);
   var w=new WindowLayout {AppKey=Data.Key(e),EntryKeys=new List<string>{Data.Key(e)},Executable=e.Executable,Title=e.Title,ClassName=cls.ToString(),Handle=h.ToInt64(),Pid=(int)pid,Started=started,Left=p.Normal.Left,Top=p.Normal.Top,Width=p.Normal.Right-p.Normal.Left,Height=p.Normal.Bottom-p.Normal.Top,Show=p.Show,Flags=p.Flags&2,Monitor=screen.DeviceName,WorkLeft=work.Left,WorkTop=work.Top,WorkWidth=work.Width,WorkHeight=work.Height,OffsetX=work.Left-screen.Bounds.Left,OffsetY=work.Top-screen.Bounds.Top};
   return Valid(w)?w:null;
  }
  public static bool Valid(WindowLayout w){return w!=null && w.Width>0 && w.Height>0 && w.Width<=100000 && w.Height<=100000 && Math.Abs((long)w.Left)<=1000000 && Math.Abs((long)w.Top)<=1000000 && (w.Show==1 || w.Show==2 || w.Show==3) && !String.IsNullOrEmpty(w.AppKey);}
  public static string TitleKey(string text){return (text??"").Replace("*","").Replace("●","").Trim().ToUpperInvariant();}
  static bool DocumentTitle(string title,string path) {
   var stem=System.IO.Path.GetFileNameWithoutExtension(path);if(String.IsNullOrEmpty(stem))return false;
   return System.Text.RegularExpressions.Regex.IsMatch(title??"",@"(?<![\p{L}\p{N}_])"+System.Text.RegularExpressions.Regex.Escape(stem)+@"(?![\p{L}\p{N}_])",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
  }
  public static void Associate(Session s) {
   foreach(var w in s.Windows) {
    var docs=s.Files.Where(e=>Data.KindOf(e)=="File" && !String.IsNullOrEmpty(e.Executable) && e.Executable.Equals(w.Executable,StringComparison.OrdinalIgnoreCase) && DocumentTitle(w.Title,e.Path)).ToList();
    if(docs.Count==1)w.EntryKeys=new List<string>{Data.Key(docs[0])};
   }
  }
  public static List<WindowLayout> Included(Session s) {
   var keys=new HashSet<string>(s.Files.Where(e=>e.Selected).Select(Data.Key),StringComparer.OrdinalIgnoreCase);
   return (s.Windows??new List<WindowLayout>()).Where(w=>Valid(w) && w.EntryKeys!=null && w.EntryKeys.Any(keys.Contains)).ToList();
  }
  public static WindowLayout Match(WindowLayout saved,IList<WindowLayout> current,IList<WindowLayout> allSaved,HashSet<long> used) {
   var candidates=current.Where(w=>Valid(w) && !used.Contains(w.Handle) && String.Equals(w.AppKey,saved.AppKey,StringComparison.OrdinalIgnoreCase) && w.ClassName==saved.ClassName).ToList();
   var same=candidates.Where(w=>w.Handle!=0 && w.Handle==saved.Handle && w.Pid==saved.Pid && w.Started==saved.Started && TitleKey(w.Title)==TitleKey(saved.Title)).ToList();if(same.Count==1)return same[0];
   var exact=candidates.Where(w=>TitleKey(w.Title)==TitleKey(saved.Title)).ToList();if(exact.Count==1 && allSaved.Count(w=>w.AppKey==saved.AppKey && TitleKey(w.Title)==TitleKey(saved.Title))==1)return exact[0];
   var documents=saved.EntryKeys.Where(k=>k.StartsWith("File|",StringComparison.Ordinal)).Select(k=>k.Split('|')[1]).ToList();
   if(documents.Count==1) {
    var byDocument=candidates.Where(w=>DocumentTitle(w.Title,documents[0])).ToList();
    if(byDocument.Count==1 && allSaved.Count(w=>w.AppKey==saved.AppKey && DocumentTitle(w.Title,documents[0]))==1)return byDocument[0];
   }
   var folder=saved.AppKey.StartsWith("Folder|",StringComparison.Ordinal) || saved.AppKey.StartsWith("ShellFolder|",StringComparison.Ordinal);
   // A single shared app window can change title when the active tab changes. Never guess among document windows.
   bool appOnly=saved.EntryKeys.Any(k=>k==saved.AppKey);
   if((folder || appOnly) && candidates.Count==1 && allSaved.Count(w=>String.Equals(w.AppKey,saved.AppKey,StringComparison.OrdinalIgnoreCase))==1)return candidates[0];
   return null;
  }
  public static Rectangle Fit(Rectangle desired,Rectangle area) {
   int width=Math.Min(desired.Width,area.Width),height=Math.Min(desired.Height,area.Height);
   return new Rectangle(Math.Max(area.Left,Math.Min(desired.Left,area.Right-width)),Math.Max(area.Top,Math.Min(desired.Top,area.Bottom-height)),width,height);
  }
  static Placement Target(WindowLayout saved) {
   var rect=new Rectangle(saved.Left+saved.OffsetX,saved.Top+saved.OffsetY,saved.Width,saved.Height);
   var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==saved.Monitor);
   if(screen==null)screen=Screen.FromRectangle(rect);
   var area=screen.WorkingArea;
   // Preserve exact coordinates when monitors/taskbar are unchanged, including deliberately spanning windows.
   bool unchanged=screen.DeviceName==saved.Monitor && area==new Rectangle(saved.WorkLeft,saved.WorkTop,saved.WorkWidth,saved.WorkHeight);
   if(!unchanged)rect=Fit(rect,area);
   int dx=area.Left-screen.Bounds.Left,dy=area.Top-screen.Bounds.Top;
   var p=NewPlacement();p.Flags=4|(saved.Flags&2);p.Show=saved.Show==2?7:saved.Show==3?3:4;p.Min=new Point {X=-1,Y=-1};p.Max=new Point {X=-1,Y=-1};p.Normal=new Rect {Left=rect.Left-dx,Top=rect.Top-dy,Right=rect.Right-dx,Bottom=rect.Bottom-dy};return p;
  }
  static bool StillSame(WindowLayout current) {
   uint pid;GetWindowThreadProcessId(new IntPtr(current.Handle),out pid);if(pid!=current.Pid)return false;
   try{using(var process=Process.GetProcessById((int)pid))return process.StartTime.ToUniversalTime().Ticks==current.Started;}catch{return false;}
  }
  public static bool Apply(WindowLayout saved,WindowLayout current) {
   if(!Valid(saved) || !StillSame(current))return false;
   var p=Target(saved);return SetWindowPlacement(new IntPtr(current.Handle),ref p);
  }
  public static bool Verified(WindowLayout saved,WindowLayout current) {
   if(!StillSame(current))return false;
   var p=NewPlacement();if(!GetWindowPlacement(new IntPtr(current.Handle),ref p))return false;var target=Target(saved);
   return p.Show==saved.Show && Math.Abs(p.Normal.Left-target.Normal.Left)<=2 && Math.Abs(p.Normal.Top-target.Normal.Top)<=2 && Math.Abs(p.Normal.Right-target.Normal.Right)<=2 && Math.Abs(p.Normal.Bottom-target.Normal.Bottom)<=2;
  }
  public static async Task<string> Restore(Session session,CancellationToken token) {
   var saved=Included(session);if(saved.Count==0)return "창 배치 정보 없음 · 업데이트 후 현재 작업을 새로 저장하세요.";
   var pending=new List<WindowLayout>(saved);var used=new HashSet<long>();var observed=new Dictionary<WindowLayout,long>();var failures=new List<string>();int restored=0;
   var watch=Stopwatch.StartNew();
   while(pending.Count>0 && watch.Elapsed.TotalSeconds<45) {
    token.ThrowIfCancellationRequested();var snapshot=await Engine.LayoutSnapshot(token);var current=snapshot.Windows;
    foreach(var w in pending.ToArray()) {
     var match=Match(w,current,saved,used);if(match==null)continue;
     long previous;if(!observed.TryGetValue(w,out previous) || previous!=match.Handle){observed[w]=match.Handle;continue;}
     bool applied=Apply(w,match);await Task.Delay(600,token);
     if(applied && Verified(w,match))restored++;else failures.Add(w.Title+" (위치 적용 확인 실패)");
     pending.Remove(w);used.Add(match.Handle);
    }
    if(pending.Count>0)await Task.Delay(1500,token);
   }
   failures.AddRange(pending.Select(w=>w.Title+" (창을 찾지 못했거나 구분 불가)"));
   return "창 위치·크기·상태 복원 확인 "+restored+"/"+saved.Count+"개"+(failures.Count>0?Environment.NewLine+String.Join(Environment.NewLine,failures):"");
  }
 }
}
