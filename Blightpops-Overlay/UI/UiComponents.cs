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
    // Resize preview overlay.
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
    // Flicker-free panel drawing.
    internal sealed class BufferedPanel : Panel {
        public BufferedPanel(){DoubleBuffered=true;ResizeRedraw=true;}
    }
}
