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
    // Damage hit and target debuffs.
    internal sealed class Hit { public string Kind, SpellId, SpellName, Target, TargetGuid; public long Amount, Overkill; public bool Crit; public readonly List<AuraSnapshot> Debuffs=new List<AuraSnapshot>(); }

    // Aura state at event time.
    internal sealed class AuraSnapshot { public string Name, SpellId, IconPath; public bool? Active; public int Stacks; public bool HasDose; }

    // Tracked cast and damage event.
    internal sealed class Entry {
        public readonly List<AuraSnapshot> AuraSnapshots=new List<AuraSnapshot>();
        public bool Beast, Custom, DOpen, VOpen, MOpen, Exploded, BlightfallBefore;
        public string CustomName;
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

    // Player identity by GUID.
    internal sealed class CombatPlayer {
        public string Guid, Name;
        public DateTime FirstSeen, LastSeen;
        public int EventCount;
    }
}
