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
    // Compare card rendering and control reuse.
    internal sealed partial class Overlay {
        // Rebuild a comparison pane only when its data or width changes.
        private void PopulateCompareColumn(CompareColumn column,Tracker source,Dictionary<string,string> names,string guid,bool showBlightfall,bool showBeasts){
                var panel=column.Content;
                int width=Math.Max(150,column.View.ClientSize.Width-column.Track.Width-12);
                // Reuse the complete card tree when neither player, data nor column width changed.
                // Moving the window and height-only resizing never rebuild controls or icons.
                int eventCount=0;long lastSequence=-1;
                foreach(var candidate in source.Entries)if(candidate.PlayerGuid==guid&&((!candidate.Beast&&showBlightfall)||(candidate.Beast&&showBeasts))){
                    eventCount++;if(candidate.Sequence>lastSequence)lastSequence=candidate.Sequence;
                }
                if(Object.ReferenceEquals(column.RenderSource,source)&&column.RenderGuid==guid&&
                    column.RenderWidth==width&&column.RenderCount==eventCount&&
                    column.RenderLastSequence==lastSequence){column.UpdateScroll();return;}
                int previousOffset=column.Offset;
                panel.SuspendLayout();
                // Clear() only detaches WinForms controls; dispose old cards to avoid leaking handles.
                while(panel.Controls.Count>0){Control old=panel.Controls[0];panel.Controls.RemoveAt(0);old.Dispose();}
                var events=new List<Entry>();foreach(var e in source.Entries)if(e.PlayerGuid==guid&&((!e.Beast&&showBlightfall)||(e.Beast&&showBeasts)))events.Add(e);
                events.Sort(delegate(Entry a,Entry b){int t=a.Time.CompareTo(b.Time);return t!=0?t:a.Sequence.CompareTo(b.Sequence);});
                long dread=0,virulent=0;int hits=0;foreach(var e in events){dread+=EventMetrics.First(e);virulent+=EventMetrics.Second(e);hits+=e.Hits.Count;}
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
                    var total=Label(Format(EventMetrics.Total(e)),green,totalX,1,
                        Math.Max(35,width-totalX-8),true);
                    total.TextAlign=ContentAlignment.TopRight;card.Controls.Add(total);
                    var dp=SpellIcon(e.Beast?"CB":"DP",55,29,iconSize);
                    var vp=SpellIcon(e.Beast?"BiL":"VP",secondX,29,iconSize);
                    card.Controls.Add(dp);card.Controls.Add(vp);
                    var dpValue=Label(Format(EventMetrics.First(e)),e.Hits.Exists(h=>h.Kind==(e.Beast?"CB":"DP")&&h.Crit)?Color.Gold:dim,
                        55+iconSize+4,28,Math.Max(30,secondX-55-iconSize-8));
                    var vpValue=Label(Format(EventMetrics.Second(e)),e.Beast?dim:VirulentDamageColor(e),
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
                    }
    }
}
