using System;
using System.Collections.Generic;
using System.Reflection;
using BlightfallPopsDesktop;
class TrackerTests {
 static int checks;
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Send(Tracker t,int ms,string payload){typeof(Tracker).GetMethod("Process",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(t,new object[]{new DateTime(2026,9,30,12,0,0).AddTicks(ms*1000L).ToString("M/d/yyyy HH:mm:ss.ffff",System.Globalization.CultureInfo.InvariantCulture)+"  "+payload});}
 static string Prefix(string type,string spell){return type+",Player-1-Test,Tester,0x511,0x0,Creature-1-Test,Enemy,0xa28,0x0,"+spell+",Spell,0x20";}
 static string Damage(string spell,long amount,long overkill,bool crit,bool advanced,bool shortTail){
 string suffix=amount+","+amount+","+overkill+",32,0,0,0,"+(crit?"1":"nil")+",nil,nil";
 if(shortTail)suffix=suffix.Substring(0,suffix.LastIndexOf(','));
 return Prefix("SPELL_DAMAGE",spell)+(advanced?",Creature-1-Test,0000000000000000,0,100000,0,0,0,0,0,0,0,0,0,0,0,0,0,0,80":"")+","+suffix;
 }
 static void Main(string[] args){
 foreach(bool advanced in new[]{false,true})foreach(bool shortTail in new[]{false,true}){
 var t=new Tracker();Send(t,0,Prefix("SPELL_CAST_SUCCESS","1271967"));
 Send(t,1000,Damage("1241171",150000,120000,true,advanced,shortTail));
 Send(t,2000,"ENCOUNTER_END,1,Test Boss,1,5,1");
 Send(t,2100,Damage("1241167",40000,30000,false,advanced,shortTail));
 Assert(t.Entries.Count==1,"One cast after encounter-end");var e=t.Entries[0];
 Assert(e.Hits.Count==2,"Late lethal hit counted");Assert(e.Dread==150000&&e.Virulent==40000,"Full damage retained");
 Assert(e.Overkill==150000,"Overkill separate, not double counted");Assert(e.Hits[0].Crit&&!e.Hits[1].Crit,"Crit parsed");
 Send(t,13000,Damage("1241167",99,50,false,advanced,shortTail));Assert(e.Hits.Count==2,"Outside window excluded");
 Send(t,2200,Damage("1241167",99,50,false,advanced,shortTail).Replace("Player-1-Test","Player-Other"));Assert(e.Hits.Count==2,"Other player excluded");
 t.Reset();Send(t,0,Prefix("SPELL_CAST_SUCCESS","1271967"));Send(t,1000,Damage("1241171",100,-1,false,advanced,shortTail));t.Finish();
 Assert(t.Entries[0].Overkill==0,"Nonlethal -1 becomes zero");Assert(t.Entries[0].Hits.Count==1,"Reset clears old cast");
 }
 var lethalBoss=new Tracker();
 Send(lethalBoss,0,"ENCOUNTER_START,1,Test Boss,1,5");
 Send(lethalBoss,5660,Prefix("SPELL_CAST_SUCCESS","1271967"));
 Send(lethalBoss,5670,"ENCOUNTER_END,1,Test Boss,1,5,1");
 Send(lethalBoss,5700,Damage("1241171",1965834,623336,true,true,false));
 Assert(lethalBoss.Entries[0].Hits.Count==1,"Boss-ending DP Erupt is counted");
 Assert(lethalBoss.Entries[0].Dread==1965834,"Screenshot damage includes lethal portion");
 Assert(lethalBoss.Entries[0].Overkill==623336,"Screenshot overkill shown separately");
 Assert(lethalBoss.Entries[0].Dread-lethalBoss.Entries[0].Overkill==1342498,"Effective damage does not double-count overkill");
 // Tick the prior real log and require lethal hit data to survive file parsing.
 if(args.Length>0){var t=new Tracker();do{t.Tick(args[0]);}while(t.Position<new System.IO.FileInfo(args[0]).Length);t.Finish();int lethal=0,hits=0;long overkill=0;foreach(var e in t.Entries){foreach(var h in e.Hits){hits++;if(h.Overkill>0)lethal++;}overkill+=e.Overkill;}Assert(lethal>0&&overkill>0,"Real log retains lethal hits");Console.WriteLine("Real log: "+hits+" hits, "+lethal+" with overkill, "+overkill+" overkill");}
 // Stream across the 64 KiB read boundary without losing UTF-8 target names.
 string file=System.IO.Path.GetTempFileName();
 try{
 string line=new DateTime(2026,9,30,12,0,0).ToString("M/d/yyyy HH:mm:ss.ffff",System.Globalization.CultureInfo.InvariantCulture)+"  ";
 string cast=line+Prefix("SPELL_CAST_SUCCESS","1271967")+"\n";
 string damage=line+Damage("1241171",777,100,true,false,false).Replace(",Enemy,",",ÄEnemy,")+"\n";
 int split=damage.IndexOf("Ä",StringComparison.Ordinal);
 string padding=new string('x',65535-System.Text.Encoding.UTF8.GetByteCount(cast)-System.Text.Encoding.UTF8.GetByteCount(damage.Substring(0,split))-1)+"\n";
 System.IO.File.WriteAllText(file,padding+cast+damage,new System.Text.UTF8Encoding(false));
 var streamed=new Tracker();do{streamed.Tick(file);}while(streamed.Position<new System.IO.FileInfo(file).Length);streamed.Finish();
 Assert(streamed.Entries.Count==1&&streamed.Entries[0].Dread==777,"Chunked log retains damage");
 Assert(streamed.Entries[0].Hits[0].Target=="ÄEnemy","UTF-8 target survives buffer boundary");
 streamed.Reset(new System.IO.FileInfo(file).Length);streamed.SkipFirstLine=false;
 System.IO.File.AppendAllText(file,cast+damage,new System.Text.UTF8Encoding(false));
 do{streamed.Tick(file);}while(streamed.Position<new System.IO.FileInfo(file).Length);streamed.Finish();
 Assert(streamed.Entries.Count==1&&streamed.Entries[0].Hits.Count==1,"EOF reset reads only newly appended events");
 }finally{System.IO.File.Delete(file);}
 Console.WriteLine("Passed "+checks+" checks");
 }
}
