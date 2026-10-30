// Git push test - Blightfall Pops
// Blightfall Pops desktop overlay: .NET Framework 4.8, Windows Forms, no PowerShell.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BlightfallPopsDesktop {
    // Persisted user preferences.
    internal sealed class Settings {
        public int Width=500, Height=390, X=int.MinValue, Y=int.MinValue, WindowMs=1200, IconSize=26, TextSize=10;
        public bool Locked=false, AlwaysOnTop=true, WatchLog=true, ShowBeasts=true, ShowBeastBlightfall=true, ShowSoulReaper=false, ShowScythe=true, ShowOverkill=false, CompactNumbers=true, Collapsed=false, GroupByEventType=false, MiniCards=true;
        public string Log="";
        public string PlayerMode="Myself", SelectedPlayerGuid="";
        public static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BlightfallPopsDesktop");
        public static Settings Load() {
            var s=new Settings(); var file=Path.Combine(DirectoryPath,"settings.txt");
            if (!File.Exists(file)) return s;
            foreach (var line in File.ReadAllLines(file)) {
                int sep=line.IndexOf('='); if(sep<1)continue;
                var key=line.Substring(0,sep); var value=line.Substring(sep+1); int n; bool b;
                if (key=="Log") s.Log=value;
                else if(key=="PlayerMode"&&(value=="Myself"||value=="Specific Player"||value=="All Players"))s.PlayerMode=value;
                else if(key=="SelectedPlayerGuid")s.SelectedPlayerGuid=value;
                else if (int.TryParse(value,out n)) {
                    switch(key) { case "Width": s.Width=n;break;case "Height":s.Height=n;break;case "X":s.X=n;break;case "Y":s.Y=n;break;case "WindowMs":s.WindowMs=n;break;case "IconSize":s.IconSize=n;break;case "TextSize":s.TextSize=n;break; }
                } else if(bool.TryParse(value,out b)) {
                    switch(key) { case "Locked":s.Locked=b;break;case "AlwaysOnTop":s.AlwaysOnTop=b;break;case "WatchLog":s.WatchLog=b;break;case "ShowBeasts":s.ShowBeasts=b;break;case "ShowBeastBlightfall":s.ShowBeastBlightfall=b;break;case "ShowSoulReaper":s.ShowSoulReaper=b;break;case "ShowScythe":s.ShowScythe=b;break;case "ShowOverkill":s.ShowOverkill=b;break;case "CompactNumbers":s.CompactNumbers=b;break;case "Collapsed":s.Collapsed=b;break;case "GroupByEventType":s.GroupByEventType=b;break;case "ExtraDetails":s.MiniCards=!b;break; }
                }
            }
            s.Width=Math.Max(340,Math.Min(2000,s.Width));s.Height=Math.Max(70,Math.Min(1500,s.Height));
            s.WindowMs=Math.Max(500,Math.Min(2000,s.WindowMs));
            s.IconSize=Math.Max(18,Math.Min(40,s.IconSize));s.TextSize=Math.Max(8,Math.Min(18,s.TextSize));
            return s;
        }
        public void Save() {
            Directory.CreateDirectory(DirectoryPath);
            var lines=new [] { "Log="+Log,"Width="+Width,"Height="+Height,"X="+X,"Y="+Y,"WindowMs="+WindowMs,
                "IconSize="+IconSize,"TextSize="+TextSize,"Locked="+Locked,"AlwaysOnTop="+AlwaysOnTop,"WatchLog="+WatchLog,"ShowBeasts="+ShowBeasts,
                "ShowBeastBlightfall="+ShowBeastBlightfall,"ShowSoulReaper="+ShowSoulReaper,"ShowScythe="+ShowScythe,"ShowOverkill="+ShowOverkill,"CompactNumbers="+CompactNumbers,"Collapsed="+Collapsed,
                "GroupByEventType="+GroupByEventType,"ExtraDetails="+(!MiniCards),"PlayerMode="+PlayerMode,"SelectedPlayerGuid="+SelectedPlayerGuid };
            File.WriteAllLines(Path.Combine(DirectoryPath,"settings.txt"),lines,Encoding.UTF8);
        }
    }
    // Player identities are keyed by GUID, never by display name. Realm suffixes
    // are retained exactly as reported by the combat log.
}
