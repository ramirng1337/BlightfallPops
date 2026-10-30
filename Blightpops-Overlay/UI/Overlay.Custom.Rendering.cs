using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BlightfallPopsDesktop {
    internal sealed partial class Overlay {
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
        private int CustomCardHeight(Entry entry) {
            return ShowMiniCards?68:CustomIsExpanded(entry)?Math.Max(88,62+entry.Hits.Count*35):66;
        }
        private string CustomCardSignature(Entry entry,int width) {
            long total=0;foreach(var hit in entry.Hits)total+=hit.Amount;
            return width+"|"+ShowMiniCards+"|"+settings.TextSize+"|"+settings.CompactNumbers+
                "|"+entry.Hits.Count+"|"+total+"|"+entry.CustomName+"|"+CustomIsExpanded(entry);
        }
        private void RenderCustomCard(Panel card,Entry entry) {
            card.Height=CustomCardHeight(entry);
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=card.Height,BackColor=green});
            string title=(String.IsNullOrWhiteSpace(entry.CustomName)?"Custom event":entry.CustomName)+
                "  #"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            var heading=Label((CustomIsExpanded(entry)?"▾  ":"▸  ")+title,Color.White,12,8,Math.Max(80,card.Width-24),true);
            heading.Cursor=Cursors.Hand;
            Action toggle=delegate{customExpanded[entry]=!CustomIsExpanded(entry);RefreshCards();};
            heading.Click+=delegate{toggle();};card.Click+=delegate{toggle();};
            card.Controls.Add(heading);
            long total=0;int crits=0;
            foreach(var hit in entry.Hits){total+=hit.Amount;if(hit.Crit)crits++;}
            card.Controls.Add(Label(Format(total)+" damage  •  "+entry.Hits.Count+
                " hits  •  "+crits+(crits==1?" crit":" crits"),dim,12,34,Math.Max(80,card.Width-24)));
            if(ShowMiniCards||!CustomIsExpanded(entry))return;
            int y=62;
            foreach(var hit in entry.Hits){
                var line=new BufferedPanel{Left=8,Top=y,Width=Math.Max(80,card.Width-16),Height=32,BackColor=Color.FromArgb(23,28,33)};
                string name=String.IsNullOrWhiteSpace(hit.SpellName)?hit.SpellId:hit.SpellName;
                string target=String.IsNullOrWhiteSpace(hit.Target)?"Unknown target":hit.Target;
                var amount=Label(Format(hit.Amount),hit.Crit?Color.Gold:dim,8,6,88);
                var outcome=Label(hit.Crit?"Crit":"Hit",hit.Crit?Color.Gold:dim,102,6,44,true);
                var details=Label(name+"  •  "+target,dim,153,6,Math.Max(30,line.Width-162));
                line.Controls.Add(amount);line.Controls.Add(outcome);line.Controls.Add(details);
                if(hit.Crit)line.Controls.Add(new Panel{Left=1,Top=3,Width=2,Height=26,BackColor=Color.Gold});
                card.Controls.Add(line);y+=35;
            }
        }
    }
}
