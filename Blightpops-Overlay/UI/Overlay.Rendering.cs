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
    // Event cards, damage rows and auras.
    internal sealed partial class Overlay {
        // Custom aura indicators share one layout area. Detailed view reserves rows below
        // the damage summary; compact view uses only the free space before status icons.
        private List<AuraSnapshot> VisibleCustomAuras(Entry entry,bool mini){
            var result=new List<AuraSnapshot>();
            if(entry.Beast)return result;
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var aura in entry.AuraSnapshots){
                CustomEffect info;
                if(!customAuraInfo.TryGetValue(aura.SpellId,out info))continue;
                if(mini?!info.ShowCompact:!info.ShowDetailed)continue;
                if(info.Type=="Debuff")continue; // Enemy debuffs belong beside individual hit targets.
                if(!seen.Add(aura.SpellId))continue;
                aura.Name=info.Name;aura.IconPath=info.IconPath;
                result.Add(aura);
            }
            return result;
        }
        private List<AuraSnapshot> HitDebuffIcons(Hit hit,bool mini){
            var result=new List<AuraSnapshot>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var aura in hit.Debuffs){
                CustomEffect info;
                if(!customAuraInfo.TryGetValue(aura.SpellId,out info)||info.Type!="Debuff")continue;
                if(mini?!info.ShowCompact:!info.ShowDetailed)continue;
                if(!seen.Add(aura.SpellId))continue;
                // Only confirmed active debuffs are drawn. Unknown state is not zero stacks.
                if(aura.Active!=true)continue;
                aura.Name=info.Name;aura.IconPath=info.IconPath;
                result.Add(aura);
            }
            return result;
        }
        private int HitDebuffExtraHeight(Hit hit,int rowWidth,int targetX,int targetWidth,int iconSize,int lineHeight){
            var auras=HitDebuffIcons(hit,ShowMiniCards);
            if(auras.Count==0)return 0;
            string target=String.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
            int nameWidth=TextRenderer.MeasureText(target,LabelFont(false)).Width;
            int iconSpace=auras.Count*(iconSize+3);
            if(nameWidth+iconSpace<=targetWidth)return 0;
            int columns=Math.Max(1,(rowWidth-(19+iconSize)-8)/(iconSize+3));
            int rows=(auras.Count+columns-1)/columns;
            return rows*(iconSize+3)+3;
        }
        private int DetailedAuraHeight(Entry entry,int width,int iconSize){
            var list=VisibleCustomAuras(entry,false);
            if(list.Count==0)return 0;
            int usable=Math.Max(1,width-112); // Reserve the left-side label column.
            int columns=Math.Max(1,usable/(iconSize+4));
            int buffs=0,debuffs=0;
            foreach(var aura in list){CustomEffect info;
                if(customAuraInfo.TryGetValue(aura.SpellId,out info)&&info.Type=="Debuff")debuffs++;
                else buffs++;
            }
            int rows=(buffs+columns-1)/columns+(debuffs+columns-1)/columns;
            return rows*(iconSize+5)+8;
        }
        private void AddDetailedAuras(Panel card,Entry entry,int top,int iconSize){
            var list=VisibleCustomAuras(entry,false);
            if(list.Count==0)return;
            int x0=105,step=iconSize+4,columns=Math.Max(1,(card.Width-x0-9)/step);
            int y=top+4;
            foreach(string type in new[]{"Buff","Debuff"}){
                var group=new List<AuraSnapshot>();
                foreach(var aura in list){CustomEffect info;
                    if(customAuraInfo.TryGetValue(aura.SpellId,out info)&&info.Type==type)group.Add(aura);
                }
                if(group.Count==0)continue;
                var label=Label(type=="Buff"?"Buffs":"Debuffs",dim,9,y+3,90);
                card.Controls.Add(label);
                for(int i=0;i<group.Count;i++){
                    int col=i%columns,row=i/columns;
                    card.Controls.Add(CustomAuraIcon(group[i],x0+col*step,y+row*(iconSize+5),iconSize));
                }
                y+=((group.Count+columns-1)/columns)*(iconSize+5);
            }
        }
        private void AddCompactAuras(Panel card,Entry entry,int top,int iconSize,int leftBound,int rightBound){
            var list=VisibleCustomAuras(entry,true);
            int right=rightBound;
            foreach(var aura in list){
                int x=right-iconSize;
                if(x<leftBound)break; // Never draw over damage text.
                card.Controls.Add(CustomAuraIcon(aura,x,top,iconSize));
                right=x-3;
            }
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
                    var debuffs=HitDebuffIcons(hit,ShowMiniCards);
                    int auraSize=Math.Max(18,Math.Min(26,settings.IconSize));
                    int auraWidth=debuffs.Count*(auraSize+3);
                    int nameWidth=TextRenderer.MeasureText(target,regular).Width;
                    bool wrap=nameWidth>targetWidth;
                    hitRow.Controls.Add(Label(amount,hit.Crit?Color.Gold:dim,amountX,5,amountWidth));
                    hitRow.Controls.Add(Label(result,hit.Crit?Color.Gold:dim,critX,5,critWidth,true));
                    Label targetLabel;
                    int auraY=5,auraX=targetX;
                    if(wrap){
                        int wrapX=amountX,wrapWidth=Math.Max(25,hitRow.Width-wrapX-8);
                        int wrapHeight=Math.Max(lineHeight,TextRenderer.MeasureText(target,regular,
                            new Size(wrapWidth,int.MaxValue),TextFormatFlags.WordBreak).Height);
                        targetLabel=Label(target,dim,wrapX,hitSize+6,wrapWidth);
                        targetLabel.AutoEllipsis=false;targetLabel.Height=wrapHeight+3;
                        hitRow.Height=hitSize+9+wrapHeight+3;
                        auraX=wrapX;auraY=hitRow.Height+2;
                    }else{
                        targetLabel=Label(target,dim,targetX,5,Math.Min(targetWidth,nameWidth+2));
                        auraX=targetX+nameWidth+5;
                        if(auraX+auraWidth>hitRow.Width-5){auraX=amountX;auraY=hitRow.Height+2;}
                    }
                    hitRow.Controls.Add(targetLabel);
                    toolTip.SetToolTip(targetLabel,target);
                    if(debuffs.Count>0){
                        int left=amountX,limit=hitRow.Width-6;
                        for(int i=0;i<debuffs.Count;i++){
                            if(auraX+auraSize>limit){auraX=left;auraY+=auraSize+3;}
                            hitRow.Controls.Add(CustomAuraIcon(debuffs[i],auraX,auraY,auraSize));
                            auraX+=auraSize+3;
                        }
                        if(auraY>5)hitRow.Height=Math.Max(hitRow.Height,auraY+auraSize+3);
                    }
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
                string total=Format(EventMetrics.Total(entry));
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
            string total=Format(EventMetrics.Total(entry));
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
            long firstValue=EventMetrics.First(entry),secondValue=EventMetrics.Second(entry);
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
            int auraHeight=DetailedAuraHeight(entry,card.Width,g.IconSize);
            if(auraHeight>0)AddDetailedAuras(card,entry,g.BaseHeight,g.IconSize);
            ConfigureDetails(card,entry,first,g.BaseHeight+auraHeight+2,g.BaseHeight+auraHeight,firstLabel,secondLabel);
        }
        private void RenderMiniCard(Panel card,Entry entry,bool large=false){
            if(large){RenderDetailedCard(card,entry);return;}
            int baseHeight=55;
            int damageTop=29;
            int headingTop=1;
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=baseHeight,BackColor=entry.Beast?red:green});
            string title=entry.Beast?"Beast #"+entry.Number:"#"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            string total=Format(EventMetrics.Total(entry));
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
            long firstValue=EventMetrics.First(entry),secondValue=EventMetrics.Second(entry);
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
            int compactAuraLeft=secondX+iconSize+4+TextRenderer.MeasureText(Format(secondValue),SizedFont(settings.TextSize,false)).Width+7;
            AddCompactAuras(card,entry,damageTop,iconSize,compactAuraLeft,showExtra?statusX-4:card.Width-8);
            ConfigureDetails(card,entry,first,baseHeight+2,baseHeight,firstLabel,secondLabel);
        }
        // Reuse cards to reduce UI rebuilds.
        private void RefreshCards(bool followLatest=false){if(cards.IsDisposed)return;
            int oldScroll=scrollPixels;
            bool wasAtBottom=oldScroll>=Math.Max(0,totalContentHeight-cards.ClientSize.Height)-2;
            refreshingCards=true;cards.SuspendLayout();
            try{
            var live=new HashSet<Entry>(tracker.Entries);
            foreach(var custom in tracker.CustomEntries)live.Add(custom);
            var stale=new List<Entry>();foreach(var entry in cardCache.Keys)if(!live.Contains(entry))stale.Add(entry);
            foreach(var entry in stale){var state=cardCache[entry];cards.Controls.Remove(state.Card);state.Card.Dispose();cardCache.Remove(entry);}
            foreach(var state in cardCache.Values)if(!PlayerVisible(state.Entry)||state.Entry.Beast&&!settings.ShowBeasts)state.Card.Visible=false;
            foreach(var header in playerHeaders.Values){header.Visible=false;header.Tag=null;}
            visibleCards.Clear();
            int y=3;
            var ordered=new List<Entry>(tracker.Entries);
            ordered.AddRange(tracker.CustomEntries);
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
                    header.Width=Math.Max(260,cards.ClientSize.Width-7);header.Left=3;header.Tag=y;header.Top=y-oldScroll;
                    header.Controls.Clear();
                    var nameLabel=new Label{Text=(open?"▾  ":"▸  ")+(String.IsNullOrEmpty(playerName)?groupKey:playerName),
                        ForeColor=Color.White,Font=LabelFont(true),AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
                        Padding=new Padding(9,0,0,0),Cursor=Cursors.Hand};
                    nameLabel.Click+=delegate {bool expanded;playerExpanded.TryGetValue(groupKey,out expanded);playerExpanded[groupKey]=!expanded;RefreshCards();};
                    header.Controls.Add(nameLabel);header.Visible=y+header.Height>=oldScroll-4&&y<=oldScroll+cards.ClientSize.Height+4;y+=header.Height+5;
                }
                if(settings.PlayerMode=="All Players"){
                    bool expanded;if(playerExpanded.TryGetValue(entry.PlayerGuid??"unknown",out expanded)&&!expanded){
                        CardState hidden;if(cardCache.TryGetValue(entry,out hidden))hidden.Card.Visible=false;
                        continue;
                    }
                }
                int width=Math.Max(260,cards.ClientSize.Width-7);
                string signature=entry.Custom?CustomCardSignature(entry,width):CardSignature(entry,width);CardState state;
                if(!cardCache.TryGetValue(entry,out state)){
                    state=new CardState{Entry=entry,Card=new BufferedPanel{Left=3,Width=width,BackColor=rowColor}};
                    cardCache.Add(entry,state);state.Card.MouseWheel+=ScrollWheel;cards.Controls.Add(state.Card);
                }
                var card=state.Card;visibleCards.Add(state);card.Tag=y;card.Top=y-oldScroll;card.Visible=y+Math.Max(card.Height,(entry.Custom?CustomCardHeight(entry):PreviewEventHeight(entry,width,ShowMiniCards)))>=oldScroll-4&&y<=oldScroll+cards.ClientSize.Height+4;
                if(state.Signature==signature){y+=card.Height+(ShowMiniCards?4:5);continue;}
                int predictedHeight=(entry.Custom?CustomCardHeight(entry):PreviewEventHeight(entry,width,ShowMiniCards));
                if(y+predictedHeight<oldScroll-80||y>oldScroll+cards.ClientSize.Height+80){
                    // Retain lightweight geometry; defer control construction and wrapping until visible.
                    card.Width=width;card.Height=predictedHeight;
                    y+=predictedHeight+(ShowMiniCards?4:5);continue;
                }
                card.SuspendLayout();
                var oldControls=new List<Control>();foreach(Control control in card.Controls)oldControls.Add(control);
                card.Controls.Clear();foreach(var control in oldControls)control.Dispose();state.Rows.Clear();
                card.Width=width;state.Signature=signature;
                if(entry.Custom){
                    RenderCustomCard(card,entry);
                    foreach(Control child in card.Controls)HookWheel(child);
                    card.ResumeLayout();y+=card.Height+4;
                    continue;
                }
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
    }
}
