using System;

namespace BlightfallPopsDesktop {
    // Shared damage totals for Blightfall and Blood Beast cards.
    internal static class EventMetrics {
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
