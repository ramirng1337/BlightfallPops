using System;

namespace BlightfallPopsDesktop {
    // Shared damage totals for Blightfall and Blood Beast cards.
    internal static class EventMetrics {
        // Prefer Spell ID; use name only when no ID is provided.
        internal static bool Matches(Hit hit,string spellId,string spellName) {
            if(hit==null)return false;
            if(!String.IsNullOrWhiteSpace(spellId))
                return String.Equals(hit.SpellId,spellId.Trim(),StringComparison.Ordinal);
            return !String.IsNullOrWhiteSpace(spellName) &&
                String.Equals(hit.SpellName,spellName.Trim(),StringComparison.OrdinalIgnoreCase);
        }

        // Sum recorded hits for any configured damage spell.
        internal static long Damage(Entry entry,string spellId,string spellName=null) {
            if(entry==null)return 0;
            long total=0;
            foreach(var hit in entry.Hits)
                if(Matches(hit,spellId,spellName))total+=hit.Amount;
            foreach(var hit in entry.MeleeHits)
                if(Matches(hit,spellId,spellName))total+=hit.Amount;
            return total;
        }

        internal static long First(Entry entry) {
            return entry.Beast ? entry.Corrupted : entry.Dread;
        }

        internal static long Second(Entry entry) {
            return entry.Beast ? entry.Life : entry.Virulent;
        }

        internal static long Total(Entry entry) {
            return First(entry) + Second(entry);
        }
    }
}
