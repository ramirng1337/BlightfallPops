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
    // Custom event card editor.
    internal sealed partial class Overlay {
        // Saved custom event definition.
        private sealed class CustomCard {
            public string Name="Custom event", SpellId="", IconPath="";
            public List<CustomEffect> Effects=new List<CustomEffect>();
        }
        private static CustomCard CloneCard(CustomCard source){
            var copy=new CustomCard{Name=source.Name,SpellId=source.SpellId,IconPath=source.IconPath};
            if(source.Effects!=null)foreach(var effect in source.Effects)if(effect!=null)copy.Effects.Add(CopyEffect(effect));
            return copy;
        }
        private static string CardDefinitionsPath {
            get { return Path.Combine(Settings.DirectoryPath,"event-cards.json"); }
        }
        private static List<CustomCard> ReadCustomCards(){
            try {
                if(File.Exists(CardDefinitionsPath)){
                    var result=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<CustomCard>>(File.ReadAllText(CardDefinitionsPath));
                    if(result!=null)return result;
                }
            }catch{}
            return new List<CustomCard>();
        }
        private static void WriteCustomCards(List<CustomCard> definitions){
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(CardDefinitionsPath,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(definitions),Encoding.UTF8);
        }
        private static Button BuilderButton(string text){
            var b=new Button{Text=text,Height=30,AutoSize=false,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(45,51,59),ForeColor=Color.White,Font=new Font("Segoe UI",9)};
            b.FlatAppearance.BorderColor=Color.FromArgb(68,76,85);return b;
        }
        // Uniform caption buttons for every borderless tool window.
        private static Button CaptionButton(string glyph, Color foreground, Color background){
            var button=new Button{Text=glyph,Width=42,Height=32,Margin=Padding.Empty,
                Padding=Padding.Empty,FlatStyle=FlatStyle.Flat,BackColor=background,
                ForeColor=foreground,Font=new Font("Segoe UI Symbol",11,FontStyle.Regular),
                TextAlign=ContentAlignment.MiddleCenter,TabStop=false,UseVisualStyleBackColor=false};
            button.FlatAppearance.BorderSize=0;
            button.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            button.FlatAppearance.MouseDownBackColor=Color.FromArgb(65,69,76);
            return button;
        }
        private static Label BuilderLabel(string text){return new Label{Text=text,ForeColor=Color.FromArgb(178,183,183),Height=22,AutoSize=false,Font=new Font("Segoe UI",9)};}
        private static TextBox BuilderText(string value){return new TextBox{Text=value,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,Font=new Font("Segoe UI",10),Height=26};}
        private static Image ReadBuilderImage(string path){
            try{if(!String.IsNullOrEmpty(path)&&File.Exists(path))using(var loaded=Image.FromFile(path))return new Bitmap(loaded);}catch{}
            return null;
        }
        private static string AssetFolder { get { return Path.Combine(Settings.DirectoryPath,"Assets","CustomIcons"); } }
        private static bool IsSupportedAsset(string path){
            string ext=Path.GetExtension(path).ToLowerInvariant();
            return ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".bmp"||ext==".gif";
        }
        private static string ImportAsset(string source){
            Directory.CreateDirectory(AssetFolder);
            if(!IsSupportedAsset(source))throw new InvalidOperationException("Unsupported image format.");
            // Validate image before copying it into the shared library.
            using(var img=Image.FromFile(source)){if(img.Width<1||img.Height<1)throw new InvalidDataException("Invalid image.");}
            string baseName=Path.GetFileNameWithoutExtension(source);
            foreach(char c in Path.GetInvalidFileNameChars())baseName=baseName.Replace(c,'_');
            if(String.IsNullOrWhiteSpace(baseName))baseName="icon";
            string ext=Path.GetExtension(source).ToLowerInvariant();
            string destination=Path.Combine(AssetFolder,baseName+ext);
            if(String.Equals(Path.GetFullPath(source),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))return destination;
            int suffix=2;
            while(File.Exists(destination))destination=Path.Combine(AssetFolder,baseName+"-"+(suffix++)+ext);
            File.Copy(source,destination);
            return destination;
        }
        private static string ChooseLibraryAsset(IWin32Window owner,string current){
            Directory.CreateDirectory(AssetFolder);
            string chosen=null;
            using(var window=new Form{Text="Blightfall Pops — Asset Library",Size=new Size(670,520),MinimumSize=new Size(480,380),
                StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(25,29,34),ForeColor=Color.White,ShowIcon=false}){
                var top=new Panel{Dock=DockStyle.Top,Height=75,Padding=new Padding(10,9,10,7)};
                var search=BuilderText("");search.Dock=DockStyle.Top;
                var searchCaption=BuilderLabel("SEARCH ICONS BY NAME");searchCaption.Dock=DockStyle.Top;
                top.Controls.Add(search);top.Controls.Add(searchCaption);
                var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(7)};
                var use=BuilderButton("Use selected icon");use.Width=155;
                var import=BuilderButton("+ Import images");import.Width=140;
                var remove=BuilderButton("Delete asset");remove.Width=105;
                var reset=BuilderButton("Use default icon");reset.Width=135;
                footer.Controls.Add(use);footer.Controls.Add(reset);footer.Controls.Add(remove);footer.Controls.Add(import);
                var list=new ListView{Dock=DockStyle.Fill,View=View.LargeIcon,MultiSelect=false,HideSelection=false,
                    BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,BorderStyle=BorderStyle.None};
                var images=new ImageList{ImageSize=new Size(54,54),ColorDepth=ColorDepth.Depth32Bit};
                list.LargeImageList=images;
                Action reload=delegate{
                    string old=list.SelectedItems.Count>0?list.SelectedItems[0].Tag as string:current;
                    list.BeginUpdate();list.Items.Clear();images.Images.Clear();
                    string[] paths=Directory.GetFiles(AssetFolder);
                    Array.Sort(paths,StringComparer.OrdinalIgnoreCase);
                    foreach(string path in paths){
                        if(!IsSupportedAsset(path)||Path.GetFileNameWithoutExtension(path).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)<0)continue;
                        using(var original=ReadBuilderImage(path)){
                            if(original==null)continue;
                            var thumb=new Bitmap(54,54);
                            using(var g=Graphics.FromImage(thumb)){
                                g.Clear(Color.FromArgb(42,47,54));
                                g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                float ratio=Math.Min(54f/original.Width,54f/original.Height);
                                int w=Math.Max(1,(int)(original.Width*ratio)),h=Math.Max(1,(int)(original.Height*ratio));
                                g.DrawImage(original,(54-w)/2,(54-h)/2,w,h);
                            }
                            images.Images.Add(thumb);
                        }
                        var item=new ListViewItem(Path.GetFileNameWithoutExtension(path),images.Images.Count-1){Tag=path};
                        list.Items.Add(item);
                        if(String.Equals(old,path,StringComparison.OrdinalIgnoreCase))item.Selected=true;
                    }
                    list.EndUpdate();
                };
                search.TextChanged+=delegate{reload();};
                import.Click+=delegate{
                    using(var dialog=new OpenFileDialog{Title="Import icons into asset library",Filter="Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",Multiselect=true}){
                        if(dialog.ShowDialog(window)!=DialogResult.OK)return;
                        foreach(string path in dialog.FileNames)try{current=ImportAsset(path);}catch(Exception ex){MessageBox.Show(window,ex.Message,"Import failed",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                        search.Text="";reload();
                    }
                };
                // Import files dropped from Explorer onto the library or its thumbnail grid.
                Action<string[]> importDropped=delegate(string[] paths){
                    int imported=0;
                    var errors=new System.Collections.Generic.List<string>();
                    foreach(string path in paths){
                        try{
                            if(!File.Exists(path)||!IsSupportedAsset(path))continue;
                            current=ImportAsset(path);
                            imported++;
                        }catch(Exception ex){errors.Add(Path.GetFileName(path)+": "+ex.Message);}
                    }
                    if(imported>0){search.Text="";reload();}
                    if(errors.Count>0)MessageBox.Show(window,String.Join("\n",errors.ToArray()),"Some images could not be imported",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                };
                Action<Control> enableDrop=delegate(Control target){
                    target.AllowDrop=true;
                    target.DragEnter+=delegate(object sender,DragEventArgs e){
                        if(!e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effect=DragDropEffects.None;return;}
                        var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
                        bool valid=false;
                        if(paths!=null)foreach(string path in paths)if(File.Exists(path)&&IsSupportedAsset(path)){valid=true;break;}
                        e.Effect=valid?DragDropEffects.Copy:DragDropEffects.None;
                    };
                    target.DragDrop+=delegate(object sender,DragEventArgs e){
                        if(!e.Data.GetDataPresent(DataFormats.FileDrop))return;
                        var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
                        if(paths!=null)importDropped(paths);
                    };
                };
                enableDrop(window);
                enableDrop(list);
                enableDrop(top);
                enableDrop(footer);
                remove.Click+=delegate{
                    if(list.SelectedItems.Count==0)return;
                    string path=list.SelectedItems[0].Tag as string;
                    if(MessageBox.Show(window,"Delete this icon from the shared library? Cards using it will need a replacement.","Delete asset",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
                    try{File.Delete(path);current=null;reload();}catch(Exception ex){MessageBox.Show(window,ex.Message,"Delete failed");}
                };
                use.Click+=delegate{if(list.SelectedItems.Count==0)return;chosen=list.SelectedItems[0].Tag as string;window.DialogResult=DialogResult.OK;window.Close();};
                list.DoubleClick+=delegate{use.PerformClick();};
                reset.Click+=delegate{chosen="";window.DialogResult=DialogResult.OK;window.Close();};
                window.Controls.Add(list);window.Controls.Add(footer);window.Controls.Add(top);
                reload();
                if(window.ShowDialog(owner)==DialogResult.OK)return chosen;
            }
            return null; // Cancel is distinct from selecting the default icon.
        }
        private static Bitmap MakeBuilderMenuIcon(){
            // Draw a sharp, transparent 20px card-and-pencil glyph for the cogwheel menu.
            var bitmap=new Bitmap(20,20,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var back=new SolidBrush(Color.FromArgb(54,74,95)))g.FillRectangle(back,2,3,13,13);
                using(var pen=new Pen(Color.FromArgb(162,201,230),1.5f))g.DrawRectangle(pen,2,3,13,13);
                using(var icon=new SolidBrush(Color.FromArgb(106,193,218)))g.FillRectangle(icon,4,5,5,5);
                using(var line=new Pen(Color.FromArgb(196,209,223),1.2f)){
                    g.DrawLine(line,10,7,13,7);g.DrawLine(line,10,10,13,10);g.DrawLine(line,4,13,11,13);
                }
                using(var pen=new Pen(Color.FromArgb(235,202,112),3f)){
                    pen.StartCap=System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen,11,17,18,10);
                }
            }
            return bitmap;
        }
        // Open custom card editor.
        private void ShowEventCardBuilder(){
            var definitions=ReadCustomCards();
            // Offer the existing built-in event types as editable presets, without
            // overwriting user-created cards or silently writing to disk.
            if(!definitions.Exists(c=>c.SpellId=="1271974"||c.SpellId=="1271967"))
                definitions.Insert(0,new CustomCard{Name="Blightfall (built-in)",SpellId="1271974"});
            if(!definitions.Exists(c=>c.SpellId=="434237"))
                definitions.Add(new CustomCard{Name="Blood Beast (built-in)",SpellId="434237"});
            var card=new CustomCard();
            var editor=new Form{Text="Blightfall Pops — Event Card Builder",Size=new Size(1060,650),MinimumSize=new Size(790,480),
                StartPosition=FormStartPosition.CenterScreen,BackColor=background,ForeColor=Color.White,FormBorderStyle=FormBorderStyle.None,ShowIcon=false,ShowInTaskbar=true};
            var titleBar=new Panel{Dock=DockStyle.Top,Height=33,BackColor=Color.FromArgb(10,11,13)};
            var heading=new Label{Text="BLIGHTFALL POPS  /  EVENT CARD BUILDER",ForeColor=Color.FromArgb(222,224,218),
                Font=new Font("Segoe UI",9,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill};
            heading.Padding=new Padding(12,0,0,0);
            var close=CaptionButton("×",red,titleBar.BackColor);close.Dock=DockStyle.Right;
            var minimize=CaptionButton("−",dim,titleBar.BackColor);minimize.Dock=DockStyle.Right;
            close.Click+=delegate{editor.Close();};minimize.Click+=delegate{editor.WindowState=FormWindowState.Minimized;};
            titleBar.Controls.Add(heading);titleBar.Controls.Add(minimize);titleBar.Controls.Add(close);editor.Controls.Add(titleBar);
            Point? drag=null;Point origin=Point.Empty;
            MouseEventHandler down=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){drag=Control.MousePosition;origin=editor.Location;}};
            MouseEventHandler move=delegate{if(drag.HasValue){Point pt=Control.MousePosition;editor.Location=new Point(origin.X+pt.X-drag.Value.X,origin.Y+pt.Y-drag.Value.Y);}};
            MouseEventHandler up=delegate{drag=null;};
            foreach(Control c in new Control[]{titleBar,heading}){c.MouseDown+=down;c.MouseMove+=move;c.MouseUp+=up;}
            var footer=new Panel{Dock=DockStyle.Bottom,Height=53,BackColor=Color.FromArgb(27,31,36),Padding=new Padding(12,9,52,9)};
            var save=BuilderButton("Save card");save.Dock=DockStyle.Right;save.Width=130;
            var deleteCard=BuilderButton("Delete card");deleteCard.Dock=DockStyle.Right;deleteCard.Width=120;deleteCard.Margin=new Padding(0,0,10,0);
            var statusLabel=new Label{Text="Card definitions are saved locally; live tracking is not changed.",Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft,ForeColor=dim,Font=new Font("Segoe UI",9)};
            footer.Controls.Add(statusLabel);footer.Controls.Add(save);footer.Controls.Add(deleteCard);editor.Controls.Add(footer);
            // SplitContainer starts with zero width before docking/layout. Setting minimums
            // during initialization can throw InvalidOperationException on .NET Framework.
            var split=new SplitContainer{Dock=DockStyle.Fill,BackColor=background};
            split.Panel1MinSize=0;split.Panel2MinSize=0;
            editor.Controls.Add(split);split.BringToFront();titleBar.BringToFront();footer.BringToFront();
            var left=new BufferedPanel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(25,29,34),TabStop=true};
            var leftTrack=new Panel{Dock=DockStyle.Right,Width=9,BackColor=Color.FromArgb(22,25,29)};
            var leftThumb=new Panel{BackColor=Color.FromArgb(91,102,110),Width=6,Cursor=Cursors.Hand};
            leftTrack.Controls.Add(leftThumb);
            split.Panel1.Controls.Add(left);left.Controls.Add(leftTrack);
            var form=new TableLayoutPanel{AutoSize=false,ColumnCount=2,RowCount=1,
                Location=new Point(14,12),Padding=new Padding(0,0,6,4),BackColor=left.BackColor};
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var eventFields=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=0,Dock=DockStyle.Top,Margin=new Padding(0,0,9,0)};
            var effectFields=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=0,Dock=DockStyle.Top,Margin=new Padding(9,24,0,0)};
            eventFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            effectFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            form.Controls.Add(eventFields,0,0);form.Controls.Add(effectFields,1,0);
            left.Controls.Add(form);
            int leftScroll=0;bool thumbDragging=false;int thumbDragY=0,thumbDragOffset=0;
            Action updateLeftScroll=delegate{
                int viewport=Math.Max(1,left.ClientSize.Height);
                int extent=Math.Max(0,form.Height+20-viewport);
                leftScroll=Math.Max(0,Math.Min(extent,leftScroll));
                form.Location=new Point(14,12-leftScroll);
                leftTrack.Visible=extent>0;
                if(extent==0)return;
                int trackHeight=Math.Max(1,leftTrack.ClientSize.Height);
                int thumbHeight=Math.Min(trackHeight,Math.Max(28,(int)((long)trackHeight*viewport/Math.Max(viewport,form.Height+20))));
                leftThumb.Bounds=new Rectangle(1,(int)((long)leftScroll*(trackHeight-thumbHeight)/Math.Max(1,extent)),6,thumbHeight);
            };
            Action layoutLeft=delegate{
                int w=Math.Max(310,left.ClientSize.Width-leftTrack.Width-24);
                if(form.Width!=w)form.Width=w;
                int half=Math.Max(145,(w-6)/2);
                eventFields.Width=half;effectFields.Width=half;
                eventFields.PerformLayout();effectFields.PerformLayout();
                // Include the top margin of the effects column; otherwise the last
                // row is clipped by the parent TableLayoutPanel even when the
                // outer window has plenty of unused space.
                form.Height=Math.Max(eventFields.PreferredSize.Height+eventFields.Margin.Vertical,
                    effectFields.PreferredSize.Height+effectFields.Margin.Vertical)+form.Padding.Vertical+8;
                form.PerformLayout();
                updateLeftScroll();
            };
            left.Resize+=delegate{layoutLeft();};form.SizeChanged+=delegate{updateLeftScroll();};
            MouseEventHandler leftWheel=delegate(object sender,MouseEventArgs e){leftScroll-=Math.Sign(e.Delta)*Math.Max(32,SystemInformation.MouseWheelScrollLines*23);updateLeftScroll();};
            left.MouseWheel+=leftWheel;form.MouseWheel+=leftWheel;leftTrack.MouseWheel+=leftWheel;leftThumb.MouseWheel+=leftWheel;
            leftTrack.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){leftScroll+=(e.Y<leftThumb.Top?-1:1)*Math.Max(60,left.Height-60);updateLeftScroll();}};
            leftThumb.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){thumbDragging=true;thumbDragY=Cursor.Position.Y;thumbDragOffset=leftScroll;leftThumb.Capture=true;}};
            leftThumb.MouseMove+=delegate{if(thumbDragging){int extent=Math.Max(0,form.Height+20-left.Height);int travel=Math.Max(1,leftTrack.Height-leftThumb.Height);leftScroll=thumbDragOffset+(Cursor.Position.Y-thumbDragY)*extent/travel;updateLeftScroll();}};
            leftThumb.MouseUp+=delegate{thumbDragging=false;leftThumb.Capture=false;};
            leftThumb.MouseCaptureChanged+=delegate{thumbDragging=false;};
            TableLayoutPanel activeFields=eventFields;
            // Explicit compact row heights avoid the large gaps created by AutoSize
            // TableLayoutPanel rows. Empty captions are actions, not empty label rows.
            Action<string,Control> addField=delegate(string caption,Control control){
                if(!String.IsNullOrEmpty(caption)){
                    var label=BuilderLabel(caption);
                    label.Dock=DockStyle.Fill;label.Margin=new Padding(0);
                    int row=activeFields.RowCount++;
                    activeFields.RowStyles.Add(new RowStyle(SizeType.Absolute,20));
                    activeFields.Controls.Add(label,0,row);
                }
                int controlHeight=control is ListBox?94:control is TableLayoutPanel?(control is TableLayoutPanel && control.Controls.Count>2?94:29):control is ComboBox?27:control is Button?29:26;
                control.Dock=DockStyle.Fill;
                control.Margin=new Padding(0,0,0,4);
                int fieldRow=activeFields.RowCount++;
                activeFields.RowStyles.Add(new RowStyle(SizeType.Absolute,controlHeight+4));
                activeFields.Controls.Add(control,0,fieldRow);
            };
            var presets=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,Height=29};
            var name=BuilderText(card.Name);var spell=BuilderText(card.SpellId);
            var imageButton=BuilderButton("Choose main icon (Asset Library)...");
            var clearImage=BuilderButton("Use Spell ID / default icon");
            var addBuff=BuilderButton("+ Add Buff");
            var addDebuff=BuilderButton("+ Add Debuff");
            var reuseEffect=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,Height=29,MaxDropDownItems=8,IntegralHeight=false};
            var templateSearch=BuilderText("");
            var reuseButton=BuilderButton("+ Add selected template");
            var templateEffects=new List<CustomEffect>();
            var addButtons=new TableLayoutPanel{ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.FromArgb(25,29,34)};
            addButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            addButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            addBuff.Dock=DockStyle.Fill;addDebuff.Dock=DockStyle.Fill;
            addBuff.Margin=new Padding(0,0,3,0);addDebuff.Margin=new Padding(3,0,0,0);
            addButtons.Controls.Add(addBuff,0,0);addButtons.Controls.Add(addDebuff,1,0);
            // Separate buff/debuff selectors; the hidden master list preserves the
            // existing editing and save logic, including its selected index.
            var effects=new ListBox();
            var buffList=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle,IntegralHeight=false};
            var debuffList=new ListBox{Dock=DockStyle.Fill,BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle,IntegralHeight=false};
            var effectLists=new TableLayoutPanel{ColumnCount=2,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=Color.FromArgb(25,29,34)};
            effectLists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            effectLists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            effectLists.RowStyles.Add(new RowStyle(SizeType.Absolute,18));
            effectLists.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            effectLists.Controls.Add(new Label{Text="BUFFS",ForeColor=dim,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8,FontStyle.Bold)},0,0);
            effectLists.Controls.Add(new Label{Text="DEBUFFS",ForeColor=dim,Dock=DockStyle.Fill,Font=new Font("Segoe UI",8,FontStyle.Bold)},1,0);
            buffList.Margin=new Padding(0,0,4,0);debuffList.Margin=new Padding(4,0,0,0);
            effectLists.Controls.Add(buffList,0,1);effectLists.Controls.Add(debuffList,1,1);
            var buffIndices=new List<int>();var debuffIndices=new List<int>();
            bool selectingEffect=false;
            buffList.SelectedIndexChanged+=delegate{
                if(selectingEffect||buffList.SelectedIndex<0)return;
                selectingEffect=true;debuffList.ClearSelected();effects.SelectedIndex=buffIndices[buffList.SelectedIndex];selectingEffect=false;
            };
            debuffList.SelectedIndexChanged+=delegate{
                if(selectingEffect||debuffList.SelectedIndex<0)return;
                selectingEffect=true;buffList.ClearSelected();effects.SelectedIndex=debuffIndices[debuffList.SelectedIndex];selectingEffect=false;
            };
            var effectName=BuilderText("");var effectSpell=BuilderText("");
            var effectType=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White};
            effectType.Items.AddRange(new object[]{"Buff","Debuff"});
            var condition=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White};
            condition.Items.AddRange(new object[]{"Active at event","Applied by event","Detected in event window","Removed in event window"});
            var showCompact=new CheckBox{Text="Show in Compact",Checked=false,AutoSize=true,ForeColor=Color.White,BackColor=Color.FromArgb(25,29,34)};
            var showDetailed=new CheckBox{Text="Show in Detailed",Checked=true,AutoSize=true,ForeColor=Color.White,BackColor=Color.FromArgb(25,29,34)};
            var effectImage=BuilderButton("Choose effect icon (Asset Library)...");var saveEffect=BuilderButton("Save selected effect");var removeEffect=BuilderButton("Remove selected effect");
            addField("Saved card",presets);addField("Card name",name);addField("Main Spell ID",spell);
            addField("Main icon",imageButton);addField("",clearImage);
            addField("Find saved effect template",templateSearch);
            addField("Select saved effect",reuseEffect);addField("",reuseButton);
            activeFields=effectFields;
            addField("Linked buffs / debuffs",effectLists);addField("",addButtons);
                        addField("Selected effect name",effectName);addField("Selected effect Spell ID",effectSpell);
            addField("Display condition",condition);
            addField("",showCompact);addField("",showDetailed);
            addField("Effect icon — optional custom image",effectImage);addField("",saveEffect);addField("",removeEffect);
            foreach(var fieldColumn in new[]{eventFields,effectFields})
                foreach(Control control in fieldColumn.Controls)if(!(control is ListBox)&&!(control is ComboBox))control.MouseWheel+=leftWheel;
            var right=new Panel{Dock=DockStyle.Fill,Padding=new Padding(14),BackColor=background};split.Panel2.Controls.Add(right);
            var previewHeading=BuilderLabel("LIVE EVENT CARD PREVIEW  •  CLICK AN ELEMENT TO EDIT");previewHeading.Dock=DockStyle.Top;
            var preview=new BufferedPanel{Dock=DockStyle.Top,Height=128,BackColor=background};
            right.Controls.Add(preview);right.Controls.Add(previewHeading);
            var cardSurface=new BufferedPanel{BackColor=rowColor,Height=94};preview.Controls.Add(cardSurface);
            var accent=new Panel{BackColor=green,Width=3,Dock=DockStyle.Left};cardSurface.Controls.Add(accent);
            var mainIcon=new PictureBox{Bounds=new Rectangle(10,26,42,42),BackColor=Color.FromArgb(45,51,59),SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
            var mainText=new Label{ForeColor=Color.White,Font=new Font("Segoe UI",10,FontStyle.Bold),Cursor=Cursors.Hand,AutoEllipsis=true};
            var subText=new Label{ForeColor=dim,Font=new Font("Segoe UI",8),Cursor=Cursors.Hand,AutoEllipsis=true};
            var previewTotal=new Label{Text="1.35M",ForeColor=green,Font=new Font("Segoe UI",10,FontStyle.Bold),TextAlign=ContentAlignment.TopRight,Cursor=Cursors.Hand};
            var previewHits=new Label{Text="8 hits",ForeColor=dim,Font=new Font("Segoe UI",8),TextAlign=ContentAlignment.TopRight,Cursor=Cursors.Hand};
            var effectPreview=new FlowLayoutPanel{Height=34,BackColor=rowColor,WrapContents=false,AutoScroll=false};
            cardSurface.Controls.AddRange(new Control[]{mainIcon,mainText,subText,previewTotal,previewHits,effectPreview});
            Action layoutPreview=delegate{
                int width=Math.Max(180,preview.ClientSize.Width-4);
                cardSurface.SetBounds(2,6,width,94);
                int infoWidth=Math.Max(40,width-175);
                mainText.SetBounds(61,10,infoWidth,24);
                subText.SetBounds(61,35,infoWidth,21);
                previewTotal.SetBounds(Math.Max(105,width-102),10,Math.Min(90,width-110),24);
                previewHits.SetBounds(Math.Max(105,width-102),35,Math.Min(90,width-110),21);
                // Keep the primary icon vertically centered in the 94px card, with linked effects to its right.
                mainIcon.Location=new Point(10,(cardSurface.Height-mainIcon.Height)/2);
                effectPreview.SetBounds(61,60,Math.Max(40,width-71),32);
            };
            preview.Resize+=delegate{layoutPreview();};layoutPreview();
            // All text and icon regions in the preview are direct editor shortcuts.
            mainText.Click+=delegate{name.Focus();};subText.Click+=delegate{spell.Focus();};
            previewTotal.Click+=delegate{name.Focus();};previewHits.Click+=delegate{spell.Focus();};
            mainIcon.Paint+=delegate(object sender,PaintEventArgs e){if(mainIcon.Image==null){
                using(var pen=new Pen(Color.FromArgb(145,164,184),1.5f)){
                    e.Graphics.DrawRectangle(pen,11,12,20,17);e.Graphics.DrawEllipse(pen,15,15,5,5);
                    e.Graphics.DrawLine(pen,12,27,20,21);e.Graphics.DrawLine(pen,20,21,28,27);
                }
            }};
            var ownedImages=new List<Image>();
            Action<Image,PictureBox> setImage=delegate(Image img,PictureBox box){
                var previous=box.Image;box.Image=img;
                if(previous!=null){ownedImages.Remove(previous);previous.Dispose();}
                if(img!=null)ownedImages.Add(img);
            };
            // Preview the same embedded artwork used by the overlay when no custom
            // library image is selected. Clone shared bitmaps because the preview owns
            // and disposes its image instances independently.
            Func<string,string,Image> previewIcon=delegate(string customPath,string spellId){
                Image selected=ReadBuilderImage(customPath);
                if(selected!=null)return selected;
                string key=null;
                switch((spellId??"").Trim()){
                    case "1271974": case "1271967": key="BF";break;
                    case "1240996": case "1241171": key="DP";break;
                    case "1241167": key="VP";break;
                    case "1241521": key="SR";break;
                    case "1241077": key="SC";break;
                    case "434237": key="BB";break;
                    case "374598": key="CB";break;
                    case "434246": key="BiL";break;
                }
                // Built-in Blightfall cards have a known default icon.
                if(key==null && String.IsNullOrWhiteSpace(spellId))key="BF";
                Image embedded;
                if(key!=null && spellIcons.TryGetValue(key,out embedded) && embedded!=null)
                    return new Bitmap(embedded);
                return null;
            };
            Action refresh=delegate{
                mainText.Text=String.IsNullOrWhiteSpace(name.Text)?"Custom event":name.Text;
                subText.Text="Spell ID: "+(String.IsNullOrWhiteSpace(spell.Text)?"not assigned":spell.Text);
                string previewKey=(card.IconPath??"")+"|"+(spell.Text??"");
                if(!String.Equals(mainIcon.Tag as string,previewKey,StringComparison.Ordinal)){
                    setImage(previewIcon(card.IconPath,spell.Text),mainIcon);mainIcon.Tag=previewKey;
                }
                mainIcon.Invalidate();
                while(effectPreview.Controls.Count>0){Control c=effectPreview.Controls[0];effectPreview.Controls.RemoveAt(0);c.Dispose();}
                foreach(var effect in card.Effects){
                    var pic=new PictureBox{Size=new Size(35,35),Margin=new Padding(2),BackColor=Color.FromArgb(45,51,59),SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
                    setImage(previewIcon(effect.IconPath,effect.SpellId),pic);
                    if(pic.Image==null){pic.Paint+=delegate(object sender,PaintEventArgs e){TextRenderer.DrawText(e.Graphics,"+",SystemFonts.MessageBoxFont,pic.ClientRectangle,dim,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};}
                    var selectedEffect=effect;pic.Click+=delegate{effects.SelectedIndex=card.Effects.IndexOf(selectedEffect);effectImage.PerformClick();};
                    toolTip.SetToolTip(pic,effect.Name+" ("+effect.Type+", "+effect.SpellId+")");
                    effectPreview.Controls.Add(pic);
                }
            };
            Action fillEffects=delegate{
                int previous=effects.SelectedIndex;
                selectingEffect=true;effects.Items.Clear();buffList.Items.Clear();debuffList.Items.Clear();buffIndices.Clear();debuffIndices.Clear();
                for(int n=0;n<card.Effects.Count;n++){
                    var e=card.Effects[n];
                    effects.Items.Add(e.Type+": "+e.Name+" ["+e.SpellId+"]");
                    if(e.Type=="Debuff"){debuffIndices.Add(n);debuffList.Items.Add(e.Name+" ["+e.SpellId+"]");}
                    else{buffIndices.Add(n);buffList.Items.Add(e.Name+" ["+e.SpellId+"]");}
                }
                selectingEffect=false;
                if(effects.Items.Count>0)effects.SelectedIndex=Math.Max(0,Math.Min(previous,effects.Items.Count-1));
                else effects.SelectedIndex=-1;
                refresh();
            };
            // Keep a full list so searching does not lose templates from saved cards.
            var allTemplateEffects=new List<CustomEffect>();
            var allTemplateLabels=new List<string>();
            Action filterEffectTemplates=delegate{
                string selectedText=reuseEffect.SelectedItem as string;
                string query=(templateSearch.Text??"").Trim();
                // SQL LIKE-style matching: plain text is a case-insensitive substring;
                // % matches any sequence, _ matches one character.
                string likePattern=".*"+System.Text.RegularExpressions.Regex.Escape(query)
                    .Replace(@"\%", ".*").Replace(@"\_", ".")
                    .Replace("%", ".*").Replace("_", ".")+".*";
                reuseEffect.BeginUpdate();
                reuseEffect.Items.Clear();templateEffects.Clear();
                for(int i=0;i<allTemplateEffects.Count;i++){
                    string label=allTemplateLabels[i];
                    if(query.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(label,likePattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant))continue;
                    templateEffects.Add(allTemplateEffects[i]);
                    reuseEffect.Items.Add(label);
                }
                reuseEffect.EndUpdate();
                int oldIndex=selectedText==null?-1:reuseEffect.Items.IndexOf(selectedText);
                if(oldIndex>=0)reuseEffect.SelectedIndex=oldIndex;
                else if(reuseEffect.Items.Count>0)reuseEffect.SelectedIndex=0;
                else reuseEffect.SelectedIndex=-1;
            };
            Action refreshEffectTemplates=delegate{
                allTemplateEffects.Clear();allTemplateLabels.Clear();
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var sourceCard in definitions){
                    if(sourceCard==null||sourceCard.Effects==null)continue;
                    foreach(var sourceEffect in sourceCard.Effects){
                        if(sourceEffect==null||String.IsNullOrWhiteSpace(sourceEffect.SpellId))continue;
                        string key=(sourceEffect.Type??"")+"|"+sourceEffect.SpellId;
                        if(!seen.Add(key))continue;
                        allTemplateEffects.Add(CopyEffect(sourceEffect));
                        allTemplateLabels.Add(sourceEffect.Type+": "+sourceEffect.Name+" ["+sourceEffect.SpellId+"]");
                    }
                }
                filterEffectTemplates();
            };
            templateSearch.TextChanged+=delegate{filterEffectTemplates();};
            reuseEffect.DropDown+=delegate{refreshEffectTemplates();};
            reuseButton.Click+=delegate{
                int selected=reuseEffect.SelectedIndex;
                if(selected<0||selected>=templateEffects.Count){
                    statusLabel.Text="Select a saved effect template first.";return;
                }
                var copy=CopyEffect(templateEffects[selected]);
                card.Effects.Add(copy);
                fillEffects();effects.SelectedIndex=card.Effects.Count-1;
                statusLabel.Text=copy.Name+" copied into this card. Click Save card to keep it.";
            };
            refreshEffectTemplates();
            Action fillPresets=delegate{
                presets.Items.Clear();presets.Items.Add("New card");foreach(var c in definitions)presets.Items.Add(c.Name);
            };
            bool loading=false;
            presets.SelectedIndexChanged+=delegate{
                if(loading)return;int i=presets.SelectedIndex;
                card=i>0&&i<=definitions.Count?CloneCard(definitions[i-1]):new CustomCard();
                loading=true;name.Text=card.Name;spell.Text=card.SpellId;loading=false;fillEffects();
            };
            name.TextChanged+=delegate{card.Name=name.Text;refresh();};
            spell.TextChanged+=delegate{card.SpellId=spell.Text;refresh();};
            Action<Button,Action<string>> chooseImage=delegate(Button button,Action<string> accept){
                button.Click+=delegate{using(var dialog=new OpenFileDialog{Filter="Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",Title="Select custom icon"}){
                    if(dialog.ShowDialog(editor)==DialogResult.OK)accept(dialog.FileName);
                }};
            };
            imageButton.Click+=delegate{string path=ChooseLibraryAsset(editor,card.IconPath);if(path!=null){card.IconPath=path;refresh();}};
            clearImage.Click+=delegate{card.IconPath="";refresh();};
            mainIcon.Click+=delegate{imageButton.PerformClick();};
            Action<string> createEffect=delegate(string type){
                // Save the current field edits in memory, then create a correctly typed effect.
                var created=new CustomEffect{Type=type,Name=type=="Buff"?"New Buff":"New Debuff",Condition="Active at event",ShowCompact=false,ShowDetailed=true};
                card.Effects.Add(created);
                fillEffects();
                effects.SelectedIndex=card.Effects.Count-1;
                effectName.Focus();effectName.SelectAll();
                statusLabel.Text=type+" added to draft. Enter name and Spell ID, then Save selected effect.";
            };
            addBuff.Click+=delegate{createEffect("Buff");};
            addDebuff.Click+=delegate{createEffect("Debuff");};
            effects.SelectedIndexChanged+=delegate{
                int i=effects.SelectedIndex;bool enabled=i>=0&&i<card.Effects.Count;
                selectingEffect=true;
                buffList.SelectedIndex=buffIndices.IndexOf(i);
                debuffList.SelectedIndex=debuffIndices.IndexOf(i);
                selectingEffect=false;
                foreach(Control c in new Control[]{effectName,effectSpell,effectType,condition,effectImage,saveEffect,removeEffect,showCompact,showDetailed})c.Enabled=enabled;
                loading=true;
                effectName.Text=enabled?card.Effects[i].Name:"";effectSpell.Text=enabled?card.Effects[i].SpellId:"";
                effectType.SelectedItem=enabled?card.Effects[i].Type:null;
                condition.SelectedItem=enabled?card.Effects[i].Condition:null;
                showCompact.Checked=enabled&&card.Effects[i].ShowCompact;
                showDetailed.Checked=enabled&&card.Effects[i].ShowDetailed;
                loading=false;
            };
            // Editing only changes the in-memory draft. Never rebuild the list or preview
            // on every keystroke: that used to cause lag and could disturb text focus.
            effectName.TextChanged+=delegate{if(!loading&&effects.SelectedIndex>=0)card.Effects[effects.SelectedIndex].Name=effectName.Text;};
            effectSpell.TextChanged+=delegate{if(!loading&&effects.SelectedIndex>=0)card.Effects[effects.SelectedIndex].SpellId=effectSpell.Text;};
            effectType.SelectedIndexChanged+=delegate{if(!loading&&effects.SelectedIndex>=0&&effectType.SelectedItem!=null)
                card.Effects[effects.SelectedIndex].Type=effectType.SelectedItem.ToString();};
            condition.SelectedIndexChanged+=delegate{if(!loading&&effects.SelectedIndex>=0&&condition.SelectedItem!=null)
                card.Effects[effects.SelectedIndex].Condition=condition.SelectedItem.ToString();};
            showCompact.CheckedChanged+=delegate{if(!loading&&effects.SelectedIndex>=0)card.Effects[effects.SelectedIndex].ShowCompact=showCompact.Checked;};
            showDetailed.CheckedChanged+=delegate{if(!loading&&effects.SelectedIndex>=0)card.Effects[effects.SelectedIndex].ShowDetailed=showDetailed.Checked;};
            Action commitEffect=delegate{
                int i=effects.SelectedIndex;
                if(i<0||i>=card.Effects.Count)return;
                var e=card.Effects[i];
                // Repaint only the changed row without replacing the selected item.
                effects.BeginUpdate();
                effects.Items[i]=e.Type+": "+e.Name+" ["+e.SpellId+"]";
                effects.SelectedIndex=i;
                effects.EndUpdate();
                // Refresh both selectors when an effect is renamed or changes type.
                fillEffects();effects.SelectedIndex=i;
                refresh();
                statusLabel.Text="Effect updated in draft. Click Save card to store it permanently.";
            };
            saveEffect.Click+=delegate{commitEffect();};
            effectName.Leave+=delegate{commitEffect();};
            effectSpell.Leave+=delegate{commitEffect();};
            effectImage.Click+=delegate{if(effects.SelectedIndex<0)return;string path=ChooseLibraryAsset(editor,card.Effects[effects.SelectedIndex].IconPath);if(path!=null){card.Effects[effects.SelectedIndex].IconPath=path;commitEffect();}};
            removeEffect.Click+=delegate{
                int i=effects.SelectedIndex;if(i<0||i>=card.Effects.Count)return;
                card.Effects.RemoveAt(i);
                fillEffects();
                if(card.Effects.Count>0)effects.SelectedIndex=Math.Min(i,card.Effects.Count-1);
                statusLabel.Text="Effect removed from draft. Click Save card to store the change.";
            };
            deleteCard.Click+=delegate{
                int selected=presets.SelectedIndex;
                if(selected<=0||selected>definitions.Count){
                    statusLabel.Text="Select a saved card to delete.";return;
                }
                var candidate=definitions[selected-1];
                // Built-in defaults are generated when the editor opens; deleting them
                // would only make them reappear next time. Keep them as safe templates.
                if(candidate.Name=="Blightfall (built-in)"||candidate.Name=="Blood Beast (built-in)"){
                    MessageBox.Show(editor,"Built-in templates cannot be deleted. You can edit and save them instead.",
                        "Event Card Builder",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
                }
                if(MessageBox.Show(editor,"Permanently delete the card '"+candidate.Name+"'?",
                    "Delete event card",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                definitions.RemoveAt(selected-1);
                try{
                    WriteCustomCards(definitions);
                    fillPresets();presets.SelectedIndex=0;
                    statusLabel.Text="Card deleted. Restart or re-scan before relying on previous aura tracking.";
                }catch(Exception ex){
                    definitions.Insert(selected-1,candidate);
                    MessageBox.Show(editor,ex.Message,"Unable to delete card",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    fillPresets();presets.SelectedIndex=selected;
                }
            };
            save.Click+=delegate{
                // Persist the current draft only on explicit Save card.
                int id; if(!int.TryParse(spell.Text,out id)||id<=0){MessageBox.Show(editor,"Enter a valid numeric main Spell ID.","Event Card Builder",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                foreach(var effect in card.Effects){int effectId;if(!int.TryParse(effect.SpellId,out effectId)||effectId<=0){MessageBox.Show(editor,"Each linked effect needs a valid numeric Spell ID.","Event Card Builder",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}}
                int selected=presets.SelectedIndex;
                if(selected>0&&selected<=definitions.Count)definitions[selected-1]=card;else definitions.Add(card);
                try{WriteCustomCards(definitions);fillPresets();presets.SelectedIndex=definitions.IndexOf(card)+1;refreshEffectTemplates();
                    if((card.SpellId=="1271967"||card.SpellId=="1271974"))foreach(var effect in card.Effects){
                        tracker.WatchedAuras[effect.SpellId]=effect.Type;customAuraInfo[effect.SpellId]=effect;
                    }
                    statusLabel.Text="Saved. Linked aura tracking applies to subsequent Blightfall casts. Re-scan the log for older events.";
                }catch(Exception ex){MessageBox.Show(editor,ex.Message,"Unable to save",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            };
            var grip=new Panel{Size=new Size(18,18),Cursor=Cursors.SizeNWSE,BackColor=Color.FromArgb(43,48,52),Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
            grip.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(dim,1.5f)){e.Graphics.DrawLine(pen,5,15,15,5);e.Graphics.DrawLine(pen,10,15,15,10);}};
            editor.Controls.Add(grip);grip.BringToFront();
            Action placeGrip=delegate{grip.Location=new Point(editor.ClientSize.Width-19,editor.ClientSize.Height-19);};
            editor.Resize+=delegate{placeGrip();};placeGrip();
            // Compare Mode style resize preview: the real WinForms controls stay
            // stationary during the drag, so textboxes and dropdowns never jitter.
            Point? resizeAt=null;Size original=Size.Empty,resizeCandidate=Size.Empty;
            ResizeOutline resizeOutline=null;
            PaintEventHandler paintBuilderGrid=delegate(object sender,PaintEventArgs e){
                var size=resizeCandidate;
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var pen=new Pen(green,1.5f)){
                    pen.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen,1,1,Math.Max(1,size.Width-3),Math.Max(1,size.Height-3));
                    TextRenderer.DrawText(e.Graphics,size.Width+" × "+size.Height+"  ·  Card Builder layout",
                        LabelFont(true),new Rectangle(12,8,Math.Max(1,size.Width-24),24),green,
                        TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                    int bodyTop=titleBar.Height+6,bodyBottom=size.Height-footer.Height-6;
                    int available=Math.Max(1,size.Width-10);
                    int divider=(int)(available*0.59);
                    int leftX=8,rightX=divider+12;
                    int leftWidth=Math.Max(1,divider-14),rightWidth=Math.Max(1,size.Width-rightX-10);
                    e.Graphics.DrawRectangle(pen,leftX,bodyTop,leftWidth,Math.Max(1,bodyBottom-bodyTop));
                    e.Graphics.DrawRectangle(pen,rightX,bodyTop,rightWidth,Math.Max(1,bodyBottom-bodyTop));
                    int colW=Math.Max(1,(leftWidth-26)/2);
                    for(int column=0;column<2;column++){
                        int x=leftX+8+column*(colW+10);
                        int y=bodyTop+12;
                        int[] heights=column==0?new[]{27,26,26,29,29}:new[]{78,29,26,26,27,27,29,29,29};
                        foreach(int h in heights){
                            if(y+h>bodyBottom-8)break;
                            e.Graphics.DrawRectangle(pen,x,y,colW,Math.Max(1,h));y+=h+8;
                        }
                    }
                    if(rightWidth>32)e.Graphics.DrawRectangle(pen,rightX+12,bodyTop+12,Math.Max(1,rightWidth-24),94);
                    e.Graphics.DrawRectangle(pen,Math.Max(1,size.Width-147),Math.Max(1,size.Height-43),130,29);
                }
            };
            Action stopBuilderResize=delegate{
                if(!resizeAt.HasValue)return;
                resizeAt=null;
                if(resizeOutline!=null){resizeOutline.Hide();resizeOutline.Dispose();resizeOutline=null;}
                if(editor.Size!=resizeCandidate)editor.Size=resizeCandidate;
                layoutLeft();layoutPreview();
            };
            grip.MouseDown+=delegate(object sender,MouseEventArgs e){
                if(e.Button!=MouseButtons.Left)return;
                resizeAt=Control.MousePosition;original=editor.Size;resizeCandidate=original;
                resizeOutline=new ResizeOutline{Bounds=new Rectangle(editor.Location,original)};
                resizeOutline.Paint+=paintBuilderGrid;
                resizeOutline.Show(editor);
                grip.Capture=true;
            };
            grip.MouseMove+=delegate{
                if(!resizeAt.HasValue||resizeOutline==null)return;
                Point pt=Control.MousePosition;
                resizeCandidate=new Size(Math.Max(editor.MinimumSize.Width,original.Width+pt.X-resizeAt.Value.X),
                    Math.Max(editor.MinimumSize.Height,original.Height+pt.Y-resizeAt.Value.Y));
                resizeOutline.Bounds=new Rectangle(editor.Location,resizeCandidate);
                resizeOutline.Invalidate();
            };
            grip.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)stopBuilderResize();};
            editor.FormClosing+=delegate{if(resizeOutline!=null){resizeOutline.Dispose();resizeOutline=null;}resizeAt=null;};
            editor.FormClosed+=delegate{foreach(var img in ownedImages)img.Dispose();};
            fillPresets();presets.SelectedIndex=0;fillEffects();
            editor.Shown+=delegate{
                layoutLeft();layoutPreview();
                // Wait until the form has a real client width before setting the divider.
                int usable=Math.Max(1,split.ClientSize.Width-split.SplitterWidth);
                int min=Math.Min(220,Math.Max(0,(usable-1)/2));
                split.Panel1MinSize=min;
                split.Panel2MinSize=min;
                int maximum=Math.Max(min,usable-min);
                split.SplitterDistance=Math.Max(min,Math.Min((int)(usable*0.59),maximum));
                // Reset any scroll offset induced by WinForms initial focus/layout.
                // Both columns must start below the titlebar, with captions visible.
                leftScroll=0;
                updateLeftScroll();
                editor.BeginInvoke((Action)delegate {
                    if(editor.IsDisposed)return;
                    leftScroll=0;
                    updateLeftScroll();
                });
            };
            // Independent top-level window: minimizing the overlay must not minimize the builder.
            editor.Show();
        }

    }
}
