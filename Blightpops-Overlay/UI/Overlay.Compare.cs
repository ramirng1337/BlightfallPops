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
    internal sealed partial class Overlay {
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
            var result=new Tracker{TrackAllPlayers=true,WindowMs=settings.WindowMs};
            // A separate combat log must use the same configured aura spell IDs.
            // Otherwise its historical hit snapshots cannot contain custom debuffs.
            foreach(var watched in tracker.WatchedAuras)
                result.WatchedAuras[watched.Key]=watched.Value;
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
                var panel=column.Content;
                int width=Math.Max(150,column.View.ClientSize.Width-column.Track.Width-12);
                // Reuse the complete card tree when neither player, data nor column width changed.
                // Moving the window and height-only resizing never rebuild controls or icons.
                int eventCount=0;long lastSequence=-1;
                foreach(var candidate in source.Entries)if(candidate.PlayerGuid==guid&&((!candidate.Beast&&compareBlightfallEnabled)||(candidate.Beast&&compareBeastEnabled))){
                    eventCount++;if(candidate.Sequence>lastSequence)lastSequence=candidate.Sequence;
                }
                if(Object.ReferenceEquals(column.RenderSource,source)&&column.RenderGuid==guid&&
                    column.RenderWidth==width&&column.RenderCount==eventCount&&
                    column.RenderLastSequence==lastSequence){column.UpdateScroll();return;}
                int previousOffset=column.Offset;
                panel.SuspendLayout();
                // Clear() only detaches WinForms controls; dispose old cards to avoid leaking handles.
                while(panel.Controls.Count>0){Control old=panel.Controls[0];panel.Controls.RemoveAt(0);old.Dispose();}
                var events=new List<Entry>();foreach(var e in source.Entries)if(e.PlayerGuid==guid&&((!e.Beast&&compareBlightfallEnabled)||(e.Beast&&compareBeastEnabled)))events.Add(e);
                events.Sort(delegate(Entry a,Entry b){int t=a.Time.CompareTo(b.Time);return t!=0?t:a.Sequence.CompareTo(b.Sequence);});
                long dread=0,virulent=0;int hits=0;foreach(var e in events){dread+=e.Beast?e.Corrupted:e.Dread;virulent+=e.Beast?e.Life:e.Virulent;hits+=e.Hits.Count;}
                var header=new Panel{Width=width,Height=77,BackColor=Color.FromArgb(28,34,37),Margin=new Padding(2,2,2,8)};
                header.Controls.Add(new Label{Text=names[guid],ForeColor=Color.White,Font=new Font("Segoe UI",12,FontStyle.Bold),
                    Bounds=new Rectangle(12,8,width-24,27),AutoEllipsis=true});
                header.Controls.Add(new Label{Text=events.Count+" casts    •    "+Format(dread+virulent)+" damage    •    "+hits+" hits",
                    ForeColor=green,Font=new Font("Segoe UI",10,FontStyle.Bold),Bounds=new Rectangle(12,41,width-24,25),AutoEllipsis=true});
                panel.Controls.Add(header);
                int index=0;
                foreach(var e in events){
                    index++;
                    // Match the main overlay's compact card: 55px high, BF/DP/VP icons,
                    // two damage columns, hit counter and right-aligned total.
                    var card=new Panel{Width=width,Height=55,BackColor=rowColor,
                        Margin=new Padding(2,2,2,4)};
                    var accent=new Panel{Bounds=new Rectangle(0,0,3,55),BackColor=e.Beast?red:green};
                    card.Controls.Add(accent);
                    int iconSize=Math.Max(18,Math.Min(23,settings.IconSize));
                    int secondX=Math.Max(165,width/2-6);
                    int totalX=Math.Max(secondX+80,width-96);
                    var compareAuras=VisibleCustomAuras(e,true);
                    int statusCount=e.Beast?(settings.ShowBeastBlightfall?1:0):((settings.ShowSoulReaper?1:0)+(settings.ShowScythe?1:0));
                    int auraSlots=compareAuras.Count;
                    int statusX=width-9-(statusCount+auraSlots)*iconSize-Math.Max(0,statusCount+auraSlots-1)*3;
                    // Keep the VP damage value readable even with many configured auras.
                    int vpTextStart=secondX+iconSize+4;
                    int safeIconStart=Math.Max(vpTextStart+46,statusX);
                    int availableSlots=Math.Max(0,(width-9-safeIconStart+3)/(iconSize+3));
                    int customSlots=Math.Min(auraSlots,Math.Max(0,availableSlots-statusCount));
                    statusX=width-9-(statusCount+customSlots)*iconSize-Math.Max(0,statusCount+customSlots-1)*3;
                    var bf=SpellIcon(e.Beast?"BB":"BF",9,9,36);
                    card.Controls.Add(bf);
                    var title=Label((e.Beast?"Beast #":"#")+index+"  "+e.Time.ToString("HH:mm:ss"),Color.White,52,1,
                        Math.Max(35,secondX-60),true);
                    card.Controls.Add(title);
                    var count=Label(e.Hits.Count+(e.Hits.Count==1?" hit":" hits"),dim,
                        secondX-3,3,Math.Max(40,totalX-secondX));
                    card.Controls.Add(count);
                    var total=Label(Format(e.Beast?e.Corrupted+e.Life:e.Dread+e.Virulent),green,totalX,1,
                        Math.Max(35,width-totalX-8),true);
                    total.TextAlign=ContentAlignment.TopRight;card.Controls.Add(total);
                    var dp=SpellIcon(e.Beast?"CB":"DP",55,29,iconSize);
                    var vp=SpellIcon(e.Beast?"BiL":"VP",secondX,29,iconSize);
                    card.Controls.Add(dp);card.Controls.Add(vp);
                    var dpValue=Label(Format(e.Beast?e.Corrupted:e.Dread),e.Hits.Exists(h=>h.Kind==(e.Beast?"CB":"DP")&&h.Crit)?Color.Gold:dim,
                        55+iconSize+4,28,Math.Max(30,secondX-55-iconSize-8));
                    var vpValue=Label(Format(e.Beast?e.Life:e.Virulent),e.Beast?dim:VirulentDamageColor(e),
                        secondX+iconSize+4,28,Math.Max(16,(statusCount+customSlots>0?statusX:width-7)-secondX-iconSize-8));
                    card.Controls.Add(dpValue);card.Controls.Add(vpValue);
                    // Use the same status icons, activity indicators, and tooltips as main compact cards.
                    if(!e.Beast&&settings.ShowSoulReaper)
                        card.Controls.Add(SpellIcon("SR",statusX,29,iconSize,SoulReaperTip(e),e.SoulReaper));
                    if(e.Beast&&settings.ShowBeastBlightfall)
                        card.Controls.Add(SpellIcon("BF",statusX,29,iconSize,"Blightfall before Blood Beast",e.BlightfallBefore));
                    if(!e.Beast&&settings.ShowScythe){
                        string scTip="Festering Scythe: "+(e.Scythe.HasValue?(e.Scythe.Value?"active":"inactive"):"unknown");
                        card.Controls.Add(SpellIcon("SC",statusX+(statusCount-1)*(iconSize+3),29,iconSize,scTip,e.Scythe));
                    }
                    for(int ai=0;ai<customSlots;ai++){
                        int ax=statusX+(statusCount+ai)*(iconSize+3);
                        card.Controls.Add(CustomAuraIcon(compareAuras[ai],ax,29,iconSize));
                    }
                    var details=new Panel{Visible=false,BackColor=Color.FromArgb(27,31,36),
                        Location=new Point(5,55),Width=Math.Max(60,width-10)};
                    int y=5;
                    foreach(var hit in e.Hits){
                        int hitSize=Math.Min(32,Math.Max(23,settings.IconSize));
                        int rowWidth=Math.Max(70,width-20);
                        var hitRow=new BufferedPanel{Left=4,Top=y,Width=rowWidth,Height=hitSize+6,
                            BackColor=Color.FromArgb(23,28,33)};
                        hitRow.Controls.Add(SpellIcon(hit.Kind,7,3,hitSize));
                        string amount=Format(hit.Amount);
                        string target=String.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
                        string result=hit.Crit?"Crit":"Hit";
                        int amountX=13+hitSize;
                        using(var regular=new Font("Segoe UI",settings.TextSize))
                        using(var bold=new Font("Segoe UI",settings.TextSize,FontStyle.Bold)){
                            int amountWidth=TextRenderer.MeasureText(amount,regular).Width+6;
                            int critWidth=TextRenderer.MeasureText(result,bold).Width+6;
                            int critX=amountX+amountWidth+6;
                            int targetX=critX+critWidth+8;
                            int targetWidth=Math.Max(20,rowWidth-targetX-8);
                            int nameWidth=TextRenderer.MeasureText(target,regular).Width;
                            var debuffs=HitDebuffIcons(hit,true);
                            int auraSize=Math.Max(18,Math.Min(26,settings.IconSize));
                            int auraWidth=debuffs.Count*(auraSize+3);
                            hitRow.Controls.Add(Label(amount,hit.Crit?Color.Gold:dim,amountX,5,amountWidth));
                            hitRow.Controls.Add(Label(result,hit.Crit?Color.Gold:dim,critX,5,critWidth,true));
                            int auraX=targetX+nameWidth+5,auraY=5;
                            Label targetLabel;
                            if(nameWidth>targetWidth){
                                int wrapWidth=Math.Max(25,rowWidth-amountX-8);
                                int wrapHeight=Math.Max(20,TextRenderer.MeasureText(target,regular,
                                    new Size(wrapWidth,int.MaxValue),TextFormatFlags.WordBreak).Height);
                                targetLabel=Label(target,dim,amountX,hitSize+6,wrapWidth);
                                targetLabel.AutoEllipsis=false;targetLabel.Height=wrapHeight+3;
                                hitRow.Height=hitSize+9+wrapHeight+3;
                                auraX=amountX;auraY=hitRow.Height+2;
                            }else{
                                targetLabel=Label(target,dim,targetX,5,Math.Min(targetWidth,nameWidth+2));
                                if(auraX+auraWidth>rowWidth-5){auraX=amountX;auraY=hitRow.Height+2;}
                            }
                            hitRow.Controls.Add(targetLabel);
                            toolTip.SetToolTip(targetLabel,target);
                            foreach(var aura in debuffs){
                                if(auraX+auraSize>rowWidth-6){auraX=amountX;auraY+=auraSize+3;}
                                hitRow.Controls.Add(CustomAuraIcon(aura,auraX,auraY,auraSize));
                                auraX+=auraSize+3;
                            }
                            if(debuffs.Count>0&&auraY>5)hitRow.Height=Math.Max(hitRow.Height,auraY+auraSize+3);
                            if(settings.ShowOverkill&&hit.Overkill>0){
                                var overkill=Label("Overkill: "+Format(hit.Overkill),dim,amountX,hitRow.Height,rowWidth-amountX-8);
                                hitRow.Controls.Add(overkill);hitRow.Height+=25;
                            }
                        }
                        if(hit.Crit)hitRow.Controls.Add(new Panel{Left=1,Top=3,Width=2,
                            Height=Math.Max(2,hitRow.Height-6),BackColor=Color.Gold});
                        details.Controls.Add(hitRow);y+=hitRow.Height+2;
                    }
                    if(e.Hits.Count==0){
                        details.Controls.Add(new Label{Text="No recorded hits",ForeColor=dim,
                            Font=LabelFont(false),Bounds=new Rectangle(8,5,Math.Max(40,width-28),24)});
                        y+=25;
                    }
                    details.Height=y+5;card.Controls.Add(details);
                    Action toggle=delegate{
                        details.Visible=!details.Visible;
                        card.Height=details.Visible?55+details.Height+4:55;
                        accent.Height=card.Height;
                        panel.Height=CompareContentHeight(panel);column.UpdateScroll();
                    };
                    bf.Cursor=title.Cursor=count.Cursor=total.Cursor=dp.Cursor=vp.Cursor=dpValue.Cursor=vpValue.Cursor=Cursors.Hand;
                    bf.Click+=delegate{toggle();};title.Click+=delegate{toggle();};
                    count.Click+=delegate{toggle();};total.Click+=delegate{toggle();};
                    dp.Click+=delegate{toggle();};vp.Click+=delegate{toggle();};
                    dpValue.Click+=delegate{toggle();};vpValue.Click+=delegate{toggle();};
                    panel.Controls.Add(card);
                }
                panel.Width=Math.Max(120,column.View.ClientSize.Width-column.Track.Width);
                panel.Height=CompareContentHeight(panel);panel.ResumeLayout();
                column.RenderSource=source;column.RenderGuid=guid;column.RenderWidth=width;
                column.RenderCount=eventCount;column.RenderLastSequence=lastSequence;
                column.Offset=previousOffset;column.UpdateScroll();
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
