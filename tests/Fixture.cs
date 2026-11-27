using System;
using System.Windows.Forms;
class Fixture {
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles();
  var f=new Form {Text="OpenMemory disposable test window",Width=480,Height=180,StartPosition=FormStartPosition.CenterScreen};
  f.Controls.Add(new Label {Text="OpenMemory integration test — temporary window",Dock=DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleCenter});
  if(args.Length>0)f.Text=args[0];Application.Run(f);
 }
}
