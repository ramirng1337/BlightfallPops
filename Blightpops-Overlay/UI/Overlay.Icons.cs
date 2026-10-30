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
    // Spell icons and aura status indicators.
    internal sealed partial class Overlay {
        // Load shared spell images once per overlay.
        private void LoadSpellIcons(){ SpellIconCache.LoadInto(spellIcons); }
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
        private PictureBox CustomAuraIcon(AuraSnapshot aura,int x,int y,int size){
            var box=new PictureBox{Left=x,Top=y,Width=size,Height=size,BackColor=Color.FromArgb(21,23,27),
                SizeMode=PictureBoxSizeMode.Zoom};
            if(!String.IsNullOrEmpty(aura.IconPath)){
                try{box.Image=ReadBuilderImage(aura.IconPath);}catch{}
                box.Disposed+=delegate{if(box.Image!=null)box.Image.Dispose();};
            }
            toolTip.SetToolTip(box,(aura.Name??aura.SpellId)+": "+(aura.Active.HasValue?(aura.Active.Value?"active":"inactive"):"unknown")+
                (aura.HasDose&&aura.Active==true?" ("+aura.Stacks+" stacks)":""));
            box.Paint+=delegate(object sender,PaintEventArgs e){
                if(aura.Active==false){using(var pen=new Pen(red,Math.Max(2,size/9))){
                    e.Graphics.DrawLine(pen,2,2,size-3,size-3);e.Graphics.DrawLine(pen,size-3,2,2,size-3);}}
                else if(!aura.Active.HasValue){TextRenderer.DrawText(e.Graphics,"?",SizedFont(Math.Max(8,size/2),true),
                    box.ClientRectangle,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
                if(aura.Active==true&&aura.HasDose&&aura.Stacks>0){
                    string count=aura.Stacks.ToString(CultureInfo.InvariantCulture);
                    using(var font=new Font("Segoe UI",Math.Max(8,size*0.35f),FontStyle.Bold)){
                        var flags=TextFormatFlags.Right|TextFormatFlags.Bottom|TextFormatFlags.NoPadding;
                        var area=new Rectangle(0,0,size-1,size-1);
                        TextRenderer.DrawText(e.Graphics,count,font,new Rectangle(1,1,size-1,size-1),Color.Black,flags);
                        TextRenderer.DrawText(e.Graphics,count,font,area,Color.White,flags);
                    }
                }
            };
            return box;
        }
    }
}
