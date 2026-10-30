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
        private int CustomCardHeight(Entry entry) {
            return ShowMiniCards?68:Math.Max(88,60+CustomDamageGroups(entry).Count*26);
        }
        private string CustomCardSignature(Entry entry,int width) {
            long total=0;foreach(var hit in entry.Hits)total+=hit.Amount;
            return width+"|"+ShowMiniCards+"|"+settings.TextSize+"|"+settings.CompactNumbers+
                "|"+entry.Hits.Count+"|"+total+"|"+entry.CustomName;
        }
        private void RenderCustomCard(Panel card,Entry entry) {
            card.Height=CustomCardHeight(entry);
            card.Controls.Add(new Panel{Left=0,Top=0,Width=3,Height=card.Height,BackColor=green});
            string title=(String.IsNullOrWhiteSpace(entry.CustomName)?"Custom event":entry.CustomName)+
                "  #"+entry.Number+"  "+entry.Time.ToString("HH:mm:ss");
            card.Controls.Add(Label(title,Color.White,12,8,Math.Max(80,card.Width-24),true));
            long total=0;int crits=0;
            foreach(var hit in entry.Hits){total+=hit.Amount;if(hit.Crit)crits++;}
            card.Controls.Add(Label(Format(total)+" damage  •  "+entry.Hits.Count+
                " hits  •  "+crits+" crits",dim,12,34,Math.Max(80,card.Width-24)));
            if(ShowMiniCards)return;
            int y=60;
            foreach(var group in CustomDamageGroups(entry)) {
                string effect=String.IsNullOrWhiteSpace(group.Value.Name)?"Damage":group.Value.Name;
                card.Controls.Add(Label(effect+" ["+group.Key+"]  "+Format(group.Value.Damage)+"  •  "+group.Value.Hits+" hits  •  "+group.Value.Crits+" crits",dim,18,y,Math.Max(80,card.Width-32)));
                y+=26;
            }
        }
    }
}
