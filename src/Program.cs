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
 static class Program {
  [STAThread] static int Main(string[] args) {
   try {
    if(args.Length == 3 && args[0] == "--collect") { Data.Write(args[2],Collector.Run(args[1])); return 0; }
    if(args.Length == 2 && args[0] == "--scan") {Data.Write(args[1],Engine.Scan().GetAwaiter().GetResult());return 0;}
    if(args.Length == 2 && args[0] == "--self-test") return Tests.Run(args[1]);
    if(args.Length == 2 && args[0] == "--install-connections") {File.WriteAllText(args[1],Engine.InstallConnections());return 0;}
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    if(args.Length==3 && args[0]=="--integration")return IntegrationTests.Run(args[1],args[2]);
    if(args.Length==3 && args[0]=="--layout-test")return LayoutTests.Run(args[1],args[2]);
    if(args.Length == 2 && args[0] == "--render") {
     using(var f = new MainForm(true)) { f.Show(); Application.DoEvents(); using(var bmp = new Bitmap(f.Width,f.Height)) { f.DrawToBitmap(bmp,new Rectangle(Point.Empty,f.Size)); bmp.Save(args[1]); } f.Close(); } return 0;
    }
    using(var mutex = new Mutex(false,"Local\\OpenMemory.Desktop")) {
     if(!mutex.WaitOne(0,false)) { MessageBox.Show("OpenMemory가 이미 실행 중입니다. 작업 표시줄에서 확인하세요.","OpenMemory"); return 0; }
     try { Application.Run(new MainForm(false,args.Contains("--startup"),args.Contains("--tray"))); } finally { mutex.ReleaseMutex(); }
    }
    return 0;
   } catch(Exception e) { if(args.Length == 0) MessageBox.Show(e.Message,"OpenMemory",MessageBoxButtons.OK,MessageBoxIcon.Error); else { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),e.ToString()); } catch {} } return 1; }
  }
 }
 public class HistoryItem {
  public string Path;
  public override string ToString() { return System.IO.Path.GetFileNameWithoutExtension(Path); }
 }
 public class MainForm : Form {
  readonly Color navy = Color.FromArgb(24,43,69), blue = Color.FromArgb(41,99,218);
  ListView files; TextBox details; Label status, count; ComboBox history;
  Button save, restore, scan, add, remove, version, install, export, cancel, addFolder;
  NumericUpDown delay; Session current = new Session(); string currentPath; bool busy, preview;
  CancellationTokenSource cancellation;
  NotifyIcon tray; ToolStripMenuItem traySave,trayOpen,trayReset,trayStartup,trayExit; bool quitting, autoPending;
  public MainForm(bool previewMode,bool startupMode=false,bool trayMode=false) {
   preview = previewMode; Text="OpenMemory 2.3 · 작업 이어 열기"; Size=new Size(1200,850); MinimumSize=new Size(1120,760); StartPosition=FormStartPosition.CenterScreen;
   Font=new Font("맑은 고딕",10); BackColor=Color.FromArgb(245,247,251); ForeColor=navy; AutoScaleMode=AutoScaleMode.Dpi;
   var layout = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(28,20,28,18), ColumnCount=1, RowCount=8 };
   foreach(var height in new[]{68,80,48,42}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
   layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,102)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
   Controls.Add(layout);
   var head = new Panel {Dock=DockStyle.Fill};
   head.Controls.Add(new Label {Text="OpenMemory",Font=new Font("맑은 고딕",24,FontStyle.Bold),AutoSize=true,Location=new Point(0,0)});
   head.Controls.Add(new Label {Text="프로그램 · 폴더 · 문서를 기억하고, 내일 이어 여세요.",AutoSize=true,ForeColor=Color.FromArgb(96,112,135),Location=new Point(270,19)}); layout.Controls.Add(head,0,0);
   var actions = new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
   save = Button("현재 작업 저장",220,56,true); restore=Button("목록의 작업 열기",220,56,true); scan=Button("열린 작업 새로 확인",180,56,false); cancel=Button("복원 중지",112,56,false); cancel.Enabled=false;
   actions.Controls.AddRange(new Control[]{save,restore,scan,cancel}); layout.Controls.Add(actions,0,1);
   var saved = new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
   saved.Controls.Add(new Label {Text="저장 기록",AutoSize=true,Margin=new Padding(0,10,14,0)});
   history=new ComboBox {Width=370,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,5,12,0)};
   export=Button("목록 내보내기",136,34,false); var import=Button("목록 가져오기",136,34,false);
   saved.Controls.AddRange(new Control[]{history,export,import}); layout.Controls.Add(saved,0,2);
   var toolbar = new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
   count = new Label {Text="작업 0개",AutoSize=true,Margin=new Padding(0,10,12,0)};
   add=Button("파일 추가",98,32,false); addFolder=Button("폴더 추가",98,32,false); remove=Button("목록에서 제외",126,32,false); version=Button("Revit 버전 지정",138,32,false); install=Button("문서 연결 설정",138,32,false);
   toolbar.Controls.AddRange(new Control[]{count,add,addFolder,remove,version,install}); layout.Controls.Add(toolbar,0,3);
   files=new ListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,CheckBoxes=false,GridLines=false,HideSelection=false,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.White};
   files.Columns.Add("실행순번",75);files.Columns.Add("대상 이름",200);files.Columns.Add("종류",75); files.Columns.Add("프로그램",130); files.Columns.Add("복원 범위 / 확인 사항",330); files.Columns.Add("경로 / 실행 대상",370);
   files.SelectedIndexChanged+=(s,e)=> { if(files.SelectedItems.Count > 0) { var entry=(Entry)files.SelectedItems[0].Tag; details.Text=entry.Path + Environment.NewLine + "감지: " + entry.Source + Environment.NewLine + "프로그램: " + (String.IsNullOrEmpty(entry.Executable) ? "Windows 기본 연결 앱" : entry.Executable) + Environment.NewLine + entry.Note + (entry.WindowTitles.Count>0 ? Environment.NewLine + "기록한 창·문서 제목:" + Environment.NewLine + String.Join(Environment.NewLine,entry.WindowTitles.ToArray()) : ""); } };
   layout.Controls.Add(files,0,4);
   var options = new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
   options.Controls.Add(new Label {Text="파일 열기 간격",AutoSize=true,Margin=new Padding(0,12,10,0)});
   delay=new NumericUpDown {Minimum=1,Maximum=60,Value=3,Width=58,Margin=new Padding(0,8,8,0)}; options.Controls.Add(delay);
   options.Controls.Add(new Label {Text="초   ·   수정한 내용은 각 프로그램에서 먼저 저장하세요.",AutoSize=true,ForeColor=Color.FromArgb(96,112,135),Margin=new Padding(0,12,0,0)}); layout.Controls.Add(options,0,5);
   details=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Text="‘현재 작업 저장’으로 작업표시줄 앱, 탐색기 폴더·감지 가능한 문서와 창 위치·크기를 기록합니다.\r\n프로그램만 다시 열리는 항목과 문서까지 복원되는 항목을 구분해서 확인하세요.\r\n창을 닫아도 알림영역 아이콘에 남습니다. 우클릭 메뉴에서 저장·열기·시작 시 자동 복원을 설정할 수 있습니다."}; layout.Controls.Add(details,0,6);
   status=new Label {Dock=DockStyle.Fill,Text="준비됨 · 파일 내용은 복사하지 않고 경로·창 배치를 이 PC에 저장합니다.",TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(96,112,135)}; layout.Controls.Add(status,0,7);
   save.Click+=async(s,e)=>await CaptureSession(true); scan.Click+=async(s,e)=>await CaptureSession(false); restore.Click+=async(s,e)=>await RestoreAll();
   cancel.Click+=(s,e)=> { if(cancellation!=null) cancellation.Cancel(); };
   add.Click+=(s,e)=>AddFiles(); remove.Click+=(s,e)=>RemoveSelected();
   addFolder.Click+=(s,e)=>{using(var d=new FolderBrowserDialog {Description="다시 열 폴더 선택"})if(d.ShowDialog()==DialogResult.OK){var item=new Entry {Kind="Folder",Path=d.SelectedPath,App="파일 탐색기",Source="직접 추가",Note="폴더 다시 열기"};current.ExcludedKeys.RemoveAll(k=>k.Equals(Data.Key(item),StringComparison.OrdinalIgnoreCase));current.Files=Data.Unique(current.Files.Concat(new[]{item}));currentPath=null;ShowFiles();status.Text="폴더를 추가했습니다. 현재 작업 저장으로 기록하세요.";}};
   version.Click+=(s,e)=>SetRevitVersion(); install.Click+=(s,e)=> { try { details.Text=Engine.InstallConnections(); status.Text="문서 연결 결과를 확인하세요 · 이미 실행 중인 앱은 다음 재시작부터 적용"; } catch(Exception ex) { Error(ex); } };
   history.SelectedIndexChanged+=(s,e)=> { if(!busy && history.SelectedItem is HistoryItem) LoadSession(((HistoryItem)history.SelectedItem).Path); };
   export.Click+=(s,e)=> { using(var d=new SaveFileDialog {Filter="OpenMemory 목록 (*.xml)|*.xml",FileName="OpenMemory-session.xml"}) if(d.ShowDialog()==DialogResult.OK) try { Data.Write(d.FileName,current); status.Text="목록을 내보냈습니다."; } catch(Exception ex) { Error(ex); } };
   import.Click+=(s,e)=> { using(var d=new OpenFileDialog {Filter="OpenMemory 목록 (*.xml)|*.xml"}) if(d.ShowDialog()==DialogResult.OK) LoadSession(d.FileName); };
   // Keep all controls, including import, disabled during long operations.
   mutableControls=new Control[]{save,restore,scan,add,addFolder,remove,version,install,export,import,history,files,delay};
   FormClosing+=(s,e)=> {
    if(e.CloseReason==CloseReason.WindowsShutDown || e.CloseReason==CloseReason.TaskManagerClosing){quitting=true;autoPending=false;return;}
    if(!preview && !quitting){e.Cancel=true;Hide();ShowInTaskbar=false;return;}
    if(busy && !preview){e.Cancel=true;quitting=false;if(cancellation!=null)cancellation.Cancel();status.Text="작업 정리 후 종료를 다시 선택하세요.";}
   };
   FormClosed+=(s,e)=>{if(tray!=null){tray.Visible=false;tray.Dispose();}};
   if(!preview) { RefreshHistory(null); if(history.Items.Count>0) history.SelectedIndex=0; }
   if(!preview) {
    SetupTray();
    if(startupMode || trayMode){WindowState=FormWindowState.Minimized;ShowInTaskbar=false;}
    Shown+=async(s,e)=>{
     if(startupMode || trayMode)Hide();
     if(startupMode && Startup.IsEnabled(Application.ExecutablePath)){
      autoPending=true;status.Text="로그인 후 15초 대기 중 · 저장된 작업을 자동으로 엽니다.";
      await Task.Delay(Startup.DelayMilliseconds);
      if(autoPending && !IsDisposed && !busy && Startup.IsEnabled(Application.ExecutablePath)){autoPending=false;await RestoreLatest();}
     }
    };
   }
  }
  Control[] mutableControls;
  Button Button(string text,int width,int height,bool primary) {
   var b=new Button {Text=text,Width=width,Height=height,FlatStyle=FlatStyle.Flat,BackColor=primary?blue:Color.White,ForeColor=primary?Color.White:navy,Margin=new Padding(0,0,12,0),Cursor=Cursors.Hand}; b.FlatAppearance.BorderColor=primary?blue:Color.FromArgb(212,221,234); return b;
  }
  void Busy(bool value) { if(value)busy=true; foreach(var c in mutableControls) c.Enabled=!value; cancel.Enabled=value && cancellation!=null; UseWaitCursor=value; if(tray!=null)foreach(var item in new[]{traySave,trayOpen,trayReset,trayStartup,trayExit})item.Enabled=!value; if(!value)busy=false; }
  void Error(Exception e) { details.Text=e.Message; status.Text="작업을 완료하지 못했습니다. 아래 내용을 확인하세요.";Notify(e.Message); }
  void ShowFiles() {
   current.Files=current.Files.OrderBy(e=>Data.KindOf(e)=="File"?0:1).ToList();
   files.BeginUpdate(); files.Items.Clear();int sequence=0;
   foreach(var e in current.Files) {e.Selected=true;var item=new ListViewItem(new[]{(++sequence).ToString(),Data.Label(e),Data.KindLabel(e),e.App,e.Note,e.Path}) {Tag=e}; files.Items.Add(item);}
   files.EndUpdate(); count.Text="작업 " + current.Files.Count + "개";
  }
  void RefreshHistory(string select) { history.Items.Clear(); foreach(var p in Engine.History()) history.Items.Add(new HistoryItem {Path=p}); if(select!=null) for(int i=0;i<history.Items.Count;i++) if(((HistoryItem)history.Items[i]).Path==select) {history.SelectedIndex=i;break;} }
  void LoadSession(string path) {
   try { var s=Data.Read<Session>(path); if((s.Schema!=1 && s.Schema!=2) || s.Files==null || s.Warnings==null) throw new IOException("지원하지 않는 목록 형식입니다."); if(s.Files.Any(e=>e==null || Data.EntryPath(e).Length==0)) throw new IOException("목록에 지원하지 않는 경로가 포함되어 있습니다."); s.ExcludedKeys=new List<string>();s.Files=Data.Unique(s.Files.Where(e=>e.Selected)); current=s; currentPath=path; ShowFiles(); details.Text="저장 시각: " + s.Created + Environment.NewLine + String.Join(Environment.NewLine,s.Warnings.ToArray()); status.Text="목록의 작업을 실행순번대로 엽니다. 제외할 행은 클릭해서 선택하세요."; }
   catch(Exception e) { Error(e); }
  }
  async Task CaptureSession(bool saveNow) {
   if(busy)return;autoPending=false;
   Busy(true); status.Text="열린 문서 확인 중… 최대 약 20초가 걸릴 수 있습니다.";
   try {
    var manual=current.Files.Where(e=>e.Source=="직접 추가").ToList();
    var old=current.Files.ToDictionary(Data.Key,e=>e,StringComparer.OrdinalIgnoreCase);
    var s=await Engine.Scan();
    foreach(var entry in s.Files) {entry.Selected=true;Entry previous; if(old.TryGetValue(Data.Key(entry),out previous) && String.IsNullOrEmpty(entry.Executable)) entry.Executable=previous.Executable; }
    s.Files=Data.Unique(s.Files.Concat(manual));
    s.ExcludedKeys.Clear();
    if(s.Files.Count==0) { details.Text=String.Join(Environment.NewLine,s.Warnings.ToArray()); status.Text="감지된 파일이 없습니다. 현재 목록과 기존 저장 기록은 유지됩니다. 파일을 직접 추가할 수 있습니다."; return; }
    current=s; currentPath=null;
    if(saveNow) { currentPath=Engine.Save(current); RefreshHistory(currentPath); }
    ShowFiles(); details.Text=(saveNow?"파일 경로와 창 배치 저장 완료. 원본 파일의 수정 내용은 각 프로그램에서 저장하세요.":"현재 열린 파일을 확인했습니다. ‘현재 작업 저장’을 누르면 기록됩니다.") + Environment.NewLine + String.Join(Environment.NewLine,current.Warnings.ToArray());
    status.Text=(saveNow?"저장 완료":"확인 완료") + " · " + current.Files.Count + "개 · 창 배치 " + OpenMemory.Layout.Included(current).Count + "개" + (current.Warnings.Count>0?" · 확인 사항 " + current.Warnings.Count + "건 (아래 목록 확인)":"");if(saveNow)Notify(status.Text);
   } catch(Exception e) { Error(e); } finally { Busy(false); }
  }
  void AddFiles() {
   using(var d=new OpenFileDialog {Multiselect=true,Filter="작업 파일|*.dwg;*.dxf;*.dwt;*.rvt;*.rfa;*.rte;*.xlsx;*.xls;*.xlsm;*.xlsb;*.csv;*.hwp;*.hwpx;*.doc;*.docx;*.pdf;*.ppt;*.pptx;*.txt;*.nwd;*.nwf;*.skp;*.3dm;*.psd;*.ai;*.png;*.jpg|모든 파일|*.*"}) {
    if(d.ShowDialog()!=DialogResult.OK) return;
    var rejected=new List<string>();
    foreach(var p in d.FileNames) { if(Data.Normalize(p).Length==0) {rejected.Add(p);continue;} var entry=new Entry {Path=p,App=Data.AppFor(p),Source="직접 추가",Note=Data.IsRevit(p)?"Revit 버전 지정 필요 · 추가 기능 없이 열기 가능":"직접 추가"};current.ExcludedKeys.RemoveAll(k=>k.Equals(Data.Key(entry),StringComparison.OrdinalIgnoreCase));current.Files.Add(entry); }
    current.Files=Data.Unique(current.Files); currentPath=null; ShowFiles(); status.Text="파일을 추가했습니다. ‘현재 작업 저장’으로 기록하세요.";
    if(rejected.Count>0) details.Text="지원하지 않는 파일: " + String.Join(", ",rejected.ToArray());
   }
  }
  void SetRevitVersion() {
   var selected=files.SelectedItems.Cast<ListViewItem>().Select(i=>(Entry)i.Tag).Where(e=>Data.KindOf(e)=="File" && Data.IsRevit(e.Path)).ToArray();
   if(selected.Length==0) { status.Text="목록에서 Revit 파일 행을 먼저 선택하세요. Ctrl로 여러 개를 선택할 수 있습니다."; return; }
   using(var d=new OpenFileDialog {Title="이 모델을 열 Revit 버전의 Revit.exe 선택",Filter="Revit 프로그램|Revit.exe",InitialDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk")}) {
    if(d.ShowDialog()!=DialogResult.OK) return;
    foreach(var e in selected) {e.Executable=d.FileName; e.App=Directory.GetParent(d.FileName).Name; e.Source="직접 추가"; e.Note="사용자가 지정한 Revit 버전";} currentPath=null; ShowFiles(); status.Text="Revit 버전을 지정했습니다. ‘현재 작업 저장’으로 기록하세요.";
   }
  }
  async Task RestoreAll() {
   if(busy)return;autoPending=false;
   var selected=current.Files.ToArray(); if(selected.Length==0) {status.Text="다시 열 작업이 없습니다. 현재 작업 저장으로 목록을 새로 만드세요.";Notify(status.Text);return;}
   cancellation=new CancellationTokenSource(); Busy(true); details.Clear();
   int processed=0;
   try {
    status.Text="이미 열린 파일을 확인 중…";
    var active=await Engine.Scan(); var open=new HashSet<string>(active.Files.Select(Data.Key),StringComparer.OrdinalIgnoreCase);
    foreach(var e in selected) {
     cancellation.Token.ThrowIfCancellationRequested(); status.Text="복원 " + (processed+1) + "/" + selected.Length + " · " + Data.Label(e);
     string message;
     if(open.Contains(Data.Key(e))) message="이미 열려 있어 건너뜀";
     else { try { message=await Engine.Restore(e,cancellation.Token); } catch(OperationCanceledException) {throw;} catch(Exception ex) {message="실패: " + ex.Message;} }
     details.AppendText(Data.Label(e) + " — " + message + Environment.NewLine); processed++;
     if(processed<selected.Length) await Task.Delay((int)delay.Value*1000,cancellation.Token);
    }
    status.Text="창 위치·크기 적용 중… 늦게 열리는 창을 최대 45초 기다립니다.";
    details.AppendText(await OpenMemory.Layout.Restore(current,cancellation.Token)+Environment.NewLine);
    status.Text="복원 처리 종료 · " + processed + "개 결과를 확인하세요. 앱별 대화상자는 직접 확인해야 합니다.";Notify(status.Text);
   } catch(OperationCanceledException) {status.Text="복원 중지 · 이후 파일 요청을 취소했습니다. 이미 시작된 열기는 계속될 수 있습니다.";}
   catch(Exception e) {Error(e);} finally { cancellation.Dispose(); cancellation=null; Busy(false); }
  }
  void RemoveSelected() {
   autoPending=false;
   var selected=files.SelectedItems.Cast<ListViewItem>().Select(i=>(Entry)i.Tag).ToArray();
   if(selected.Length==0){status.Text="제외할 행을 먼저 클릭하세요. Ctrl/Shift로 여러 행을 선택할 수 있습니다.";return;}
   Data.Exclude(current,selected);currentPath=null;ShowFiles();
   try {currentPath=Engine.SaveEdited(current);RefreshHistory(currentPath);status.Text=selected.Length+"개 항목을 이 목록에서 제외하고 저장했습니다. 현재 작업 저장을 누르면 열린 작업을 새로 수집합니다.";}catch(Exception e){Error(e);}
  }
  void Notify(string message) {if(tray!=null && !Visible){tray.BalloonTipTitle="OpenMemory";tray.BalloonTipText=message;tray.ShowBalloonTip(4000);}}
  void Reveal() {ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();}
  async Task RestoreLatest() {
   if(busy)return;
   var paths=Engine.History();if(paths.Length==0){Notify("저장된 작업이 없습니다. 현재 작업 저장을 먼저 실행하세요.");return;}
   LoadSession(paths[0]);if(currentPath!=paths[0]){Notify("저장 목록을 읽지 못했습니다. 창에서 확인하세요.");return;}await RestoreAll();
  }
  void ResetSaved() {
   if(busy)return;autoPending=false;
   try {var n=Startup.ResetSessions(Data.SessionRoot);current=new Session();currentPath=null;RefreshHistory(null);ShowFiles();details.Text="저장된 작업 목록과 제외 설정을 초기화했습니다. 원본 파일은 변경하지 않았습니다.\r\n이전 목록은 OpenMemory의 reset-backups 폴더에 보관됩니다.";status.Text="저장된 작업 초기화 완료 · "+n+"개 기록";Notify(status.Text);}catch(Exception e){Error(e);Notify(e.Message);}
  }
  void SetupTray() {
   var menu=new ContextMenuStrip();
   traySave=new ToolStripMenuItem("현재 작업 저장");traySave.Click+=async(s,e)=>await CaptureSession(true);
   trayReset=new ToolStripMenuItem("저장된 작업 초기화");trayReset.Click+=(s,e)=>ResetSaved();
   trayOpen=new ToolStripMenuItem("저장된 파일 열기");trayOpen.Click+=async(s,e)=>await RestoreLatest();
   trayStartup=new ToolStripMenuItem("컴퓨터 시작 시 열기"){Checked=Startup.IsEnabled(Application.ExecutablePath),ToolTipText="Windows 로그인 후 저장된 작업을 자동으로 엽니다."};
   trayStartup.Click+=(s,e)=>{try {var enabled=!trayStartup.Checked;Startup.SetEnabled(Application.ExecutablePath,enabled);trayStartup.Checked=Startup.IsEnabled(Application.ExecutablePath);if(!enabled)autoPending=false;status.Text=enabled?"Windows 로그인 시 OpenMemory 실행 및 저장 작업 자동 복원 켜짐":"시작 시 자동 복원 꺼짐";}catch(Exception ex){Error(ex);}};
   bool keepOpenForStartup=false;
   menu.ItemClicked+=(s,e)=>{keepOpenForStartup=e.ClickedItem==trayStartup;};
   menu.Closing+=(s,e)=>{if(e.CloseReason==ToolStripDropDownCloseReason.ItemClicked && keepOpenForStartup){e.Cancel=true;}keepOpenForStartup=false;};
   var show=new ToolStripMenuItem("목록 창 열기");show.Click+=(s,e)=>Reveal();
   trayExit=new ToolStripMenuItem("OpenMemory 종료");trayExit.Click+=(s,e)=>{autoPending=false;quitting=true;Close();};
   menu.Items.AddRange(new ToolStripItem[]{traySave,trayReset,trayOpen,new ToolStripSeparator(),trayStartup,new ToolStripSeparator(),show,trayExit});
   tray=new NotifyIcon {Icon=SystemIcons.Application,Text="OpenMemory · 작업 저장과 복원",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(s,e)=>Reveal();
  }
 }
}
