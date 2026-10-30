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
    internal sealed partial class Overlay {
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

    }
}
