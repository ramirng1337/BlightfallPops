using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using BlightfallPopsDesktop;

// Regression tests for the actual WinForms overlay in this source tree.
// The previous test referenced removed SideBySide/TransparentBackground UI APIs.
class UiPerformanceTests {
    static int checks;
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(object target, string name) {
        var field = target.GetType().GetField(name, Hidden | BindingFlags.Public);
        if (field == null) throw new Exception("Missing field: " + name);
        return field.GetValue(target);
    }
    static object Call(object target, string name, params object[] args) {
        var method = target.GetType().GetMethod(name, Hidden);
        if (method == null) throw new Exception("Missing method: " + name);
        return method.Invoke(target, args);
    }
    static void Assert(bool value, string description) {
        if (!value) throw new Exception(description);
        checks++;
    }
    static Entry Sample(int number) {
        var entry = new Entry { Number = number, Sequence = number,
            Time = new DateTime(2026, 9, 30, 12, 0, number),
            Segment = "Test", Dread = 100, Virulent = 200,
            PlayerGuid = "Player-Test-Own", PlayerName = "Tester-Realm" };
        entry.Hits.Add(new Hit { Kind = "DP", Amount = 100, Target = "Target" });
        entry.Hits.Add(new Hit { Kind = "VP", Amount = 200, Target = "Target" });
        return entry;
    }
    static Control Card(IDictionary cache, Entry entry) {
        return (Control)Field(cache[entry], "Card");
    }
    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var overlay = new Overlay(false)) {
            var settings = (Settings)Field(overlay, "settings");
            settings.Log = "";
            settings.ShowBeasts = true;
            settings.MiniCards = false;
            overlay.ClientSize = new System.Drawing.Size(500, 390);
            var tracker = (Tracker)Field(overlay, "tracker");
            tracker.Guid = "Player-Test-Own";
            settings.PlayerMode = "Myself";
            var cache = (IDictionary)Field(overlay, "cardCache");
            var first = Sample(1);
            var second = Sample(2);
            tracker.Entries.Add(first);
            tracker.Entries.Add(second);
            Call(overlay, "RefreshCards", false);
            Assert(cache.Count == 2, "Two own-player cards are created");
            var foreign = Sample(99);
            foreign.PlayerGuid = "Player-Test-Other";
            tracker.Entries.Add(foreign);
            Call(overlay, "RefreshCards", false);
            Assert(!cache.Contains(foreign), "Myself hides another player");
            settings.PlayerMode = "Specific Player";
            settings.SelectedPlayerGuid = "Player-Test-Other";
            Call(overlay, "RefreshCards", false);
            Assert(cache.Contains(foreign), "Specific Player shows selected player");
            Assert(!Card(cache, first).Visible, "Specific Player hides own card");
            settings.PlayerMode = "Myself";
            Call(overlay, "RefreshCards", false);
            var firstCard = Card(cache, first);
            var secondCard = Card(cache, second);
            Assert(firstCard.Height > 0 && secondCard.Height > 0, "Cards have height");
            Assert(secondCard.Top >= firstCard.Top + firstCard.Height, "Cards do not overlap");
            var originalHeight = firstCard.Height;
            Call(overlay, "ToggleDetails", first, true);
            Assert(first.DOpen, "DP details open");
            Assert(firstCard.Height >= originalHeight, "Expanded card does not shrink");
            Assert(Object.ReferenceEquals(firstCard, Card(cache, first)), "Expand reuses card");
            Assert(Object.ReferenceEquals(secondCard, Card(cache, second)), "Expand reuses other card");
            Call(overlay, "ToggleDetails", first, true);
            Assert(!first.DOpen, "DP details close");
            Assert(firstCard.Height == originalHeight, "Closed card restores height");
            tracker.Entries.Remove(foreign);
            Call(overlay, "RefreshCards", false);
            var third = Sample(3);
            tracker.Entries.Add(third);
            Call(overlay, "RefreshCards", true);
            Assert(cache.Count == 3, "Appended event creates a card");
            Assert(Object.ReferenceEquals(firstCard, Card(cache, first)), "Append preserves earlier card");
            Assert(Object.ReferenceEquals(secondCard, Card(cache, second)), "Append preserves second card");
            settings.ShowBeasts = false;
            var beast = Sample(4); beast.Beast = true;
            tracker.Entries.Add(beast);
            Call(overlay, "RefreshCards", false);
            Assert(!cache.Contains(beast), "Hidden beast does not allocate a card");
            settings.ShowBeasts = true;
            Call(overlay, "RefreshCards", false);
            Assert(cache.Contains(beast), "Showing beasts creates beast card");
            Call(overlay, "ResetSession");
            Assert(tracker.Entries.Count == 0, "Reset clears entries");
            Assert(cache.Count == 0, "Reset clears cached cards");
            Assert(firstCard.IsDisposed && secondCard.IsDisposed, "Reset disposes cards");
            settings.MiniCards = true;
            var compact = Sample(5);
            tracker.Entries.Add(compact);
            Call(overlay, "RefreshCards", false);
            Assert(Card(cache, compact).Height > 0, "Compact layout renders");
            Assert((int)Call(overlay, "PreviewEventHeight", compact, Card(cache, compact).Width, true) > 0,
                "Compact height preview is valid");
        }
        Console.WriteLine("UI regression checks passed: " + checks);
    }
}
