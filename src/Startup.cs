using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OpenMemory {
 public static class Startup {
  internal static int DelayMilliseconds=15000;
  public const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
  public static string Command(string executable) {if(!Data.IsExecutable(executable))throw new ArgumentException("실행 파일 경로 확인 필요");return Desktop.Quote(executable)+" --startup";}
  public static bool IsEnabled(string executable) {using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null && String.Equals(Convert.ToString(key.GetValue("OpenMemory")),Command(executable),StringComparison.OrdinalIgnoreCase);}
  public static void SetEnabled(string executable,bool enabled) {
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {if(enabled)key.SetValue("OpenMemory",Command(executable),RegistryValueKind.String);else key.DeleteValue("OpenMemory",false);}
  }
  public static int ResetSessions(string root) {
   // Reset only our session XML files; original documents and startup preferences are untouched.
   if(!Directory.Exists(root))return 0;
   var paths=Data.SessionFiles(root);if(paths.Length==0)return 0;
   var archive=Path.Combine(Path.GetDirectoryName(root),"reset-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   Directory.CreateDirectory(archive);
   foreach(var path in paths)File.Move(path,Path.Combine(archive,Path.GetFileName(path)));
   return paths.Length;
  }
 }
}
