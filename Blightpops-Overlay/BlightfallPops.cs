// Blightfall Pops desktop overlay: .NET Framework 4.8, Windows Forms, no PowerShell.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BlightfallPopsDesktop {
    internal sealed class Hit { public string Kind, Target; public long Amount; public bool Crit; }
    internal sealed class Entry {
        public bool Beast, DOpen, VOpen, Exploded, BlightfallBefore;
        public bool? Scythe, SoulReaper;
        public int SoulReaperTargets;
        public int Number;
        public long Sequence;
        public DateTime Time;
        public string Segment, Guid;
        public long Dread, Virulent, Corrupted, Life;
        public readonly List<Hit> Hits = new List<Hit>();
    }
    internal sealed class Settings {
        public int Width=500, Height=390, X=int.MinValue, Y=int.MinValue, WindowMs=1200, IconSize=26, TextSize=10;
        public bool Locked=false, AlwaysOnTop=true, ShowBeasts=true, ShowBeastBlightfall=true, ShowSoulReaper=false, CompactNumbers=true, Collapsed=false, GroupByEventType=false, MiniCards=false;
        public string Log="";
        public static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BlightfallPopsDesktop");
        public static Settings Load() {
            var s=new Settings(); var file=Path.Combine(DirectoryPath,"settings.txt");
            if (!File.Exists(file)) return s;
            foreach (var line in File.ReadAllLines(file)) {
                int sep=line.IndexOf('='); if(sep<1)continue;
                var key=line.Substring(0,sep); var value=line.Substring(sep+1); int n; bool b;
                if (key=="Log") s.Log=value;
                else if (int.TryParse(value,out n)) {
                    switch(key) { case "Width": s.Width=n;break;case "Height":s.Height=n;break;case "X":s.X=n;break;case "Y":s.Y=n;break;case "WindowMs":s.WindowMs=n;break;case "IconSize":s.IconSize=n;break;case "TextSize":s.TextSize=n;break; }
                } else if(bool.TryParse(value,out b)) {
                    switch(key) { case "Locked":s.Locked=b;break;case "AlwaysOnTop":s.AlwaysOnTop=b;break;case "ShowBeasts":s.ShowBeasts=b;break;case "ShowBeastBlightfall":s.ShowBeastBlightfall=b;break;case "ShowSoulReaper":s.ShowSoulReaper=b;break;case "CompactNumbers":s.CompactNumbers=b;break;case "Collapsed":s.Collapsed=b;break;case "GroupByEventType":s.GroupByEventType=b;break;case "MiniCards":s.MiniCards=b;break; }
                }
            }
            s.Width=Math.Max(340,Math.Min(2000,s.Width));s.Height=Math.Max(230,Math.Min(1500,s.Height));
            s.WindowMs=Math.Max(500,Math.Min(2000,s.WindowMs));
            s.IconSize=Math.Max(18,Math.Min(40,s.IconSize));s.TextSize=Math.Max(8,Math.Min(18,s.TextSize));
            return s;
        }
        public void Save() {
            Directory.CreateDirectory(DirectoryPath);
            var lines=new [] { "Log="+Log,"Width="+Width,"Height="+Height,"X="+X,"Y="+Y,"WindowMs="+WindowMs,
                "IconSize="+IconSize,"TextSize="+TextSize,"Locked="+Locked,"AlwaysOnTop="+AlwaysOnTop,"ShowBeasts="+ShowBeasts,
                "ShowBeastBlightfall="+ShowBeastBlightfall,"ShowSoulReaper="+ShowSoulReaper,"CompactNumbers="+CompactNumbers,"Collapsed="+Collapsed,
                "GroupByEventType="+GroupByEventType,"MiniCards="+MiniCards };
            File.WriteAllLines(Path.Combine(DirectoryPath,"settings.txt"),lines,Encoding.UTF8);
        }
    }
    internal sealed class Tracker {
        public readonly List<Entry> Entries=new List<Entry>();
        public event Action Changed;
        public string Player="", Guid="", Segment="";
        public int WindowMs=1200;
        public long Position;
        public DateTime LastWrite=DateTime.MinValue;
        public bool SkipFirstLine;
        private string remainder="", encounter="";
        private DateTime lastOwnCombat=DateTime.MinValue;
        private Entry pending, recentBeast;
        private readonly Dictionary<string,Entry> beasts=new Dictionary<string,Entry>();
        private readonly Dictionary<string,int> attempts=new Dictionary<string,int>();
        private readonly Dictionary<string,bool> scythe=new Dictionary<string,bool>();
        // Soul Reaper 1241521 is an enemy aura; keep each caster's targets separate.
        private readonly Dictionary<string,Dictionary<string,DateTime>> soulReaper=new Dictionary<string,Dictionary<string,DateTime>>();
        private int castNumber,beastNumber,pullNumber;
        private long nextSequence;
        private static readonly Regex prefix=new Regex(@"^\s*(\d{1,2}[/.]\d{1,2}[/.]\d{4} \d\d:\d\d:\d\d\.\d+)(?:[+-]\d+)?\s{2,}(.*)$",RegexOptions.Compiled);
        private static readonly string[] logDates={"M/d/yyyy HH:mm:ss.FFFFFFF","d.M.yyyy HH:mm:ss.FFFFFFF"};
        private void Notify(){if(Changed!=null)Changed();}
        public void Reset(long at=0) {
            Entries.Clear();pending=null;recentBeast=null;beasts.Clear();attempts.Clear();scythe.Clear();soulReaper.Clear();
            castNumber=beastNumber=pullNumber=0;nextSequence=0;Guid=Player=Segment=encounter=remainder="";
            lastOwnCombat=DateTime.MinValue;Position=at;SkipFirstLine=at>0;Notify();
        }
        public void Finish() {
            if(pending==null)return;
            pending.Number=++castNumber;Entries.Add(pending);pending=null;Notify();
        }
        public void Tick(string path) {
            if(!File.Exists(path))return;
            var file=new FileInfo(path);
            if(file.Length<Position)Reset();
            if(file.Length>Position) {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    stream.Seek(Position,SeekOrigin.Begin);
                    var buffer=new byte[65536]; int count;
                    while((count=stream.Read(buffer,0,buffer.Length))>0) {
                        Position+=count;
                        remainder+=Encoding.UTF8.GetString(buffer,0,count);
                        int newline;
                        while((newline=remainder.IndexOf('\n'))>=0) {
                            string line=remainder.Substring(0,newline).TrimEnd('\r');
                            remainder=remainder.Substring(newline+1);
                            if(SkipFirstLine)SkipFirstLine=false;else Process(line);
                        }
                        if(remainder.Length>1048576)remainder="";
                    }
                }
            }
            LastWrite=file.LastWriteTime;
            if(pending!=null && (DateTime.Now-file.LastWriteTime).TotalSeconds>2.5)Finish();
        }
        private static List<string> Csv(string text) {
            var result=new List<string>();var field=new StringBuilder();bool quoted=false;
            for(int i=0;i<text.Length;i++) {
                char ch=text[i];
                if(ch=='"') { if(quoted&&i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else quoted=!quoted; }
                else if(ch==','&&!quoted){result.Add(field.ToString());field.Clear();}else field.Append(ch);
            }
            result.Add(field.ToString());return result;
        }
        private static string F(List<string> f,int i){return i<f.Count?f[i]:"";}
        private static bool OwnFlags(string value) {
            long flags;return long.TryParse(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?value.Substring(2):value,
                NumberStyles.HexNumber,CultureInfo.InvariantCulture,out flags) && (flags&1)!=0;
        }
        private void UpdatePull(DateTime time) {
            if(encounter!="")return;
            if(lastOwnCombat==DateTime.MinValue || (time-lastOwnCombat).TotalSeconds>12)Segment="Estimated trash pull "+(++pullNumber);
            lastOwnCombat=time;
        }
        private static long Amount(List<string> f) {long n;return long.TryParse(F(f,f.Count>=41?31:12),out n)?Math.Max(0,n):0;}
        private static bool Crit(List<string> f) {return F(f,f.Count>=41?38:19)=="1";}
        private void Process(string line) {
            var match=prefix.Match(line);if(!match.Success)return;
            DateTime time;
            if(!DateTime.TryParseExact(match.Groups[1].Value,logDates,CultureInfo.InvariantCulture,DateTimeStyles.None,out time))return;
            var f=Csv(match.Groups[2].Value);string kind=F(f,0);
            if(kind=="ENCOUNTER_START") {
                Finish();string key=F(f,1);int n=0;attempts.TryGetValue(key,out n);attempts[key]=++n;
                encounter=key;Segment=F(f,2)+" — attempt "+n;lastOwnCombat=DateTime.MinValue;return;
            }
            if(kind=="ENCOUNTER_END"){Finish();encounter=Segment="";lastOwnCombat=DateTime.MinValue;return;}
            if(f.Count<12)return;
            string source=F(f,1), dest=F(f,5), spell=F(f,9);
            if(kind=="SPELL_SUMMON"&&spell=="434237"&&source.StartsWith("Player-")&&OwnFlags(F(f,3))&&(Guid==""||Guid==source)) {
                if(Guid==""){Guid=source;Player=F(f,2);}UpdatePull(time);
                var beast=new Entry {Beast=true,Number=++beastNumber,Sequence=++nextSequence,Time=time,Segment=Segment,Guid=dest};
                beasts[dest]=beast;recentBeast=beast;Entries.Add(beast);Notify();return;
            }
            if(kind=="SPELL_DAMAGE"&&(spell=="434574"||spell=="434246")) {
                Entry beast=null;
                if(spell=="434574")beasts.TryGetValue(source,out beast);
                else if(source==Guid)beast=recentBeast;
                if(beast!=null&&time>=beast.Time){long amount=Amount(f);
                    if(spell=="434574")beast.Corrupted+=amount;else{beast.Life+=amount;beast.Exploded=true;}
                    beast.Hits.Add(new Hit{Kind=spell=="434574"?"CB":"BiL",Amount=amount,Target=F(f,6),Crit=Crit(f)});Notify();}
            }
            if(encounter=="" && (kind=="SPELL_DAMAGE"||kind=="SPELL_PERIODIC_DAMAGE"||kind=="SWING_DAMAGE"||kind=="RANGE_DAMAGE"||kind=="SPELL_MISSED"||kind=="SWING_MISSED")
                &&OwnFlags(F(f,3))&&source.StartsWith("Player-")&&(dest.StartsWith("Creature-")||dest.StartsWith("Vehicle-")))UpdatePull(time);
            if(spell=="1241077"&&dest.StartsWith("Player-")) {
                if(kind=="SPELL_AURA_APPLIED"||kind=="SPELL_AURA_REFRESH")scythe[dest]=true;
                else if(kind=="SPELL_AURA_REMOVED")scythe[dest]=false;
            }
            if(spell=="1241521"&&(dest.StartsWith("Creature-")||dest.StartsWith("Vehicle-"))) {
                if((kind=="SPELL_AURA_APPLIED"||kind=="SPELL_AURA_REFRESH")&&source.StartsWith("Player-")) {
                    Dictionary<string,DateTime> targets;
                    if(!soulReaper.TryGetValue(source,out targets))soulReaper[source]=targets=new Dictionary<string,DateTime>();
                    targets[dest]=time.AddSeconds(8);
                } else if(kind=="SPELL_AURA_REMOVED") {
                    Dictionary<string,DateTime> targets;
                    if(soulReaper.TryGetValue(source,out targets))targets.Remove(dest);
                    else foreach(var caster in soulReaper.Values)caster.Remove(dest);
                }
            }
            if(kind=="SPELL_CAST_SUCCESS"&&spell=="1271967"&&source.StartsWith("Player-")&&OwnFlags(F(f,3))) {
                if(Guid==""){Guid=source;Player=F(f,2);}if(Guid!=source)return;
                if(recentBeast!=null&&!recentBeast.Exploded&&time>=recentBeast.Time){recentBeast.BlightfallBefore=true;Notify();}
                Finish();UpdatePull(time);
                int affected=0;Dictionary<string,DateTime> enemies;
                if(soulReaper.TryGetValue(source,out enemies)){
                    var expired=new List<string>();
                    foreach(var target in enemies)
                        if(target.Value>time)affected++;else expired.Add(target.Key);
                    foreach(var target in expired)enemies.Remove(target);
                }
                bool value;pending=new Entry {Time=time,Sequence=++nextSequence,Segment=Segment,
                    Scythe=scythe.TryGetValue(source,out value)?(bool?)value:null,
                    SoulReaper=affected>0,SoulReaperTargets=affected};return;
            }
            if(pending==null||source!=Guid||(kind!="SPELL_DAMAGE"&&kind!="SPELL_PERIODIC_DAMAGE"))return;
            double ms=(time-pending.Time).TotalMilliseconds;
            if(ms<0||ms>WindowMs)return;
            if(spell!="1241171"&&spell!="1241167")return;
            long damage=Amount(f);string plague=spell=="1241171"?"DP":"VP";
            if(plague=="DP")pending.Dread+=damage;else pending.Virulent+=damage;
            pending.Hits.Add(new Hit{Kind=plague,Amount=damage,Target=F(f,6),Crit=Crit(f)});
        }
    }
    internal sealed class Overlay : Form {
        private readonly Settings settings=Settings.Load();
        private readonly Tracker tracker=new Tracker();
        private readonly Panel bar=new Panel(), cards=new Panel(), options=new Panel(), grip=new Panel(), scrollTrack=new Panel();
        private readonly Label status=new Label();
        private readonly Timer poll=new Timer();
        private readonly ToolTip toolTip=new ToolTip();
        private bool dirty;
        private Button lockButton,beastButton,collapseButton,miniButton;
        private readonly bool framed;
        private Image artwork;
        private readonly Dictionary<string,Image> spellIcons=new Dictionary<string,Image>();
        private Point? dragStart,resizeStart;
        private Size resizeOrigin;
        private Point dragOrigin;
        private int scrollPixels,totalContentHeight;
        private bool scrollDragging;
        private readonly Color background=Color.FromArgb(18,20,24),rowColor=Color.FromArgb(34,38,44),dim=Color.FromArgb(178,183,183);
        private static readonly Color green=Color.FromArgb(134,218,130),red=Color.FromArgb(230,87,83);
        public Overlay(bool useFrame) {
            framed=useFrame;
            LoadSpellIcons();
            if(framed)try {artwork=Image.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wraith-frame-dark-smooth.png"));}catch{}
            MinimumSize=new Size(340,230);Size=new Size(settings.Width,settings.Height);
            StartPosition=FormStartPosition.CenterScreen;
            if(settings.X!=int.MinValue&&settings.Y!=int.MinValue) {
                var requested=new Rectangle(settings.X,settings.Y,Width,Height);
                foreach(var screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(requested)){StartPosition=FormStartPosition.Manual;Location=requested.Location;break;}
            }
            Text="Blightfall Pops";FormBorderStyle=FormBorderStyle.None;BackColor=background;ForeColor=Color.White;
            try {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            TopMost=settings.AlwaysOnTop;DoubleBuffered=true;KeyPreview=true;
            bar.BackColor=Color.FromArgb(10,11,13);bar.Height=75;bar.Width=ClientSize.Width;Controls.Add(bar);
            bar.MouseDown+=BeginMove;bar.MouseMove+=MoveWindow;bar.MouseUp+=EndMove;
            var title=new Label{Text="BLIGHTFALL POPS",ForeColor=Color.FromArgb(222,224,218),Font=new Font("Georgia",11,FontStyle.Bold),AutoSize=true,Left=11,Top=35};
            bar.Controls.Add(title);title.MouseDown+=BeginMove;title.MouseMove+=MoveWindow;title.MouseUp+=EndMove;
            status.ForeColor=dim;status.Font=new Font("Segoe UI",8);status.Text="Select a WoWCombatLog.txt to begin";
            status.Left=12;status.Top=55;status.Width=430;status.Height=19;bar.Controls.Add(status);
            status.MouseDown+=BeginMove;status.MouseMove+=MoveWindow;status.MouseUp+=EndMove;
            var actions=new FlowLayoutPanel {Dock=DockStyle.Top,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Height=33,BackColor=bar.BackColor,Padding=new Padding(1,2,1,0)};
            bar.Controls.Add(actions);actions.BringToFront();
            DrawToolbarIcon(AddAction(actions,"","Close",red,delegate {Close();}),"close");
            DrawToolbarIcon(AddAction(actions,"","Hide from OBS while minimized",dim,delegate {WindowState=FormWindowState.Minimized;}),"minimize");
            collapseButton=AddAction(actions,"⌃","Show events only",dim,delegate {settings.Collapsed=!settings.Collapsed;ApplyCollapsed();Save();});
            DrawToolbarIcon(collapseButton,"collapse");
            lockButton=AddAction(actions,"","Lock movement and resizing",dim,delegate {settings.Locked=!settings.Locked;ApplyLock();Save();});
            lockButton.Paint+=PaintLockButton;
            DrawToolbarIcon(AddAction(actions,"","Choose combat log",dim,delegate {ChooseLog();}),"folder");
            DrawToolbarIcon(AddAction(actions,"","New session",dim,delegate {ResetSession();}),"refresh");
            DrawToolbarIcon(AddAction(actions,"","Options",dim,delegate {options.Visible=!options.Visible;LayoutWindow();}),"options");
            // RightToLeft flow places the last added control at the far left.
            beastButton=AddAction(actions,"","Show or hide Blood Beast",red,delegate {settings.ShowBeasts=!settings.ShowBeasts;ApplyBeastButton();RefreshCards();Save();});
            Image beastImage;if(spellIcons.TryGetValue("BB",out beastImage)){
                beastButton.BackgroundImage=beastImage;beastButton.BackgroundImageLayout=ImageLayout.Zoom;
            }
            beastButton.Paint+=PaintBeastButton;
            miniButton=AddAction(actions,"","Toggle two-line mini cards",dim,delegate {
                settings.MiniCards=!settings.MiniCards;scrollPixels=0;miniButton.Invalidate();RefreshCards();Save();
            });
            DrawToolbarIcon(miniButton,"mini");
            cards.BackColor=background;cards.Padding=new Padding(7,3,7,6);Controls.Add(cards);
            cards.MouseWheel+=ScrollWheel;
            scrollTrack.BackColor=background;scrollTrack.Paint+=PaintScrollTrack;
            scrollTrack.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){scrollDragging=true;scrollTrack.Capture=true;ScrollFromTrack(e.Y);}};
            scrollTrack.MouseMove+=delegate(object sender,MouseEventArgs e){if(scrollDragging)ScrollFromTrack(e.Y);};
            scrollTrack.MouseUp+=delegate {scrollDragging=false;scrollTrack.Capture=false;};
            scrollTrack.MouseWheel+=ScrollWheel;Controls.Add(scrollTrack);
            options.Height=136;options.BackColor=Color.FromArgb(30,33,37);options.Visible=false;Controls.Add(options);
            SetupOptions();
            grip.Size=new Size(18,18);grip.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;
            grip.BackColor=Color.FromArgb(51,55,58);grip.Cursor=Cursors.SizeNWSE;
            grip.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(dim,1.5f)){e.Graphics.DrawLine(pen,5,15,15,5);e.Graphics.DrawLine(pen,10,15,15,10);}};
            grip.MouseDown+=delegate(object sender,MouseEventArgs e){if(!settings.Locked&&e.Button==MouseButtons.Left){resizeStart=MousePosition;resizeOrigin=Size;grip.Capture=true;}};
            grip.MouseMove+=delegate {if(resizeStart.HasValue&&!settings.Locked){var cursor=MousePosition;Size=new Size(Math.Max(MinimumSize.Width,resizeOrigin.Width+cursor.X-resizeStart.Value.X),Math.Max(MinimumSize.Height,resizeOrigin.Height+cursor.Y-resizeStart.Value.Y));}};
            grip.MouseUp+=delegate {resizeStart=null;grip.Capture=false;Save();};
            Controls.Add(grip);grip.BringToFront();
            Resize+=delegate {LayoutWindow();RefreshCards();};Move+=delegate {if(Visible&&WindowState==FormWindowState.Normal)Save();};
            FormClosing+=delegate {Save();poll.Stop();toolTip.Dispose();if(artwork!=null)artwork.Dispose();foreach(var image in spellIcons.Values)image.Dispose();};
            tracker.WindowMs=settings.WindowMs;tracker.Changed+=delegate {dirty=true;};
            poll.Interval=350;poll.Tick+=delegate {try {if(settings.Log!=""){tracker.Tick(settings.Log);status.Text="Tracking "+(tracker.Player==""?Path.GetFileName(settings.Log):tracker.Player);}}
                catch(Exception ex) {status.Text="Log error: "+ex.Message;}
                if(dirty){dirty=false;RefreshCards();}};
            ApplyCollapsed();ApplyLock();ApplyBeastButton();LayoutWindow();
            Shown+=delegate {if(!File.Exists(settings.Log))ChooseLog();else LoadLog(settings.Log);poll.Start();};
        }
        private Button AddAction(Control parent,string glyph,string hint,Color color,Action click) {
            var button=new Button{Text=glyph,Font=new Font("Segoe UI Symbol",14,FontStyle.Bold),ForeColor=color,BackColor=bar.BackColor,
                Width=31,Height=29,FlatStyle=FlatStyle.Flat,Margin=new Padding(1,0,1,0),TabStop=false};
            button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            toolTip.SetToolTip(button,hint);parent.Controls.Add(button);button.Click+=delegate {click();};return button;
        }
        private void DrawToolbarIcon(Button button,string kind){
            button.Text="";
            button.Paint+=delegate(object sender,PaintEventArgs e){
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var pen=new Pen(kind=="close"?red:kind=="mini"&&settings.MiniCards?green:dim,2.2f)){
                    if(kind=="folder"){
                        e.Graphics.DrawLines(pen,new[]{new Point(5,9),new Point(11,9),new Point(13,11),new Point(25,11),new Point(25,23),new Point(5,23),new Point(5,9)});
                        e.Graphics.DrawLine(pen,6,14,24,14);
                    }else if(kind=="refresh"){
                        e.Graphics.DrawArc(pen,7,6,17,17,35,285);
                        e.Graphics.DrawLines(pen,new[]{new Point(20,5),new Point(24,7),new Point(24,12)});
                    }else if(kind=="options"){
                        var teeth=new PointF[32];
                        for(int i=0;i<8;i++){
                            double angle=i*Math.PI/4;
                            for(int edge=0;edge<4;edge++){
                                double offset=(edge==0?-0.35:edge==1?-0.20:edge==2?0.20:0.35);
                                double radius=(edge==1||edge==2)?11:8;
                                teeth[i*4+edge]=new PointF(15.5f+(float)(radius*Math.Cos(angle+offset)),
                                    14.5f+(float)(radius*Math.Sin(angle+offset)));
                            }
                        }
                        using(var brush=new SolidBrush(dim))e.Graphics.FillPolygon(brush,teeth);
                        using(var hole=new SolidBrush(button.BackColor))e.Graphics.FillEllipse(hole,12,11,7,7);
                    }else if(kind=="mini"){
                        e.Graphics.DrawRectangle(pen,6,7,5,5);
                        e.Graphics.DrawRectangle(pen,6,17,5,5);
                        e.Graphics.DrawLine(pen,14,10,25,10);
                        e.Graphics.DrawLine(pen,14,20,25,20);
                    }else if(kind=="collapse"){
                        if(settings.Collapsed){e.Graphics.DrawLine(pen,8,11,15,18);e.Graphics.DrawLine(pen,15,18,22,11);}
                        else{e.Graphics.DrawLine(pen,8,18,15,11);e.Graphics.DrawLine(pen,15,11,22,18);}
                    }else if(kind=="minimize")e.Graphics.DrawLine(pen,8,20,23,20);
                    else if(kind=="close"){
                        e.Graphics.DrawLine(pen,9,8,22,21);e.Graphics.DrawLine(pen,22,8,9,21);
                    }
                }
            };
        }
        private void PaintLockButton(object sender,PaintEventArgs e){
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color color=settings.Locked?green:dim;
            using(var pen=new Pen(color,2f)){
                e.Graphics.DrawArc(pen,9,4,10,11,180,180);
                e.Graphics.DrawLine(pen,9,9,9,15);e.Graphics.DrawLine(pen,19,9,19,15);
                e.Graphics.DrawRectangle(pen,7,14,14,10);
            }
            using(var brush=new SolidBrush(color))e.Graphics.FillEllipse(brush,13,18,3,3);
        }
        private void ApplyBeastButton(){
            beastButton.FlatAppearance.BorderSize=0;
            toolTip.SetToolTip(beastButton,settings.ShowBeasts?"Hide Blood Beast events":"Show Blood Beast events");
            beastButton.Invalidate();
        }
        private void PaintBeastButton(object sender,PaintEventArgs e){
            if(settings.ShowBeasts)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var shadow=new Pen(Color.FromArgb(235,14,15,17),5f)){
                e.Graphics.DrawEllipse(shadow,6,5,19,19);
                e.Graphics.DrawLine(shadow,8,21,23,8);
            }
            using(var pen=new Pen(red,2.8f)){
                e.Graphics.DrawEllipse(pen,6,5,19,19);
                e.Graphics.DrawLine(pen,8,21,23,8);
            }
        }
        private void SetupOptions() {
            var time=new NumericUpDown{Minimum=500,Maximum=2000,Increment=100,Value=settings.WindowMs,Width=65,Left=12,Top=12};
            var font=new NumericUpDown{Minimum=8,Maximum=18,Value=settings.TextSize,Width=52,Left=137,Top=12};
            var icons=new NumericUpDown{Minimum=18,Maximum=40,Value=settings.IconSize,Width=52,Left=218,Top=12};
            var number=new CheckBox{Text="Short numbers",Checked=settings.CompactNumbers,AutoSize=true,Left=12,Top=62,ForeColor=dim};
            var beastIcon=new CheckBox{Text="Blightfall on Blood Beast",Checked=settings.ShowBeastBlightfall,AutoSize=true,Left=143,Top=62,ForeColor=dim};
            var grouped=new CheckBox{Text="Beasts first, then Blightfall",Checked=settings.GroupByEventType,AutoSize=true,Left=12,Top=85,ForeColor=dim};
            var soulIcon=new CheckBox{Text="Soul Reaper",Checked=settings.ShowSoulReaper,AutoSize=true,Left=223,Top=85,ForeColor=dim};
            var onTop=new CheckBox{Text="Always on top (WoW overlay)",Checked=settings.AlwaysOnTop,AutoSize=true,Left=12,Top=109,ForeColor=dim};
            toolTip.SetToolTip(onTop,"Turn off for a second-screen OBS window capture; keep the overlay open.");
            options.Controls.AddRange(new Control[]{time,font,icons,number,beastIcon,grouped,soulIcon,onTop,
                new Label{Text="Blightfall ms",Left=12,Top=35,Width=115,ForeColor=dim},
                new Label{Text="Text size",Left=137,Top=35,Width=70,ForeColor=dim},
                new Label{Text="Icon size",Left=218,Top=35,Width=70,ForeColor=dim}});
            time.ValueChanged+=delegate {settings.WindowMs=(int)time.Value;tracker.WindowMs=settings.WindowMs;Save();};
            font.ValueChanged+=delegate {settings.TextSize=(int)font.Value;RefreshCards();Save();};
            icons.ValueChanged+=delegate {settings.IconSize=(int)icons.Value;RefreshCards();Save();};
            number.CheckedChanged+=delegate {settings.CompactNumbers=number.Checked;RefreshCards();Save();};
            beastIcon.CheckedChanged+=delegate {settings.ShowBeastBlightfall=beastIcon.Checked;RefreshCards();Save();};
            grouped.CheckedChanged+=delegate {settings.GroupByEventType=grouped.Checked;scrollPixels=0;RefreshCards();Save();};
            soulIcon.CheckedChanged+=delegate {settings.ShowSoulReaper=soulIcon.Checked;RefreshCards();Save();};
            onTop.CheckedChanged+=delegate {settings.AlwaysOnTop=onTop.Checked;TopMost=settings.AlwaysOnTop;Save();};
        }
        private void BeginMove(object sender,MouseEventArgs e){if(!settings.Locked&&e.Button==MouseButtons.Left){dragStart=MousePosition;dragOrigin=Location;}}
        private void MoveWindow(object sender,MouseEventArgs e){if(dragStart.HasValue&&!settings.Locked){var p=MousePosition;Location=new Point(dragOrigin.X+p.X-dragStart.Value.X,dragOrigin.Y+p.Y-dragStart.Value.Y);}}
        private void EndMove(object sender,MouseEventArgs e){dragStart=null;Save();}
        private void ApplyLock(){grip.Visible=!settings.Locked;lockButton.Invalidate();}
        private void ApplyCollapsed(){bar.Height=settings.Collapsed?10:75;
            foreach(Control child in bar.Controls)child.Visible=!settings.Collapsed;
            bar.Cursor=settings.Collapsed?Cursors.Hand:Cursors.Default;
            collapseButton.Invalidate();
            bar.Click-=ExpandFromStrip;bar.Click+=ExpandFromStrip;
            LayoutWindow();}
        private void ExpandFromStrip(object sender,EventArgs e){if(settings.Collapsed){settings.Collapsed=false;ApplyCollapsed();Save();}}
        private void LayoutWindow(){
            bar.Width=ClientSize.Width;
            options.Bounds=new Rectangle(0,Math.Max(bar.Bottom,ClientSize.Height-options.Height),ClientSize.Width,options.Height);
            int bottom=options.Visible?options.Top:ClientSize.Height;
            cards.Bounds=new Rectangle(0,bar.Bottom,Math.Max(0,ClientSize.Width-12),Math.Max(0,bottom-bar.Bottom));
            scrollTrack.Bounds=new Rectangle(Math.Max(0,ClientSize.Width-11),cards.Top,9,cards.Height);
            scrollTrack.Visible=totalContentHeight>cards.ClientSize.Height;
            grip.Location=new Point(ClientSize.Width-19,ClientSize.Height-19);grip.BringToFront();
            if(options.Visible)options.BringToFront();scrollTrack.BringToFront();bar.BringToFront();
            ScrollTo(scrollPixels);Invalidate();
        }
        private void ScrollWheel(object sender,MouseEventArgs e){ScrollTo(scrollPixels-Math.Sign(e.Delta)*Math.Max(28,settings.TextSize*3));}
        private void HookWheel(Control control){control.MouseWheel+=ScrollWheel;foreach(Control child in control.Controls)HookWheel(child);}
        private void ScrollTo(int pixels){
            scrollPixels=Math.Max(0,Math.Min(pixels,Math.Max(0,totalContentHeight-cards.ClientSize.Height)));
            foreach(Control control in cards.Controls)if(control.Tag is int)control.Top=(int)control.Tag-scrollPixels;
            scrollTrack.Visible=totalContentHeight>cards.ClientSize.Height;
            scrollTrack.Invalidate();
        }
        private void ScrollFromTrack(int y){
            if(scrollTrack.Height<=0)return;
            ScrollTo((int)((double)Math.Max(0,y)/scrollTrack.Height*Math.Max(0,totalContentHeight-cards.ClientSize.Height)));
        }
        private void PaintScrollTrack(object sender,PaintEventArgs e){
            e.Graphics.Clear(background);
            if(totalContentHeight<=cards.ClientSize.Height||scrollTrack.Height<1)return;
            int thumbHeight=Math.Max(28,(int)((double)cards.ClientSize.Height/totalContentHeight*scrollTrack.Height));
            thumbHeight=Math.Min(scrollTrack.Height,thumbHeight);
            int range=Math.Max(1,totalContentHeight-cards.ClientSize.Height);
            int top=(int)((double)scrollPixels/range*(scrollTrack.Height-thumbHeight));
            using(var brush=new SolidBrush(Color.FromArgb(87,91,91)))
                e.Graphics.FillRectangle(brush,2,top,5,thumbHeight);
        }
        private void Save(){if(WindowState!=FormWindowState.Normal)return;
            settings.Width=Width;settings.Height=Height;settings.X=Left;settings.Y=Top;
            try{settings.Save();}catch{}}
        private void ChooseLog(){using(var dialog=new OpenFileDialog{Title="Select the active WoWCombatLog.txt",Filter="Combat logs (*.txt)|*.txt|All files (*.*)|*.*"}){
            if(File.Exists(settings.Log))dialog.FileName=settings.Log;
            if(dialog.ShowDialog(this)==DialogResult.OK){settings.Log=dialog.FileName;LoadLog(settings.Log);Save();}}}
        private void LoadLog(string file){try{tracker.Reset();var size=new FileInfo(file).Length;
            tracker.Position=Math.Max(0,size-8388608L);tracker.SkipFirstLine=tracker.Position>0;
            tracker.Tick(file);dirty=false;RefreshCards();status.Text="Watching "+Path.GetFileName(file);}
            catch(Exception ex){status.Text="Cannot open log: "+ex.Message;}}
        private void ResetSession(){try{tracker.Reset(File.Exists(settings.Log)?new FileInfo(settings.Log).Length:0);tracker.SkipFirstLine=false;
            dirty=false;RefreshCards();status.Text="New session — waiting for combat";}catch(Exception ex){status.Text=ex.Message;}}
        private string Format(long value){if(value>=1000000)return (value/1000000.0).ToString("0.##",CultureInfo.CurrentCulture)+"mil";
            if(settings.CompactNumbers&&value>=1000)return (value/1000.0).ToString("0.#",CultureInfo.CurrentCulture)+"K";
            return value.ToString("N0",CultureInfo.CurrentCulture);}
        private Label Label(string text,Color color,int left,int top,int width,bool bold=false){return new Label{Text=text,ForeColor=color,Font=new Font("Segoe UI",settings.TextSize,bold?FontStyle.Bold:FontStyle.Regular),
            Left=left,Top=top,Width=width,Height=25,AutoEllipsis=true,BackColor=Color.Transparent};}
        private void LoadSpellIcons(){
            var files=new Dictionary<string,string>{
                {"BF","inv_nullstone_shadow.jpg"},{"DP","inv12_ability_deathknight_empowereddreadplague.jpg"},
                {"VP","ability_creature_disease_02.jpg"},{"SC","inv_polearm_2h_mawnecromancerboss_d_01_darkblue.jpg"},
                {"SR","ability_deathknight_soulreaper.jpg"},
                {"BB","achievement_nazmir_boss_bloodofghuun.jpg"},{"CB","inv_artifact_bloodoftheassassinated.jpg"},
                {"BiL","ability_ironmaidens_corruptedblood.jpg"}};
            foreach(var item in files)try{
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BlightfallPops.Icons."+item.Value)){
                    if(stream!=null)using(var decoded=Image.FromStream(stream))spellIcons[item.Key]=new Bitmap(decoded);
                }
            }catch{} // A damaged icon should not prevent combat log tracking.
            // A legible fallback remains available if an embedded resource is damaged.
            if(!spellIcons.ContainsKey("SR"))using(var badge=new Bitmap(64,64)){
                using(var g=Graphics.FromImage(badge)){
                    g.Clear(Color.FromArgb(40,23,49));
                    using(var brush=new SolidBrush(Color.FromArgb(177,139,198)))
                        g.FillEllipse(brush,8,8,48,48);
                    using(var brush=new SolidBrush(Color.FromArgb(29,17,35)))
                        g.FillEllipse(brush,13,13,38,38);
                    using(var font=new Font("Segoe UI",17,FontStyle.Bold))
                        TextRenderer.DrawText(g,"SR",font,new Rectangle(0,0,64,64),Color.FromArgb(225,204,235),
                            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                }
                spellIcons["SR"]=new Bitmap(badge);
            }
        }
        private static string SoulReaperTip(Entry entry){
            return entry.SoulReaperTargets>0
                ?"Soul Reaper: on "+entry.SoulReaperTargets+" enemy target"+(entry.SoulReaperTargets==1?"":"s")+" at Blightfall cast"
                :"Soul Reaper: no tracked enemy debuff at Blightfall cast";
        }
        private PictureBox SpellIcon(string key,int x,int y,int size,string tip=null,bool? active=null){
            Image img;spellIcons.TryGetValue(key,out img);
            var box=new PictureBox{Left=x,Top=y,Width=size,Height=size,Image=img,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(21,23,27)};
            if(tip!=null){box.Paint+=delegate(object sender,PaintEventArgs e){
                if(active.HasValue&&!active.Value){using(var pen=new Pen(red,Math.Max(2,size/9))){e.Graphics.DrawLine(pen,2,2,size-3,size-3);e.Graphics.DrawLine(pen,size-3,2,2,size-3);}}
                if(!active.HasValue)using(var font=new Font("Segoe UI",Math.Max(11,size/2),FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics,"?",font,new Rectangle(0,0,size,size),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            };}
            if(tip!=null)toolTip.SetToolTip(box,tip);
            return box;
        }
        private int AddHitRows(Panel card,Entry entry,string first,int y){
            foreach(var hit in entry.Hits){
                if(!(hit.Kind==first?entry.DOpen:entry.VOpen))continue;
                int hitSize=Math.Min(32,Math.Max(23,settings.IconSize));
                var hitRow=new Panel{Left=8,Top=y,Width=card.Width-16,Height=hitSize+6,BackColor=Color.FromArgb(23,28,33)};
                hitRow.Controls.Add(SpellIcon(hit.Kind,13,3,hitSize));
                string amount=Format(hit.Amount), target=string.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
                string result=hit.Crit?"Crit":"Hit";
                int amountX=19+hitSize,amountWidth,critWidth,targetWidth,lineHeight;
                using(var regular=new Font("Segoe UI",settings.TextSize))
                using(var bold=new Font("Segoe UI",settings.TextSize,FontStyle.Bold)){
                    amountWidth=TextRenderer.MeasureText(amount,regular).Width+6;
                    critWidth=TextRenderer.MeasureText(result,bold).Width+6;
                    lineHeight=TextRenderer.MeasureText("Ag",regular).Height;
                    int critX=amountX+amountWidth+6;
                    int targetX=critX+critWidth+8;
                    targetWidth=Math.Max(20,hitRow.Width-targetX-8);
                    bool wrap=TextRenderer.MeasureText(target,regular).Width>targetWidth;
                    hitRow.Controls.Add(Label(amount,hit.Crit?Color.Gold:dim,amountX,5,amountWidth));
                    hitRow.Controls.Add(Label(result,hit.Crit?Color.Gold:dim,critX,5,critWidth,true));
                    Label targetLabel;
                    if(wrap){
                        // Leave damage and the full Crit/Hit label readable on the first line.
                        int wrapX=amountX,wrapWidth=Math.Max(25,hitRow.Width-wrapX-8);
                        int wrapHeight=Math.Max(lineHeight,TextRenderer.MeasureText(target,regular,
                            new Size(wrapWidth,int.MaxValue),TextFormatFlags.WordBreak).Height);
                        targetLabel=Label(target,dim,wrapX,hitSize+6,wrapWidth);
                        targetLabel.AutoEllipsis=false;targetLabel.Height=wrapHeight+3;
                        hitRow.Height=hitSize+9+wrapHeight+3;
                    }else targetLabel=Label(target,dim,targetX,5,targetWidth);
                    hitRow.Controls.Add(targetLabel);
                    toolTip.SetToolTip(targetLabel,target);
                }
                if(hit.Crit)hitRow.Controls.Add(new Panel{Left=1,Top=3,Width=2,Height=hitRow.Height-6,BackColor=Color.Gold});
                card.Controls.Add(hitRow);y+=hitRow.Height+2;
            }
            return y;
        }
        private void RenderMiniCard(Panel card,Entry entry){
            const int baseHeight=55;
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=baseHeight,BackColor=entry.Beast?red:green});
            string title=entry.Beast?"Beast #"+entry.Number:"#"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            string total=Format(entry.Beast?entry.Corrupted+entry.Life:entry.Dread+entry.Virulent);
            string hits=entry.Hits.Count+" hits";
            int iconSize=Math.Max(18,Math.Min(23,settings.IconSize));
            // Align the first icon to the title and the second icon to the hit count above it.
            int firstX=55;
            int statusCount=entry.Beast?(settings.ShowBeastBlightfall?1:0):(settings.ShowSoulReaper?2:1);
            int statusX=card.Width-9-statusCount*iconSize-(statusCount-1)*3;
            int totalWidth,hitsWidth=0;
            using(var font=new Font("Segoe UI",settings.TextSize,FontStyle.Bold))totalWidth=TextRenderer.MeasureText(total,font).Width;
            if(!entry.Beast)using(var font=new Font("Segoe UI",settings.TextSize))hitsWidth=TextRenderer.MeasureText(hits,font).Width;
            int totalX=card.Width-9-totalWidth;
            int secondX=Math.Max(165,Math.Min(Math.Max(168,card.Width/2)+iconSize+7,
                Math.Min(statusX-iconSize-53,totalX-hitsWidth-3)));
            int countX=secondX-3; // Label text has a small inset; its glyph matches the icon edge.
            var eventIcon=SpellIcon(entry.Beast?"BB":"BF",9,9,36);
            if(!string.IsNullOrEmpty(entry.Segment))toolTip.SetToolTip(eventIcon,entry.Segment);
            card.Controls.Add(eventIcon);
            var titleLabel=Label(title,Color.White,52,1,Math.Max(30,(entry.Beast?totalX:countX)-57),true);
            toolTip.SetToolTip(titleLabel,title+(entry.Segment==""?"":" — "+entry.Segment));
            card.Controls.Add(titleLabel);
            card.Controls.Add(Label(total,green,totalX,1,totalWidth+4,true));
            if(!entry.Beast)card.Controls.Add(Label(hits,dim,countX,3,hitsWidth+3));
            string first=entry.Beast?"CB":"DP",second=entry.Beast?"BiL":"VP";
            long firstValue=entry.Beast?entry.Corrupted:entry.Dread,secondValue=entry.Beast?entry.Life:entry.Virulent;
            bool showExtra=statusCount>0;
            var firstIcon=SpellIcon(first,firstX,29,iconSize);
            var secondIcon=SpellIcon(second,secondX,29,iconSize);
            var firstLabel=Label(Format(firstValue),!entry.Beast&&entry.Hits.Exists(h=>h.Kind=="DP"&&h.Crit)?Color.Gold:dim,
                firstX+iconSize+4,28,Math.Max(16,secondX-firstX-iconSize-8));
            var secondLabel=Label(Format(secondValue),dim,secondX+iconSize+4,28,
                Math.Max(16,(showExtra?statusX:card.Width-7)-secondX-iconSize-8));
            card.Controls.Add(firstIcon);card.Controls.Add(secondIcon);card.Controls.Add(firstLabel);card.Controls.Add(secondLabel);
            string firstTip=first+" "+Format(firstValue)+" — click for hits";
            string secondTip=second+" "+Format(secondValue)+" — click for hits";
            toolTip.SetToolTip(firstIcon,firstTip);toolTip.SetToolTip(firstLabel,firstTip);
            toolTip.SetToolTip(secondIcon,secondTip);toolTip.SetToolTip(secondLabel,secondTip);
            firstIcon.Cursor=firstLabel.Cursor=secondIcon.Cursor=secondLabel.Cursor=Cursors.Hand;
            EventHandler toggleFirst=delegate {entry.DOpen=!entry.DOpen;RefreshCards();};
            EventHandler toggleSecond=delegate {entry.VOpen=!entry.VOpen;RefreshCards();};
            firstIcon.Click+=toggleFirst;firstLabel.Click+=toggleFirst;
            secondIcon.Click+=toggleSecond;secondLabel.Click+=toggleSecond;
            if(showExtra){
                bool? active=entry.Beast?(entry.Exploded?(bool?)entry.BlightfallBefore:null):entry.Scythe;
                string tip=entry.Beast?(entry.Exploded?(entry.BlightfallBefore?"Blightfall before Blood Is Life":"No Blightfall before Blood Is Life"):"Waiting for Blood Is Life"):
                    "Festering Scythe: "+(entry.Scythe.HasValue?(entry.Scythe.Value?"active":"inactive"):"unknown");
                if(!entry.Beast&&settings.ShowSoulReaper)
                    card.Controls.Add(SpellIcon("SR",statusX,29,iconSize,SoulReaperTip(entry),entry.SoulReaper));
                card.Controls.Add(SpellIcon(entry.Beast?"BF":"SC",statusX+(statusCount-1)*(iconSize+3),29,iconSize,tip,active));
            }
            int endY=AddHitRows(card,entry,first,baseHeight+2);
            card.Height=Math.Max(baseHeight,endY+1);card.Controls[0].Height=card.Height;
        }
        private void RefreshCards(){if(cards.IsDisposed)return;int oldScroll=scrollPixels;
            cards.SuspendLayout();
            var oldCards=new List<Control>();foreach(Control existing in cards.Controls)oldCards.Add(existing);
            cards.Controls.Clear();foreach(Control existing in oldCards)existing.Dispose();
            int y=3;
            var ordered=new List<Entry>(tracker.Entries);
            ordered.Sort(delegate(Entry a,Entry b){
                if(settings.GroupByEventType&&a.Beast!=b.Beast)return a.Beast?-1:1;
                int byTime=a.Time.CompareTo(b.Time);
                return byTime!=0?byTime:a.Sequence.CompareTo(b.Sequence);
            });
            foreach(var entry in ordered){if(entry.Beast&&!settings.ShowBeasts)continue;
                var card=new Panel{Left=3,Top=y,Width=Math.Max(260,cards.ClientSize.Width-7),BackColor=rowColor,Tag=y};
                if(settings.MiniCards){
                    RenderMiniCard(card,entry);
                    HookWheel(card);cards.Controls.Add(card);y+=card.Height+4;
                    continue;
                }
                card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=90,BackColor=entry.Beast?red:green});
                bool compact=card.Width<380||settings.TextSize>14;
                string head=entry.Beast?(compact?"Beast #":"Blood Beast #")+entry.Number:("#"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss"));
                long total=entry.Beast?entry.Corrupted+entry.Life:entry.Dread+entry.Virulent;
                // The event art fills the left side of the header without adding a separate row.
                card.Controls.Add(SpellIcon(entry.Beast?"BB":"BF",9,14,55));
                const int contentLeft=76; // Shared starting edge, with space after the portrait.
                string totalText=Format(total);
                string countText=entry.Hits.Count+" hits";
                int totalWidth,countWidth=0;
                using(var totalFont=new Font("Segoe UI",settings.TextSize,FontStyle.Bold))
                    totalWidth=TextRenderer.MeasureText(totalText,totalFont).Width;
                if(!entry.Beast)using(var countFont=new Font("Segoe UI",settings.TextSize))
                    countWidth=TextRenderer.MeasureText(countText,countFont).Width;
                int totalX=card.Width-12-totalWidth;
                int countX=totalX-countWidth-9;
                int titleEnd=entry.Beast?totalX:countX;
                card.Controls.Add(Label(head,Color.White,contentLeft,3,Math.Max(30,titleEnd-contentLeft-6),true));
                card.Controls.Add(Label(totalText,green,totalX,3,totalWidth+4,true));
                if(!entry.Beast)card.Controls.Add(Label(countText,dim,countX,5,countWidth+3));
                card.Controls.Add(Label(entry.Segment??"",dim,contentLeft,27,card.Width-contentLeft-11));
                var first=entry.Beast?"CB":"DP";var second=entry.Beast?"BiL":"VP";
                var firstValue=entry.Beast?entry.Corrupted:entry.Dread;var secondValue=entry.Beast?entry.Life:entry.Virulent;
                // WinForms labels inset their glyphs slightly; offset the icon to the visible text edge.
                int iconSize=settings.IconSize,firstX=contentLeft+5,secondX=Math.Max(168,card.Width/2),detailTop=58;
                int secondY=detailTop;
                int extraSize=iconSize;
                int statusCount=entry.Beast?(settings.ShowBeastBlightfall?1:0):(settings.ShowSoulReaper?2:1);
                bool showExtra=statusCount>0;
                bool extraRow=showExtra&&(card.Width<(statusCount==2?445:390)||(iconSize>32&&card.Width<425)||settings.TextSize>15);
                int statusX=card.Width-12-statusCount*extraSize-(statusCount-1)*4;
                var firstIcon=SpellIcon(first,firstX,detailTop,iconSize);
                var secondIcon=SpellIcon(second,secondX,secondY,iconSize);
                card.Controls.Add(firstIcon);card.Controls.Add(secondIcon);
                var firstLabel=Label((entry.DOpen?"▾ ":"▸ ")+first+" "+Format(firstValue),
                    !entry.Beast&&entry.Hits.Exists(h=>h.Kind=="DP"&&h.Crit)?Color.Gold:dim,firstX+iconSize+5,detailTop+2,
                    Math.Max(18,secondX-firstX-iconSize-10));
                var secondLabel=Label((entry.VOpen?"▾ ":"▸ ")+second+" "+Format(secondValue),dim,secondX+iconSize+5,secondY+2,
                    Math.Max(44,(showExtra&&!extraRow?statusX:card.Width-8)-secondX-iconSize-8));
                firstLabel.Cursor=secondLabel.Cursor=Cursors.Hand;card.Controls.Add(firstLabel);card.Controls.Add(secondLabel);
                toolTip.SetToolTip(firstLabel,firstLabel.Text);toolTip.SetToolTip(secondLabel,secondLabel.Text);
                EventHandler toggleFirst=delegate {entry.DOpen=!entry.DOpen;RefreshCards();};
                EventHandler toggleSecond=delegate {entry.VOpen=!entry.VOpen;RefreshCards();};
                firstLabel.Click+=toggleFirst;firstIcon.Cursor=Cursors.Hand;firstIcon.Click+=toggleFirst;
                secondLabel.Click+=toggleSecond;secondIcon.Cursor=Cursors.Hand;secondIcon.Click+=toggleSecond;
                if(showExtra){
                    string tip=entry.Beast?(entry.Exploded?(entry.BlightfallBefore?"Blightfall before Blood Is Life":"No Blightfall before Blood Is Life"):"Waiting for Blood Is Life"):
                        "Festering Scythe: "+(entry.Scythe.HasValue?(entry.Scythe.Value?"active":"inactive"):"unknown");
                    bool? active=entry.Beast?(entry.Exploded?(bool?)entry.BlightfallBefore:null):entry.Scythe;
                    int statusY=secondY+(extraRow?iconSize+6:0);
                    if(!entry.Beast&&settings.ShowSoulReaper)
                        card.Controls.Add(SpellIcon("SR",statusX,statusY,extraSize,SoulReaperTip(entry),entry.SoulReaper));
                    card.Controls.Add(SpellIcon(entry.Beast?"BF":"SC",statusX+(statusCount-1)*(extraSize+4),statusY,extraSize,tip,active));
                }
                int detailY=secondY+iconSize+(extraRow?iconSize+6:0)+5;
                detailY=AddHitRows(card,entry,first,detailY);
                card.Height=Math.Max(90,detailY+2);card.Controls[0].Height=card.Height;
                HookWheel(card);cards.Controls.Add(card);y+=card.Height+5;
            }
            totalContentHeight=y;cards.ResumeLayout();ScrollTo(oldScroll);
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);
            if(framed&&artwork!=null&&!settings.Collapsed){using(var pen=new Pen(Color.FromArgb(75,107,92),2))e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);}
        }
    }
    internal static class Program {
        [STAThread] private static void Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool framed=args.Length>0&&args[0]=="--framed";
            Application.Run(new Overlay(framed));
        }
    }
}
