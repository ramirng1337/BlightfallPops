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
 var settings=(Settings)Field(overlay,"settings");settings.Log="";settings.ShowBeasts=true;settings.TextSize=10;settings.IconSize=26;
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
 bool aligned=false;foreach(Control child in panel.Controls){var label=child as Label;if(label!=null&&label.Text.EndsWith(" hits"))aligned=label.Left==secondX-3;}
 Assert(aligned,"Hit count stays aligned above VP");
 }
 // Narrow normal cards must keep both amounts and the complete count readable.
 settings.MiniCards=false;settings.ShowSoulReaper=true;settings.ShowScythe=true;
 foreach(int windowWidth in new[]{340,385,500})foreach(int textSize in new[]{10,14,18})foreach(int iconSize in new[]{18,40})foreach(bool beastLayout in new[]{false,true}){
 tracker.Reset();var sample=MakeEntry(1);sample.Beast=beastLayout;sample.Dread=10500000;sample.Virulent=10100000;
 sample.Corrupted=501100;sample.Life=496800;sample.Segment="Estimated trash pull 1";tracker.Entries.Add(sample);
 settings.TextSize=textSize;settings.IconSize=iconSize;overlay.ClientSize=new System.Drawing.Size(windowWidth,600);
 Call(overlay,"RefreshCards",false);var state=cache[sample];var panel=Card(cache,sample);
 var left=(Label)Field(state,"FirstLabel");var right=(Label)Field(state,"SecondLabel");
 Assert(left.Width>=TextRenderer.MeasureText(left.Text,left.Font).Width,"Narrow normal first damage fits");
 Assert(right.Width>=TextRenderer.MeasureText(right.Text,right.Font).Width,"Narrow normal second damage fits");
 Assert(!left.Bounds.IntersectsWith(right.Bounds),"Damage labels never overlap");
 Assert(left.Top==right.Top,"Normal damage values always share one row");
 PictureBox firstDamage=null,secondDamage=null;
 foreach(Control child in panel.Controls){if((child.Tag as string)==(beastLayout?"CB":"DP"))firstDamage=child as PictureBox;if((child.Tag as string)==(beastLayout?"BiL":"VP"))secondDamage=child as PictureBox;}
 Assert(firstDamage.Top==secondDamage.Top,"Normal spell icons never stack");
 bool encounter=false;foreach(Control child in panel.Controls)if(child.Text==sample.Segment){encounter=true;Assert(child.Top>right.Bottom,"Encounter text sits below damage row");}
 Assert(encounter,"Normal summary includes encounter text");
 Assert(left.Right<=panel.Width-8&&right.Right<=panel.Width-8,"Damage stays inside normal card");
 if(!beastLayout){bool found=false;foreach(Control child in panel.Controls){var label=child as Label;
 if(label!=null&&label.Text.EndsWith(" hits")){found=true;Assert(label.Width>=TextRenderer.MeasureText(label.Text,label.Font).Width,"Hit count never ellipsizes");
 PictureBox vp=null;foreach(Control item in panel.Controls)if((item.Tag as string)=="VP")vp=item as PictureBox;
 Assert(label.Left==vp.Left-3,"Hit counter follows VP column");}}
 Assert(found,"Normal card has complete hit count");}
 int predictedHeight=(int)overlay.GetType().GetMethod("PreviewEventHeight",Hidden).Invoke(overlay,new object[]{sample,panel.Width,false});
 Assert(predictedHeight==panel.Height,"Responsive resize preview matches actual card height");
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
 Call(overlay,"ResetSession");Assert(cache.Count==0,"Reset clears deferred and rendered cards");
 }
 Console.WriteLine("Passed "+checks+" UI performance checks");
 }
}
