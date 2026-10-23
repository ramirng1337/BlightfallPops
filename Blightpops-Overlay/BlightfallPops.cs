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
    internal sealed class Hit { public string Kind, Target; public long Amount, Overkill; public bool Crit; }
    internal sealed class Entry {
        public bool Beast, DOpen, VOpen, MOpen, Exploded, BlightfallBefore;
        public bool? Scythe, SoulReaper;
        public int SoulReaperTargets;
        public int Number;
        public long Sequence;
        public DateTime Time;
        public string Segment, Guid;
        public string PlayerGuid, PlayerName;
        public long Dread, Virulent, Corrupted, Life, Melee;
        // Damage totals already include overkill. Keep this as a separate breakdown.
        public long Overkill {get{long total=0;foreach(var hit in Hits)total+=hit.Overkill;return total;}}
        public readonly List<Hit> Hits = new List<Hit>();
        public readonly List<Hit> MeleeHits = new List<Hit>();
    }
    internal sealed class Settings {
        public int Width=500, Height=390, X=int.MinValue, Y=int.MinValue, WindowMs=1200, IconSize=26, TextSize=10;
        public bool Locked=false, AlwaysOnTop=true, WatchLog=true, ShowBeasts=true, ShowBeastBlightfall=true, ShowSoulReaper=false, ShowScythe=true, ShowOverkill=false, CompactNumbers=true, Collapsed=false, GroupByEventType=false, MiniCards=true;
        public string Log="";
        public string PlayerMode="Myself", SelectedPlayerGuid="";
        public static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BlightfallPopsDesktop");
        public static Settings Load() {
            var s=new Settings(); var file=Path.Combine(DirectoryPath,"settings.txt");
            if (!File.Exists(file)) return s;
            foreach (var line in File.ReadAllLines(file)) {
                int sep=line.IndexOf('='); if(sep<1)continue;
                var key=line.Substring(0,sep); var value=line.Substring(sep+1); int n; bool b;
                if (key=="Log") s.Log=value;
                else if(key=="PlayerMode"&&(value=="Myself"||value=="Specific Player"||value=="All Players"))s.PlayerMode=value;
                else if(key=="SelectedPlayerGuid")s.SelectedPlayerGuid=value;
                else if (int.TryParse(value,out n)) {
                    switch(key) { case "Width": s.Width=n;break;case "Height":s.Height=n;break;case "X":s.X=n;break;case "Y":s.Y=n;break;case "WindowMs":s.WindowMs=n;break;case "IconSize":s.IconSize=n;break;case "TextSize":s.TextSize=n;break; }
                } else if(bool.TryParse(value,out b)) {
                    switch(key) { case "Locked":s.Locked=b;break;case "AlwaysOnTop":s.AlwaysOnTop=b;break;case "WatchLog":s.WatchLog=b;break;case "ShowBeasts":s.ShowBeasts=b;break;case "ShowBeastBlightfall":s.ShowBeastBlightfall=b;break;case "ShowSoulReaper":s.ShowSoulReaper=b;break;case "ShowScythe":s.ShowScythe=b;break;case "ShowOverkill":s.ShowOverkill=b;break;case "CompactNumbers":s.CompactNumbers=b;break;case "Collapsed":s.Collapsed=b;break;case "GroupByEventType":s.GroupByEventType=b;break;case "ExtraDetails":s.MiniCards=!b;break; }
                }
            }
            s.Width=Math.Max(340,Math.Min(2000,s.Width));s.Height=Math.Max(70,Math.Min(1500,s.Height));
            s.WindowMs=Math.Max(500,Math.Min(2000,s.WindowMs));
            s.IconSize=Math.Max(18,Math.Min(40,s.IconSize));s.TextSize=Math.Max(8,Math.Min(18,s.TextSize));
            return s;
        }
        public void Save() {
            Directory.CreateDirectory(DirectoryPath);
            var lines=new [] { "Log="+Log,"Width="+Width,"Height="+Height,"X="+X,"Y="+Y,"WindowMs="+WindowMs,
                "IconSize="+IconSize,"TextSize="+TextSize,"Locked="+Locked,"AlwaysOnTop="+AlwaysOnTop,"WatchLog="+WatchLog,"ShowBeasts="+ShowBeasts,
                "ShowBeastBlightfall="+ShowBeastBlightfall,"ShowSoulReaper="+ShowSoulReaper,"ShowScythe="+ShowScythe,"ShowOverkill="+ShowOverkill,"CompactNumbers="+CompactNumbers,"Collapsed="+Collapsed,
                "GroupByEventType="+GroupByEventType,"ExtraDetails="+(!MiniCards),"PlayerMode="+PlayerMode,"SelectedPlayerGuid="+SelectedPlayerGuid };
            File.WriteAllLines(Path.Combine(DirectoryPath,"settings.txt"),lines,Encoding.UTF8);
        }
    }
    // Player identities are keyed by GUID, never by display name. Realm suffixes
    // are retained exactly as reported by the combat log.
    internal sealed class CombatPlayer {
        public string Guid, Name;
        public DateTime FirstSeen, LastSeen;
        public int EventCount;
    }
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
        private readonly Dictionary<string,bool> scythe=new Dictionary<string,bool>();
        // Soul Reaper 1241521 is an enemy aura; keep each caster's targets separate.
        private readonly Dictionary<string,Dictionary<string,DateTime>> soulReaper=new Dictionary<string,Dictionary<string,DateTime>>();
        private int castNumber,beastNumber,pullNumber;
        private long nextSequence;
        private static readonly Regex prefix=new Regex(@"^\s*(\d{1,2}[/.]\d{1,2}[/.]\d{4} \d\d:\d\d:\d\d\.\d+)(?:[+-]\d+)?\s{2,}(.*)$",RegexOptions.Compiled);
        private static readonly string[] logDates={"M/d/yyyy HH:mm:ss.FFFFFFF","d.M.yyyy HH:mm:ss.FFFFFFF"};
        private void Notify(){if(Changed!=null)Changed();}
        public void Reset(long at=0) {
            Entries.Clear();Players.Clear();otherPending.Clear();otherRecent.Clear();otherCastNumbers.Clear();pending=null;recentBlightfall=null;recentBeast=null;beasts.Clear();attempts.Clear();scythe.Clear();soulReaper.Clear();
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
                    beast.MeleeHits.Add(new Hit{Kind="ME",Amount=amount,Overkill=Overkill(f),Target=F(f,6),Crit=Crit(f)});Notify();return;
                }
            }
            if(kind=="SPELL_DAMAGE"&&(spell=="434574"||spell=="434246")) {
                Entry beast=null;
                if(spell=="434574")beasts.TryGetValue(source,out beast);
                else if(source==Guid)beast=recentBeast;
                if(beast!=null&&time>=beast.Time){long amount=Amount(f);
                    if(spell=="434574")beast.Corrupted+=amount;else{beast.Life+=amount;beast.Exploded=true;}
                    beast.Hits.Add(new Hit{Kind=spell=="434574"?"CB":"BiL",Amount=amount,Overkill=Overkill(f),Target=F(f,6),Crit=Crit(f)});Notify();}
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
                    SoulReaper=affected>0,SoulReaperTargets=affected,PlayerGuid=source,PlayerName=F(f,2)};return;
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
            eruption.Hits.Add(new Hit{Kind=plague,Amount=damage,Overkill=Overkill(f),Target=F(f,6),Crit=Crit(f)});
            if(source!=Guid||pending==null)Notify();
        }
    }
    // Separate transparent outline: the real event controls stay at their original size during a drag.
    internal sealed class ResizeOutline : Form {
        public ResizeOutline(){
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{
            var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080|0x00000020;return p;
        }}
        protected override void WndProc(ref Message message){
            if(message.Msg==0x0084){message.Result=(IntPtr)(-1);return;}
            if(message.Msg==0x0021){message.Result=(IntPtr)3;return;}
            base.WndProc(ref message);
        }
    }
    internal sealed class BufferedPanel : Panel {
        public BufferedPanel(){DoubleBuffered=true;ResizeRedraw=true;}
    }
    internal sealed class Overlay : Form {
        private sealed class CardState {
            public Entry Entry;public Panel Card;public string Signature,First;
            public int DetailsTop,MinimumHeight;public Label FirstLabel,SecondLabel;
            public readonly Dictionary<Hit,Panel> Rows=new Dictionary<Hit,Panel>();
        }
        private readonly Dictionary<Entry,CardState> cardCache=new Dictionary<Entry,CardState>();
        private readonly List<CardState> visibleCards=new List<CardState>();
        private readonly Dictionary<string,Font> labelFonts=new Dictionary<string,Font>();
        private Font LabelFont(bool bold){return SizedFont(settings.TextSize,bold);}
        private Font SizedFont(int size,bool bold){
            string key=size+":"+bold;Font font;
            if(!labelFonts.TryGetValue(key,out font)){font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);labelFonts.Add(key,font);}
            return font;
        }
        private Color VirulentDamageColor(Entry entry){
            if(entry.Beast)return dim;
            int hits=0;
            foreach(var hit in entry.Hits){
                if(hit.Kind!="VP")continue;
                if(!hit.Crit)return dim;
                hits++;
            }
            return hits>3?red:dim;
        }
        private string CardSignature(Entry e,int width){
            return string.Join("|",new object[]{width,ShowMiniCards,settings.IconSize,settings.TextSize,
                settings.CompactNumbers,settings.ShowBeastBlightfall,settings.ShowSoulReaper,settings.ShowScythe,settings.ShowOverkill,
                e.Hits.Count,ShowMiniCards?0:e.MeleeHits.Count,ShowMiniCards?0:e.Melee,VirulentDamageColor(e).ToArgb(),e.Dread,e.Virulent,e.Corrupted,e.Life,e.Scythe,e.SoulReaper,e.SoulReaperTargets,
                e.Exploded,e.BlightfallBefore,e.Number,e.Time.Ticks,e.Segment,
                ShowMiniCards?0:detailTitleWidth,ShowMiniCards?0:detailTotalWidth});
        }
        private void ConfigureDetails(Panel card,Entry entry,string first,int top,int minimum,Label firstLabel,Label secondLabel){
            var state=cardCache[entry];state.First=first;state.DetailsTop=top;state.MinimumHeight=minimum;
            state.FirstLabel=firstLabel;state.SecondLabel=secondLabel;LayoutDetails(state);
        }
        private void LayoutDetails(CardState state){
            int end=AddHitRows(state.Card,state.Entry,state.First,state.DetailsTop);
            state.Card.Height=Math.Max(state.MinimumHeight,end+2);state.Card.Controls[0].Height=state.Card.Height;
        }
        private void ToggleDetails(Entry entry,bool first){
            CardState state;if(!cardCache.TryGetValue(entry,out state))return;
            if(first)entry.DOpen=!entry.DOpen;else entry.VOpen=!entry.VOpen;
            int previous=scrollPixels;cards.SuspendLayout();state.Card.SuspendLayout();
            LayoutDetails(state);state.Card.ResumeLayout();PositionCards();cards.ResumeLayout();ScrollTo(previous);
        }
        private void ToggleMeleeDetails(Entry entry){
            CardState state;if(!cardCache.TryGetValue(entry,out state)||ShowMiniCards)return;
            entry.MOpen=!entry.MOpen;int previous=scrollPixels;cards.SuspendLayout();state.Card.SuspendLayout();
            LayoutDetails(state);state.Card.ResumeLayout();PositionCards();cards.ResumeLayout();ScrollTo(previous);
        }
        private static IEnumerable<Hit> DisplayHits(Entry entry){
            foreach(var hit in entry.Hits)yield return hit;
            foreach(var hit in entry.MeleeHits)yield return hit;
        }
        private void PositionCards(){
            int y=3;foreach(var state in visibleCards){state.Card.Tag=y;state.Card.Top=y-scrollPixels;y+=state.Card.Height+(ShowMiniCards?4:5);}
            totalContentHeight=y;
        }

        private readonly Settings settings=Settings.Load();
        private readonly Tracker tracker=new Tracker();
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
            tracker.WindowMs=settings.WindowMs;tracker.TrackAllPlayers=true;tracker.Changed+=delegate {dirty=true;};
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
            foreach(var entry in source.Entries)if(!entry.Beast&&!keys.Contains(entry.PlayerGuid))keys.Add(entry.PlayerGuid);
            keys.Sort(delegate(string a,string b){return String.Compare(names[a],names[b],StringComparison.OrdinalIgnoreCase);});
            return keys;
        }
        // Editable card definitions are independent of the live Blightfall parser.
        private sealed class CustomEffect {
            public string Name="New effect", SpellId="", Type="Buff", IconPath="", Condition="Active at event";
        }
        private sealed class CustomCard {
            public string Name="Custom event", SpellId="", IconPath="";
            public List<CustomEffect> Effects=new List<CustomEffect>();
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
        private static Label BuilderLabel(string text){return new Label{Text=text,ForeColor=Color.FromArgb(178,183,183),Height=22,AutoSize=false,Font=new Font("Segoe UI",9)};}
        private static TextBox BuilderText(string value){return new TextBox{Text=value,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,Font=new Font("Segoe UI",10),Height=26};}
        private static Image ReadBuilderImage(string path){
            try{if(!String.IsNullOrEmpty(path)&&File.Exists(path))using(var loaded=Image.FromFile(path))return new Bitmap(loaded);}catch{}
            return null;
        }
        private void ShowEventCardBuilder(){
            var definitions=ReadCustomCards();
            var card=new CustomCard();
            var editor=new Form{Text="Blightfall Pops — Event Card Builder",Size=new Size(940,700),MinimumSize=new Size(700,480),
                StartPosition=FormStartPosition.CenterParent,BackColor=background,ForeColor=Color.White,FormBorderStyle=FormBorderStyle.None,ShowIcon=false};
            var titleBar=new Panel{Dock=DockStyle.Top,Height=33,BackColor=Color.FromArgb(10,11,13)};
            var heading=new Label{Text="BLIGHTFALL POPS  /  EVENT CARD BUILDER",ForeColor=Color.FromArgb(222,224,218),
                Font=new Font("Segoe UI",9,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill};
            heading.Padding=new Padding(12,0,0,0);
            var close=BuilderButton("×");close.Width=36;close.Dock=DockStyle.Right;close.BackColor=titleBar.BackColor;close.ForeColor=red;
            var minimize=BuilderButton("−");minimize.Width=36;minimize.Dock=DockStyle.Right;minimize.BackColor=titleBar.BackColor;
            close.Click+=delegate{editor.Close();};minimize.Click+=delegate{editor.WindowState=FormWindowState.Minimized;};
            titleBar.Controls.Add(heading);titleBar.Controls.Add(minimize);titleBar.Controls.Add(close);editor.Controls.Add(titleBar);
            Point? drag=null;Point origin=Point.Empty;
            MouseEventHandler down=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){drag=Control.MousePosition;origin=editor.Location;}};
            MouseEventHandler move=delegate{if(drag.HasValue){Point pt=Control.MousePosition;editor.Location=new Point(origin.X+pt.X-drag.Value.X,origin.Y+pt.Y-drag.Value.Y);}};
            MouseEventHandler up=delegate{drag=null;};
            foreach(Control c in new Control[]{titleBar,heading}){c.MouseDown+=down;c.MouseMove+=move;c.MouseUp+=up;}
            var footer=new Panel{Dock=DockStyle.Bottom,Height=53,BackColor=Color.FromArgb(27,31,36),Padding=new Padding(12,9,12,9)};
            var save=BuilderButton("Save card");save.Dock=DockStyle.Right;save.Width=130;
            var statusLabel=new Label{Text="Card definitions are saved locally; live tracking is not changed.",Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft,ForeColor=dim,Font=new Font("Segoe UI",9)};
            footer.Controls.Add(statusLabel);footer.Controls.Add(save);editor.Controls.Add(footer);
            var split=new SplitContainer{Dock=DockStyle.Fill,SplitterDistance=440,BackColor=background,Panel1MinSize=290,Panel2MinSize=290};
            editor.Controls.Add(split);split.BringToFront();titleBar.BringToFront();footer.BringToFront();
            var left=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(25,29,34),Padding=new Padding(14)};
            split.Panel1.Controls.Add(left);
            var form=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(0,0,12,12)};
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));left.Controls.Add(form);
            Action<string,Control> addField=delegate(string caption,Control control){
                var label=BuilderLabel(caption);label.Dock=DockStyle.Top;form.Controls.Add(label);
                control.Dock=DockStyle.Top;control.Margin=new Padding(0,0,0,12);form.Controls.Add(control);
            };
            var presets=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White,Height=29};
            var name=BuilderText(card.Name);var spell=BuilderText(card.SpellId);
            var imageButton=BuilderButton("Choose main icon image...");
            var clearImage=BuilderButton("Use Spell ID / default icon");
            var addEffect=BuilderButton("+ Add buff / debuff");
            var effects=new ListBox{Height=125,BackColor=Color.FromArgb(35,40,47),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
            var effectName=BuilderText("");var effectSpell=BuilderText("");
            var effectType=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White};
            effectType.Items.AddRange(new object[]{"Buff","Debuff"});
            var condition=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(39,44,51),ForeColor=Color.White};
            condition.Items.AddRange(new object[]{"Active at event","Applied by event","Detected in event window","Removed in event window"});
            var effectImage=BuilderButton("Choose effect icon image...");var removeEffect=BuilderButton("Remove selected effect");
            addField("Saved card",presets);addField("Card name",name);addField("Main Spell ID (combat-log ID)",spell);
            addField("Main icon — optional custom image",imageButton);addField("",clearImage);
            addField("Linked buffs / debuffs",effects);addField("",addEffect);
            addField("Selected effect name",effectName);addField("Selected effect Spell ID",effectSpell);
            addField("Effect type",effectType);addField("Display condition",condition);
            addField("Effect icon — optional custom image",effectImage);addField("",removeEffect);
            var right=new Panel{Dock=DockStyle.Fill,Padding=new Padding(14),BackColor=background};split.Panel2.Controls.Add(right);
            var previewHeading=BuilderLabel("LIVE CARD STRUCTURE PREVIEW");previewHeading.Dock=DockStyle.Top;right.Controls.Add(previewHeading);
            var preview=new BufferedPanel{Dock=DockStyle.Top,Height=150,BackColor=rowColor};right.Controls.Add(preview);preview.BringToFront();
            var note=BuilderLabel("Click the image placeholders to import a custom icon.");note.Dock=DockStyle.Top;note.Height=45;
            right.Controls.Add(note);note.BringToFront();
            var mainIcon=new PictureBox{Bounds=new Rectangle(12,14,48,48),BackColor=Color.FromArgb(45,51,59),SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
            var mainText=new Label{Bounds=new Rectangle(72,14,300,27),ForeColor=Color.White,Font=new Font("Segoe UI",11,FontStyle.Bold)};
            var subText=new Label{Bounds=new Rectangle(72,42,300,24),ForeColor=dim,Font=new Font("Segoe UI",9)};
            var effectPreview=new FlowLayoutPanel{Bounds=new Rectangle(12,78,400,60),BackColor=rowColor,WrapContents=false,AutoScroll=true};
            preview.Controls.AddRange(new Control[]{mainIcon,mainText,subText,effectPreview});
            var ownedImages=new List<Image>();
            Action<Image,PictureBox> setImage=delegate(Image img,PictureBox box){if(img!=null){ownedImages.Add(img);box.Image=img;}else box.Image=null;};
            Action refresh=delegate{
                mainText.Text=String.IsNullOrWhiteSpace(name.Text)?"Custom event":name.Text;
                subText.Text="Spell ID: "+(String.IsNullOrWhiteSpace(spell.Text)?"not assigned":spell.Text);
                setImage(ReadBuilderImage(card.IconPath),mainIcon);
                while(effectPreview.Controls.Count>0){Control c=effectPreview.Controls[0];effectPreview.Controls.RemoveAt(0);c.Dispose();}
                foreach(var effect in card.Effects){
                    var pic=new PictureBox{Size=new Size(35,35),Margin=new Padding(2),BackColor=Color.FromArgb(45,51,59),SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
                    setImage(ReadBuilderImage(effect.IconPath),pic);
                    if(pic.Image==null){pic.Paint+=delegate(object sender,PaintEventArgs e){TextRenderer.DrawText(e.Graphics,"+",new Font("Segoe UI",13),pic.ClientRectangle,dim,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};}
                    var selectedEffect=effect;pic.Click+=delegate{effects.SelectedIndex=card.Effects.IndexOf(selectedEffect);effectImage.PerformClick();};
                    new ToolTip().SetToolTip(pic,effect.Name+" ("+effect.Type+", "+effect.SpellId+")");
                    effectPreview.Controls.Add(pic);
                }
            };
            Action fillEffects=delegate{
                int previous=effects.SelectedIndex;effects.Items.Clear();
                foreach(var e in card.Effects)effects.Items.Add(e.Type+": "+e.Name+" ["+e.SpellId+"]");
                if(effects.Items.Count>0)effects.SelectedIndex=Math.Max(0,Math.Min(previous,effects.Items.Count-1));
                refresh();
            };
            Action fillPresets=delegate{
                presets.Items.Clear();presets.Items.Add("New card");foreach(var c in definitions)presets.Items.Add(c.Name);
            };
            bool loading=false;
            presets.SelectedIndexChanged+=delegate{
                if(loading)return;int i=presets.SelectedIndex;
                card=i>0&&i<=definitions.Count?definitions[i-1]:new CustomCard();
                loading=true;name.Text=card.Name;spell.Text=card.SpellId;loading=false;fillEffects();
            };
            name.TextChanged+=delegate{card.Name=name.Text;refresh();};
            spell.TextChanged+=delegate{card.SpellId=spell.Text;refresh();};
            Action<Button,Action<string>> chooseImage=delegate(Button button,Action<string> accept){
                button.Click+=delegate{using(var dialog=new OpenFileDialog{Filter="Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",Title="Select custom icon"}){
                    if(dialog.ShowDialog(editor)==DialogResult.OK)accept(dialog.FileName);
                }};
            };
            chooseImage(imageButton,delegate(string path){card.IconPath=path;refresh();});
            clearImage.Click+=delegate{card.IconPath="";refresh();};
            mainIcon.Click+=delegate{imageButton.PerformClick();};
            addEffect.Click+=delegate{card.Effects.Add(new CustomEffect());fillEffects();effects.SelectedIndex=card.Effects.Count-1;};
            effects.SelectedIndexChanged+=delegate{
                int i=effects.SelectedIndex;bool enabled=i>=0&&i<card.Effects.Count;
                foreach(Control c in new Control[]{effectName,effectSpell,effectType,condition,effectImage,removeEffect})c.Enabled=enabled;
                loading=true;
                effectName.Text=enabled?card.Effects[i].Name:"";effectSpell.Text=enabled?card.Effects[i].SpellId:"";
                effectType.SelectedItem=enabled?card.Effects[i].Type:null;
                condition.SelectedItem=enabled?card.Effects[i].Condition:null;
                loading=false;
            };
            effectName.TextChanged+=delegate{if(loading||effects.SelectedIndex<0)return;card.Effects[effects.SelectedIndex].Name=effectName.Text;int i=effects.SelectedIndex;effects.Items[i]=card.Effects[i].Type+": "+effectName.Text+" ["+card.Effects[i].SpellId+"]";refresh();};
            effectSpell.TextChanged+=delegate{if(loading||effects.SelectedIndex<0)return;card.Effects[effects.SelectedIndex].SpellId=effectSpell.Text;int i=effects.SelectedIndex;effects.Items[i]=card.Effects[i].Type+": "+card.Effects[i].Name+" ["+effectSpell.Text+"]";refresh();};
            effectType.SelectedIndexChanged+=delegate{if(loading||effects.SelectedIndex<0||effectType.SelectedItem==null)return;card.Effects[effects.SelectedIndex].Type=effectType.SelectedItem.ToString();fillEffects();};
            condition.SelectedIndexChanged+=delegate{if(!loading&&effects.SelectedIndex>=0&&condition.SelectedItem!=null)card.Effects[effects.SelectedIndex].Condition=condition.SelectedItem.ToString();};
            chooseImage(effectImage,delegate(string path){if(effects.SelectedIndex>=0){card.Effects[effects.SelectedIndex].IconPath=path;refresh();}});
            removeEffect.Click+=delegate{if(effects.SelectedIndex>=0){card.Effects.RemoveAt(effects.SelectedIndex);fillEffects();}};
            save.Click+=delegate{
                int id; if(!int.TryParse(spell.Text,out id)||id<=0){MessageBox.Show(editor,"Enter a valid numeric main Spell ID.","Event Card Builder",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                foreach(var effect in card.Effects){int effectId;if(!int.TryParse(effect.SpellId,out effectId)||effectId<=0){MessageBox.Show(editor,"Each linked effect needs a valid numeric Spell ID.","Event Card Builder",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}}
                int selected=presets.SelectedIndex;
                if(selected>0&&selected<=definitions.Count)definitions[selected-1]=card;else definitions.Add(card);
                try{WriteCustomCards(definitions);fillPresets();presets.SelectedIndex=definitions.IndexOf(card)+1;
                    statusLabel.Text="Saved to event-cards.json. Tracking integration comes next.";
                }catch(Exception ex){MessageBox.Show(editor,ex.Message,"Unable to save",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            };
            var grip=new Panel{Size=new Size(18,18),Cursor=Cursors.SizeNWSE,BackColor=Color.FromArgb(43,48,52),Anchor=AnchorStyles.Bottom|AnchorStyles.Right};
            grip.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(dim,1.5f)){e.Graphics.DrawLine(pen,5,15,15,5);e.Graphics.DrawLine(pen,10,15,15,10);}};
            editor.Controls.Add(grip);grip.BringToFront();
            Action placeGrip=delegate{grip.Location=new Point(editor.ClientSize.Width-19,editor.ClientSize.Height-19);};
            editor.Resize+=delegate{placeGrip();};placeGrip();
            Point? resizeAt=null;Size original=Size.Empty;
            grip.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){resizeAt=Control.MousePosition;original=editor.Size;}};
            grip.MouseMove+=delegate{if(resizeAt.HasValue){Point pt=Control.MousePosition;editor.Size=new Size(Math.Max(editor.MinimumSize.Width,original.Width+pt.X-resizeAt.Value.X),Math.Max(editor.MinimumSize.Height,original.Height+pt.Y-resizeAt.Value.Y));}};
            grip.MouseUp+=delegate{resizeAt=null;};
            editor.FormClosed+=delegate{foreach(var img in ownedImages)img.Dispose();};
            fillPresets();presets.SelectedIndex=0;fillEffects();
            editor.Show(this);
        }

        private void ShowCompareWindow(){
            var identities=CompareIdentities(tracker);
            // A single player can still compare against another combat log.
            var compare=new Form{Text="Blightfall Pops — Compare players",Size=new Size(1120,720),
                StartPosition=FormStartPosition.CenterParent,BackColor=background,ForeColor=Color.White,
                MinimumSize=new Size(740,420),FormBorderStyle=FormBorderStyle.None,ShowIcon=false};
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
            var leftLabel=new Label{Text="PLAYER 1",ForeColor=dim,Font=new Font("Segoe UI",9,FontStyle.Bold),AutoSize=true};
            var rightLabel=new Label{Text="PLAYER 2",ForeColor=dim,Font=new Font("Segoe UI",9,FontStyle.Bold),AutoSize=true};
            toolbar.Controls.AddRange(new Control[]{leftLabel,rightLabel,leftChoice,rightChoice,mode,leftFile,rightFile});
            Action layoutToolbar=delegate{
                int half=toolbar.ClientSize.Width/2;
                mode.SetBounds(12,4,Math.Min(270,Math.Max(160,half-25)),25);
                bool separate=mode.SelectedIndex==1;
                leftFile.Visible=rightFile.Visible=separate;
                leftFile.SetBounds(half-143,4,130,25);
                rightFile.SetBounds(toolbar.ClientSize.Width-143,4,130,25);
                leftLabel.Location=new Point(13,37);rightLabel.Location=new Point(half+13,37);
                leftChoice.SetBounds(12,56,Math.Max(120,half-25),28);
                rightChoice.SetBounds(half+12,56,Math.Max(120,half-25),28);
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
            var compareClose=new Button{Text="×",Font=new Font("Segoe UI",15),ForeColor=red,
                BackColor=compareTitleBar.BackColor,FlatStyle=FlatStyle.Flat,Size=new Size(36,30),TabStop=false};
            var compareMinimize=new Button{Text="−",Font=new Font("Segoe UI",14),ForeColor=dim,
                BackColor=compareTitleBar.BackColor,FlatStyle=FlatStyle.Flat,Size=new Size(36,30),TabStop=false};
            compareClose.FlatAppearance.BorderSize=0;
            compareMinimize.FlatAppearance.BorderSize=0;
            compareClose.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            compareMinimize.FlatAppearance.MouseOverBackColor=Color.FromArgb(49,51,54);
            compareClose.Click+=delegate{compare.Close();};
            compareMinimize.Click+=delegate{compare.WindowState=FormWindowState.Minimized;};
            compareTitleBar.Controls.AddRange(new Control[]{compareTitle,compareMinimize,compareClose});
            Action layoutCompareTitle=delegate{
                compareClose.Location=new Point(compareTitleBar.ClientSize.Width-37,1);
                compareMinimize.Location=new Point(compareTitleBar.ClientSize.Width-74,1);
                compareTitle.Width=Math.Max(80,compareTitleBar.ClientSize.Width-90);
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
                foreach(var candidate in source.Entries)if(candidate.PlayerGuid==guid&&!candidate.Beast){
                    eventCount++;if(candidate.Sequence>lastSequence)lastSequence=candidate.Sequence;
                }
                if(Object.ReferenceEquals(column.RenderSource,source)&&column.RenderGuid==guid&&
                    column.RenderWidth==width&&column.RenderCount==eventCount&&
                    column.RenderLastSequence==lastSequence){column.UpdateScroll();return;}
                int previousOffset=column.Offset;
                panel.SuspendLayout();
                // Clear() only detaches WinForms controls; dispose old cards to avoid leaking handles.
                while(panel.Controls.Count>0){Control old=panel.Controls[0];panel.Controls.RemoveAt(0);old.Dispose();}
                var events=new List<Entry>();foreach(var e in source.Entries)if(e.PlayerGuid==guid&&!e.Beast)events.Add(e);
                events.Sort(delegate(Entry a,Entry b){int t=a.Time.CompareTo(b.Time);return t!=0?t:a.Sequence.CompareTo(b.Sequence);});
                long dread=0,virulent=0;int hits=0;foreach(var e in events){dread+=e.Dread;virulent+=e.Virulent;hits+=e.Hits.Count;}
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
                    var accent=new Panel{Bounds=new Rectangle(0,0,3,55),BackColor=green};
                    card.Controls.Add(accent);
                    int iconSize=Math.Max(18,Math.Min(23,settings.IconSize));
                    int secondX=Math.Max(165,width/2-6);
                    int totalX=Math.Max(secondX+80,width-96);
                    int statusCount=(settings.ShowSoulReaper?1:0)+(settings.ShowScythe?1:0);
                    int statusX=width-9-statusCount*iconSize-Math.Max(0,statusCount-1)*3;
                    var bf=SpellIcon("BF",9,9,36);
                    card.Controls.Add(bf);
                    var title=Label("#"+index+"  "+e.Time.ToString("HH:mm:ss"),Color.White,52,1,
                        Math.Max(35,secondX-60),true);
                    card.Controls.Add(title);
                    var count=Label(e.Hits.Count+(e.Hits.Count==1?" hit":" hits"),dim,
                        secondX-3,3,Math.Max(40,totalX-secondX));
                    card.Controls.Add(count);
                    var total=Label(Format(e.Dread+e.Virulent),green,totalX,1,
                        Math.Max(35,width-totalX-8),true);
                    total.TextAlign=ContentAlignment.TopRight;card.Controls.Add(total);
                    var dp=SpellIcon("DP",55,29,iconSize);
                    var vp=SpellIcon("VP",secondX,29,iconSize);
                    card.Controls.Add(dp);card.Controls.Add(vp);
                    var dpValue=Label(Format(e.Dread),e.Hits.Exists(h=>h.Kind=="DP"&&h.Crit)?Color.Gold:dim,
                        55+iconSize+4,28,Math.Max(30,secondX-55-iconSize-8));
                    var vpValue=Label(Format(e.Virulent),VirulentDamageColor(e),
                        secondX+iconSize+4,28,Math.Max(16,(statusCount>0?statusX:width-7)-secondX-iconSize-8));
                    card.Controls.Add(dpValue);card.Controls.Add(vpValue);
                    // Use the same status icons, activity indicators, and tooltips as main compact cards.
                    if(settings.ShowSoulReaper)
                        card.Controls.Add(SpellIcon("SR",statusX,29,iconSize,SoulReaperTip(e),e.SoulReaper));
                    if(settings.ShowScythe){
                        string scTip="Festering Scythe: "+(e.Scythe.HasValue?(e.Scythe.Value?"active":"inactive"):"unknown");
                        card.Controls.Add(SpellIcon("SC",statusX+(statusCount-1)*(iconSize+3),29,iconSize,scTip,e.Scythe));
                    }
                    var details=new Panel{Visible=false,BackColor=Color.FromArgb(27,31,36),
                        Location=new Point(5,55),Width=Math.Max(60,width-10)};
                    int y=5;
                    foreach(var hit in e.Hits){
                        var line=new Label{Text=hit.Kind+"   "+Format(hit.Amount)+(hit.Crit?"   CRIT":"")+"   "+(hit.Target??""),
                            ForeColor=hit.Crit?Color.Gold:dim,Font=LabelFont(hit.Crit),
                            Bounds=new Rectangle(8,y,Math.Max(40,width-28),24),AutoEllipsis=true};
                        details.Controls.Add(line);y+=25;
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
            compare.ResizeEnd+=delegate{
                int leftWidth=Math.Max(150,left.View.ClientSize.Width-left.Track.Width-12);
                int rightWidth=Math.Max(150,right.View.ClientSize.Width-right.Track.Width-12);
                if(leftWidth!=left.RenderWidth||rightWidth!=right.RenderWidth)refresh();
                else {left.UpdateScroll();right.UpdateScroll();}
            };
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
            compare.Show(this);
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
            var builderItem=new ToolStripMenuItem("Event Card Builder...");
            builderItem.Click+=delegate{ShowEventCardBuilder();};
            menu.Items.Add(builderItem);
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
        private void SetupOptions() {
            var time=new NumericUpDown{Minimum=500,Maximum=2000,Increment=100,Value=settings.WindowMs,Width=65,Left=12,Top=12};
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
            options.Controls.AddRange(new Control[]{time,font,icons,number,grouped,onTop,beastIcon,updateButton,overkill,
                new Label{Text="Blightfall ms",Left=12,Top=35,Width=115,ForeColor=dim},
                new Label{Text="Text size",Left=137,Top=35,Width=70,ForeColor=dim},
                new Label{Text="Icon size",Left=218,Top=35,Width=70,ForeColor=dim}});
            beastButton=OptionIcon("BB",62,"Blood Beast events",delegate{return settings.ShowBeasts;},delegate{settings.ShowBeasts=!settings.ShowBeasts;});
            OptionIcon("SR",104,"Soul Reaper marker",delegate{return settings.ShowSoulReaper;},delegate{settings.ShowSoulReaper=!settings.ShowSoulReaper;});
            OptionIcon("SC",146,"Festering Scythe marker",delegate{return settings.ShowScythe;},delegate{settings.ShowScythe=!settings.ShowScythe;});
            time.ValueChanged+=delegate {settings.WindowMs=(int)time.Value;tracker.WindowMs=settings.WindowMs;Save();};
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
            else{var geometry=LargeSummaryLayout(entry,width);top=geometry.BaseHeight+2;minimum=geometry.BaseHeight;}
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
        private void ScrollWheel(object sender,MouseEventArgs e){ScrollTo(scrollPixels-Math.Sign(e.Delta)*Math.Max(28,settings.TextSize*3));}
        private void HookWheel(Control control){control.MouseWheel+=ScrollWheel;foreach(Control child in control.Controls)HookWheel(child);}
        private void ScrollTo(int pixels){
            scrollPixels=Math.Max(0,Math.Min(pixels,Math.Max(0,totalContentHeight-cards.ClientSize.Height)));
            foreach(Control control in cards.Controls)if(control.Tag is int)control.Top=(int)control.Tag-scrollPixels;
            scrollTrack.Visible=totalContentHeight>cards.ClientSize.Height;
            scrollTrack.Invalidate();
            // Build changed off-screen cards only when they enter the viewport.
            if(!refreshingCards){
                int width=Math.Max(260,cards.ClientSize.Width-7);
                foreach(var state in visibleCards){
                    if(state.Card.Bottom>=-80&&state.Card.Top<=cards.ClientSize.Height+80&&
                        state.Signature!=CardSignature(state.Entry,width)){RefreshCards();break;}
                }
            }
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
            var box=new PictureBox{Tag=key,Left=x,Top=y,Width=size,Height=size,Image=img,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(21,23,27)};
            if(key=="ME")box.Paint+=delegate(object sender,PaintEventArgs e){
                using(var pen=new Pen(dim,Math.Max(2,size/10))){
                    e.Graphics.DrawLine(pen,size/4,size/4,size*3/4,size*3/4);
                    e.Graphics.DrawLine(pen,size*3/4,size/4,size/4,size*3/4);
                    e.Graphics.DrawLine(pen,size/6,size*2/3,size/3,size*5/6);
                    e.Graphics.DrawLine(pen,size*2/3,size*5/6,size*5/6,size*2/3);
                }
            };
            if(tip!=null){box.Paint+=delegate(object sender,PaintEventArgs e){
                if(active.HasValue&&!active.Value){using(var pen=new Pen(red,Math.Max(2,size/9))){e.Graphics.DrawLine(pen,2,2,size-3,size-3);e.Graphics.DrawLine(pen,size-3,2,2,size-3);}}
                if(!active.HasValue)using(var font=new Font("Segoe UI",Math.Max(11,size/2),FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics,"?",font,new Rectangle(0,0,size,size),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            };}
            if(tip!=null)toolTip.SetToolTip(box,tip);
            return box;
        }
        private int AddHitRows(Panel card,Entry entry,string first,int y){
            var state=cardCache[entry];
            foreach(var row in state.Rows.Values)row.Visible=false;
            foreach(var hit in DisplayHits(entry)){
                if(!(hit.Kind=="ME"?entry.MOpen&&!ShowMiniCards:hit.Kind==first?entry.DOpen:entry.VOpen))continue;
                Panel cached;
                if(state.Rows.TryGetValue(hit,out cached)){cached.Top=y;cached.Visible=true;y+=cached.Height+2;continue;}
                int hitSize=Math.Min(32,Math.Max(23,settings.IconSize));
                var hitRow=new BufferedPanel{Left=8,Top=y,Width=card.Width-16,Height=hitSize+6,BackColor=Color.FromArgb(23,28,33)};
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
                    if(settings.ShowOverkill&&hit.Overkill>0){
                        string overkillText="Overkill: "+Format(hit.Overkill);
                        int overkillY=hitRow.Height,available=Math.Max(25,hitRow.Width-amountX-8);
                        int overkillHeight=TextRenderer.MeasureText(overkillText,regular,
                            new Size(available,int.MaxValue),TextFormatFlags.WordBreak).Height+3;
                        var overkillLabel=Label(overkillText,dim,amountX,overkillY,available);
                        overkillLabel.AutoEllipsis=false;overkillLabel.Height=overkillHeight;
                        toolTip.SetToolTip(overkillLabel,"Overkill: "+hit.Overkill.ToString("N0")+" (included in hit damage)");
                        hitRow.Controls.Add(overkillLabel);hitRow.Height+=overkillHeight+2;
                    }
                }
                if(hit.Crit)hitRow.Controls.Add(new Panel{Left=1,Top=3,Width=2,Height=hitRow.Height-6,BackColor=Color.Gold});
                card.Controls.Add(hitRow);state.Rows.Add(hit,hitRow);HookWheel(hitRow);y+=hitRow.Height+2;
            }
            return y;
        }
        private int detailTitleWidth,detailTotalWidth;
        private void UpdateDetailHeaderColumns(int width){
            var font=SizedFont(settings.TextSize,true);
            int titleWidth=0,totalWidth=0;
            foreach(var entry in tracker.Entries){
                if(!PlayerVisible(entry)||entry.Beast&&!settings.ShowBeasts)continue;
                string title=entry.Beast?"Beast #"+entry.Number:entry.Time.ToString("HH:mm:ss")+" #"+entry.Number;
                string total=Format(entry.Beast?entry.Corrupted+entry.Life:entry.Dread+entry.Virulent);
                titleWidth=Math.Max(titleWidth,TextRenderer.MeasureText(title,font).Width+4);
                totalWidth=Math.Max(totalWidth,TextRenderer.MeasureText(total,font).Width+4);
            }
            detailTotalWidth=Math.Min(totalWidth,Math.Max(40,width-100));
            detailTitleWidth=Math.Min(titleWidth,Math.Max(40,width-18-detailTotalWidth-60));
        }
        private void RenderDetailedCard(Panel card,Entry entry){
            var g=LargeSummaryLayout(entry,card.Width);
            int rowHeight=Math.Max(g.IconSize,TextRenderer.MeasureText("Ag",SizedFont(g.TextSize,true)).Height+2);
            int secondTop=g.DamageTop+rowHeight+6;
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=g.BaseHeight,BackColor=entry.Beast?red:green});
            var eventIcon=SpellIcon(entry.Beast?"BB":"BF",6,g.DamageTop+(secondTop+rowHeight-g.DamageTop-42)/2,42);
            toolTip.SetToolTip(eventIcon,entry.Segment??"");card.Controls.Add(eventIcon);
            string title=entry.Beast?"Beast #"+entry.Number:entry.Time.ToString("HH:mm:ss")+" #"+entry.Number;
            string total=Format(entry.Beast?entry.Corrupted+entry.Life:entry.Dread+entry.Virulent);
            // Every event uses the same heading font and column boundaries.
            var headingFont=SizedFont(settings.TextSize,true);
            var titleLabel=Label(title,Color.White,6,3,detailTitleWidth,true);titleLabel.Font=headingFont;
            card.Controls.Add(titleLabel);toolTip.SetToolTip(titleLabel,title);
            var totalLabel=Label(total,green,card.Width-9-detailTotalWidth,3,detailTotalWidth,true);totalLabel.Font=headingFont;
            totalLabel.TextAlign=ContentAlignment.TopRight;toolTip.SetToolTip(totalLabel,total);card.Controls.Add(totalLabel);
            var segment=Label(entry.Segment??"",dim,titleLabel.Right+8,3,Math.Max(20,totalLabel.Left-titleLabel.Right-16),true);
            segment.Font=headingFont;segment.TextAlign=ContentAlignment.TopCenter;
            segment.Height=titleLabel.Height=totalLabel.Height=TextRenderer.MeasureText("Ag",headingFont).Height+2;
            toolTip.SetToolTip(segment,entry.Segment??"");card.Controls.Add(segment);
            string first=entry.Beast?"CB":"DP",second=entry.Beast?"BiL":"VP";
            long firstValue=entry.Beast?entry.Corrupted:entry.Dread,secondValue=entry.Beast?entry.Life:entry.Virulent;
            var firstIcon=SpellIcon(first,55,g.DamageTop,g.IconSize);
            var secondIcon=SpellIcon(second,55,secondTop,g.IconSize);
            int amountX=55+g.IconSize+4,amountWidth=g.SecondX-amountX-8;
            var firstLabel=Label(Format(firstValue),!entry.Beast&&entry.Hits.Exists(h=>h.Kind=="DP"&&h.Crit)?Color.Gold:dim,amountX,g.DamageTop-1,amountWidth);
            var secondLabel=Label(Format(secondValue),VirulentDamageColor(entry),amountX,secondTop-1,amountWidth);
            firstLabel.Font=secondLabel.Font=SizedFont(g.TextSize,false);firstLabel.Height=secondLabel.Height=rowHeight;
            card.Controls.Add(firstIcon);card.Controls.Add(secondIcon);card.Controls.Add(firstLabel);card.Controls.Add(secondLabel);
            firstIcon.Cursor=secondIcon.Cursor=firstLabel.Cursor=secondLabel.Cursor=Cursors.Hand;
            EventHandler firstClick=delegate{ToggleDetails(entry,true);},secondClick=delegate{ToggleDetails(entry,false);};
            firstIcon.Click+=firstClick;firstLabel.Click+=firstClick;secondIcon.Click+=secondClick;secondLabel.Click+=secondClick;
            foreach(Control control in new Control[]{firstIcon,firstLabel})toolTip.SetToolTip(control,first+" — click for individual hits");
            foreach(Control control in new Control[]{secondIcon,secondLabel})toolTip.SetToolTip(control,second+" — click for individual hits");
            int crits=entry.Hits.FindAll(h=>h.Crit).Count;
            string hits=entry.Hits.Count+(entry.Hits.Count==1?" hit":" hits"),critText=crits+(crits==1?" crit":" crits");
            var hitLabel=Label(hits,dim,g.SecondX,g.DamageTop-1,TextRenderer.MeasureText(hits,SizedFont(g.TextSize,false)).Width+4);
            var critLabel=Label(critText,Color.Gold,g.SecondX,secondTop-1,TextRenderer.MeasureText(critText,SizedFont(g.TextSize,true)).Width+4,true);
            hitLabel.Font=SizedFont(g.TextSize,false);critLabel.Font=SizedFont(g.TextSize,true);
            hitLabel.Height=critLabel.Height=rowHeight;card.Controls.Add(hitLabel);card.Controls.Add(critLabel);
            int statusCount=entry.Beast?(settings.ShowBeastBlightfall?1:0):((settings.ShowSoulReaper?1:0)+(settings.ShowScythe?1:0));
            int statusX=card.Width-9-statusCount*g.IconSize-Math.Max(0,statusCount-1)*3;
            if(!entry.Beast&&settings.ShowSoulReaper)card.Controls.Add(SpellIcon("SR",statusX,secondTop,g.IconSize,SoulReaperTip(entry),entry.SoulReaper));
            if(entry.Beast&&settings.ShowBeastBlightfall){
                bool? active=entry.Exploded?(bool?)entry.BlightfallBefore:null;
                string tip=entry.Exploded?(entry.BlightfallBefore?"Blightfall before Blood Is Life":"No Blightfall before Blood Is Life"):"Waiting for Blood Is Life";
                card.Controls.Add(SpellIcon("BF",statusX,secondTop,g.IconSize,tip,active));
            }else if(!entry.Beast&&settings.ShowScythe){
                string tip="Festering Scythe: "+(entry.Scythe.HasValue?(entry.Scythe.Value?"active":"inactive"):"unknown");
                card.Controls.Add(SpellIcon("SC",statusX+(statusCount-1)*(g.IconSize+3),secondTop,g.IconSize,tip,entry.Scythe));
            }
            if(entry.Beast){
                int meleeTop=secondTop+rowHeight+6;
                var meleeIcon=SpellIcon("ME",55,meleeTop,g.IconSize);
                var meleeLabel=Label(Format(entry.Melee),dim,amountX,meleeTop-1,amountWidth);
                meleeLabel.Font=SizedFont(g.TextSize,false);meleeLabel.Height=rowHeight;
                string meleeCount=entry.MeleeHits.Count+" melee "+(entry.MeleeHits.Count==1?"hit":"hits");
                var meleeCountLabel=Label(meleeCount,dim,g.SecondX,meleeTop-1,card.Width-g.SecondX-9);
                meleeCountLabel.Font=SizedFont(g.TextSize,false);meleeCountLabel.Height=rowHeight;
                card.Controls.Add(meleeIcon);card.Controls.Add(meleeLabel);card.Controls.Add(meleeCountLabel);
                meleeIcon.Cursor=meleeLabel.Cursor=Cursors.Hand;
                EventHandler click=delegate{ToggleMeleeDetails(entry);};meleeIcon.Click+=click;meleeLabel.Click+=click;
                toolTip.SetToolTip(meleeIcon,"Blood Beast melee — click for hits; excluded from pop total");
                toolTip.SetToolTip(meleeLabel,"Melee: "+entry.Melee.ToString("N0")+" — excluded from CB + BiL total");
            }
            ConfigureDetails(card,entry,first,g.BaseHeight+2,g.BaseHeight,firstLabel,secondLabel);
        }
        private void RenderMiniCard(Panel card,Entry entry,bool large=false){
            if(large){RenderDetailedCard(card,entry);return;}
            int baseHeight=55;
            int damageTop=29;
            int headingTop=1;
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=baseHeight,BackColor=entry.Beast?red:green});
            string title=entry.Beast?"Beast #"+entry.Number:"#"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            string total=Format(entry.Beast?entry.Corrupted+entry.Life:entry.Dread+entry.Virulent);
            string hits=entry.Hits.Count+(entry.Hits.Count==1?" hit":" hits");
            int iconSize=Math.Max(18,Math.Min(23,settings.IconSize));
            // Align the first icon to the title and the second icon to the hit count above it.
            int firstX=55;
            int statusCount=entry.Beast?(settings.ShowBeastBlightfall?1:0):((settings.ShowSoulReaper?1:0)+(settings.ShowScythe?1:0));
            int statusRight=card.Width-9;
            int statusX=statusRight-statusCount*iconSize-(statusCount-1)*3;
            int totalWidth,hitsWidth=0;
            using(var font=new Font("Segoe UI",settings.TextSize,FontStyle.Bold))totalWidth=TextRenderer.MeasureText(total,font).Width;
            if(!entry.Beast)using(var font=new Font("Segoe UI",settings.TextSize))hitsWidth=TextRenderer.MeasureText(hits,font).Width;
            int totalLabelWidth=totalWidth+4;
            int totalX=statusRight-totalLabelWidth;
            int secondX=DamageColumnX(entry,card.Width,true,iconSize);
            int countX=secondX-3; // Label text has a small inset; its glyph matches the icon edge.
            var eventIcon=SpellIcon(entry.Beast?"BB":"BF",9,9,36);
            if(!string.IsNullOrEmpty(entry.Segment))toolTip.SetToolTip(eventIcon,entry.Segment);
            card.Controls.Add(eventIcon);
            var titleLabel=Label(title,Color.White,52,headingTop,Math.Max(30,(entry.Beast?totalX:countX)-57),true);
            toolTip.SetToolTip(titleLabel,title+(entry.Segment==""?"":" — "+entry.Segment));
            card.Controls.Add(titleLabel);
            var totalLabel=Label(total,green,totalX,headingTop,totalLabelWidth,true);
            totalLabel.TextAlign=ContentAlignment.TopRight;
            card.Controls.Add(totalLabel);
            if(!entry.Beast){
                var countLabel=Label(hits,dim,countX,headingTop+2,hitsWidth+3);
                countLabel.Height=TextRenderer.MeasureText("Ag",countLabel.Font).Height+2;card.Controls.Add(countLabel);
            }
            string first=entry.Beast?"CB":"DP",second=entry.Beast?"BiL":"VP";
            long firstValue=entry.Beast?entry.Corrupted:entry.Dread,secondValue=entry.Beast?entry.Life:entry.Virulent;
            bool showExtra=statusCount>0;
            var firstIcon=SpellIcon(first,firstX,damageTop,iconSize);
            var secondIcon=SpellIcon(second,secondX,damageTop,iconSize);
            var firstLabel=Label(Format(firstValue),!entry.Beast&&entry.Hits.Exists(h=>h.Kind=="DP"&&h.Crit)?Color.Gold:dim,
                firstX+iconSize+4,damageTop-1,Math.Max(16,secondX-firstX-iconSize-8));
            var secondLabel=Label(Format(secondValue),VirulentDamageColor(entry),secondX+iconSize+4,damageTop-1,
                Math.Max(16,(showExtra?statusX:card.Width-7)-secondX-iconSize-8));
            card.Controls.Add(firstIcon);card.Controls.Add(secondIcon);card.Controls.Add(firstLabel);card.Controls.Add(secondLabel);
            string firstTip=first+" "+Format(firstValue)+" — click for hits";
            string secondTip=second+" "+Format(secondValue)+" — click for hits";
            toolTip.SetToolTip(firstIcon,firstTip);toolTip.SetToolTip(firstLabel,firstTip);
            toolTip.SetToolTip(secondIcon,secondTip);toolTip.SetToolTip(secondLabel,secondTip);
            firstIcon.Cursor=firstLabel.Cursor=secondIcon.Cursor=secondLabel.Cursor=Cursors.Hand;
            EventHandler toggleFirst=delegate {ToggleDetails(entry,true);};
            EventHandler toggleSecond=delegate {ToggleDetails(entry,false);};
            firstIcon.Click+=toggleFirst;firstLabel.Click+=toggleFirst;
            secondIcon.Click+=toggleSecond;secondLabel.Click+=toggleSecond;
            if(showExtra){
                bool? active=entry.Beast?(entry.Exploded?(bool?)entry.BlightfallBefore:null):entry.Scythe;
                string tip=entry.Beast?(entry.Exploded?(entry.BlightfallBefore?"Blightfall before Blood Is Life":"No Blightfall before Blood Is Life"):"Waiting for Blood Is Life"):
                    "Festering Scythe: "+(entry.Scythe.HasValue?(entry.Scythe.Value?"active":"inactive"):"unknown");
                if(!entry.Beast&&settings.ShowSoulReaper)
                    card.Controls.Add(SpellIcon("SR",statusX,damageTop,iconSize,SoulReaperTip(entry),entry.SoulReaper));
                if(entry.Beast||settings.ShowScythe)card.Controls.Add(SpellIcon(entry.Beast?"BF":"SC",statusX+(statusCount-1)*(iconSize+3),damageTop,iconSize,tip,active));
            }
            titleLabel.Height=totalLabel.Height=TextRenderer.MeasureText("Ag",LabelFont(true)).Height+2;
            ConfigureDetails(card,entry,first,baseHeight+2,baseHeight,firstLabel,secondLabel);
        }
        private void RefreshCards(bool followLatest=false){if(cards.IsDisposed)return;
            int oldScroll=scrollPixels;
            bool wasAtBottom=oldScroll>=Math.Max(0,totalContentHeight-cards.ClientSize.Height)-2;
            refreshingCards=true;cards.SuspendLayout();
            try{
            var live=new HashSet<Entry>(tracker.Entries);
            var stale=new List<Entry>();foreach(var entry in cardCache.Keys)if(!live.Contains(entry))stale.Add(entry);
            foreach(var entry in stale){var state=cardCache[entry];cards.Controls.Remove(state.Card);state.Card.Dispose();cardCache.Remove(entry);}
            foreach(var state in cardCache.Values)if(!PlayerVisible(state.Entry)||state.Entry.Beast&&!settings.ShowBeasts)state.Card.Visible=false;
            foreach(var header in playerHeaders.Values)header.Visible=false;
            visibleCards.Clear();
            int y=3;
            var ordered=new List<Entry>(tracker.Entries);
            ordered.Sort(delegate(Entry a,Entry b){
                if(settings.PlayerMode=="All Players"&&a.PlayerGuid!=b.PlayerGuid){
                    CombatPlayer ap,bp;
                    bool ah=tracker.Players.TryGetValue(a.PlayerGuid??"",out ap),bh=tracker.Players.TryGetValue(b.PlayerGuid??"",out bp);
                    if(ah&&bh){int cmp=ap.FirstSeen.CompareTo(bp.FirstSeen);if(cmp!=0)return cmp;}
                    return String.Compare(a.PlayerGuid,b.PlayerGuid,StringComparison.Ordinal);
                }
                if(settings.GroupByEventType&&a.Beast!=b.Beast)return a.Beast?-1:1;
                int byTime=a.Time.CompareTo(b.Time);
                return byTime!=0?byTime:a.Sequence.CompareTo(b.Sequence);
            });
            if(!ShowMiniCards)UpdateDetailHeaderColumns(Math.Max(260,cards.ClientSize.Width-7));
            string lastPlayer=null;
            foreach(var entry in ordered){if(!PlayerVisible(entry)||entry.Beast&&!settings.ShowBeasts)continue;
                if(settings.PlayerMode=="All Players"&&entry.PlayerGuid!=lastPlayer){
                    lastPlayer=entry.PlayerGuid;
                    string groupKey=lastPlayer??"unknown";
                    Panel header;
                    if(!playerHeaders.TryGetValue(groupKey,out header)){
                        header=new BufferedPanel{BackColor=Color.FromArgb(42,48,56),Height=29,Cursor=Cursors.Hand};
                        playerHeaders.Add(groupKey,header);cards.Controls.Add(header);
                        header.Click+=delegate {
                            bool expanded;playerExpanded.TryGetValue(groupKey,out expanded);
                            playerExpanded[groupKey]=!expanded;RefreshCards();
                        };
                    }
                    bool open; if(!playerExpanded.TryGetValue(groupKey,out open))open=true;
                    CombatPlayer known;string playerName=tracker.Players.TryGetValue(groupKey,out known)?known.Name:entry.PlayerName;
                    header.Width=Math.Max(260,cards.ClientSize.Width-7);header.Left=3;header.Top=y-oldScroll;
                    header.Controls.Clear();
                    var nameLabel=new Label{Text=(open?"▾  ":"▸  ")+(String.IsNullOrEmpty(playerName)?groupKey:playerName),
                        ForeColor=Color.White,Font=LabelFont(true),AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
                        Padding=new Padding(9,0,0,0),Cursor=Cursors.Hand};
                    nameLabel.Click+=delegate {bool expanded;playerExpanded.TryGetValue(groupKey,out expanded);playerExpanded[groupKey]=!expanded;RefreshCards();};
                    header.Controls.Add(nameLabel);header.Visible=true;y+=header.Height+5;
                }
                if(settings.PlayerMode=="All Players"){
                    bool expanded;if(playerExpanded.TryGetValue(entry.PlayerGuid??"unknown",out expanded)&&!expanded){
                        CardState hidden;if(cardCache.TryGetValue(entry,out hidden))hidden.Card.Visible=false;
                        continue;
                    }
                }
                int width=Math.Max(260,cards.ClientSize.Width-7);
                string signature=CardSignature(entry,width);CardState state;
                if(!cardCache.TryGetValue(entry,out state)){
                    state=new CardState{Entry=entry,Card=new BufferedPanel{Left=3,Width=width,BackColor=rowColor}};
                    cardCache.Add(entry,state);state.Card.MouseWheel+=ScrollWheel;cards.Controls.Add(state.Card);
                }
                var card=state.Card;visibleCards.Add(state);card.Visible=true;card.Tag=y;card.Top=y-oldScroll;
                if(state.Signature==signature){y+=card.Height+(ShowMiniCards?4:5);continue;}
                int predictedHeight=PreviewEventHeight(entry,width,ShowMiniCards);
                if(y+predictedHeight<oldScroll-80||y>oldScroll+cards.ClientSize.Height+80){
                    // Retain lightweight geometry; defer control construction and wrapping until visible.
                    card.Width=width;card.Height=predictedHeight;
                    y+=predictedHeight+(ShowMiniCards?4:5);continue;
                }
                card.SuspendLayout();
                var oldControls=new List<Control>();foreach(Control control in card.Controls)oldControls.Add(control);
                card.Controls.Clear();foreach(var control in oldControls)control.Dispose();state.Rows.Clear();
                card.Width=width;state.Signature=signature;
                if(ShowMiniCards){
                    RenderMiniCard(card,entry);
                    foreach(Control child in card.Controls)if(!(child is Panel&&state.Rows.ContainsValue((Panel)child)))HookWheel(child);
                    card.ResumeLayout();y+=card.Height+4;
                    continue;
                }
                RenderMiniCard(card,entry,true);
                foreach(Control child in card.Controls)if(!(child is Panel&&state.Rows.ContainsValue((Panel)child)))HookWheel(child);
                card.ResumeLayout();y+=card.Height+5;
            }
            totalContentHeight=y;
            }finally{cards.ResumeLayout();refreshingCards=false;}
            ScrollTo(followLatest&&wasAtBottom?Math.Max(0,totalContentHeight-cards.ClientSize.Height):oldScroll);
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);
            if(framed&&artwork!=null&&!settings.Collapsed){using(var pen=new Pen(Color.FromArgb(75,107,92),2))e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);}
        }
    }
    internal static class Program {
        [STAThread] private static void Main(string[] args) {
            bool framed=Array.IndexOf(args,"--framed")>=0;
            if(args.Length>=4&&args[0]=="--apply-update"){
                Updater.Apply(args[1],args[2],args[3],framed);return;
            }
            if(args.Length>=2&&args[0]=="--cleanup-update")Updater.CleanupLater(args[1]);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Overlay(framed));
        }
    }
}
