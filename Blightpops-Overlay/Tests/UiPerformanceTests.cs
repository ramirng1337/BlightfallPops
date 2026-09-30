using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using BlightfallPopsDesktop;
class UiPerformanceTests {
 static int checks;
 static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Field(object o,string name){return o.GetType().GetField(name,Hidden|BindingFlags.Public).GetValue(o);}
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
 }
 Console.WriteLine("Passed "+checks+" UI performance checks");
 }
}
