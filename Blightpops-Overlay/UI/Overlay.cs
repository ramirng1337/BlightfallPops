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
    // Main window and user controls.
    internal sealed partial class Overlay : Form {
        private readonly Settings settings=Settings.Load();
        private readonly Tracker tracker=new Tracker();
        private readonly Dictionary<string,CustomEffect> customAuraInfo=new Dictionary<string,CustomEffect>(StringComparer.Ordinal);
        private readonly Dictionary<string,bool> playerExpanded=new Dictionary<string,bool>(StringComparer.Ordinal);
        private readonly Dictionary<string,Panel> playerHeaders=new Dictionary<string,Panel>(StringComparer.Ordinal);
        private readonly Panel bar=new Panel(), cards=new BufferedPanel(), options=new Panel(), grip=new Panel(), scrollTrack=new Panel();
        private readonly Label status=new Label();
        private readonly Timer poll=new Timer();
        private readonly ToolTip toolTip=new ToolTip();
        private bool dirty,refreshingCards,scanningFullLog;
        private long fullScanTarget;
        private Button scanButton;
        private Button lockButton,beastButton,collapseButton,miniButton,updateButton,logButton;
        private bool checkingUpdate,installingUpdate;
        private DateTime lastLogGrowthUtc=DateTime.MinValue;
        private string logButtonState="";
        private readonly bool framed;
        private Image artwork;
        private readonly Dictionary<string,Image> spellIcons=new Dictionary<string,Image>();
        private Point? dragStart,resizeStart;
        private Size resizeOrigin,resizeCandidate;
        private ResizeOutline resizeOutline;
        private readonly List<Entry> resizeSamples=new List<Entry>();
        private Point dragOrigin;
        private int scrollPixels,totalContentHeight;
        private readonly Timer smoothScrollTimer=new Timer();
        private int smoothScrollTarget;
        private bool smoothScrollTick;
        private bool scrollDragging;
        private int compactHeightBeforeOptions;
        private bool ShowMiniCards {get{return settings.MiniCards;}}
        private readonly Color background=Color.FromArgb(18,20,24),rowColor=Color.FromArgb(34,38,44),dim=Color.FromArgb(178,183,183);
        private static readonly Color green=Color.FromArgb(134,218,130),red=Color.FromArgb(230,87,83);
        private const int NoActivateStyle=0x08000000,AppWindowStyle=0x00040000;
        protected override CreateParams CreateParams {
            get {
                var parameters=base.CreateParams;
                parameters.ExStyle|=NoActivateStyle|AppWindowStyle;
                return parameters;
            }
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override void WndProc(ref Message message){
            // Keep the click, but leave keyboard focus with WoW by default.
            if(message.Msg==0x0021){message.Result=(IntPtr)3;return;}
            base.WndProc(ref message);
        }
        public Overlay(bool useFrame) {
            framed=useFrame;
            LoadSpellIcons();
            smoothScrollTimer.Interval=16;
            smoothScrollTimer.Tick+=delegate {
                int remaining=smoothScrollTarget-scrollPixels;
                if(Math.Abs(remaining)<=1){
                    smoothScrollTick=true;
                    try{ScrollTo(smoothScrollTarget);}finally{smoothScrollTick=false;}
                    smoothScrollTimer.Stop();return;
                }
                int step=(int)Math.Round(remaining*0.28);
                if(step==0)step=Math.Sign(remaining);
                smoothScrollTick=true;
                try{ScrollTo(scrollPixels+step);}finally{smoothScrollTick=false;}
            };
            if(framed)try {artwork=Image.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wraith-frame-dark-smooth.png"));}catch{}
            MinimumSize=new Size(340,settings.Collapsed?70:136);Size=new Size(settings.Width,settings.Height);
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
            scanButton=AddAction(actions,"","Scan entire log — show all Blightfall and Blood Beast events",dim,delegate {ScanEntireLog();});
            DrawToolbarIcon(scanButton,"scan");
            DrawToolbarIcon(AddAction(actions,"","New session",dim,delegate {ResetSession();}),"refresh");
            logButton=AddAction(actions,"","Pause overlay log reading",dim,delegate {
                settings.WatchLog=!settings.WatchLog;
                if(!settings.WatchLog){if(!scanningFullLog)tracker.Finish();status.Text="Overlay log reading paused";}
                else status.Text="Watching "+Path.GetFileName(settings.Log);
                UpdateLogButton();Save();
            });
            logButton.Paint+=PaintLogButton;
            DrawToolbarIcon(AddAction(actions,"","Options",dim,delegate {ToggleOptions();}),"options");
            // Dedicated Compare shortcut in the main toolbar, beside the player filter.
            var compareButton=AddAction(actions,"","Compare two players",dim,delegate {ShowCompareWindow();});
            DrawToolbarIcon(compareButton,"compare");
            // RightToLeft flow places the last added control at the far left.
            miniButton=AddAction(actions,"","Extra details: show or hide pull names and larger events",dim,delegate {
                settings.MiniCards=!settings.MiniCards;scrollPixels=0;miniButton.Invalidate();RefreshCards();Save();
            });
            DrawToolbarIcon(miniButton,"details");
            cards.BackColor=background;cards.Padding=new Padding(7,3,7,6);Controls.Add(cards);
            cards.MouseWheel+=ScrollWheel;
            scrollTrack.BackColor=background;scrollTrack.Paint+=PaintScrollTrack;
            scrollTrack.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){scrollDragging=true;scrollTrack.Capture=true;ScrollFromTrack(e.Y);}};
            scrollTrack.MouseMove+=delegate(object sender,MouseEventArgs e){if(scrollDragging)ScrollFromTrack(e.Y);};
            scrollTrack.MouseUp+=delegate {scrollDragging=false;scrollTrack.Capture=false;};
            scrollTrack.MouseWheel+=ScrollWheel;Controls.Add(scrollTrack);
            options.Height=248;options.BackColor=Color.FromArgb(30,33,37);options.Visible=false;Controls.Add(options);
            SetupOptions();
            grip.Size=new Size(18,18);grip.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;
            grip.BackColor=Color.FromArgb(51,55,58);grip.Cursor=Cursors.SizeNWSE;
            grip.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(dim,1.5f)){e.Graphics.DrawLine(pen,5,15,15,5);e.Graphics.DrawLine(pen,10,15,15,10);}};
            grip.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)BeginResizePreview();};
            grip.MouseMove+=delegate {if(resizeStart.HasValue){
                var cursor=MousePosition;
                UpdateResizePreview(new Size(resizeOrigin.Width+cursor.X-resizeStart.Value.X,
                    resizeOrigin.Height+cursor.Y-resizeStart.Value.Y));
            }};
            grip.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)EndResizePreview(true);};
            grip.MouseCaptureChanged+=delegate {if(resizeStart.HasValue&&!grip.Capture)EndResizePreview(false);};
            Controls.Add(grip);grip.BringToFront();
            Resize+=delegate {
                bool wasAtBottom=scrollPixels>=Math.Max(0,totalContentHeight-cards.ClientSize.Height)-2;
                LayoutWindow();RefreshCards();miniButton.Invalidate();
                if(wasAtBottom||ShowMiniCards&&cards.ClientSize.Height<=61)
                    ScrollTo(Math.Max(0,totalContentHeight-cards.ClientSize.Height));
            };Move+=delegate {if(Visible&&WindowState==FormWindowState.Normal)Save();};
            FormClosing+=delegate {EndResizePreview(false);Save();poll.Stop();toolTip.Dispose();if(artwork!=null)artwork.Dispose();foreach(var image in spellIcons.Values)image.Dispose();foreach(var font in labelFonts.Values)font.Dispose();};
            try{foreach(var custom in ReadCustomCards())if((custom.SpellId=="1271967"||custom.SpellId=="1271974"))foreach(var effect in custom.Effects)
                if(!String.IsNullOrWhiteSpace(effect.SpellId)){if(effect.Type=="Damage")tracker.WatchedDamage[effect.SpellId.Trim()]=effect.Name;else {tracker.WatchedAuras[effect.SpellId.Trim()]=effect.Type;customAuraInfo[effect.SpellId.Trim()]=effect;}}}catch{}
            tracker.WindowMs=BlightfallWindowMs(settings.WindowMs);
            ConfigureCustomEvents(tracker);
            tracker.TrackAllPlayers=true;tracker.Changed+=delegate {dirty=true;};
            poll.Interval=350;poll.Tick+=delegate {if(resizeStart.HasValue)return;
                if(scanningFullLog){ContinueFullLogScan();UpdateLogButton();return;}
                try {if(settings.WatchLog&&settings.Log!=""){
                    long before=tracker.Position;
                    tracker.Tick(settings.Log);
                    if(tracker.Position>before)lastLogGrowthUtc=DateTime.UtcNow;
                    status.Text="Tracking "+(tracker.Player==""?Path.GetFileName(settings.Log):tracker.Player);
                }}
                catch(Exception ex) {status.Text="Log error: "+ex.Message;}
                UpdateLogButton();
                if(dirty){dirty=false;RefreshCards(true);}};
            ApplyCollapsed();ApplyLock();ApplyBeastButton();UpdateLogButton();LayoutWindow();
            Shown+=delegate {if(!File.Exists(settings.Log))ChooseLog();else LoadLog(settings.Log);poll.Start();CheckForUpdates(true);};
        }
        // Filter only the presentation; retain all players in the tracker so a mode switch
        // never requires replaying or losing the current combat-log session.
        private bool PlayerVisible(Entry entry){
            if(settings.PlayerMode=="All Players")return true;
            if(settings.PlayerMode=="Specific Player")return entry.PlayerGuid==settings.SelectedPlayerGuid;
            return entry.PlayerGuid==tracker.Guid;
        }
        // Comparison uses a custom dark scrollbar instead of WinForms AutoScroll.
        private void DrawToolbarIcon(Button button,string kind){
            button.Text="";
            button.Paint+=delegate(object sender,PaintEventArgs e){
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var pen=new Pen(kind=="close"?red:kind=="details"&&!ShowMiniCards?green:dim,2.2f)){
                    if(kind=="compare"){
                        // Split View: two equal panels with a central divider.
                        e.Graphics.DrawRectangle(pen,6,7,19,17);
                        e.Graphics.DrawLine(pen,15,7,15,24);
                    }else if(kind=="folder"){
                        e.Graphics.DrawLines(pen,new[]{new Point(5,9),new Point(11,9),new Point(13,11),new Point(25,11),new Point(25,23),new Point(5,23),new Point(5,9)});
                        e.Graphics.DrawLine(pen,6,14,24,14);
                    }else if(kind=="scan"){
                        e.Graphics.DrawLines(pen,new[]{new Point(6,5),new Point(18,5),new Point(22,9),new Point(22,14)});
                        e.Graphics.DrawLines(pen,new[]{new Point(6,5),new Point(6,25),new Point(13,25)});
                        e.Graphics.DrawLine(pen,10,10,17,10);e.Graphics.DrawLine(pen,10,15,13,15);
                        e.Graphics.DrawEllipse(pen,14,15,9,9);e.Graphics.DrawLine(pen,22,23,27,28);
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
                    }else if(kind=="details"){
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
        private void UpdateLogButton(){
            if(logButton==null)return;
            string state=!settings.WatchLog?"paused":string.IsNullOrEmpty(settings.Log)||!File.Exists(settings.Log)?"missing":
                (DateTime.UtcNow-lastLogGrowthUtc).TotalSeconds<10?"recent":"idle";
            if(state==logButtonState)return;
            logButtonState=state;
            string hint=state=="paused"?"Overlay log reading paused. Click to resume (missed lines will be read). WoW /combatlog is separate.":
                state=="missing"?"No combat log file selected or file missing. Click to pause overlay reading.":
                state=="recent"?"Combat log file grew recently. Click to pause overlay reading.":
                "Waiting for new log lines. This cannot tell if WoW /combatlog is off. Click to pause overlay reading.";
            toolTip.SetToolTip(logButton,hint);logButton.Invalidate();
        }
        private void PaintLogButton(object sender,PaintEventArgs e){
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color color=logButtonState=="recent"?green:logButtonState=="paused"?red:dim;
            using(var pen=new Pen(color,2.2f)){
                e.Graphics.DrawRectangle(pen,7,5,17,20);
                if(logButtonState=="paused"){
                    e.Graphics.DrawLine(pen,12,11,12,19);e.Graphics.DrawLine(pen,19,11,19,19);
                }else if(logButtonState=="recent"){
                    e.Graphics.DrawLine(pen,10,15,14,19);e.Graphics.DrawLine(pen,14,19,22,10);
                }else e.Graphics.DrawLine(pen,11,16,20,16);
            }
        }
        private void ApplyBeastButton(){
            beastButton.FlatAppearance.BorderSize=0;
            toolTip.SetToolTip(beastButton,settings.ShowBeasts?"Hide Blood Beast events":"Show Blood Beast events");
            beastButton.Invalidate();
        }
        private Button OptionIcon(string key,int top,string caption,Func<bool> enabled,Action toggle){
            var button=new Button{Left=options.ClientSize.Width-52,Top=top,Width=36,Height=36,
                Tag=key,Anchor=AnchorStyles.Top|AnchorStyles.Right,FlatStyle=FlatStyle.Flat,BackColor=options.BackColor,
                TabStop=false,Cursor=Cursors.Hand};
            button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            button.Paint+=delegate(object sender,PaintEventArgs e){
                Image image;if(spellIcons.TryGetValue(key,out image))e.Graphics.DrawImage(image,new Rectangle(2,2,32,32));
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if(!enabled()){
                    using(var shadow=new Pen(Color.FromArgb(14,15,17),5)){
                        e.Graphics.DrawEllipse(shadow,7,7,22,22);e.Graphics.DrawLine(shadow,9,27,27,9);
                    }
                    using(var pen=new Pen(red,2.8f)){
                        e.Graphics.DrawEllipse(pen,7,7,22,22);e.Graphics.DrawLine(pen,9,27,27,9);
                    }
                }else using(var pen=new Pen(green,2))e.Graphics.DrawLine(pen,3,35,33,35);
            };
            Action tip=delegate {toolTip.SetToolTip(button,caption+": "+(enabled()?"on":"off")+" — click to toggle");};
            button.Click+=delegate {toggle();tip();button.Invalidate();RefreshCards();Save();};
            tip();options.Controls.Add(button);return button;
        }
        // Load independent custom cast and damage rules from saved cards.
        private static void ConfigureCustomEvents(Tracker target) {
            foreach(var card in ReadCustomCards()) {
                if(card==null||String.IsNullOrWhiteSpace(card.SpellId))continue;
                if(card.SpellId=="1271967"||card.SpellId=="1271974"||card.SpellId=="434237")continue;
                var rule=new Tracker.CustomEventRule{Name=card.Name,Spell=card.SpellId,
                    WindowMs=card.WindowMs>0?card.WindowMs:1200};
                if(card.Effects!=null)foreach(var effect in card.Effects)
                    if(effect!=null&&effect.Type=="Damage"&&!String.IsNullOrWhiteSpace(effect.SpellId))
                        rule.Damage[effect.SpellId.Trim()]=effect.Name;
                target.CustomRules.Add(rule);
            }
        }
        // Existing settings remain the fallback for cards saved before window configuration.
        private static int BlightfallWindowMs(int fallback) {
            try {foreach(var card in ReadCustomCards())
                if((card.SpellId=="1271967"||card.SpellId=="1271974")&&card.WindowMs>0)return card.WindowMs;
            }catch{}
            return fallback;
        }
        private void SetupOptions() {
            var font=new NumericUpDown{Minimum=8,Maximum=18,Value=settings.TextSize,Width=52,Left=137,Top=12};
            var icons=new NumericUpDown{Minimum=18,Maximum=40,Value=settings.IconSize,Width=52,Left=218,Top=12};
            var number=new CheckBox{Text="Short numbers",Checked=settings.CompactNumbers,AutoSize=true,Left=12,Top=62,ForeColor=dim};
            var grouped=new CheckBox{Text="Beasts first, then Blightfall",Checked=settings.GroupByEventType,AutoSize=true,Left=12,Top=85,ForeColor=dim};
            var onTop=new CheckBox{Text="Overlay stays on top",Checked=settings.AlwaysOnTop,AutoSize=true,Left=12,Top=108,ForeColor=dim};
            toolTip.SetToolTip(onTop,"Keep the overlay above other windows. Turn off for a second-screen OBS capture.");
            var beastIcon=new CheckBox{Text="Blightfall on Blood Beast",Checked=settings.ShowBeastBlightfall,AutoSize=true,Left=12,Top=131,ForeColor=dim};
            var overkill=new CheckBox{Text="Show overkill",Checked=settings.ShowOverkill,AutoSize=true,Left=12,Top=154,ForeColor=dim};
            toolTip.SetToolTip(overkill,"Show overkill only in expanded hit details. Full damage and hit counts always include lethal hits.");
            updateButton=new Button{Text="Check for updates",Left=12,Top=177,Width=150,Height=27,
                ForeColor=dim,BackColor=Color.FromArgb(42,46,51),FlatStyle=FlatStyle.Flat,TabStop=false};
            updateButton.FlatAppearance.BorderColor=Color.FromArgb(87,91,91);
            updateButton.Click+=delegate {CheckForUpdates(false);};
            // The player filter lives in Options; the Split View toolbar shortcut remains.
            var playerFilter=new Button{Text="Player filter: "+settings.PlayerMode+"  ▾",Left=12,Top=211,
                Width=258,Height=28,ForeColor=Color.White,BackColor=Color.FromArgb(42,46,51),
                FlatStyle=FlatStyle.Flat,TabStop=false,TextAlign=ContentAlignment.MiddleLeft};
            playerFilter.FlatAppearance.BorderColor=Color.FromArgb(87,91,91);
            playerFilter.Click+=delegate{
                ShowPlayerMenu();
            };
            var playerModeRefresh=new Timer{Interval=300};
            playerModeRefresh.Tick+=delegate{
                if(!playerFilter.IsDisposed){
                    string label="Player filter: "+settings.PlayerMode+"  ▾";
                    if(playerFilter.Text!=label)playerFilter.Text=label;
                }
            };
            playerFilter.Disposed+=delegate{playerModeRefresh.Stop();playerModeRefresh.Dispose();};
            playerModeRefresh.Start();
            options.Controls.Add(playerFilter);
            options.Controls.AddRange(new Control[]{font,icons,number,grouped,onTop,beastIcon,updateButton,overkill,
                new Label{Text="Text size",Left=137,Top=35,Width=70,ForeColor=dim},
                new Label{Text="Icon size",Left=218,Top=35,Width=70,ForeColor=dim}});
            // Card Builder is a dedicated Options shortcut, not a player-filter menu entry.
            // Keep the entire control inside the dark options panel and away from the resize grip.
            var builderButton=new Button{Width=36,Height=36,Top=10,Left=Math.Max(12,options.ClientSize.Width-52),
                Anchor=AnchorStyles.Top|AnchorStyles.Right,BackColor=options.BackColor,
                FlatStyle=FlatStyle.Flat,TabStop=false,Cursor=Cursors.Hand};
            builderButton.FlatAppearance.BorderSize=0;
            builderButton.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            builderButton.Paint+=delegate(object sender,PaintEventArgs e){
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var icon=MakeBuilderMenuIcon())
                    e.Graphics.DrawImage(icon,new Rectangle(5,5,26,26));
            };
            builderButton.Click+=delegate{ShowEventCardBuilder();};
            toolTip.SetToolTip(builderButton,"Open Event Card Builder");
            options.Controls.Add(builderButton);
            var builderCaption=new Label{Text="Event Card Builder",AutoSize=false,
                Width=135,Height=20,Top=19,Left=Math.Max(12,options.ClientSize.Width-194),
                Anchor=AnchorStyles.Top|AnchorStyles.Right,ForeColor=dim,
                TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",9)};
            options.Controls.Add(builderCaption);
            builderButton.BringToFront();
            beastButton=OptionIcon("BB",62,"Blood Beast events",delegate{return settings.ShowBeasts;},delegate{settings.ShowBeasts=!settings.ShowBeasts;});
            OptionIcon("SR",104,"Soul Reaper marker",delegate{return settings.ShowSoulReaper;},delegate{settings.ShowSoulReaper=!settings.ShowSoulReaper;});
            OptionIcon("SC",146,"Festering Scythe marker",delegate{return settings.ShowScythe;},delegate{settings.ShowScythe=!settings.ShowScythe;});
            font.ValueChanged+=delegate {settings.TextSize=(int)font.Value;RefreshCards();Save();};
            icons.ValueChanged+=delegate {settings.IconSize=(int)icons.Value;RefreshCards();Save();};
            number.CheckedChanged+=delegate {settings.CompactNumbers=number.Checked;RefreshCards();Save();};
            beastIcon.CheckedChanged+=delegate {settings.ShowBeastBlightfall=beastIcon.Checked;RefreshCards();Save();};
            grouped.CheckedChanged+=delegate {settings.GroupByEventType=grouped.Checked;scrollPixels=0;RefreshCards();Save();};
            overkill.CheckedChanged+=delegate {settings.ShowOverkill=overkill.Checked;RefreshCards();Save();};
            onTop.CheckedChanged+=delegate {settings.AlwaysOnTop=onTop.Checked;TopMost=settings.AlwaysOnTop;Save();};
        }
        private void BeginMove(object sender,MouseEventArgs e){if(!settings.Locked&&e.Button==MouseButtons.Left){dragStart=MousePosition;dragOrigin=Location;}}
        private void MoveWindow(object sender,MouseEventArgs e){if(dragStart.HasValue&&!settings.Locked){var p=MousePosition;Location=new Point(dragOrigin.X+p.X-dragStart.Value.X,dragOrigin.Y+p.Y-dragStart.Value.Y);}}
        private void EndMove(object sender,MouseEventArgs e){dragStart=null;Save();}
        private void ApplyLock(){if(settings.Locked)EndResizePreview(false);grip.Visible=!settings.Locked;lockButton.Invalidate();LayoutWindow();}
        private void ToggleOptions(){
            if(options.Visible){
                options.Visible=false;
                if(compactHeightBeforeOptions>0&&Height==320)Height=compactHeightBeforeOptions;
                compactHeightBeforeOptions=0;
            }else{
                if(Height<320){compactHeightBeforeOptions=Height;Height=320;}
                options.Visible=true;
            }
            LayoutWindow();
        }
        private void ApplyCollapsed(){
            if(settings.Collapsed&&options.Visible){
                options.Visible=false;
                if(compactHeightBeforeOptions>0&&Height==320)Height=compactHeightBeforeOptions;
                compactHeightBeforeOptions=0;
            }
            MinimumSize=new Size(340,settings.Collapsed?70:136);
            bar.Height=settings.Collapsed?10:75;
            foreach(Control child in bar.Controls)child.Visible=!settings.Collapsed;
            bar.Cursor=settings.Collapsed?Cursors.Hand:Cursors.Default;
            collapseButton.Invalidate();
            bar.Click-=ExpandFromStrip;bar.Click+=ExpandFromStrip;
            LayoutWindow();RefreshCards();miniButton.Invalidate();}
        private void ExpandFromStrip(object sender,EventArgs e){if(settings.Collapsed){settings.Collapsed=false;ApplyCollapsed();Save();}}
        private void LayoutWindow(){
            bar.Width=ClientSize.Width;
            options.Bounds=new Rectangle(0,Math.Max(bar.Bottom,ClientSize.Height-options.Height),ClientSize.Width,options.Height);
            int bottom=options.Visible?options.Top:ClientSize.Height;
            cards.Bounds=new Rectangle(0,bar.Bottom,Math.Max(0,ClientSize.Width-12),Math.Max(0,bottom-bar.Bottom));
            // Reserve the lower corner for the resize grip instead of putting it over the scrollbar.
            int trackBottom=settings.Locked?bottom:Math.Min(bottom,ClientSize.Height-grip.Height-4);
            scrollTrack.Bounds=new Rectangle(Math.Max(0,ClientSize.Width-11),cards.Top,9,Math.Max(0,trackBottom-cards.Top));
            scrollTrack.Visible=totalContentHeight>cards.ClientSize.Height;
            grip.Location=new Point(ClientSize.Width-19,ClientSize.Height-19);grip.BringToFront();
            if(options.Visible)options.BringToFront();scrollTrack.BringToFront();bar.BringToFront();
            // Keep the resize corner above the options panel as well as the event area.
            grip.BringToFront();
            ScrollTo(scrollPixels);Invalidate();
        }
        private void BeginResizePreview(){
            if(settings.Locked||resizeStart.HasValue)return;
            resizeStart=MousePosition;resizeOrigin=Size;resizeCandidate=Size;
            resizeSamples.Clear();
            foreach(var state in visibleCards){
                if(state.Card.Bottom>0&&state.Card.Top<cards.ClientSize.Height){
                    resizeSamples.Add(state.Entry);if(resizeSamples.Count==2)break;
                }
            }
            if(resizeSamples.Count==0&&visibleCards.Count>0)resizeSamples.Add(visibleCards[visibleCards.Count-1].Entry);
            resizeOutline=new ResizeOutline{Bounds=new Rectangle(Location,resizeCandidate),TopMost=TopMost};
            resizeOutline.Paint+=DrawResizePreview;
            resizeOutline.Show(this);grip.Capture=true;
        }
        private void UpdateResizePreview(Size proposed){
            if(!resizeStart.HasValue)return;
            resizeCandidate=new Size(Math.Min(32767,Math.Max(MinimumSize.Width,proposed.Width)),
                Math.Min(32767,Math.Max(MinimumSize.Height,proposed.Height)));
            if(resizeOutline!=null){resizeOutline.Bounds=new Rectangle(Location,resizeCandidate);resizeOutline.Invalidate();}
        }
        private void EndResizePreview(bool apply){
            if(!resizeStart.HasValue)return;
            Size chosen=resizeCandidate;resizeStart=null;grip.Capture=false;
            if(resizeOutline!=null){resizeOutline.Close();resizeOutline.Dispose();resizeOutline=null;}
            resizeSamples.Clear();
            if(apply){Size=chosen;Save();}
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData){
            if(keyData==Keys.Escape&&resizeStart.HasValue){EndResizePreview(false);return true;}
            return base.ProcessCmdKey(ref message,keyData);
        }
        private int DamageColumnX(Entry entry,int width,bool mini,int iconSize){
            long firstValue=entry.Beast?entry.Corrupted:entry.Dread;
            int minimum=55+iconSize+4+TextRenderer.MeasureText(Format(firstValue),LabelFont(false)).Width+8;
            return Math.Max(minimum,Math.Max(150,width/2-6));
        }
        private sealed class SummaryLayout {
            public int IconSize,TextSize,SecondX,DamageTop,EncounterTop,BaseHeight;
        }
        private SummaryLayout LargeSummaryLayout(Entry entry,int width){
            var g=new SummaryLayout{IconSize=Math.Min(32,settings.IconSize),TextSize=settings.TextSize};
            int status=entry.Beast?(settings.ShowBeastBlightfall?1:0):((settings.ShowSoulReaper?1:0)+(settings.ShowScythe?1:0));
            string hits=entry.Hits.Count+(entry.Hits.Count==1?" hit":" hits");
            int crits=entry.Hits.FindAll(h=>h.Crit).Count;
            string critText=crits+(crits==1?" crit":" crits");
            while(true){
                var f=SizedFont(g.TextSize,false);
                int first=TextRenderer.MeasureText(Format(entry.Beast?entry.Corrupted:entry.Dread),f).Width;
                int second=TextRenderer.MeasureText(Format(entry.Beast?entry.Life:entry.Virulent),f).Width;
                if(entry.Beast)second=Math.Max(second,TextRenderer.MeasureText(Format(entry.Melee),f).Width);
                g.SecondX=55+g.IconSize+4+Math.Max(first,second)+12;
                int countWidth=Math.Max(TextRenderer.MeasureText(hits,f).Width,TextRenderer.MeasureText(critText,SizedFont(g.TextSize,true)).Width)+4;
                if(entry.Beast)countWidth=Math.Max(countWidth,TextRenderer.MeasureText(entry.MeleeHits.Count+" melee hits",f).Width+4);
                int right=width-9-status*g.IconSize-Math.Max(0,status-1)*3;
                if(g.SecondX+countWidth+6<=right)break;
                if(g.TextSize>8){g.TextSize--;continue;}
                if(g.IconSize>18){g.IconSize--;continue;}
                break;
            }
            g.EncounterTop=3;
            g.DamageTop=TextRenderer.MeasureText("Ag",LabelFont(true)).Height+8;
            int rowHeight=Math.Max(g.IconSize,TextRenderer.MeasureText("Ag",SizedFont(g.TextSize,true)).Height+2);
            g.BaseHeight=g.DamageTop+(entry.Beast?3:2)*(rowHeight+6);
            return g;
        }
        private int PreviewEventHeight(Entry entry,int width,bool mini){
            int top,minimum;
            if(mini){top=57;minimum=55;}
            else{var geometry=LargeSummaryLayout(entry,width);int extra=DetailedAuraHeight(entry,width,geometry.IconSize);top=geometry.BaseHeight+extra+2;minimum=geometry.BaseHeight+extra;}
            string first=entry.Beast?"CB":"DP";
            foreach(var hit in DisplayHits(entry)){
                if(!(hit.Kind=="ME"?entry.MOpen&&!mini:hit.Kind==first?entry.DOpen:entry.VOpen))continue;
                int size=Math.Min(32,Math.Max(23,settings.IconSize)),amountX=19+size;
                int rowWidth=width-16,height=size+6;
                int amountWidth=TextRenderer.MeasureText(Format(hit.Amount),LabelFont(false)).Width+6;
                int critWidth=TextRenderer.MeasureText(hit.Crit?"Crit":"Hit",LabelFont(true)).Width+6;
                int targetWidth=Math.Max(20,rowWidth-(amountX+amountWidth+6+critWidth+8)-8);
                string target=string.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
                if(TextRenderer.MeasureText(target,LabelFont(false)).Width>targetWidth){
                    int wrapped=TextRenderer.MeasureText(target,LabelFont(false),new Size(Math.Max(25,rowWidth-amountX-8),int.MaxValue),TextFormatFlags.WordBreak).Height;
                    height=size+12+Math.Max(TextRenderer.MeasureText("Ag",LabelFont(false)).Height,wrapped);
                }
                var debuffs=HitDebuffIcons(hit,mini);
                if(debuffs.Count>0){
                    int auraSize=Math.Max(18,Math.Min(26,settings.IconSize));
                    int iconsWidth=debuffs.Count*(auraSize+3);
                    int targetX=amountX+amountWidth+6+critWidth+8;
                    int available=Math.Max(20,rowWidth-targetX-8);
                    if(TextRenderer.MeasureText(target,LabelFont(false)).Width+iconsWidth>available){
                        int cols=Math.Max(1,(rowWidth-amountX-8)/(auraSize+3));
                        height+=((debuffs.Count+cols-1)/cols)*(auraSize+3)+5;
                    }
                }
                if(settings.ShowOverkill&&hit.Overkill>0)
                    height+=TextRenderer.MeasureText("Overkill: "+Format(hit.Overkill),LabelFont(false),
                        new Size(Math.Max(25,rowWidth-amountX-8),int.MaxValue),TextFormatFlags.WordBreak).Height+5;
                top+=height+2;
            }
            return Math.Max(minimum,top+2);
        }
        private void DrawResizePreview(object sender,PaintEventArgs e){
            var size=resizeCandidate;bool mini=settings.MiniCards;
            using(var pen=new Pen(green,2)){
                pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawRectangle(pen,1,1,Math.Max(1,size.Width-3),Math.Max(1,size.Height-3));
                string mode=mini?"Compact":"Extra details";
                TextRenderer.DrawText(e.Graphics,size.Width+" × "+size.Height+" · "+mode,LabelFont(true),
                    new Rectangle(9,9,Math.Max(1,size.Width-18),25),green,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                int bottom=options.Visible?Math.Max(bar.Bottom,size.Height-options.Height):size.Height;
                int y=Math.Max(38,bar.Bottom+3),width=Math.Max(260,size.Width-19);
                foreach(var entry in resizeSamples){
                    if(y>=bottom-4)break;
                    int height=PreviewEventHeight(entry,width,mini),shown=Math.Min(height,bottom-y-4);
                    e.Graphics.DrawRectangle(pen,3,y,width,Math.Max(1,shown));
                    string name=entry.Beast?"Blood Beast":"Blightfall";
                    TextRenderer.DrawText(e.Graphics,name+" #"+entry.Number+" · "+width+" × "+height+
                        (shown<height?" · scroll to see more":""),LabelFont(false),
                        new Rectangle(12,y+7,Math.Max(1,width-18),25),green,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                    y+=height+(mini?4:5);
                }
            }
        }
        private void ScrollWheel(object sender,MouseEventArgs e){
            int max=Math.Max(0,totalContentHeight-cards.ClientSize.Height);
            int delta=-Math.Sign(e.Delta)*Math.Max(45,settings.TextSize*4);
            if(!smoothScrollTimer.Enabled)smoothScrollTarget=scrollPixels;
            smoothScrollTarget=Math.Max(0,Math.Min(max,smoothScrollTarget+delta));
            if(smoothScrollTarget!=scrollPixels)smoothScrollTimer.Start();
        }
        private void HookWheel(Control control){control.MouseWheel+=ScrollWheel;foreach(Control child in control.Controls)HookWheel(child);}
        private void ScrollTo(int pixels){
            int next=Math.Max(0,Math.Min(pixels,Math.Max(0,totalContentHeight-cards.ClientSize.Height)));
            bool changed=next!=scrollPixels;
            scrollPixels=next;
            if(!smoothScrollTick){smoothScrollTimer.Stop();smoothScrollTarget=next;}
            // Never leave hundreds of off-screen child controls visible. WinForms can
            // otherwise leave stale child pixels while moving controls during scrolling.
            cards.SuspendLayout();
            try{
                var activeCards=new HashSet<Control>();
                foreach(var state in visibleCards)activeCards.Add(state.Card);
                foreach(var header in playerHeaders.Values)if(header.Tag is int)activeCards.Add(header);
                foreach(Control control in cards.Controls){
                    if(!activeCards.Contains(control)){control.Visible=false;continue;}
                    if(!(control.Tag is int))continue;
                    int top=(int)control.Tag-scrollPixels;
                    bool inView=top+control.Height>=-4&&top<=cards.ClientSize.Height+4;
                    control.Visible=inView;
                    if(inView&&control.Top!=top)control.Top=top;
                }
            }finally{cards.ResumeLayout(false);}
            scrollTrack.Visible=totalContentHeight>cards.ClientSize.Height;
            scrollTrack.Invalidate();
            // Build a deferred card only after it enters the visible viewport.
            if(!refreshingCards){
                int width=Math.Max(260,cards.ClientSize.Width-7);
                foreach(var state in visibleCards){
                    if(state.Card.Visible&&state.Signature!=(state.Entry.Custom
                        ?CustomCardSignature(state.Entry,width):CardSignature(state.Entry,width))){
                        RefreshCards();break;
                    }
                }
            }
            if(changed)cards.Invalidate();
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
        private void CheckForUpdates(bool quiet){
            if(checkingUpdate||installingUpdate)return;
            checkingUpdate=true;updateButton.Enabled=false;
            Task.Run(delegate{
                ReleaseUpdate latest=null;Exception error=null;
                try{latest=Updater.Latest();}catch(Exception ex){error=ex;}
                if(IsDisposed||!IsHandleCreated)return;
                try{BeginInvoke((MethodInvoker)delegate{
                    checkingUpdate=false;updateButton.Enabled=true;
                    if(error!=null){
                        if(!quiet)MessageBox.Show(this,"Could not check GitHub releases.\n\n"+error.Message,
                            "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        return;
                    }
                    var current=Assembly.GetExecutingAssembly().GetName().Version;
                    if(latest.Version.CompareTo(current)<=0){
                        if(!quiet)MessageBox.Show(this,"You already have the latest version ("+current+").",
                            "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        return;
                    }
                    if(string.IsNullOrEmpty(latest.DownloadUrl)){
                        if(!quiet)MessageBox.Show(this,"The latest release has no BlightfallPops.zip asset.",
                            "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        return;
                    }
                    if(MessageBox.Show(this,"Version "+latest.Tag+" is available. Download and install it now?\n\n"+
                            "The overlay will close and restart after the update.","Blightfall Pops update",
                            MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                    DownloadUpdate(latest);
                });}catch(ObjectDisposedException){}catch(InvalidOperationException){}
            });
        }
        private void DownloadUpdate(ReleaseUpdate latest){
            installingUpdate=true;updateButton.Enabled=false;
            Task.Run(delegate{
                string directory=null;Exception error=null;
                try{directory=Updater.Stage(latest);}catch(Exception ex){error=ex;}
                if(IsDisposed||!IsHandleCreated){
                    if(directory!=null)try{System.IO.Directory.Delete(directory,true);}catch{}
                    return;
                }
                try{BeginInvoke((MethodInvoker)delegate{
                    installingUpdate=false;updateButton.Enabled=true;
                    if(error!=null){
                        MessageBox.Show(this,"Could not download the update.\n\n"+error.Message,
                            "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Error);
                        return;
                    }
                    try{Updater.StartInstaller(directory,Application.ExecutablePath,framed);Close();}
                    catch(Exception ex){
                        try{System.IO.Directory.Delete(directory,true);}catch{}
                        MessageBox.Show(this,"Could not start the updater.\n\n"+ex.Message,
                            "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    }
                });}catch(ObjectDisposedException){}catch(InvalidOperationException){}
            });
        }
        private void Save(){if(WindowState!=FormWindowState.Normal)return;
            settings.Width=Width;settings.Height=Height;settings.X=Left;settings.Y=Top;
            try{settings.Save();}catch{}}
        private void ChooseLog(){using(var dialog=new OpenFileDialog{Title="Select the active WoWCombatLog.txt",Filter="Combat logs (*.txt)|*.txt|All files (*.*)|*.*"}){
            if(File.Exists(settings.Log))dialog.FileName=settings.Log;
            if(dialog.ShowDialog(this)==DialogResult.OK){settings.Log=dialog.FileName;LoadLog(settings.Log);UpdateLogButton();Save();}}}
        private void CancelFullLogScan(){
            scanningFullLog=false;poll.Interval=350;if(scanButton!=null)scanButton.Enabled=true;
        }
        private void ScanEntireLog(){
            if(scanningFullLog)return;
            if(!File.Exists(settings.Log)){ChooseLog();if(!File.Exists(settings.Log))return;}
            try{
                fullScanTarget=new FileInfo(settings.Log).Length;
                tracker.Reset();tracker.SkipFirstLine=false;scrollPixels=0;
                settings.ShowBeasts=true;ApplyBeastButton();Save();
                dirty=false;RefreshCards();scanningFullLog=true;scanButton.Enabled=false;poll.Interval=50;
                status.Text="Scanning entire log — 0%";
            }catch(Exception ex){CancelFullLogScan();status.Text="Cannot scan log: "+ex.Message;}
        }
        private void ContinueFullLogScan(){
            if(!scanningFullLog)return;
            try{
                if(!File.Exists(settings.Log))throw new IOException("Selected log file is missing");
                if(new FileInfo(settings.Log).Length<fullScanTarget)throw new IOException("Log was shortened during scan; scan it again");
                tracker.Tick(settings.Log);
                int percent=fullScanTarget==0?100:(int)Math.Min(100,tracker.Position*100.0/fullScanTarget);
                status.Text="Scanning entire log — "+percent+"%";
                if(tracker.Position<fullScanTarget)return;
                tracker.Finish();CancelFullLogScan();dirty=false;RefreshCards();
                ScrollTo(Math.Max(0,totalContentHeight-cards.ClientSize.Height));
                status.Text="Full log scanned — "+tracker.Entries.Count+" events";
                UpdateLogButton();
            }catch(Exception ex){CancelFullLogScan();dirty=false;RefreshCards();status.Text="Scan error: "+ex.Message;}
        }
        private void LoadLog(string file){CancelFullLogScan();try{tracker.Reset();scrollPixels=0;var size=new FileInfo(file).Length;
            lastLogGrowthUtc=DateTime.MinValue;
            tracker.Position=settings.WatchLog?Math.Max(0,size-8388608L):size;
            tracker.SkipFirstLine=settings.WatchLog&&tracker.Position>0;
            if(settings.WatchLog)tracker.Tick(file);
            dirty=false;RefreshCards();
            ScrollTo(Math.Max(0,totalContentHeight-cards.ClientSize.Height));
            status.Text=settings.WatchLog?"Watching "+Path.GetFileName(file):"Overlay log reading paused";UpdateLogButton();}
            catch(Exception ex){status.Text="Cannot open log: "+ex.Message;}}
        private void ResetSession(){CancelFullLogScan();try{
            long end=0;bool partial=false;
            if(File.Exists(settings.Log))using(var stream=new FileStream(settings.Log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                end=stream.Length;if(end>0){stream.Seek(end-1,SeekOrigin.Begin);partial=stream.ReadByte()!=10;}
            }
            tracker.Reset(end);tracker.SkipFirstLine=partial;scrollPixels=0;
            dirty=false;RefreshCards();status.Text="New session — waiting for combat";}catch(Exception ex){status.Text=ex.Message;}}
        private string Format(long value){if(value>=1000000)return (value/1000000.0).ToString("0.##",CultureInfo.CurrentCulture)+"M";
            if(settings.CompactNumbers&&value>=1000)return (value/1000.0).ToString("0.#",CultureInfo.CurrentCulture)+"K";
            return value.ToString("N0",CultureInfo.CurrentCulture);}
        private Label Label(string text,Color color,int left,int top,int width,bool bold=false){return new Label{Text=text,ForeColor=color,Font=LabelFont(bold),
            Left=left,Top=top,Width=width,Height=25,AutoEllipsis=true,BackColor=Color.Transparent};}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);
            if(framed&&artwork!=null&&!settings.Collapsed){using(var pen=new Pen(Color.FromArgb(75,107,92),2))e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);}
        }
    }
}
