using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenMemory {
 public static class LayoutTests {
  static List<string> log=new List<string>();
  static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
  static void Wait(Func<bool> ready,int ms=12000){var t=Stopwatch.StartNew();while(!ready() && t.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(60);}if(!ready())throw new TimeoutException("Layout test timed out");}
  static WindowLayout Copy(WindowLayout w){return new WindowLayout {AppKey=w.AppKey,EntryKeys=new List<string>(w.EntryKeys),Executable=w.Executable,Title=w.Title,ClassName=w.ClassName,Monitor=w.Monitor,Handle=w.Handle,Pid=w.Pid,Started=w.Started,Left=w.Left,Top=w.Top,Width=w.Width,Height=w.Height,Show=w.Show,Flags=w.Flags,WorkLeft=w.WorkLeft,WorkTop=w.WorkTop,WorkWidth=w.WorkWidth,WorkHeight=w.WorkHeight,OffsetX=w.OffsetX,OffsetY=w.OffsetY};}
  public static int Run(string root,string fixture) {
   Directory.CreateDirectory(root);var processes=new List<Process>();
   Func<List<WindowLayout>> live=()=>Desktop.Windows().Windows.Where(w=>String.Equals(w.Executable,fixture,StringComparison.OrdinalIgnoreCase)).ToList();
   Action<string> start=title=>{processes.Add(Process.Start(new ProcessStartInfo(fixture,Desktop.Quote(title)){UseShellExecute=false}));};
   Action close=()=>{foreach(var p in processes){if(!p.HasExited){p.CloseMainWindow();if(!p.WaitForExit(4000))p.Kill();}p.Dispose();}processes.Clear();};
   try {
    var area=new Rectangle(-1920,0,1920,1040);var fit=Layout.Fit(new Rectangle(9000,-500,2400,1200),area);
    Check(area.Contains(fit) && fit.Width==1920,"Missing monitor / oversized bounds fit within negative-coordinate working area");
    start("Layout fixture A — 설계 & 문서");start("Layout fixture B — 설계 & 문서");Wait(()=>live().Count==2);
    var windows=live().OrderBy(w=>w.Title).ToList();
    for(int i=0;i<2;i++){var desired=Copy(windows[i]);desired.Left=desired.WorkLeft-desired.OffsetX+80+i*120;desired.Top=desired.WorkTop-desired.OffsetY+90+i*80;desired.Width=540+i*50;desired.Height=310+i*40;desired.Show=1;Check(Layout.Apply(desired,windows[i]),"Initial fixture placement "+i);Wait(()=>Layout.Verified(desired,windows[i]));}
    windows=live().OrderBy(w=>w.Title).ToList();
    var e=new Entry {Kind="Application",Path=fixture,Executable=fixture};var s=new Session {Files=new List<Entry>{e},Windows=windows};
    var file=Path.Combine(root,"layout.xml");Data.Write(file,s);s=Data.Read<Session>(file);
    Check(s.Windows.Count==2 && s.Windows[0].Left==windows[0].Left && s.Windows[1].Width==windows[1].Width,"Two independent window placements survive Unicode XML round trip");
    var changed=Copy(windows[0]);changed.Handle=0;changed.Started=0;changed.Title="unrelated document";changed.EntryKeys=new List<string>{"File|C:\\a.xlsx|"};
    Check(Layout.Match(changed,windows,new[]{changed},new HashSet<long>())==null,"Unmatched document never moves an unrelated window");
    var docSaved=Copy(windows[0]);docSaved.Handle=0;docSaved.Title="Building A.xlsx - Excel";docSaved.EntryKeys=new List<string>{"File|C:\\work\\Building A.xlsx|"};
    var docCurrent=Copy(windows[1]);docCurrent.Title="Building A.xlsx [Read Only] - Excel";
    Check(Layout.Match(docSaved,new[]{docCurrent},new[]{docSaved},new HashSet<long>())==docCurrent,"Document identity survives read-only caption changes");
    docCurrent.Title="Building AB.xlsx - Excel";Check(Layout.Match(docSaved,new[]{docCurrent},new[]{docSaved},new HashSet<long>())==null,"Similar document name is not mistaken for the saved document");
    var ambiguous=Copy(windows[0]);ambiguous.Handle=0;ambiguous.Title="same";var other=Copy(ambiguous);other.Handle=9;
    Check(Layout.Match(ambiguous,new[]{ambiguous,other},new[]{ambiguous,other},new HashSet<long>())==null,"Ambiguous equal-title windows are not assigned arbitrarily");
    Data.Exclude(s,new[]{e});Check(Layout.Included(s).Count==0,"Removing every entry excludes all associated placements");s=Data.Read<Session>(file);
    close();start("Layout fixture B — 설계 & 문서");start("Layout fixture A — 설계 & 문서");Wait(()=>live().Count==2);
    var task=Task.Run(()=>Layout.Restore(s,CancellationToken.None));Wait(()=>task.IsCompleted,60000);var result=task.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(root,"restore-result.txt"),result);
    var reopened=live();Check(windows.All(w=>{var match=reopened.Single(x=>x.Title==w.Title);return Layout.Verified(w,match);}),"After process restart, reversed creation order restores each named window to its saved bounds");
    var current=reopened[0];var max=Copy(current);max.Show=3;Check(Layout.Apply(max,current),"Maximized placement accepted");Wait(()=>Layout.Verified(max,current));Check(true,"Maximized state and normal restore bounds verified");
    var minimized=Copy(max);minimized.Show=2;minimized.Flags=2;Check(Layout.Apply(minimized,current),"Minimized-from-maximized placement accepted");Wait(()=>Layout.Verified(minimized,current));
    var captured=live().Single(w=>w.Handle==current.Handle);Check(captured.Show==2 && (captured.Flags&2)!=0,"Minimized state preserves restore-to-maximized flag");
    var normal=Copy(current);normal.Show=1;normal.Flags=0;Layout.Apply(normal,current);Wait(()=>Layout.Verified(normal,current));Check(true,"Restoring a normal window clears minimized/maximized state");
    var offscreen=Copy(normal);offscreen.Monitor="disconnected";offscreen.Left=90000;offscreen.Top=90000;Layout.Apply(offscreen,current);
    Wait(()=>Layout.Verified(offscreen,current));var visible=live().Single(w=>w.Handle==current.Handle);Check(Screen.AllScreens.Any(sc=>sc.WorkingArea.IntersectsWith(new Rectangle(visible.Left+visible.OffsetX,visible.Top+visible.OffsetY,visible.Width,visible.Height))),"Disconnected monitor placement is brought onto an actual screen");
    var legacy=Path.Combine(root,"old.xml");File.WriteAllText(legacy,"<Session><Schema>2</Schema><Files/><Warnings/></Session>");Check(Data.Read<Session>(legacy).Windows.Count==0,"Old records without placement information still load");
    return 0;
   }catch(Exception ex){log.Add(ex.ToString());return 1;}
   finally{close();File.WriteAllLines(Path.Combine(root,"results.txt"),log);}
  }
 }
}
