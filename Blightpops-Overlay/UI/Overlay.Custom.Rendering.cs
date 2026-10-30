using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BlightfallPopsDesktop {
    internal sealed partial class Overlay {
        // Neutral icon slot for custom events and effects without artwork.
        private PictureBox CustomPlaceholder(int x,int y,int size) {
            var icon=new PictureBox{Left=x,Top=y,Width=size,Height=size,BackColor=Color.FromArgb(45,51,59),SizeMode=PictureBoxSizeMode.Zoom};
            icon.Paint+=delegate(object sender,PaintEventArgs e){
                if(icon.Image!=null)return;
                var g=e.Graphics;
                using(var pen=new Pen(Color.FromArgb(145,164,184),1.4f)){
                    int margin=Math.Max(5,size/5),w=Math.Max(8,size-2*margin);
                    g.DrawRectangle(pen,margin,margin,w,w);
                    g.DrawEllipse(pen,margin+3,margin+3,Math.Max(3,size/8),Math.Max(3,size/8));
                    g.DrawLine(pen,margin+2,margin+w-3,margin+w/2,margin+w/2);
                    g.DrawLine(pen,margin+w/2,margin+w/2,margin+w-2,margin+w-3);
                }
            };
            return icon;
        }
        // Resolve saved artwork by the custom event's configured trigger spell.
        private CustomCard CustomDefinition(Entry entry) {
            foreach(var definition in ReadCustomCards())
                if(definition!=null&&String.Equals((definition.SpellId??"").Trim(),(entry.CustomSpell??"").Trim(),StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }
        private PictureBox CustomArtwork(string path,int x,int y,int size) {
            var slot=CustomPlaceholder(x,y,size);
            slot.Image=ReadBuilderImage(path);
            return slot;
        }
        // Keep custom card layout independent from built-in Blightfall cards.
        private Dictionary<string,CustomDamageSummary> CustomDamageGroups(Entry entry) {
            var totals=new Dictionary<string,CustomDamageSummary>(StringComparer.Ordinal);
            foreach(var hit in entry.Hits) {
                string key=!String.IsNullOrWhiteSpace(hit.SpellId)?hit.SpellId:hit.SpellName??"Damage";
                CustomDamageSummary value;
                if(!totals.TryGetValue(key,out value)){value=new CustomDamageSummary{Name=hit.SpellName,SpellId=hit.SpellId};totals[key]=value;}
                value.Damage+=hit.Amount;value.Hits++;if(hit.Crit)value.Crits++;
            }
            return totals;
        }
        private sealed class CustomDamageSummary {public string Name,SpellId;public long Damage;public int Hits,Crits;}
        private readonly Dictionary<Entry,bool> customExpanded=new Dictionary<Entry,bool>();
        private bool CustomIsExpanded(Entry entry){bool open;return customExpanded.TryGetValue(entry,out open)&&open;}
        // Group hits by configured damage spell, with main spell first.
        private List<string> CustomGroupOrder(Entry entry,CustomCard definition) {
            var order=new List<string>();
            if(definition!=null) {
                if(!String.IsNullOrWhiteSpace(definition.SpellId))order.Add(definition.SpellId.Trim());
                if(definition.Effects!=null)foreach(var effect in definition.Effects)
                    if(effect!=null&&effect.Type=="Damage"&&!String.IsNullOrWhiteSpace(effect.SpellId)&&!order.Contains(effect.SpellId.Trim()))
                        order.Add(effect.SpellId.Trim());
            }
            foreach(var hit in entry.Hits){
                string id=!String.IsNullOrWhiteSpace(hit.SpellId)?hit.SpellId:hit.SpellName??"Damage";
                if(!order.Contains(id))order.Add(id);
            }
            return order;
        }
        private string CustomHitGroup(Hit hit) {
            return !String.IsNullOrWhiteSpace(hit.SpellId)?hit.SpellId:hit.SpellName??"Damage";
        }
        private int CustomActiveGroupCount(Entry entry) {
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var hit in entry.Hits)ids.Add(CustomHitGroup(hit));
            return ids.Count;
        }
        private int CustomCardHeight(Entry entry) {
            return CustomIsExpanded(entry)?Math.Max(88,62+entry.Hits.Count*35+CustomActiveGroupCount(entry)*19):(ShowMiniCards?68:66);
        }
        private string CustomCardSignature(Entry entry,int width) {
            long total=0;foreach(var hit in entry.Hits)total+=hit.Amount;
            return width+"|"+ShowMiniCards+"|"+settings.TextSize+"|"+settings.CompactNumbers+
                "|"+entry.Hits.Count+"|"+total+"|"+entry.CustomName+"|"+CustomIsExpanded(entry)+"|"+(CustomDefinition(entry)==null?"":CustomDefinition(entry).IconPath);
        }
        private void RenderCustomCard(Panel card,Entry entry) {
            card.Height=CustomCardHeight(entry);
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=card.Height,BackColor=green});
            string title=(String.IsNullOrWhiteSpace(entry.CustomName)?"Custom event":entry.CustomName)+
                "  #"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            var definition=CustomDefinition(entry);
            var eventIcon=CustomArtwork(definition==null?null:definition.IconPath,10,10,42);
            eventIcon.Cursor=Cursors.Hand;
            card.Controls.Add(eventIcon);
            var heading=Label((CustomIsExpanded(entry)?"▾  ":"▸  ")+title,Color.White,60,8,Math.Max(80,card.Width-72),true);
            heading.Cursor=Cursors.Hand;
            Action toggle=delegate{customExpanded[entry]=!CustomIsExpanded(entry);RefreshCards();};
            heading.Click+=delegate{toggle();};eventIcon.Click+=delegate{toggle();};card.Click+=delegate{toggle();};
            card.Controls.Add(heading);
            long total=0;int crits=0;
            foreach(var hit in entry.Hits){total+=hit.Amount;if(hit.Crit)crits++;}
            card.Controls.Add(Label(Format(total)+" damage  •  "+entry.Hits.Count+
                " hits  •  "+crits+(crits==1?" crit":" crits"),dim,60,34,Math.Max(80,card.Width-72)));
            if(!CustomIsExpanded(entry))return;
            int y=62;
            var groups=CustomDamageGroups(entry);
            foreach(var groupId in CustomGroupOrder(entry,definition)) {
                CustomDamageSummary summary;
                if(!groups.TryGetValue(groupId,out summary))continue;
                bool secondary=definition!=null&&!String.Equals(groupId,definition.SpellId,StringComparison.OrdinalIgnoreCase);
                // Minimal separator; the actual damage events stay directly below.
                var section=new BufferedPanel{Left=8,Top=y,Width=Math.Max(80,card.Width-16),Height=16,
                    BackColor=Color.FromArgb(30,36,42)};
                section.Controls.Add(new Panel{Left=0,Top=0,Width=2,Height=16,BackColor=secondary?Color.FromArgb(92,160,208):green});
                section.Controls.Add(Label(secondary?"Secondary Damage Effect":"Main Damage Effect",
                    dim,8,0,Math.Max(40,section.Width-16)));
                card.Controls.Add(section);y+=19;
                foreach(var hit in entry.Hits) {
                    if(!String.Equals(CustomHitGroup(hit),groupId,StringComparison.OrdinalIgnoreCase))continue;
                    var line=new BufferedPanel{Left=8,Top=y,Width=Math.Max(80,card.Width-16),Height=32,BackColor=Color.FromArgb(23,28,33)};
                    string name=String.IsNullOrWhiteSpace(hit.SpellName)?hit.SpellId:hit.SpellName;
                    string target=String.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
                    string effectIcon=null;
                    if(definition!=null&&definition.Effects!=null)
                        foreach(var effect in definition.Effects)
                            if(effect!=null&&effect.Type=="Damage"&&
                               (String.Equals(effect.SpellId,hit.SpellId,StringComparison.OrdinalIgnoreCase)||
                                String.Equals(effect.SpellId,hit.SpellName,StringComparison.OrdinalIgnoreCase))) {
                                effectIcon=effect.IconPath;break;
                            }
                    line.Controls.Add(CustomArtwork(effectIcon,4,3,26));
                    var amount=Label(Format(hit.Amount),hit.Crit?Color.Gold:dim,36,6,80);
                    var outcome=Label(hit.Crit?"Crit":"Hit",hit.Crit?Color.Gold:dim,122,6,44,true);
                    var details=Label(name+"  •  "+target,dim,173,6,Math.Max(30,line.Width-182));
                    line.Controls.Add(amount);line.Controls.Add(outcome);line.Controls.Add(details);
                    if(hit.Crit)line.Controls.Add(new Panel{Left=1,Top=3,Width=2,Height=26,BackColor=Color.Gold});
                    card.Controls.Add(line);y+=35;
                }
            }
        }
    }
}
