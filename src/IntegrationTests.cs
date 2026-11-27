using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenMemory {
 public static class IntegrationTests {
  static List<string> log=new List<string>();
  static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);log.Add("PASS: "+text);}
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
  static void ClickMenu(ContextMenuStrip menu,ToolStripItem item) {
   var point=new Point(item.Bounds.Left+item.Bounds.Width/2,item.Bounds.Top+item.Bounds.Height/2);
   var position=new IntPtr(point.X|(point.Y<<16));
   SendMessage(menu.Handle,0x200,IntPtr.Zero,position);SendMessage(menu.Handle,0x201,new IntPtr(1),position);SendMessage(menu.Handle,0x202,IntPtr.Zero,position);Application.DoEvents();
  }
  static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
  static void PumpUntil(Func<bool> ready,int milliseconds){var watch=Stopwatch.StartNew();while(!ready() && watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(30);}Application.DoEvents();if(!ready())throw new TimeoutException("Test wait timed out");}
  static void StopFixtures(string executable){foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))using(p)if(Desktop.ProcessPath(p.Id).Equals(executable,StringComparison.OrdinalIgnoreCase)){p.CloseMainWindow();if(!p.WaitForExit(4000))p.Kill();}}
  static bool FixtureVisible(string executable){return Desktop.Windows().Files.Any(e=>e.Path.Equals(executable,StringComparison.OrdinalIgnoreCase));}
  static void CloseFolder(string path){
   object shell=null,windows=null;
   try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));windows=Invoke(shell,"Windows");int n=Convert.ToInt32(Get(windows,"Count"));for(int i=n-1;i>=0;i--){object win=null,doc=null,folder=null,self=null;try{win=Invoke(windows,"Item",i);doc=Get(win,"Document");folder=Get(doc,"Folder");self=Get(folder,"Self");if(Convert.ToString(Get(self,"Path")).Equals(path,StringComparison.OrdinalIgnoreCase))Invoke(win,"Quit");}catch{}finally{Release(self);Release(folder);Release(doc);Release(win);}}}finally{Release(windows);Release(shell);}
  }
  static object Get(object o,string n){return o.GetType().InvokeMember(n,BindingFlags.GetProperty,null,o,null);}
  static object Invoke(object o,string n,params object[] args){return o.GetType().InvokeMember(n,BindingFlags.GetProperty|BindingFlags.InvokeMethod,null,o,args);}
  static void Release(object o){if(o!=null && Marshal.IsComObject(o))try{Marshal.ReleaseComObject(o);}catch{}}
  public static int Run(string root,string fixture) {
   Directory.CreateDirectory(root);Data.TestRoot=Path.Combine(root,"isolated-app-data");
   var originalStartup=(object)null;var hadStartup=false;var originalKind=RegistryValueKind.String;
   using(var key=Registry.CurrentUser.OpenSubKey(Startup.RunKey)){if(key!=null){hadStartup=key.GetValueNames().Contains("OpenMemory");if(hadStartup){originalStartup=key.GetValue("OpenMemory",null,RegistryValueOptions.DoNotExpandEnvironmentNames);originalKind=key.GetValueKind("OpenMemory");}}}
   MainForm f=null,auto=null;var folder=Path.Combine(root,"탐색기 테스트 폴더");Directory.CreateDirectory(folder);
   var appEntry=new Entry {Kind="Application",Path=fixture,Executable=fixture,App="Test Fixture",Source="직접 추가"};
   try {
    using(var p=Process.Start(fixture)){}PumpUntil(()=>FixtureVisible(fixture),12000);
    Check(FixtureVisible(fixture),"Visible taskbar app captured with its executable path");
    var folderEntry=new Entry {Kind="Folder",Path=folder,App="파일 탐색기",Source="직접 추가"};
    var open=Task.Run(()=>Engine.Restore(folderEntry,CancellationToken.None));PumpUntil(()=>open.IsCompleted,15000);Check(open.Result.Contains("폴더"),"Explorer folder launch request succeeds");
    PumpUntil(()=>Desktop.Folders().Files.Any(e=>e.Path==folder),12000);Check(true,"Actually opened Explorer folder captured by full path");
    var folderLayout=Desktop.Folders().Windows.Single(w=>w.AppKey==Data.Key(folderEntry));
    var folderSnapshot=new Session {Files=new List<Entry>{folderEntry},Windows=new List<WindowLayout>{folderLayout}};
    var layoutFile=Path.Combine(root,"folder-layout.xml");Data.Write(layoutFile,folderSnapshot);
    var changedFolder=Data.Read<Session>(layoutFile).Windows[0];changedFolder.Left+=40;changedFolder.Top+=30;changedFolder.Show=1;
    Check(Layout.Apply(changedFolder,folderLayout),"Explorer test folder accepts position change");PumpUntil(()=>Layout.Verified(changedFolder,folderLayout),8000);
    var placeFolder=Task.Run(()=>Layout.Restore(folderSnapshot,CancellationToken.None));PumpUntil(()=>placeFolder.IsCompleted,60000);
    Check(placeFolder.Result.Contains("1/1") && Layout.Verified(folderLayout,folderLayout),"Explorer folder path matches and restores saved bounds and state");
    StopFixtures(fixture);Check(!Desktop.Running(fixture),"Test application closed before restore");
    var restored=Task.Run(()=>Engine.Restore(appEntry,CancellationToken.None));PumpUntil(()=>restored.IsCompleted,12000);PumpUntil(()=>FixtureVisible(fixture),12000);Check(true,"Closed application actually reopened by restore engine");
    var again=Task.Run(()=>Engine.Restore(appEntry,CancellationToken.None));PumpUntil(()=>again.IsCompleted,12000);Check(again.Result.Contains("이미 실행"),"Already running application not duplicated");
    Engine.SaveEdited(new Session {Files=new List<Entry>{appEntry,folderEntry}});
    f=new MainForm(false);f.Show();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());Application.DoEvents();
    var list=Field<ListView>(f,"files");Check(list.Items.Count==2,"Saved mixed session loaded into actual UI");
    Check(!list.CheckBoxes && list.Columns[0].Text=="실행순번" && list.Items[0].Text=="1" && list.Items[1].Text=="2","Checkboxes replaced with visible sequential execution numbers");
    list.Items[0].Selected=true;list.Focus();Field<Button>(f,"remove").PerformClick();Application.DoEvents();
    Check(list.Items.Count==1,"Exclude button really removes the selected UI row");
    var latestPath=Engine.History()[0];
    var saved=Data.Read<Session>(latestPath);Check(saved.Files.Count==1 && saved.ExcludedKeys.Count==0 && list.Items[0].Text=="1","Exclude persists row removal and renumbers remaining rows without a blacklist");
    foreach(ListViewItem item in list.Items)item.Selected=true;Field<Button>(f,"remove").PerformClick();Application.DoEvents();
    Check(list.Items.Count==0 && Data.Read<Session>(Engine.History()[0]).Files.Count==0,"All rows can be excluded and empty record is persisted");
    Field<Button>(f,"save").PerformClick();PumpUntil(()=>!Field<bool>(f,"busy"),30000);
    var recaptured=Data.Read<Session>(Engine.History()[0]);Check(recaptured.ExcludedKeys.Count==0 && recaptured.Files.Any(e=>Data.Key(e)==Data.Key(appEntry)) && recaptured.Files.Any(e=>e.Path==folder),"Current work save recaptures open app and folder after excluding every row");
    Check(recaptured.Windows.Any(w=>w.AppKey==Data.Key(appEntry)) && recaptured.Windows.Any(w=>w.AppKey==Data.Key(folderEntry)),"Save button persists both app and Explorer placements");
    Check(list.Items.Cast<ListViewItem>().Select((item,index)=>item.Text==(index+1).ToString()).All(ok=>ok),"Recaptured list execution numbers are contiguous");
    using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(Point.Empty,f.Size));bmp.Save(Path.Combine(root,"numbered-list.png"));}
    var icon=Field<NotifyIcon>(f,"tray");Check(icon.Visible,"Notification-area icon registered");
    var menu=icon.ContextMenuStrip;Check(menu.Items[0].Text=="현재 작업 저장" && menu.Items[1].Text=="저장된 작업 초기화" && menu.Items[2].Text=="저장된 파일 열기","Requested tray actions in correct order");
    menu.Show(f,new Point(30,30));Application.DoEvents();using(var bmp=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(bmp,new Rectangle(Point.Empty,menu.Size));bmp.Save(Path.Combine(root,"tray-menu.png"));}menu.Close();
    f.Close();Application.DoEvents();Check(!f.IsDisposed && !f.Visible && icon.Visible,"Closing main window keeps app resident in tray");
    var toggle=Field<ToolStripMenuItem>(f,"trayStartup");
    if(toggle.Checked)toggle.PerformClick();
    menu.Show(new Point(80,80));Application.DoEvents();ClickMenu(menu,toggle);
    Check(Startup.IsEnabled(Application.ExecutablePath) && toggle.Checked,"Startup checkbox registers actual HKCU login command");
    Check(menu.Visible && icon.Visible,"Startup mouse click keeps the menu and tray icon visible");
    ClickMenu(menu,toggle);Check(!Startup.IsEnabled(Application.ExecutablePath) && !toggle.Checked && menu.Visible,"Uncheck succeeds without closing the menu");
    menu.Close();Application.DoEvents();Check(!menu.Visible,"Menu still closes when explicitly dismissed");
    Field<ToolStripMenuItem>(f,"traySave").PerformClick();PumpUntil(()=>!Field<bool>(f,"busy"),30000);Check(Engine.History().Length>=3,"Tray save performs an actual desktop capture and stores it");
    Field<ToolStripMenuItem>(f,"trayReset").PerformClick();if(Engine.History().Length!=0)log.Add("Reset diagnostic: "+Field<Label>(f,"status").Text+" / "+Field<TextBox>(f,"details").Text+" / enabled="+Field<ToolStripMenuItem>(f,"trayReset").Enabled);Check(Engine.History().Length==0 && Directory.Exists(folder),"Tray reset clears test session history without deleting original folder");
    Engine.SaveEdited(new Session {Files=new List<Entry>{appEntry}});StopFixtures(fixture);
    Field<ToolStripMenuItem>(f,"trayOpen").PerformClick();PumpUntil(()=>!Field<bool>(f,"busy"),30000);PumpUntil(()=>FixtureVisible(fixture),12000);Check(true,"Tray open reopens the latest saved test application");
    Field<ToolStripMenuItem>(f,"trayExit").PerformClick();Application.DoEvents();Check(f.IsDisposed,"Explicit tray Exit terminates the main form");f=null;
    StopFixtures(fixture);Startup.SetEnabled(Application.ExecutablePath,true);Startup.DelayMilliseconds=80;
    auto=new MainForm(false,true);auto.Show();PumpUntil(()=>FixtureVisible(fixture),35000);PumpUntil(()=>!Field<bool>(auto,"busy"),35000);
    Check(!auto.Visible && Field<NotifyIcon>(auto,"tray").Visible,"Startup mode stays in tray while automatically restoring saved work");
    Field<ToolStripMenuItem>(auto,"trayExit").PerformClick();Application.DoEvents();auto=null;
    log.Add("NOTE: login restoration code path tested; physical reboot/logoff not performed.");return 0;
   }catch(Exception e){log.Add(e.ToString());return 1;}
   finally {
    foreach(var form in new[]{f,auto})if(form!=null){try{var icon=Field<NotifyIcon>(form,"tray");if(icon!=null){icon.Visible=false;icon.Dispose();}form.Dispose();}catch{}}
    StopFixtures(fixture);try{CloseFolder(folder);}catch(Exception e){log.Add("Cleanup: "+e.Message);}
    using(var key=Registry.CurrentUser.CreateSubKey(Startup.RunKey)){if(hadStartup)key.SetValue("OpenMemory",originalStartup,originalKind);else key.DeleteValue("OpenMemory",false);}
    Startup.DelayMilliseconds=15000;Data.TestRoot=null;File.WriteAllLines(Path.Combine(root,"integration-results.txt"),log);
   }
  }
 }
}
