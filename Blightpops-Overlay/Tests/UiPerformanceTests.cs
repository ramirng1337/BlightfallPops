using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using BlightfallPopsDesktop;
class UiPerformanceTests {
 static int checks;
 static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Field(object o,string name){return o.GetType().GetField(name,Hidden|BindingFlags.Public).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,Hidden|BindingFlags.Public).SetValue(o,value);}
 static void Call(object o,string name,params object[] args){o.GetType().GetMethod(name,Hidden).Invoke(o,args);}
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static Entry MakeEntry(int n){var e=new Entry{Number=n,Sequence=n,Time=new DateTime(2026,9,30,12,0,n),Segment="Test",Dread=100,Virulent=200};
 e.Hits.Add(new Hit{Kind="DP",Amount=100,Overkill=30,Crit=true,Target="A long enemy name that wraps at smaller window widths"});
 e.Hits.Add(new Hit{Kind="VP",Amount=200,Target="Enemy"});return e;}
 static Control Card(IDictionary cache,Entry e){return (Control)Field(cache[e],"Card");}
 [STAThread] static void Main(){
 Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 using(var overlay=new Overlay(false)){
 var settings=(Settings)Field(overlay,"settings");settings.Log="";settings.SideBySide=false;settings.ShowBeasts=true;settings.TextSize=10;settings.IconSize=26;
 settings.ShowOverkill=true;settings.GroupByEventType=false;overlay.ClientSize=new System.Drawing.Size(500,390);
 var menu=(Control)Field(overlay,"options");Button bb=null,sr=null,sc=null;
 foreach(Control c in menu.Controls){if((c.Tag as string)=="BB")bb=c as Button;if((c.Tag as string)=="SR")sr=c as Button;if((c.Tag as string)=="SC")sc=c as Button;}
 Assert(bb!=null&&sr!=null&&sc!=null,"All spell toggles are in options");
 Assert(bb.Size==sr.Size&&sr.Size==sc.Size&&bb.Left==sr.Left&&sr.Left==sc.Left,"Icon sizes and columns align");
 Assert(sr.Top-bb.Top==sc.Top-sr.Top,"Icon row spacing is equal");
 foreach(Control c in menu.Controls)if(c!=bb&&c!=sr&&c!=sc)Assert(!c.Bounds.IntersectsWith(bb.Bounds)&&!c.Bounds.IntersectsWith(sr.Bounds)&&!c.Bounds.IntersectsWith(sc.Bounds),"Option controls do not overlap icons");
 Assert(settings.GetType().GetField("KeepGameFocus")==null,"Focus toggle is no longer persisted");
 bool oldScythe=settings.ShowScythe;typeof(Control).GetMethod("OnClick",Hidden).Invoke(sc,new object[]{EventArgs.Empty});Assert(settings.ShowScythe!=oldScythe,"Scythe icon toggles visibility");typeof(Control).GetMethod("OnClick",Hidden).Invoke(sc,new object[]{EventArgs.Empty});
 bool oldSoul=settings.ShowSoulReaper;typeof(Control).GetMethod("OnClick",Hidden).Invoke(sr,new object[]{EventArgs.Empty});Assert(settings.ShowSoulReaper!=oldSoul,"Soul Reaper icon toggles visibility");typeof(Control).GetMethod("OnClick",Hidden).Invoke(sr,new object[]{EventArgs.Empty});
 var tracker=(Tracker)Field(overlay,"tracker");var cache=(IDictionary)Field(overlay,"cardCache");
 foreach(bool mini in new[]{false,true}){
 settings.MiniCards=mini;tracker.Reset();var first=MakeEntry(1);var next=MakeEntry(2);tracker.Entries.Add(first);tracker.Entries.Add(next);
 Call(overlay,"RefreshCards",false);var card=Card(cache,first);var secondCard=Card(cache,next);int height=card.Height;
 Call(overlay,"ToggleDetails",first,true);var rows=(IDictionary)Field(cache[first],"Rows");var row=rows[first.Hits[0]];
 Assert(card.Height>height,"Dropdown grows clicked card");Assert(Object.ReferenceEquals(card,Card(cache,first)),"Dropdown retains card");
 Assert(Object.ReferenceEquals(secondCard,Card(cache,next)),"Dropdown retains unrelated card");
 Assert((int)secondCard.Tag>(int)card.Tag+height,"Following card moves below expanded rows");
 Call(overlay,"ToggleDetails",first,true);Assert(card.Height==height,"Closing restores exact height");
 Call(overlay,"ToggleDetails",first,true);Assert(Object.ReferenceEquals(row,rows[first.Hits[0]]),"Reopening reuses hit row");
 Call(overlay,"ToggleDetails",first,false);Assert(rows.Count==2,"Second dropdown creates only missing row");
 int openHeight=(int)overlay.GetType().GetMethod("PreviewEventHeight",Hidden).Invoke(overlay,new object[]{first,card.Width,mini});
 Assert(openHeight==card.Height,"Preview matches expanded event height in both modes");
 tracker.Entries.Add(MakeEntry(3));Call(overlay,"RefreshCards",true);
 Assert(Object.ReferenceEquals(secondCard,Card(cache,next)),"Appending retains historical card");
 Assert(Object.ReferenceEquals(row,rows[first.Hits[0]]),"Appending retains open hit row");
 first.Hits.Add(new Hit{Kind="DP",Amount=50,Target="Late lethal hit"});first.Dread+=50;Call(overlay,"RefreshCards",true);
 Assert(Object.ReferenceEquals(card,Card(cache,first)),"Late damage retains outer card");
 Assert(Object.ReferenceEquals(secondCard,Card(cache,next)),"Late damage leaves unrelated card intact");
 Assert(((IDictionary)Field(cache[first],"Rows")).Count==3,"Late damage appears in open dropdowns");
 settings.ShowBeasts=false;var beast=MakeEntry(4);beast.Beast=true;tracker.Entries.Add(beast);Call(overlay,"RefreshCards",false);
 Assert(!cache.Contains(beast),"Hidden beast does not allocate a card");settings.ShowBeasts=true;Call(overlay,"RefreshCards",false);
 Assert(cache.Contains(beast),"Enabling beast renders it");
 var size=overlay.Size;Call(overlay,"ResetSession");
 Assert(cache.Count==0,"Session reset removes all cached cards");Assert(card.IsDisposed&&secondCard.IsDisposed,"Reset disposes historical cards");
 Assert(tracker.Entries.Count==0,"Session reset clears tracker");Assert(overlay.Size==size,"Reset preserves window size");
 }
 // Wide first-column numbers retain their measured space in narrow cards.
 tracker.Reset();var millions=MakeEntry(1);millions.Dread=10500000;millions.Virulent=10100000;tracker.Entries.Add(millions);
 overlay.ClientSize=new System.Drawing.Size(385,390);settings.IconSize=23;settings.TextSize=14;
 foreach(bool miniLayout in new[]{true,false}){
 settings.MiniCards=miniLayout;Call(overlay,"RefreshCards",false);
 var state=cache[millions];var panel=Card(cache,millions);var firstLabel=(Label)Field(state,"FirstLabel");var secondLabel=(Label)Field(state,"SecondLabel");
 Assert(firstLabel.Width>=TextRenderer.MeasureText(firstLabel.Text,firstLabel.Font).Width,"10M first damage value has enough width");
 PictureBox secondIcon=null;foreach(Control child in panel.Controls)if((child.Tag as string)=="VP")secondIcon=child as PictureBox;
 int secondX=secondIcon.Left;
 Assert(secondLabel.Left==secondX+secondIcon.Width+4,"Second number follows VP icon");
 bool aligned=false;foreach(Control child in panel.Controls){var label=child as Label;if(label!=null&&label.Text.EndsWith(" hits"))aligned=miniLayout?label.Left==secondX-3:label.Top==firstLabel.Top;}
 Assert(aligned,"Hit count stays aligned above VP");
 }
 // Narrow normal cards must keep both amounts and the complete count readable.
 Assert(new Settings().MiniCards,"Compact events are the default");
 settings.MiniCards=false;settings.ShowSoulReaper=true;settings.ShowScythe=true;
 foreach(int windowWidth in new[]{340,385,500})foreach(int textSize in new[]{10,14,18})foreach(int iconSize in new[]{18,40})foreach(bool beastLayout in new[]{false,true}){
 tracker.Reset();var sample=MakeEntry(1);sample.Beast=beastLayout;sample.Dread=10500000;sample.Virulent=10100000;
 sample.Corrupted=501100;sample.Life=496800;sample.Segment="Pull 1";tracker.Entries.Add(sample);
 settings.TextSize=textSize;settings.IconSize=iconSize;overlay.ClientSize=new System.Drawing.Size(windowWidth,600);
 Call(overlay,"RefreshCards",false);var state=cache[sample];var panel=Card(cache,sample);
 var left=(Label)Field(state,"FirstLabel");var right=(Label)Field(state,"SecondLabel");
 Assert(left.Width>=TextRenderer.MeasureText(left.Text,left.Font).Width,"Narrow normal first damage fits");
 Assert(right.Width>=TextRenderer.MeasureText(right.Text,right.Font).Width,"Narrow normal second damage fits");
 Assert(!left.Bounds.IntersectsWith(right.Bounds),"Damage labels never overlap");
 Assert(left.Left==right.Left&&right.Top>=left.Bottom,"Extra details align damage values vertically");
 PictureBox firstDamage=null,secondDamage=null;
 foreach(Control child in panel.Controls){if((child.Tag as string)==(beastLayout?"CB":"DP"))firstDamage=child as PictureBox;if((child.Tag as string)==(beastLayout?"BiL":"VP"))secondDamage=child as PictureBox;}
 Assert(firstDamage.Left==secondDamage.Left&&secondDamage.Top>firstDamage.Top,"Extra detail spell icons share one column");
 bool encounter=false;foreach(Control child in panel.Controls)if(child.Text==sample.Segment){encounter=true;Assert(child.Bottom<firstDamage.Top,"Pull name sits above the event data");Assert(child.Font.Bold,"Pull name is bold");Assert(((Label)child).TextAlign==System.Drawing.ContentAlignment.TopCenter,"Pull name is centered in shared heading column");Assert(child.Font.Size==settings.TextSize,"Pull name uses selected text size");}
 Assert(encounter,"Normal summary includes encounter text");
 string expectedTitle=beastLayout?"Beast #1":sample.Time.ToString("HH:mm:ss")+" #1";bool headerFound=false;
 foreach(Control child in panel.Controls)if(child.Text==expectedTitle){headerFound=true;Assert(child.Left==6,"Extra-detail header starts at the left edge");}
 Assert(headerFound,"Blightfall header uses timestamp before event number");
 Assert(firstDamage.Left==55,"Damage column sits close beside large event icon");
 bool critCount=false;foreach(Control child in panel.Controls)if(child.Text=="1 crit"){critCount=true;Assert(child.ForeColor==System.Drawing.Color.Gold,"Crit count is yellow");Assert(child.Top==right.Top,"Crit count sits beside second damage row");}
 Assert(critCount,"Extra details include crit count");
 Assert(left.Right<=panel.Width-8&&right.Right<=panel.Width-8,"Damage stays inside normal card");
 if(!beastLayout){bool found=false;foreach(Control child in panel.Controls){var label=child as Label;
 if(label!=null&&label.Text.EndsWith(" hits")){found=true;Assert(label.Width>=TextRenderer.MeasureText(label.Text,label.Font).Width,"Hit count never ellipsizes");
 PictureBox vp=null;foreach(Control item in panel.Controls)if((item.Tag as string)=="VP")vp=item as PictureBox;
 Assert(label.Left>left.Right&&label.Top==left.Top,"Hit count sits beside first damage row");}}
 Assert(found,"Normal card has complete hit count");}
 int predictedHeight=(int)overlay.GetType().GetMethod("PreviewEventHeight",Hidden).Invoke(overlay,new object[]{sample,panel.Width,false});
 Assert(predictedHeight==panel.Height,"Responsive resize preview matches actual card height");
 }
 // VP highlights require at least four VP hits, all critical; other damage types do not count.
 foreach(bool miniVp in new[]{true,false}){
 settings.MiniCards=miniVp;settings.TextSize=10;settings.IconSize=26;overlay.ClientSize=new System.Drawing.Size(500,600);
 tracker.Reset();var vpEvent=MakeEntry(1);vpEvent.Hits.Clear();
 vpEvent.Hits.Add(new Hit{Kind="DP",Amount=100,Crit=false,Target="Enemy"});
 for(int i=0;i<3;i++)vpEvent.Hits.Add(new Hit{Kind="VP",Amount=100,Crit=true,Target="Enemy"});
 tracker.Entries.Add(vpEvent);Call(overlay,"RefreshCards",false);
 Assert(((Label)Field(cache[vpEvent],"SecondLabel")).ForeColor!=System.Drawing.Color.FromArgb(230,87,83),"Three critical VP hits do not highlight");
 vpEvent.Hits.Add(new Hit{Kind="VP",Amount=100,Crit=true,Target="Enemy"});Call(overlay,"RefreshCards",false);
 Assert(((Label)Field(cache[vpEvent],"SecondLabel")).ForeColor==System.Drawing.Color.FromArgb(230,87,83),"Four all-critical VP hits highlight despite noncrit DP");
 vpEvent.Hits[1].Crit=false;Call(overlay,"RefreshCards",false);
 Assert(((Label)Field(cache[vpEvent],"SecondLabel")).ForeColor!=System.Drawing.Color.FromArgb(230,87,83),"One normal VP hit removes red highlight");
 vpEvent.Hits[1].Crit=true;vpEvent.Beast=true;Call(overlay,"RefreshCards",false);
 Assert(((Label)Field(cache[vpEvent],"SecondLabel")).ForeColor!=System.Drawing.Color.FromArgb(230,87,83),"Blood Beast damage is never highlighted by VP rule");
 }
 // Full scan includes history older than the 8 MiB tail and works with live reading paused.
 string scanFile=System.IO.Path.GetTempFileName();
 try{
 string own="Player-1-Test,Tester,0x511,0x0,Creature-1-Test,Enemy,0xa28,0x0,";
 string stamp="10/1/2026 12:00:00.0000  ";
 string cast=stamp+"SPELL_CAST_SUCCESS,"+own+"1271967,Spell,0x20\n";
 string damage=stamp+"SPELL_DAMAGE,"+own+"1241167,Spell,0x20,200,200,-1,32,0,0,0,1,nil,nil\n";
 string summon=stamp+"SPELL_SUMMON,"+own+"434237,Beast,0x20\n";
 string beastHit=stamp+"SPELL_DAMAGE,Creature-1-Test,Beast,0xa28,0x0,Creature-2-Test,Enemy,0xa28,0x0,434574,Spell,0x20,100,100,-1,32,0,0,0,nil,nil,nil\n";
 System.IO.File.WriteAllText(scanFile,cast+damage+summon+beastHit+stamp+"SWING_DAMAGE,Creature-1-Test,Beast,0xa28,0x0,Creature-2-Test,Enemy,0xa28,0x0,300,300,-1,1,0,0,0,1,nil,nil\n"+new string('x',9*1024*1024)+"\n"+cast+damage,new System.Text.UTF8Encoding(false));
 settings.Log=scanFile;settings.WatchLog=false;settings.ShowBeasts=false;Call(overlay,"ScanEntireLog");
 Assert((bool)Field(overlay,"scanningFullLog"),"Full scan starts while live reading is paused");
 int scanBatches=0;while((bool)Field(overlay,"scanningFullLog")&&scanBatches++<10000)Call(overlay,"ContinueFullLogScan");
 Assert(!(bool)Field(overlay,"scanningFullLog"),"Full scan completes");
 Assert(tracker.Entries.Count==3,"Entire file retains two casts and an early Blood Beast");
 Assert(settings.ShowBeasts&&!settings.WatchLog,"Scan shows beasts and retains pause state");
 Assert(tracker.Entries.Exists(e=>e.Beast&&e.Corrupted==100&&e.Melee==300&&e.Hits.Count==1),"Historical beast pop and melee are separate");
 Assert(tracker.Entries.FindAll(e=>!e.Beast&&e.Virulent==200).Count==2,"Blightfalls before and after long padding retained");
 Assert((int)Field(overlay,"scrollPixels")==Math.Max(0,(int)Field(overlay,"totalContentHeight")-((Control)Field(overlay,"cards")).ClientSize.Height),"Full scan scrolls to latest event");
 Call(overlay,"ScanEntireLog");Call(overlay,"ResetSession");Assert(!(bool)Field(overlay,"scanningFullLog"),"New session cancels history scan");
 Assert(tracker.Entries.Count==0&&tracker.Position==new System.IO.FileInfo(scanFile).Length,"New session clears scanned history and starts at EOF");
 }finally{settings.Log="";settings.WatchLog=true;System.IO.File.Delete(scanFile);}
 // Melee appears only in beast extra details, with a reusable hit dropdown.
 tracker.Reset();var meleeCard=MakeEntry(1);meleeCard.Beast=true;meleeCard.Melee=300;
 var meleeHit=new Hit{Kind="ME",Amount=300,Crit=true,Target="Enemy"};meleeCard.MeleeHits.Add(meleeHit);tracker.Entries.Add(meleeCard);
 settings.MiniCards=false;Call(overlay,"RefreshCards",false);var beastPanel=Card(cache,meleeCard);int closedMeleeHeight=beastPanel.Height;
 bool hasMeleeIcon=false;foreach(Control child in beastPanel.Controls)if((child.Tag as string)=="ME")hasMeleeIcon=true;
 Assert(hasMeleeIcon,"Extra-detail beast has melee status icon");
 Control meleeMarker=null;foreach(Control child in beastPanel.Controls)if((child.Tag as string)=="ME")meleeMarker=child;
 Control lifeIcon=null;foreach(Control child in beastPanel.Controls)if((child.Tag as string)=="BiL")lifeIcon=child;
 Assert(meleeMarker!=null&&lifeIcon!=null&&meleeMarker.Size==lifeIcon.Size&&meleeMarker.Top==lifeIcon.Top,"Melee marker uses status icon size and second-row baseline");
 int enabledMarkerRight=meleeMarker.Right;
 Assert(enabledMarkerRight==beastPanel.Width-9,"Melee marker occupies fixed rightmost slot");
 bool beforeMeleeMarker=settings.ShowBeastBlightfall;settings.ShowBeastBlightfall=false;Call(overlay,"RefreshCards",false);
 Control standaloneMelee=null;foreach(Control child in beastPanel.Controls)if((child.Tag as string)=="ME")standaloneMelee=child;
 Assert(standaloneMelee!=null&&standaloneMelee.Right==enabledMarkerRight,"Melee slot stays fixed when BF marker is disabled");
 settings.ShowBeastBlightfall=beforeMeleeMarker;Call(overlay,"RefreshCards",false);
Call(overlay,"ToggleMeleeDetails",meleeCard);
 var meleeRows=(IDictionary)Field(cache[meleeCard],"Rows");Assert(meleeRows.Contains(meleeHit),"Melee dropdown creates target hit row");
 var expandedMeleeRow=(Control)meleeRows[meleeHit];
 // Visible inherits the hidden test form's state. Check the expansion geometry instead.
 Assert(meleeCard.MOpen&&expandedMeleeRow.Parent==beastPanel&&beastPanel.Height>closedMeleeHeight,"Melee dropdown expands the card");
 Assert(expandedMeleeRow.Top==(int)Field(cache[meleeCard],"DetailsTop")&&expandedMeleeRow.Bottom<beastPanel.Height,"Melee target row occupies expanded detail area");
 bool meleeTargetFound=false;foreach(Control child in expandedMeleeRow.Controls)if(child.Text==meleeHit.Target)meleeTargetFound=true;
 Assert(meleeTargetFound,"Melee dropdown includes the hit target");
 Assert(beastPanel.Height==(int)overlay.GetType().GetMethod("PreviewEventHeight",Hidden).Invoke(overlay,new object[]{meleeCard,beastPanel.Width,false}),"Preview includes melee dropdown");
 var cachedMeleeRow=meleeRows[meleeHit];Call(overlay,"ToggleMeleeDetails",meleeCard);Assert(beastPanel.Height==closedMeleeHeight,"Closing melee dropdown restores card height");
 Call(overlay,"ToggleMeleeDetails",meleeCard);Assert(Object.ReferenceEquals(cachedMeleeRow,meleeRows[meleeHit]),"Melee dropdown reuses hit row");
 settings.MiniCards=true;Call(overlay,"RefreshCards",false);foreach(Control child in Card(cache,meleeCard).Controls)Assert((child.Tag as string)!="ME","Compact view excludes melee row");
 // Different title lengths must not shift pull-name columns or change heading fonts.
 tracker.Reset();var beastHeader=MakeEntry(1);beastHeader.Beast=true;beastHeader.Corrupted=501100;beastHeader.Life=496800;
 var blightHeader=MakeEntry(2);blightHeader.Dread=239100;blightHeader.Virulent=782300;
 tracker.Entries.Add(beastHeader);tracker.Entries.Add(blightHeader);settings.TextSize=10;settings.MiniCards=false;
 foreach(int headerWidth in new[]{340,440}){
 overlay.ClientSize=new System.Drawing.Size(headerWidth,600);Call(overlay,"RefreshCards",false);
 Label beastPull=null,blightPull=null;
 foreach(Control child in Card(cache,beastHeader).Controls)if(child.Text==beastHeader.Segment)beastPull=child as Label;
 foreach(Control child in Card(cache,blightHeader).Controls)if(child.Text==blightHeader.Segment)blightPull=child as Label;
 Assert(beastPull!=null&&blightPull!=null,"Both event types show pull names");
 Assert(beastPull.Bounds==blightPull.Bounds,"Pull names share the same heading position and width");
 Assert(beastPull.Font.Size==blightPull.Font.Size&&beastPull.Font.Size==settings.TextSize,"Headers never shrink per event");
 Assert(Card(cache,beastHeader).Height==Card(cache,blightHeader).Height,"Closed detailed BF and BB cards have equal height");
 Control detailMelee=null,detailScythe=null;
 foreach(Control child in Card(cache,beastHeader).Controls)if((child.Tag as string)=="ME")detailMelee=child;
 foreach(Control child in Card(cache,blightHeader).Controls)if((child.Tag as string)=="SC")detailScythe=child;
 if(detailScythe!=null)Assert(detailMelee.Bounds==detailScythe.Bounds,"Melee and Scythe share the rightmost status slot");
 }
 settings.IconSize=26;settings.TextSize=10;overlay.ClientSize=new System.Drawing.Size(500,390);
 tracker.Reset();
 // Resizing changes geometry once at release, with only visible cards rebuilt.
 settings.MiniCards=false;settings.ShowBeasts=true;tracker.Reset();
 for(int i=1;i<=120;i++)tracker.Entries.Add(MakeEntry((i-1)%59+1));
 Call(overlay,"RefreshCards",false);
 var firstEntry=tracker.Entries[0];var distant=tracker.Entries[100];
 var firstPanel=Card(cache,firstEntry);var distantPanel=Card(cache,distant);
 Assert(firstPanel.Controls.Count>0,"Visible event rendered");
 Assert(distantPanel.Controls.Count==0,"Offscreen event defers control creation");
 var track=(Control)Field(overlay,"scrollTrack");var grip=(Control)Field(overlay,"grip");
 Assert(!track.Bounds.IntersectsWith(grip.Bounds),"Scrollbar and resize grip do not overlap");
 int predicted=(int)overlay.GetType().GetMethod("PreviewEventHeight",Hidden).Invoke(overlay,new object[]{firstEntry,firstPanel.Width,false});
 Assert(predicted==firstPanel.Height,"Preview predicts normal event height");
 var oldSize=overlay.Size;var header=firstPanel.Controls[0];
 Set(overlay,"resizeStart",new System.Drawing.Point(0,0));Set(overlay,"resizeCandidate",oldSize);
 Call(overlay,"UpdateResizePreview",new System.Drawing.Size(oldSize.Width+80,oldSize.Height+40));
 Assert(overlay.Size==oldSize,"Preview drag does not resize actual window");
 Assert(Object.ReferenceEquals(header,firstPanel.Controls[0]),"Preview drag does not rebuild events");
 Call(overlay,"EndResizePreview",false);Assert(overlay.Size==oldSize,"Cancel preserves size");
 Set(overlay,"resizeStart",new System.Drawing.Point(0,0));
 Call(overlay,"UpdateResizePreview",new System.Drawing.Size(oldSize.Width+80,oldSize.Height+40));
 Call(overlay,"EndResizePreview",true);
 Assert(overlay.Width==oldSize.Width+80&&overlay.Height==oldSize.Height+40,"Release applies preview size");
 Assert(Object.ReferenceEquals(firstPanel,Card(cache,firstEntry)),"Commit reuses outer card");
 Assert(distantPanel.Controls.Count==0,"Commit leaves distant cards deferred");
 Call(overlay,"ScrollTo",(int)distantPanel.Tag);
 Assert(distantPanel.Controls.Count>0,"Scrolling renders deferred event");
 var distantHeader=distantPanel.Controls[0];overlay.Height+=40;
 Assert(Object.ReferenceEquals(distantHeader,distantPanel.Controls[0]),"Height-only resize reuses event controls");
 settings.Locked=true;Call(overlay,"ApplyLock");Assert(!grip.Visible,"Lock hides resize handle");
 Assert(track.Bottom<=((Control)Field(overlay,"cards")).Bottom,"Locked scrollbar fits event area");
 settings.Locked=false;Call(overlay,"ApplyLock");Assert(!track.Bounds.IntersectsWith(grip.Bounds),"Unlock keeps dedicated resize corner");
 Call(overlay,"ToggleOptions");var optionsPanel=(Control)Field(overlay,"options");
 Assert(overlay.Controls.GetChildIndex(grip)<overlay.Controls.GetChildIndex(optionsPanel),"Resize handle stays above open options");
 overlay.Width+=10;Assert(overlay.Controls.GetChildIndex(grip)<overlay.Controls.GetChildIndex(optionsPanel),"Resize handle stays above options after resizing");
 Call(overlay,"ToggleOptions");
 // Large history must keep native positions bounded and reuse visible controls.
 settings.MiniCards=true;settings.GroupByEventType=false;tracker.Reset();
 overlay.ClientSize=new System.Drawing.Size(500,390);
 for(int i=0;i<1200;i++){
 var history=MakeEntry(1);history.Number=i+1;history.Sequence=i;history.Time=new DateTime(2026,10,1).AddSeconds(i);
 tracker.Entries.Add(history);
 }
 Call(overlay,"RefreshCards",false);
 int content=(int)Field(overlay,"totalContentHeight");
 Assert(content>65535,"History exceeds native signed child-coordinate range");
 var eventViewport=(Control)Field(overlay,"cards");
 var active=(IEnumerable)Field(overlay,"presentedCards");
 foreach(int position in new[]{content-eventViewport.Height,content/2,0,content-eventViewport.Height}){
 Call(overlay,"ScrollTo",position);int activeCount=0;
 var shown=new System.Collections.Generic.HashSet<Control>();
 foreach(object state in active){
 var panel=(Control)Field(state,"Card");activeCount++;shown.Add(panel);
 Assert(panel.Top==(int)panel.Tag-(int)Field(overlay,"scrollPixels"),"Visible card has correct viewport position");
 Assert(panel.Top>=-panel.Height-80&&panel.Top<=eventViewport.Height+80,"Visible native coordinates remain near viewport");
 Assert(panel.Controls.Count>0,"Entering viewport renders deferred controls");
 }
 Assert(activeCount>0&&activeCount<30,"Only a bounded viewport working set is presented");
 foreach(Control panel in eventViewport.Controls)Assert(Math.Abs(panel.Top)<2000,"Distant native controls never receive history offsets");
 }
 var latestPanel=Card(cache,tracker.Entries[1199]);
 var transparencyAction=(Button)Field(overlay,"transparencyButton");
 Assert(transparencyAction.Parent!=menu,"Transparency control lives in toolbar");
 foreach(Control option in menu.Controls)Assert(option.Text!="Transparent background","Transparency removed from Settings menu");
 overlay.ClientSize=new System.Drawing.Size(340,390);
 var toolbar=transparencyAction.Parent;int toolbarWidth=toolbar.Padding.Horizontal;
 foreach(Control action in toolbar.Controls)toolbarWidth+=action.Width+action.Margin.Horizontal;
 Assert(toolbarWidth<=toolbar.ClientSize.Width,"All toolbar buttons fit at minimum width");
 overlay.ClientSize=new System.Drawing.Size(500,390);
 Call(overlay,"ScrollTo",(int)Field(overlay,"totalContentHeight"));
 var latestHeader=latestPanel.Controls[0];
 var solidBackground=eventViewport.BackColor;var eventBackground=latestPanel.BackColor;
 settings.TransparentBackground=true;Call(overlay,"ApplyBackground");
 Assert(overlay.TransparencyKey==System.Drawing.Color.Magenta&&eventViewport.BackColor==overlay.TransparencyKey,"Transparent mode keys out empty viewport background");
 Assert(latestPanel.BackColor==eventBackground&&latestPanel.BackColor!=overlay.TransparencyKey,"Event cards remain opaque and readable");
 Assert(Object.ReferenceEquals(latestHeader,latestPanel.Controls[0]),"Transparency toggle does not rebuild events");
 settings.TransparentBackground=false;Call(overlay,"ApplyBackground");
 Assert(overlay.TransparencyKey==System.Drawing.Color.Empty&&eventViewport.BackColor==solidBackground,"Disabling transparency restores dark background");

 Call(overlay,"ScrollTo",(int)Field(overlay,"scrollPixels")-10);
 Assert(Object.ReferenceEquals(latestHeader,latestPanel.Controls[0]),"Small scroll reuses rendered controls");
 // Responsive columns preserve chronology, dropdown geometry and viewport bounds.
 tracker.Reset();settings.SideBySide=true;settings.GroupByEventType=false;settings.ShowBeasts=true;
 var gridEntries=new System.Collections.Generic.List<Entry>();
 for(int i=0;i<8;i++){
 var item=MakeEntry(1);item.Number=i+1;item.Sequence=i;item.Time=new DateTime(2026,10,1).AddSeconds(i);
 item.Beast=(i%2==1);gridEntries.Add(item);tracker.Entries.Add(item);
 }
 foreach(bool compact in new[]{true,false}){
 settings.MiniCards=compact;
 foreach(int windowWidth in new[]{679,680,1019,1020,1360,340}){
 overlay.ClientSize=new System.Drawing.Size(windowWidth,600);Call(overlay,"RefreshCards",false);Call(overlay,"ScrollTo",0);
 int columns=windowWidth/340;
 Assert((int)Field(overlay,"layoutColumns")==columns,"Column threshold matches window width");
 for(int i=0;i<gridEntries.Count;i++){
 var panel=Card(cache,gridEntries[i]);
 if(i%columns!=0){var leftPanel=Card(cache,gridEntries[i-1]);
 Assert((int)panel.Tag==(int)leftPanel.Tag,"Chronological neighbours share a row");
 Assert((int)Field(cache[gridEntries[i]],"ColumnLeft")>=(int)Field(cache[gridEntries[i-1]],"ColumnLeft")+leftPanel.Width,"Neighbour columns do not overlap");}
 if(i>=columns)Assert((int)panel.Tag>=(int)Field(cache[gridEntries[i-columns]],"RowBottom"),"Following row clears tallest previous card");
 Assert((int)Field(cache[gridEntries[i]],"ColumnLeft")+panel.Width<=((Control)Field(overlay,"cards")).ClientSize.Width,"Every column fits viewport");
 }
 }
 overlay.ClientSize=new System.Drawing.Size(680,600);Call(overlay,"RefreshCards",false);Call(overlay,"ScrollTo",0);
 var expanded=Card(cache,gridEntries[0]);var neighbour=Card(cache,gridEntries[1]);var following=Card(cache,gridEntries[2]);
 int closedRowTop=(int)following.Tag;var neighbourHeader=neighbour.Controls[0];
 Call(overlay,"ToggleDetails",gridEntries[0],true);
 Assert((int)following.Tag>closedRowTop,"Dropdown pushes next grid row down");
 Assert((int)following.Tag>=(int)expanded.Tag+expanded.Height,"Expanded dropdown does not overlap next row");
 Assert((int)expanded.Tag==(int)neighbour.Tag,"Neighbour remains aligned to same row top");
 Assert(Object.ReferenceEquals(neighbourHeader,neighbour.Controls[0]),"Grid dropdown reuses neighbour controls");
 Call(overlay,"ToggleDetails",gridEntries[0],true);Assert((int)following.Tag==closedRowTop,"Closing dropdown restores grid row");
 }
 settings.MiniCards=true;overlay.ClientSize=new System.Drawing.Size(1020,390);tracker.Reset();
 for(int i=0;i<1200;i++){var item=MakeEntry(1);item.Sequence=i;item.Time=new DateTime(2026,10,1).AddSeconds(i);tracker.Entries.Add(item);}
 Call(overlay,"RefreshCards",false);
 foreach(int offset in new[]{0,(int)Field(overlay,"totalContentHeight")/2,(int)Field(overlay,"totalContentHeight")}){
 Call(overlay,"ScrollTo",offset);int count=0;
 foreach(object state in (IEnumerable)Field(overlay,"presentedCards")){
 var panel=(Control)Field(state,"Card");count++;
 Assert(panel.Left==(int)Field(state,"ColumnLeft")&&panel.Top==(int)panel.Tag-(int)Field(overlay,"scrollPixels"),"Long grid scroll keeps visible geometry correct");
 }
 Assert(count>0&&count<90,"Long grid history presents only viewport rows");
 }
 settings.SideBySide=false;Call(overlay,"RefreshCards",false);
 Assert((int)Field(overlay,"layoutColumns")==1,"Disabling side-by-side restores one column");
 Call(overlay,"ResetSession");Assert(cache.Count==0,"Reset clears deferred and rendered cards");
 }
 Console.WriteLine("Passed "+checks+" UI performance checks");
 }
}
