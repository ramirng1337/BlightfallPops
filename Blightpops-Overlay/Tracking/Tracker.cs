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
    // Combat log parsing and event tracking.
    internal sealed class Tracker {
        public readonly List<Entry> Entries=new List<Entry>();
        public readonly Dictionary<string,CombatPlayer> Players=new Dictionary<string,CombatPlayer>(StringComparer.Ordinal);
        public List<CombatPlayer> GetPlayers() {
            var result=new List<CombatPlayer>(Players.Values);
            result.Sort(delegate(CombatPlayer a,CombatPlayer b) {
                int cmp=a.FirstSeen.CompareTo(b.FirstSeen);
                return cmp!=0?cmp:String.Compare(a.Guid,b.Guid,StringComparison.Ordinal);
            });
            return result;
        }
        private void RecordPlayer(string guid,string name,DateTime time) {
            if(!guid.StartsWith("Player-",StringComparison.Ordinal))return;
            CombatPlayer player;
            if(!Players.TryGetValue(guid,out player)) {
                player=new CombatPlayer{Guid=guid,Name=name,FirstSeen=time};
                Players.Add(guid,player);
            }
            if(!String.IsNullOrEmpty(name)&&name!="nil")player.Name=name;
            player.LastSeen=time;
            player.EventCount++;
        }
        public event Action Changed;
        public string Player="", Guid="", Segment="";
        public int WindowMs=1200;
        // Opt-in: record Blightfall casts from all players without merging their hit windows.
        public bool TrackAllPlayers=false;
        private readonly Dictionary<string,Entry> otherPending=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string,Entry> otherRecent=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string,int> otherCastNumbers=new Dictionary<string,int>(StringComparer.Ordinal);
        public long Position;
        public DateTime LastWrite=DateTime.MinValue;
        public bool SkipFirstLine;
        private string remainder="", encounter="";
        private readonly Decoder logDecoder=Encoding.UTF8.GetDecoder();
        private DateTime lastOwnCombat=DateTime.MinValue;
        private Entry pending, recentBlightfall, recentBeast;
        private readonly Dictionary<string,Entry> beasts=new Dictionary<string,Entry>();
        private readonly Dictionary<string,int> attempts=new Dictionary<string,int>();
        // Custom aura state is tracked independently per affected unit and spell ID.
        private sealed class AuraState { public bool Active; public int Stacks; public bool HasDose; }
        private readonly Dictionary<string,AuraState> auraStates=new Dictionary<string,AuraState>(StringComparer.Ordinal);
        public readonly Dictionary<string,string> WatchedAuras=new Dictionary<string,string>(StringComparer.Ordinal);
        private static string AuraKey(string target,string spell){return target+"|"+spell;}
        private void RecordAura(string kind,string spell,string target,List<string> fields){
            if(!WatchedAuras.ContainsKey(spell)||String.IsNullOrEmpty(target))return;
            string key=AuraKey(target,spell);AuraState state;
            if(!auraStates.TryGetValue(key,out state))auraStates[key]=state=new AuraState();
            if(kind=="SPELL_AURA_REMOVED"){state.Active=false;state.Stacks=0;return;}
            if(kind=="SPELL_AURA_APPLIED"){state.Active=true;state.Stacks=1;return;}
            if(kind=="SPELL_AURA_REFRESH"){state.Active=true;if(state.Stacks==0)state.Stacks=1;return;}
            if(kind=="SPELL_AURA_APPLIED_DOSE"||kind=="SPELL_AURA_REMOVED_DOSE"){
                // The aura stack amount is immediately after the spell's aura type.
                int amount; if(int.TryParse(F(fields,13),NumberStyles.Integer,CultureInfo.InvariantCulture,out amount)){
                    state.Active=amount>0;state.Stacks=Math.Max(0,amount);state.HasDose=true;
                }
            }
        }
        private void SnapshotHitDebuffs(Hit hit,string targetGuid){
            foreach(var watched in WatchedAuras){
                if(watched.Value!="Debuff")continue;
                AuraState state;
                if(!auraStates.TryGetValue(AuraKey(targetGuid,watched.Key),out state)){
                    hit.Debuffs.Add(new AuraSnapshot{SpellId=watched.Key,Active=null});
                }else{
                    hit.Debuffs.Add(new AuraSnapshot{SpellId=watched.Key,Active=state.Active,
                        Stacks=state.Stacks,HasDose=state.HasDose});
                }
            }
        }
        public void SnapshotAuras(Entry entry,string player,string target){
            foreach(var watched in WatchedAuras){
                // Buffs belong to the player; debuffs are checked on the cast target.
                string guid=watched.Value=="Debuff"?target:player;
                AuraState state=null;
                if(!String.IsNullOrEmpty(guid))auraStates.TryGetValue(AuraKey(guid,watched.Key),out state);
                entry.AuraSnapshots.Add(new AuraSnapshot{SpellId=watched.Key,Active=state==null?(bool?)null:state.Active,
                    Stacks=state==null?0:state.Stacks,HasDose=state!=null&&state.HasDose});
            }
        }
        private readonly Dictionary<string,bool> scythe=new Dictionary<string,bool>();
        // Soul Reaper 1241521 is an enemy aura; keep each caster's targets separate.
        private readonly Dictionary<string,Dictionary<string,DateTime>> soulReaper=new Dictionary<string,Dictionary<string,DateTime>>();
        private int castNumber,beastNumber,pullNumber;
        private long nextSequence;
        private static readonly Regex prefix=new Regex(@"^\s*(\d{1,2}[/.]\d{1,2}[/.]\d{4} \d\d:\d\d:\d\d\.\d+)(?:[+-]\d+)?\s{2,}(.*)$",RegexOptions.Compiled);
        private static readonly string[] logDates={"M/d/yyyy HH:mm:ss.FFFFFFF","d.M.yyyy HH:mm:ss.FFFFFFF"};
        private void Notify(){if(Changed!=null)Changed();}
        // Reset session tracking.
        public void Reset(long at=0) {
            Entries.Clear();Players.Clear();otherPending.Clear();otherRecent.Clear();otherCastNumbers.Clear();pending=null;recentBlightfall=null;recentBeast=null;beasts.Clear();attempts.Clear();auraStates.Clear();scythe.Clear();soulReaper.Clear();
            castNumber=beastNumber=pullNumber=0;nextSequence=0;Guid=Player=Segment=encounter=remainder="";
            logDecoder.Reset();lastOwnCombat=DateTime.MinValue;Position=at;SkipFirstLine=at>0;Notify();
        }
        public void Finish() {
            bool changed=false;
            if(pending!=null){pending.Number=++castNumber;Entries.Add(pending);recentBlightfall=pending;pending=null;changed=true;}
            if(TrackAllPlayers){
                foreach(var pair in otherPending){
                    int n;otherCastNumbers.TryGetValue(pair.Key,out n);
                    pair.Value.Number=n+1;otherCastNumbers[pair.Key]=n+1;
                    Entries.Add(pair.Value);otherRecent[pair.Key]=pair.Value;changed=true;
                }
                otherPending.Clear();
                if(changed)Entries.Sort(delegate(Entry a,Entry b){return a.Sequence.CompareTo(b.Sequence);});
            }
            if(changed)Notify();
        }
        // Read new log data only.
        public void Tick(string path) {
            if(!File.Exists(path))return;
            var file=new FileInfo(path);
            if(file.Length<Position)Reset();
            if(file.Length>Position) {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    stream.Seek(Position,SeekOrigin.Begin);
                    var buffer=new byte[65536];var characters=new char[65536]; int count;
                    var batch=System.Diagnostics.Stopwatch.StartNew();
                    while((count=stream.Read(buffer,0,buffer.Length))>0) {
                        Position+=count;
                        int characterCount=logDecoder.GetChars(buffer,0,count,characters,0,false);
                        remainder+=new string(characters,0,characterCount);
                        int newline,start=0;
                        while((newline=remainder.IndexOf('\n',start))>=0) {
                            string line=remainder.Substring(start,newline-start).TrimEnd('\r');
                            start=newline+1;
                            if(SkipFirstLine)SkipFirstLine=false;else Process(line);
                        }
                        if(start>0)remainder=remainder.Substring(start);
                        if(remainder.Length>1048576)remainder="";
                        // Yield between batches so large catch-ups do not monopolize the UI thread.
                        if(batch.ElapsedMilliseconds>=40)break;
                    }
                }
            }
            LastWrite=file.LastWriteTime;
            if(Position>=file.Length&&(pending!=null||(TrackAllPlayers&&otherPending.Count>0)) && (DateTime.Now-file.LastWriteTime).TotalSeconds>2.5)Finish();
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
            if(lastOwnCombat==DateTime.MinValue || (time-lastOwnCombat).TotalSeconds>12)Segment="Pull "+(++pullNumber);
            lastOwnCombat=time;
        }
        // Detect the advanced unit block by its GUID, not by the row length.
        // Retail suffix: amount, baseAmount, overkill, school, resisted, blocked,
        // absorbed, critical, glancing, crushing, [optional hint/offhand].
        private static int DamageOffset(List<string> f) {
            string unit=F(f,12);
            return unit.StartsWith("Player-")||unit.StartsWith("Creature-")||unit.StartsWith("Pet-")
                ||unit.StartsWith("Vehicle-")||unit=="0000000000000000"?31:12;
        }
        private static long DamageField(List<string> f,int offset) {
            long n;return long.TryParse(F(f,DamageOffset(f)+offset),NumberStyles.Integer,
                CultureInfo.InvariantCulture,out n)?Math.Max(0,n):0;
        }
        private static long Amount(List<string> f) {return DamageField(f,0);}
        private static long Overkill(List<string> f) {return DamageField(f,2);}
        private static bool Crit(List<string> f) {return F(f,DamageOffset(f)+7)=="1";}
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
            RecordPlayer(source,F(f,2),time);
            RecordPlayer(dest,F(f,6),time);
            if(kind.StartsWith("SPELL_AURA_",StringComparison.Ordinal))RecordAura(kind,spell,dest,f);
            if(kind=="SPELL_SUMMON"&&spell=="434237"&&source.StartsWith("Player-")&&OwnFlags(F(f,3))&&(Guid==""||Guid==source)) {
                if(Guid==""){Guid=source;Player=F(f,2);}UpdatePull(time);
                var beast=new Entry {Beast=true,Number=++beastNumber,Sequence=++nextSequence,Time=time,Segment=Segment,Guid=dest,PlayerGuid=source,PlayerName=F(f,2)};
                beasts[dest]=beast;recentBeast=beast;Entries.Add(beast);Notify();return;
            }
            if(kind=="SWING_DAMAGE"){
                Entry beast;
                if(beasts.TryGetValue(source,out beast)&&time>=beast.Time){
                    // Swings omit the three spell fields; normalize the prefix for the shared suffix reader.
                    f.InsertRange(9,new[]{"0","Melee","0x1"});
                    long amount=Amount(f);beast.Melee+=amount;
                    beast.MeleeHits.Add(new Hit{Kind="ME",SpellId="0",SpellName="Melee",Amount=amount,Overkill=Overkill(f),Target=F(f,6),Crit=Crit(f)});Notify();return;
                }
            }
            if(kind=="SPELL_DAMAGE"&&(spell=="434574"||spell=="434246")) {
                Entry beast=null;
                if(spell=="434574")beasts.TryGetValue(source,out beast);
                else if(source==Guid)beast=recentBeast;
                if(beast!=null&&time>=beast.Time){long amount=Amount(f);
                    if(spell=="434574")beast.Corrupted+=amount;else{beast.Life+=amount;beast.Exploded=true;}
                    beast.Hits.Add(new Hit{Kind=spell=="434574"?"CB":"BiL",SpellId=spell,SpellName=F(f,10),Amount=amount,Overkill=Overkill(f),Target=F(f,6),Crit=Crit(f)});Notify();}
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
                if(Guid==""){Guid=source;Player=F(f,2);}
                if(Guid!=source){
                    if(!TrackAllPlayers)return;
                    Entry old;
                    if(otherPending.TryGetValue(source,out old)){
                        int n;otherCastNumbers.TryGetValue(source,out n);
                        old.Number=n+1;otherCastNumbers[source]=n+1;
                        Entries.Add(old);otherRecent[source]=old;
                    }
                    int affectedOther=0;Dictionary<string,DateTime> otherEnemies;
                    if(soulReaper.TryGetValue(source,out otherEnemies))foreach(var target in otherEnemies)
                        if(target.Value>time)affectedOther++;
                    bool otherScythe;
                    otherPending[source]=new Entry{Time=time,Sequence=++nextSequence,Segment=Segment,
                        Scythe=scythe.TryGetValue(source,out otherScythe)?(bool?)otherScythe:null,
                        SoulReaper=affectedOther>0,SoulReaperTargets=affectedOther,
                        PlayerGuid=source,PlayerName=F(f,2)};
                    SnapshotAuras(otherPending[source],source,dest);
                    Notify();return;
                }
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
                    SoulReaper=affected>0,SoulReaperTargets=affected,PlayerGuid=source,PlayerName=F(f,2)};SnapshotAuras(pending,source,dest);return;
            }
            if(kind!="SPELL_DAMAGE"&&kind!="SPELL_PERIODIC_DAMAGE")return;
            if(source!=Guid&&!TrackAllPlayers)return;
            // Each player's pending/recent cast is independent: no cross-player eruptions.
            Entry eruption;
            if(source==Guid)eruption=pending??recentBlightfall;
            else if(!otherPending.TryGetValue(source,out eruption))otherRecent.TryGetValue(source,out eruption);
            if(eruption==null)return;
            double ms=(time-eruption.Time).TotalMilliseconds;
            if(ms<0||ms>WindowMs)return;
            if(spell!="1241171"&&spell!="1241167")return;
            long damage=Amount(f);string plague=spell=="1241171"?"DP":"VP";
            if(plague=="DP")eruption.Dread+=damage;else eruption.Virulent+=damage;
            var hit=new Hit{Kind=plague,SpellId=spell,SpellName=F(f,10),Amount=damage,Overkill=Overkill(f),Target=F(f,6),TargetGuid=dest,Crit=Crit(f)};
            SnapshotHitDebuffs(hit,dest);eruption.Hits.Add(hit);
            if(source!=Guid||pending==null)Notify();
        }
    }
    // Separate transparent outline: the real event controls stay at their original size during a drag.
}
