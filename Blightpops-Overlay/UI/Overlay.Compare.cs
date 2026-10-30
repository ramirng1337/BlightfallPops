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
    // Compare players and combat logs.
    internal sealed partial class Overlay {
        // Independent comparison pane.
        private sealed class CompareColumn {
            public Panel View, Track, Thumb;
            public FlowLayoutPanel Content;
            public int Offset;
            public bool Dragging;
            public int DragY, DragOffset;
            public Tracker RenderSource;
            public string RenderGuid;
            public int RenderWidth=-1, RenderCount=-1;
            public long RenderLastSequence=-1;
            public void InvalidateRender(){RenderSource=null;RenderGuid=null;RenderWidth=-1;RenderCount=-1;RenderLastSequence=-1;}
            public void UpdateScroll(){
                int maximum=Math.Max(0,Content.Height-View.ClientSize.Height);
                Offset=Math.Max(0,Math.Min(maximum,Offset));
                Content.Top=-Offset;
                Track.Visible=maximum>0;
                if(maximum==0)return;
                int trackHeight=Track.ClientSize.Height;
                int thumbHeight=Math.Max(28,(int)((long)trackHeight*View.ClientSize.Height/Math.Max(1,Content.Height)));
                thumbHeight=Math.Min(trackHeight,thumbHeight);
                Thumb.Bounds=new Rectangle(1,(int)((long)Offset*(trackHeight-thumbHeight)/maximum),6,thumbHeight);
            }
            public void Scroll(int delta){Offset+=delta;UpdateScroll();}
        }
        private CompareColumn MakeCompareColumn(){
            var column=new CompareColumn();
            column.View=new BufferedPanel{Dock=DockStyle.Fill,BackColor=background,TabStop=true};
            column.Track=new Panel{Dock=DockStyle.Right,Width=9,BackColor=Color.FromArgb(22,25,29)};
            column.Thumb=new Panel{BackColor=Color.FromArgb(91,102,110),Cursor=Cursors.Hand};
            column.Content=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,
                AutoScroll=false,BackColor=background,Location=Point.Empty,Margin=Padding.Empty,Padding=new Padding(2,3,2,4)};
            column.View.Controls.Add(column.Content);
            column.View.Controls.Add(column.Track);
            column.Track.Controls.Add(column.Thumb);
            column.View.Resize+=delegate{column.Content.Width=Math.Max(100,column.View.ClientSize.Width-column.Track.Width);
                foreach(Control c in column.Content.Controls)c.Width=Math.Max(100,column.Content.ClientSize.Width-12);
                column.Content.Height=CompareContentHeight(column.Content);column.UpdateScroll();};
            MouseEventHandler wheel=delegate(object sender,MouseEventArgs e){column.Scroll(-Math.Sign(e.Delta)*Math.Max(32,SystemInformation.MouseWheelScrollLines*23));};
            column.View.MouseWheel+=wheel;column.Content.MouseWheel+=wheel;
            column.Track.MouseWheel+=wheel;column.Thumb.MouseWheel+=wheel;
            column.Track.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)
                column.Scroll((e.Y<column.Thumb.Top?-1:1)*Math.Max(40,column.View.ClientSize.Height-40));};
            column.Thumb.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){column.Dragging=true;column.DragY=Cursor.Position.Y;column.DragOffset=column.Offset;column.Thumb.Capture=true;}};
            column.Thumb.MouseMove+=delegate{if(column.Dragging){int travel=Math.Max(1,column.Track.Height-column.Thumb.Height);
                int max=Math.Max(0,column.Content.Height-column.View.Height);
                column.Offset=column.DragOffset+(Cursor.Position.Y-column.DragY)*max/travel;column.UpdateScroll();}};
            column.Thumb.MouseUp+=delegate{column.Dragging=false;column.Thumb.Capture=false;};
            column.Thumb.MouseCaptureChanged+=delegate{column.Dragging=false;};
            return column;
        }
        private static int CompareContentHeight(FlowLayoutPanel panel){
            int height=panel.Padding.Top+panel.Padding.Bottom;
            foreach(Control control in panel.Controls)height+=control.Height+control.Margin.Vertical;
            return Math.Max(1,height);
        }
        // Independent, read-only log scan for compare mode; does not change the live overlay.
        private Tracker ReadCompareLog(string path){
            var result=new Tracker{TrackAllPlayers=true,WindowMs=tracker.WindowMs};
            // A separate combat log must use the same configured aura spell IDs.
            // Otherwise its historical hit snapshots cannot contain custom debuffs.
            foreach(var watched in tracker.WatchedAuras)
                result.WatchedAuras[watched.Key]=watched.Value;
            foreach(var watched in tracker.WatchedDamage)result.WatchedDamage[watched.Key]=watched.Value;
            ConfigureCustomEvents(result);
            long size=new FileInfo(path).Length;
            while(result.Position<size){
                long previous=result.Position;
                result.Tick(path);
                if(result.Position<=previous)throw new IOException("Unable to read the selected combat log.");
            }
            result.Finish();
            return result;
        }
        private static Dictionary<string,string> CompareIdentities(Tracker source){
            var names=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var player in source.GetPlayers())names[player.Guid]=String.IsNullOrEmpty(player.Name)?player.Guid:player.Name;
            foreach(var entry in source.Entries)if(!String.IsNullOrEmpty(entry.PlayerGuid)&&!names.ContainsKey(entry.PlayerGuid))
                names[entry.PlayerGuid]=String.IsNullOrEmpty(entry.PlayerName)?entry.PlayerGuid:entry.PlayerName;
            return names;
        }
        private static List<string> ComparePlayerKeys(Tracker source,Dictionary<string,string> names){
            var keys=new List<string>();
            foreach(var entry in source.Entries)if(!keys.Contains(entry.PlayerGuid))keys.Add(entry.PlayerGuid);
            keys.Sort(delegate(string a,string b){return String.Compare(names[a],names[b],StringComparison.OrdinalIgnoreCase);});
            return keys;
        }
        // Editable card definitions are independent of the live Blightfall parser.
        private sealed class CustomEffect {
            public string Name="New effect", SpellId="", Type="Buff", IconPath="", Condition="Active at event";
            public bool ShowCompact=false, ShowDetailed=true;
        }
        private static CustomEffect CopyEffect(CustomEffect source){
            return new CustomEffect{ Name=source.Name, SpellId=source.SpellId, Type=source.Type,
                IconPath=source.IconPath, Condition=source.Condition,
                ShowCompact=source.ShowCompact, ShowDetailed=source.ShowDetailed };
        }
        // Set up split comparison view.
        private void ShowCompareWindow(){
            var identities=CompareIdentities(tracker);
            // A single player can still compare against another combat log.
            var compare=new Form{Text="Blightfall Pops — Compare players",Size=new Size(1120,720),
                StartPosition=FormStartPosition.CenterScreen,BackColor=background,ForeColor=Color.White,
                MinimumSize=new Size(740,420),FormBorderStyle=FormBorderStyle.None,ShowIcon=false,ShowInTaskbar=true};
            var toolbar=new Panel{Dock=DockStyle.Top,Height=91,BackColor=Color.FromArgb(28,32,37)};
            var leftChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DrawMode=DrawMode.OwnerDrawFixed,
                ItemHeight=25,Width=290,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,
                Font=new Font("Segoe UI",10,FontStyle.Regular)};
            var rightChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DrawMode=DrawMode.OwnerDrawFixed,
                ItemHeight=25,Width=290,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,
                Font=new Font("Segoe UI",10,FontStyle.Regular)};
            DrawItemEventHandler drawChoice=delegate(object sender,DrawItemEventArgs e){
                if(e.Index<0)return;
                using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(61,78,79):Color.FromArgb(39,44,51)))e.Graphics.FillRectangle(brush,e.Bounds);
                TextRenderer.DrawText(e.Graphics,((ComboBox)sender).Items[e.Index].ToString(),((ComboBox)sender).Font,e.Bounds,
                    Color.FromArgb(235,238,235),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            };
            leftChoice.DrawItem+=drawChoice;rightChoice.DrawItem+=drawChoice;
            var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,
                BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,Font=new Font("Segoe UI",9)};
            mode.Items.Add("Two players — same combat log");
            mode.Items.Add("Two separate combat logs");
            mode.SelectedIndex=0;
            var leftFile=new Button{Text="Choose log 1...",FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(39,44,51),
                ForeColor=Color.White,Font=new Font("Segoe UI",9),Visible=false};
            var rightFile=new Button{Text="Choose log 2...",FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(39,44,51),
                ForeColor=Color.White,Font=new Font("Segoe UI",9),Visible=false};
            leftFile.FlatAppearance.BorderColor=rightFile.FlatAppearance.BorderColor=Color.FromArgb(70,80,84);
            // Searchable event-card library. Only implemented tracker types can be selected.
            // Custom Spell IDs are shown as unavailable rather than silently producing fake matches.
            var compareEventsButton=BuilderButton("Event cards: Blightfall");
            compareEventsButton.Width=188;
            bool compareBlightfallEnabled=true;
            bool compareBeastEnabled=false;
            Action refreshEventSelection=null;
            compareEventsButton.Click+=delegate{
                using(var chooser=new Form{Text="Compare / Event Card Library",Size=new Size(440,510),
                    MinimumSize=new Size(360,360),StartPosition=FormStartPosition.CenterParent,
                    BackColor=Color.FromArgb(24,28,34),ForeColor=Color.White,ShowInTaskbar=false,
                    FormBorderStyle=FormBorderStyle.None}){
                    // Match the borderless dark title bars used by the main overlay and builder.
                    var libraryHeader=new Panel{Dock=DockStyle.Top,Height=32,BackColor=Color.FromArgb(10,11,13)};
                    var libraryTitle=new Label{Text="BLIGHTFALL POPS  /  COMPARE EVENT LIBRARY",
                        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
                        ForeColor=Color.FromArgb(222,224,218),Font=new Font("Segoe UI",9,FontStyle.Bold),
                        Padding=new Padding(12,0,0,0)};
                    var libraryClose=CaptionButton("×",Color.FromArgb(235,92,92),libraryHeader.BackColor);libraryClose.Dock=DockStyle.Right;
                    var libraryMinimize=CaptionButton("−",Color.FromArgb(178,183,183),libraryHeader.BackColor);libraryMinimize.Dock=DockStyle.Right;
                    libraryClose.Click+=delegate{chooser.Close();};
                    libraryMinimize.Click+=delegate{chooser.WindowState=FormWindowState.Minimized;};
                    libraryHeader.Controls.Add(libraryTitle);
                    libraryHeader.Controls.Add(libraryMinimize);
                    libraryHeader.Controls.Add(libraryClose);
                    Point? libraryDragStart=null;Point libraryWindowStart=Point.Empty;
                    MouseEventHandler libraryDown=delegate(object sender,MouseEventArgs e){
                        if(e.Button==MouseButtons.Left){libraryDragStart=Control.MousePosition;libraryWindowStart=chooser.Location;}
                    };
                    MouseEventHandler libraryMove=delegate{
                        if(libraryDragStart.HasValue){Point now=Control.MousePosition;
                            chooser.Location=new Point(libraryWindowStart.X+now.X-libraryDragStart.Value.X,
                                libraryWindowStart.Y+now.Y-libraryDragStart.Value.Y);}
                    };
                    MouseEventHandler libraryUp=delegate{libraryDragStart=null;};
                    foreach(Control dragControl in new Control[]{libraryHeader,libraryTitle}){
                        dragControl.MouseDown+=libraryDown;dragControl.MouseMove+=libraryMove;dragControl.MouseUp+=libraryUp;
                    }
                    var search=BuilderText("");search.Dock=DockStyle.Top;
                    var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,
                        BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,
                        BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",10)};
                    var footer=new Label{Dock=DockStyle.Bottom,Height=56,
                        ForeColor=Color.FromArgb(180,185,190),
                        Text="Blightfall and Blood Beast are supported.\nOther custom spell trackers are not yet implemented.",
                        Padding=new Padding(8)};
                    var done=BuilderButton("Apply selection");done.Dock=DockStyle.Bottom;
                    var choices=new List<CustomCard>();
                    choices.Add(new CustomCard{Name="Blightfall (built-in)",SpellId="1271974"});
                    choices.Add(new CustomCard{Name="Blood Beast (built-in)",SpellId="434237"});
                    foreach(var saved in ReadCustomCards())if(saved!=null&&saved.SpellId!="1271974"&&saved.SpellId!="434237")choices.Add(saved);
                    Action refill=delegate{
                        list.Items.Clear();
                        foreach(var c in choices){
                            string name=c.Name+" ["+c.SpellId+"]";
                            if(name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)<0)continue;
                            bool supported=c.SpellId=="1271974"||c.SpellId=="1271967"||c.SpellId=="434237";
                            int ix=list.Items.Add((supported?"":"[Tracker not implemented] ")+name);
                            if(supported)list.SetItemChecked(ix,c.SpellId=="434237"?compareBeastEnabled:compareBlightfallEnabled);
                        }
                    };
                    search.TextChanged+=delegate{refill();};refill();
                    list.ItemCheck+=delegate(object sender,ItemCheckEventArgs args){
                        string item=list.Items[args.Index].ToString();
                        if(item.StartsWith("[Tracker not implemented] "))args.NewValue=CheckState.Unchecked;
                    };
                    done.Click+=delegate{
                        // Keep selections for filtered-out entries when a search is active.
                        bool bf=compareBlightfallEnabled,bb=compareBeastEnabled;
                        for(int i=0;i<list.Items.Count;i++){
                            string item=list.Items[i].ToString();
                            if(item.StartsWith("[Tracker not implemented] "))continue;
                            if(item.Contains("[434237]"))bb=list.GetItemChecked(i);
                            else if(item.Contains("[1271974]")||item.Contains("[1271967]"))bf=list.GetItemChecked(i);
                        }
                        compareBlightfallEnabled=bf;compareBeastEnabled=bb;
                        compareEventsButton.Text="Event cards: "+(bf&&bb?"BF + BB":bf?"Blightfall":bb?"Blood Beast":"None");
                        chooser.Close();
                        if(refreshEventSelection!=null)refreshEventSelection();
                    };
                    chooser.Controls.Add(list);chooser.Controls.Add(done);chooser.Controls.Add(footer);chooser.Controls.Add(search);
                    chooser.Controls.Add(libraryHeader);
                    chooser.ShowDialog(compare);
                }
            };
            var leftLabel=new Label{Text="PLAYER 1",ForeColor=dim,Font=new Font("Segoe UI",9,FontStyle.Bold),AutoSize=true};
            var rightLabel=new Label{Text="PLAYER 2",ForeColor=dim,Font=new Font("Segoe UI",9,FontStyle.Bold),AutoSize=true};
            toolbar.Controls.AddRange(new Control[]{leftLabel,rightLabel,leftChoice,rightChoice,mode,leftFile,rightFile,compareEventsButton});
            // Two-row toolbar in separate-log mode: file buttons never share space
            // with the mode selector or event-library button.
            Action layoutToolbar=delegate{
                int w=toolbar.ClientSize.Width;
                int half=w/2;
                bool separate=mode.SelectedIndex==1;
                int modeWidth=Math.Min(270,Math.Max(160,w/2-35));
                mode.SetBounds(12,4,modeWidth,25);
                int eventX=mode.Right+10;
                int eventWidth=Math.Min(185,Math.Max(100,w-eventX-12));
                compareEventsButton.SetBounds(eventX,4,eventWidth,25);
                leftFile.Visible=rightFile.Visible=separate;
                if(separate){
                    leftFile.SetBounds(12,36,Math.Min(170,Math.Max(120,half-25)),25);
                    rightFile.SetBounds(half+12,36,Math.Min(170,Math.Max(120,half-25)),25);
                }
                int labelY=separate?69:37;
                int choiceY=separate?88:56;
                leftLabel.Location=new Point(13,labelY);
                rightLabel.Location=new Point(half+13,labelY);
                leftChoice.SetBounds(12,choiceY,Math.Max(120,half-25),28);
                rightChoice.SetBounds(half+12,choiceY,Math.Max(120,half-25),28);
                int desiredHeight=separate?123:91;
                if(toolbar.Height!=desiredHeight)toolbar.Height=desiredHeight;
            };
            toolbar.Resize+=delegate{layoutToolbar();};layoutToolbar();
            Tracker leftSource=tracker,rightSource=tracker;
            Dictionary<string,string> leftNames=identities,rightNames=identities;
            List<string> leftKeys=ComparePlayerKeys(tracker,identities),rightKeys=ComparePlayerKeys(tracker,identities);
            string leftPath=settings.Log,rightPath=settings.Log;
            bool changingSelections=false;
            Action fillChoices=delegate{
                changingSelections=true;
                string oldLeft=leftChoice.SelectedIndex>=0&&leftChoice.SelectedIndex<leftKeys.Count?leftKeys[leftChoice.SelectedIndex]:null;
                string oldRight=rightChoice.SelectedIndex>=0&&rightChoice.SelectedIndex<rightKeys.Count?rightKeys[rightChoice.SelectedIndex]:null;
                leftKeys=ComparePlayerKeys(leftSource,leftNames);rightKeys=ComparePlayerKeys(rightSource,rightNames);
                leftChoice.Items.Clear();rightChoice.Items.Clear();
                foreach(var key in leftKeys)leftChoice.Items.Add(leftNames[key]+" ["+key+"]");
                foreach(var key in rightKeys)rightChoice.Items.Add(rightNames[key]+" ["+key+"]");
                if(leftKeys.Count>0)leftChoice.SelectedIndex=Math.Max(0,leftKeys.IndexOf(oldLeft));
                if(rightKeys.Count>0){int ix=rightKeys.IndexOf(oldRight);
                    rightChoice.SelectedIndex=ix>=0?ix:(mode.SelectedIndex==0&&rightKeys.Count>1?1:0);}
                changingSelections=false;
            };
            fillChoices();
            var columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=background,Padding=new Padding(5)};
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var left=MakeCompareColumn();var right=MakeCompareColumn();
            columns.Controls.Add(left.View,0,0);columns.Controls.Add(right.View,1,0);
            compare.Controls.Add(columns);compare.Controls.Add(toolbar);
            // Frameless dark title strip, matching the main overlay instead of Windows chrome.
            var compareTitleBar=new Panel{Dock=DockStyle.Top,Height=32,BackColor=Color.FromArgb(10,11,13)};
            var compareTitle=new Label{Text="BLIGHTFALL POPS  /  COMPARE",ForeColor=Color.FromArgb(222,224,218),
                Font=new Font("Segoe UI",9,FontStyle.Bold),AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,
                Bounds=new Rectangle(12,0,340,32)};
            var compareClose=CaptionButton("×",red,compareTitleBar.BackColor);
            var compareMinimize=CaptionButton("−",dim,compareTitleBar.BackColor);
            compareClose.Click+=delegate{compare.Close();};
            compareMinimize.Click+=delegate{compare.WindowState=FormWindowState.Minimized;};
            compareTitleBar.Controls.AddRange(new Control[]{compareTitle,compareMinimize,compareClose});
            Action layoutCompareTitle=delegate{
                compareClose.Location=new Point(compareTitleBar.ClientSize.Width-42,0);
                compareMinimize.Location=new Point(compareTitleBar.ClientSize.Width-84,0);
                compareTitle.Width=Math.Max(80,compareTitleBar.ClientSize.Width-96);
            };
            compareTitleBar.Resize+=delegate{layoutCompareTitle();};
            compare.Controls.Add(compareTitleBar);layoutCompareTitle();
            Point? compareDragStart=null;
            Point compareDragOrigin=Point.Empty;
            MouseEventHandler startCompareDrag=delegate(object sender,MouseEventArgs e){
                if(e.Button==MouseButtons.Left){compareDragStart=Control.MousePosition;compareDragOrigin=compare.Location;}
            };
            MouseEventHandler moveCompareDrag=delegate(object sender,MouseEventArgs e){
                if(compareDragStart.HasValue){Point now=Control.MousePosition;
                    compare.Location=new Point(compareDragOrigin.X+now.X-compareDragStart.Value.X,
                        compareDragOrigin.Y+now.Y-compareDragStart.Value.Y);}
            };
            MouseEventHandler stopCompareDrag=delegate{compareDragStart=null;};
            compareTitleBar.MouseDown+=startCompareDrag;compareTitleBar.MouseMove+=moveCompareDrag;
            compareTitleBar.MouseUp+=stopCompareDrag;
            compareTitle.MouseDown+=startCompareDrag;compareTitle.MouseMove+=moveCompareDrag;
            compareTitle.MouseUp+=stopCompareDrag;

            Action<CompareColumn,Tracker,Dictionary<string,string>,string> populate=delegate(CompareColumn column,Tracker source,Dictionary<string,string> names,string guid){
                PopulateCompareColumn(column,source,names,guid,compareBlightfallEnabled,compareBeastEnabled);
            };
            Action refresh=delegate{
                if(changingSelections)return;
                if(leftChoice.SelectedIndex>=0&&leftChoice.SelectedIndex<leftKeys.Count)
                    populate(left,leftSource,leftNames,leftKeys[leftChoice.SelectedIndex]);
                else {left.InvalidateRender();left.Content.Controls.Clear();left.Content.Height=1;left.UpdateScroll();}
                if(rightChoice.SelectedIndex>=0&&rightChoice.SelectedIndex<rightKeys.Count)
                    populate(right,rightSource,rightNames,rightKeys[rightChoice.SelectedIndex]);
                else {right.InvalidateRender();right.Content.Controls.Clear();right.Content.Height=1;right.UpdateScroll();}
            };
            refreshEventSelection=delegate{left.InvalidateRender();right.InvalidateRender();refresh();};
            leftChoice.SelectedIndexChanged+=delegate{refresh();};rightChoice.SelectedIndexChanged+=delegate{refresh();};
            mode.SelectedIndexChanged+=delegate{
                if(mode.SelectedIndex==0){leftSource=rightSource=tracker;
                    leftNames=rightNames=CompareIdentities(tracker);}
                else { // Start with the current log on both sides; either side can load a different file.
                    leftSource=rightSource=tracker;
                    leftNames=rightNames=CompareIdentities(tracker);
                }
                layoutToolbar();fillChoices();refresh();
            };
            Action<bool> chooseFile=delegate(bool first){
                using(var picker=new OpenFileDialog{Title=first?"Choose combat log for player 1":"Choose combat log for player 2",
                    Filter="Combat logs (*.txt)|*.txt|All files (*.*)|*.*",CheckFileExists=true,
                    FileName=first?leftPath:rightPath}){
                    if(picker.ShowDialog(compare)!=DialogResult.OK)return;
                    string path=picker.FileName;
                    var button=first?leftFile:rightFile;
                    button.Enabled=false;button.Text="Loading...";
                    Task.Run(delegate{return ReadCompareLog(path);}).ContinueWith(task=>{
                        if(compare.IsDisposed||!compare.IsHandleCreated)return;
                        try{compare.BeginInvoke((Action)delegate{
                            if(compare.IsDisposed)return;
                            button.Enabled=true;button.Text=first?"Choose log 1...":"Choose log 2...";
                            if(task.IsFaulted||task.IsCanceled){
                                MessageBox.Show(compare,task.Exception!=null?task.Exception.GetBaseException().Message:"Loading canceled",
                                    "Unable to read combat log",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;
                            }
                            if(first){leftSource=task.Result;leftNames=CompareIdentities(leftSource);leftPath=path;}
                            else {rightSource=task.Result;rightNames=CompareIdentities(rightSource);rightPath=path;}
                            fillChoices();refresh();
                            toolTip.SetToolTip(button,path);
                        });}catch(InvalidOperationException){}
                    });
                }
            };
            leftFile.Click+=delegate{chooseFile(true);};rightFile.Click+=delegate{chooseFile(false);};
            compare.Shown+=delegate{refresh();};
            // WinForms raises ResizeEnd after MOVING as well as resizing. Only rebuild when
            // a column actually changes width; height changes merely update scrolling.
            // Coalesce repeated width changes into one rebuild after resizing settles.
            // Keep height-only resizing cheap, and avoid rebuilding while dragging.
            var compareResizeRefresh=new System.Windows.Forms.Timer{Interval=160};
            compareResizeRefresh.Tick+=delegate{
                compareResizeRefresh.Stop();
                if(compare.IsDisposed)return;
                int lw=Math.Max(150,left.View.ClientSize.Width-left.Track.Width-12);
                int rw=Math.Max(150,right.View.ClientSize.Width-right.Track.Width-12);
                if(lw!=left.RenderWidth||rw!=right.RenderWidth)refresh();
                else {left.UpdateScroll();right.UpdateScroll();}
            };
            compare.ResizeEnd+=delegate{
                compareResizeRefresh.Stop();
                compareResizeRefresh.Start();
            };
            compare.FormClosed+=delegate{compareResizeRefresh.Stop();compareResizeRefresh.Dispose();};
            // Compact-mode style: draw a dashed, card-sized grid while resizing.
            // Keep the actual controls unchanged until the drag is committed.
            var compareGrip=new Panel{Size=new Size(18,18),BackColor=Color.FromArgb(43,48,52),
                Cursor=Cursors.SizeNWSE,Anchor=AnchorStyles.Right|AnchorStyles.Bottom};
            compareGrip.Paint+=delegate(object sender,PaintEventArgs e){
                using(var pen=new Pen(dim,1.5f)){
                    e.Graphics.DrawLine(pen,5,15,15,5);
                    e.Graphics.DrawLine(pen,10,15,15,10);
                }
            };
            compare.Controls.Add(compareGrip);compareGrip.BringToFront();
            Action placeGrip=delegate{
                compareGrip.Location=new Point(compare.ClientSize.Width-19,compare.ClientSize.Height-19);
                compareGrip.BringToFront();
            };
            compare.Resize+=delegate{placeGrip();};placeGrip();
            Point? compareResizeStart=null;
            Size compareResizeOrigin=Size.Empty,compareResizeCandidate=Size.Empty;
            ResizeOutline compareOutline=null;
            PaintEventHandler paintCompareGrid=delegate(object sender,PaintEventArgs e){
                var size=compareResizeCandidate;
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var pen=new Pen(green,1.6f)){
                    pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen,1,1,Math.Max(1,size.Width-3),Math.Max(1,size.Height-3));
                    TextRenderer.DrawText(e.Graphics,size.Width+" × "+size.Height+" · Compare / Compact grid",
                        LabelFont(true),new Rectangle(10,8,Math.Max(1,size.Width-20),25),green,
                        TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                    int usableWidth=Math.Max(0,size.Width-22);
                    int half=usableWidth/2;
                    int top=toolbar.Height+12;
                    for(int side=0;side<2;side++){
                        int x=7+side*(half+8);
                        int w=Math.Max(1,half-10);
                        TextRenderer.DrawText(e.Graphics,side==0?"PLAYER 1":"PLAYER 2",LabelFont(true),
                            new Rectangle(x,top,w,23),green,TextFormatFlags.NoPrefix);
                        int y=top+29;
                        foreach(var height in new[]{77,55,55,55,55,55,55}){
                            if(y>=size.Height-8)break;
                            int shown=Math.Min(height,size.Height-y-8);
                            e.Graphics.DrawRectangle(pen,x,y,w,Math.Max(1,shown));
                            y+=height+4;
                        }
                    }
                }
            };
            Action<bool> endCompareResize=delegate(bool apply){
                if(!compareResizeStart.HasValue)return;
                var chosen=compareResizeCandidate;
                compareResizeStart=null;
                compareGrip.Capture=false;
                if(compareOutline!=null){compareOutline.Close();compareOutline.Dispose();compareOutline=null;}
                if(apply){compare.Size=chosen;refresh();}
            };
            compareGrip.MouseDown+=delegate(object sender,MouseEventArgs e){
                if(e.Button!=MouseButtons.Left||compareResizeStart.HasValue)return;
                compareResizeStart=Control.MousePosition;
                compareResizeOrigin=compare.Size;compareResizeCandidate=compare.Size;
                compareOutline=new ResizeOutline{Bounds=new Rectangle(compare.Location,compareResizeCandidate),
                    TopMost=compare.TopMost};
                compareOutline.Paint+=paintCompareGrid;
                compareOutline.Show(compare);compareGrip.Capture=true;
            };
            compareGrip.MouseMove+=delegate{
                if(!compareResizeStart.HasValue)return;
                var mouse=Control.MousePosition;
                compareResizeCandidate=new Size(
                    Math.Max(compare.MinimumSize.Width,compareResizeOrigin.Width+mouse.X-compareResizeStart.Value.X),
                    Math.Max(compare.MinimumSize.Height,compareResizeOrigin.Height+mouse.Y-compareResizeStart.Value.Y));
                if(compareOutline!=null){
                    compareOutline.Bounds=new Rectangle(compare.Location,compareResizeCandidate);
                    compareOutline.Invalidate();
                }
            };
            compareGrip.MouseUp+=delegate(object sender,MouseEventArgs e){
                if(e.Button==MouseButtons.Left)endCompareResize(true);
            };
            compareGrip.MouseCaptureChanged+=delegate{
                if(compareResizeStart.HasValue&&!compareGrip.Capture)endCompareResize(false);
            };
            compare.FormClosing+=delegate{endCompareResize(false);};
            // Independent top-level window: minimizing the overlay must not minimize Compare.
            compare.Show();
        }
        private void ShowPlayerMenu(){
            var menu=new ContextMenuStrip{BackColor=Color.FromArgb(30,33,37),ForeColor=Color.White,ShowImageMargin=false};
            foreach(var mode in new[]{"Myself","Specific Player","All Players"}){
                string chosen=mode;
                var item=new ToolStripMenuItem(mode){Checked=settings.PlayerMode==mode};
                item.Click+=delegate {
                    settings.PlayerMode=chosen;
                    if(chosen=="Specific Player"&&String.IsNullOrEmpty(settings.SelectedPlayerGuid)){
                        var players=tracker.GetPlayers();if(players.Count>0)settings.SelectedPlayerGuid=players[0].Guid;
                    }
                    scrollPixels=0;RefreshCards();Save();
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var compareItem=new ToolStripMenuItem("Compare two players...");
            compareItem.Click+=delegate{ShowCompareWindow();};
            menu.Items.Add(compareItem);
            menu.Items.Add(new ToolStripSeparator());
            var select=new ToolStripMenuItem("Select player ("+tracker.GetPlayers().Count+")");
            foreach(var player in tracker.GetPlayers()){
                var current=player;
                var item=new ToolStripMenuItem((String.IsNullOrEmpty(player.Name)?player.Guid:player.Name)+" ("+player.EventCount+")"){
                    Checked=settings.PlayerMode=="Specific Player"&&settings.SelectedPlayerGuid==player.Guid};
                item.Click+=delegate {settings.SelectedPlayerGuid=current.Guid;settings.PlayerMode="Specific Player";scrollPixels=0;RefreshCards();Save();};
                select.DropDownItems.Add(item);
            }
            if(select.DropDownItems.Count==0)select.Enabled=false;
            menu.Items.Add(select);
            // Do not dispose here: WinForms may still be processing the clicked menu item.
            menu.Show(Cursor.Position);
        }
        private Button AddAction(Control parent,string glyph,string hint,Color color,Action click) {
            var button=new Button{Text=glyph,Font=new Font("Segoe UI Symbol",14,FontStyle.Bold),ForeColor=color,BackColor=bar.BackColor,
                Width=31,Height=29,FlatStyle=FlatStyle.Flat,Margin=new Padding(1,0,1,0),TabStop=false};
            button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            toolTip.SetToolTip(button,hint);parent.Controls.Add(button);button.Click+=delegate {click();};return button;
        }
    }
}
