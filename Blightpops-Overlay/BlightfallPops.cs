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
}
