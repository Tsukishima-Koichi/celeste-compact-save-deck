using System.Diagnostics;
using System.Text.Json;
using Celeste.Mod.CompactSaveDeck.Model;

int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
void Order(IEnumerable<string> actual, params string[] expected) => Check(actual.SequenceEqual(expected), "Order: " + string.Join(",", actual));
void Invariant(DeckDocument doc, HashSet<int> present)
{
    var members = doc.Decks.SelectMany(d => d.Slots).ToList();
    var singles = doc.RootOrder.Where(k => k.StartsWith("s:")).Select(k => int.Parse(k[2..])).ToList();
    Check(members.Concat(singles).Order().SequenceEqual(present.Order()), "Every save appears exactly once.");
    Check(doc.RootOrder.Distinct().Count() == doc.RootOrder.Count, "Root IDs are unique.");
    Check(doc.Decks.All(d => doc.RootOrder.Contains(DeckDocument.DeckKey(d.Id))), "Every deck has a root item.");
}
var doc = new DeckDocument();
doc.Reconcile(new[] { 0, 1, 2, 3, 8 });
var deck = doc.Create("练习", new[] { 1, 3 });
Order(doc.RootOrder, "s:0", DeckDocument.DeckKey(deck.Id), "s:2", "s:8");
Check(doc.Move("s:3", -1), "Move within group.");
Check(deck.Slots.SequenceEqual(new[] { 3, 1 }), "Group order.");
Check(!doc.Move("s:3", -1), "Cannot cross group boundary.");
doc.Move("s:2", -1);
Order(doc.RootOrder, "s:0", "s:2", DeckDocument.DeckKey(deck.Id), "s:8");
doc.Assign(3, null);
Order(doc.RootOrder, "s:0", "s:2", DeckDocument.DeckKey(deck.Id), "s:3", "s:8");
doc.Dissolve(deck.Id);
Order(doc.RootOrder, "s:0", "s:2", "s:1", "s:3", "s:8");
doc.Reconcile(new[] { 0, 1, 3, 8, 1000000 });
Order(doc.RootOrder, "s:0", "s:1", "s:3", "s:8", "s:1000000");
var copy = doc.Clone();
copy.Move("s:0", 1);
Check(doc.RootOrder[0] == "s:0", "Cancel snapshot is independent.");
var malformed = new DeckDocument {
    RootOrder = new() { "s:0", "s:0", "s:999", "d:missing" },
    Decks = new() { new() { Id="one", Slots = new() { 1, 1, 99 } }, new() { Id="two", Slots = new() { 1, 2 } } }
};
malformed.Reconcile(new[] { 0, 1, 2, 3 });
Invariant(malformed, new() { 0, 1, 2, 3 });

var random = new Random(20261124);
var population = Enumerable.Range(0, 1000).ToHashSet();
var big = new DeckDocument();
big.Reconcile(population);
for (int i=0; i<100; i++) big.Create("Deck " + i, Enumerable.Range(i*10, 10));
var watch = Stopwatch.StartNew();
for (int i=0; i<4000; i++)
{
    int slot = random.Next(1000);
    switch (random.Next(4))
    {
        case 0: big.Move(DeckDocument.SaveKey(slot), random.Next(2)*2-1); break;
        case 1: big.Assign(slot, random.Next(5)==0 ? null : big.Decks[random.Next(big.Decks.Count)].Id); break;
        case 2: big.Move(DeckDocument.DeckKey(big.Decks[random.Next(big.Decks.Count)].Id), random.Next(2)*2-1); break;
        case 3: big.Reconcile(population); break;
    }
    if (i % 20 == 0) Invariant(big, population);
}
Invariant(big, population);
watch.Stop();
Console.WriteLine("1000 saves / 100 decks, 4000 randomized operations: " + watch.ElapsedMilliseconds + " ms (model only).");

string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CompactSaveDeckTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string path = System.IO.Path.Combine(folder, "decks.json");
var store = new DeckStore(path);
store.Save(doc);
store.Save(big);
Check(File.Exists(path+".bak"), "Previous version backup.");
var restored = new DeckStore(path).Load();
Invariant(restored, population);
File.WriteAllText(path, "{ broken json");
var recoveredStore = new DeckStore(path);
var recovered = recoveredStore.Load();
Order(recovered.RootOrder, doc.RootOrder.ToArray());
Check(recoveredStore.Warning != null, "Corruption is reported.");
recoveredStore.Save(recovered);
Check(Directory.GetFiles(folder, "*.invalid-*").Length == 1, "Broken original retained.");
string previous = File.ReadAllText(path);
using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
{
    bool failed = false;
    try { new DeckStore(path).Save(big); } catch(IOException) { failed = true; }
    Check(failed, "A locked file must report failure.");
}
Check(File.ReadAllText(path) == previous, "Failed save preserves last good state.");
File.WriteAllText(path, "{\"SchemaVersion\":999}");
var future = new DeckStore(path);
future.Load();
bool rejected = false;
try { future.Save(doc); } catch(IOException) { rejected = true; }
Check(rejected && File.ReadAllText(path).Contains("999"), "Newer schemas are never overwritten.");

// Animation contracts: no snap on reversal, bounded cascade, deterministic endpoints.
var origin = new CardPose(984, 540, 0);
var destination = new CardPose(960, 960, 1);
var motion = new CardMotion(origin);
motion.Retarget(destination, 0.02f);
motion.Advance(0.01f);
Check(motion.Current == origin, "Cascade holds at the stack before its start.");
motion.Advance(0.10f);
Check(motion.Current.Y > origin.Y && motion.Current.Y < destination.Y, "Card spreads progressively.");
var atReversal = motion.Current;
motion.Retarget(origin, 1f);
Check(motion.Current == atReversal, "Rapid collapse starts at the current position.");
motion.Advance(0.01f);
Check(motion.Current.Y < atReversal.Y, "Interrupted animation responds without a fresh stagger.");
motion.Advance(1f);
Check(motion.Complete && motion.Current == origin, "Collapse finishes exactly at the pile.");
motion.Retarget(destination, 10f);
motion.Advance(0.32f);
Check(motion.Complete && motion.Current == destination, "Long decks cannot accumulate unlimited delays.");
motion.Retarget(origin, immediate: true);
Check(motion.Complete && motion.Current == origin, "Initial layout settles without playing a prior transition.");
motion.Retarget(destination);
motion.Advance(0.1f);
var beforeNegativeTime = motion.Current;
motion.Advance(-1);
Check(motion.Current == beforeNegativeTime, "Invalid negative time does not reverse animation.");
for (int i=0; i<120; i++)
{
    var before = motion.Current;
    motion.Retarget(i % 2 == 0 ? origin : destination);
    Check(motion.Current == before, "Repeated reversal preserves positional continuity.");
    motion.Advance(1f / 60);
    Check(float.IsFinite(motion.Current.Y) && motion.Current.Open >= 0 && motion.Current.Open <= 1,
        "Repeated reversal remains bounded.");
}
motion.Retarget(destination);
motion.Advance(1);
Check(motion.Complete && motion.Current == destination, "Repeated reversals converge.");

// Page transitions must wait for vanilla Show, then move saves and deck covers together.
var page = new SavePageMotion();
Check(!page.Drawing && !page.Interactive, "A new page starts hidden.");
page.ArmEntry();
page.Advance(2);
Check(!page.Drawing && !page.Interactive, "Loading and vanilla hand-off never reveal cards early.");
string[] pageRows = { "d:practice", "s:4", "s:1", "d:other", "s:7" };
page.Show(pageRows);
Check(page.Drawing && !page.Interactive, "Native Show begins a non-interactive entrance.");
Check(pageRows.All(key => page.Offset(key) == 1920), "Every deck and save starts outside the right edge.");
page.Advance(0.01f);
Check(page.Offset("d:practice") < 1920 && page.Offset("s:4") == 1920, "The visual order controls the entrance cascade.");
page.Advance(0.115f);
Check(Math.Abs(page.Offset("d:practice") - 960) < 0.01f, "Entrance follows vanilla's quarter-second cubic easing.");
Check(page.Offset("d:practice") < page.Offset("s:4") && page.Offset("s:4") < page.Offset("s:1"), "Decks and saves share the same ordered cascade.");
float beforeDuplicateShow = page.Offset("s:4");
page.Show(pageRows);
Check(page.Offset("s:4") == beforeDuplicateShow, "Later native slot Show calls cannot restart the page.");
page.Advance(0.20f);
Check(page.Interactive && pageRows.All(key => page.Offset(key) == 0), "All rows settle and enable navigation.");
page.Hide(pageRows, 1);
Check(page.Drawing && page.Exiting && !page.Interactive, "Leaving keeps covers visible while disabling navigation.");
page.Advance(0.125f);
Check(Math.Abs(page.Offset("d:practice") - 960) < 0.01f, "Main-menu return slides out with the same easing.");
Check(page.Offset("d:practice") > page.Offset("s:4") && page.Offset("s:4") > 0, "Outgoing saves and decks use the same cascade.");
page.Advance(0.20f);
Check(!page.Drawing && !page.Exiting, "Only a completed exit hides the page.");
page.ArmEntry();
page.Advance(1);
Check(!page.Drawing, "Re-entry restores the vanilla wait gate.");
page.Show(pageRows);
page.Advance(0.1f);
float interruptedOffset = page.Offset("d:practice");
page.Hide(pageRows, -1);
Check(page.Offset("d:practice") == interruptedOffset, "An interrupted entrance leaves from its current position.");
page.Advance(0.05f);
Check(page.Offset("d:practice") < interruptedOffset && page.Drawing, "Leftward exits continue smoothly.");
page.Advance(1);
Check(!page.Drawing, "An interrupted transition still completes.");
page.ArmEntry();
page.Settle();
Check(page.Drawing && page.Interactive && page.Offset("s:4") == 0, "Native selected-card submenu returns do not wait for Show.");
page.Hide(new[] { "s:4" }, -1);
page.Advance(0.125f);
Check(Math.Abs(page.Offset("s:4") + 960) < 0.01f, "Chapter/submenu exits travel left.");
page.ArmEntry();
page.Hide(pageRows, 1);
Check(!page.Drawing && !page.Exiting, "Leaving before Show never reveals a waiting page.");
page.ArmEntry();
page.Show(Array.Empty<string>());
Check(page.Interactive, "An empty entrance cannot deadlock navigation.");
page.Hide(Array.Empty<string>(), 1);
Check(!page.Drawing, "An empty exit completes immediately.");
page.ArmEntry();
page.Show(new[] { "s:4", "s:4" });
page.Advance(0.26f);
Check(page.Interactive, "Duplicate row IDs do not create duplicate animation tracks.");

// Menu lifetime: exact Everest easing, no removal before the outgoing frames finish.
var popup = new MenuMotion();
Check(!popup.Interactive && !popup.Closed && popup.Amount == 0, "A new popup starts off screen.");
popup.Advance(0.125f);
Check(Math.Abs(popup.Amount - 0.875f) < 0.0001f, "Popup entry uses CubeOut.");
popup.Advance(0.125f);
Check(popup.Interactive && popup.Amount == 1 && popup.Offset == 0, "Popup becomes interactive only after entry.");
popup.Close();
Check(!popup.Interactive && !popup.Closed && popup.Amount == 1, "Closing keeps the last visible frame.");
popup.Advance(0.125f);
Check(Math.Abs(popup.Amount - 0.875f) < 0.0001f, "Popup exit uses CubeIn.");
popup.Close();
popup.Advance(0.125f);
Check(popup.Closed && popup.Amount == 0 && popup.Offset == 1920, "Repeated close cannot restart or delay the exit.");
var interruptedPopup = new MenuMotion();
interruptedPopup.Advance(0.05f);
float openingAmount = interruptedPopup.Amount;
interruptedPopup.Close();
Check(interruptedPopup.Amount == openingAmount, "Closing during entry preserves continuity.");
interruptedPopup.Advance(-1);
Check(interruptedPopup.Amount == openingAmount, "Negative delta cannot rewind a popup.");
interruptedPopup.Advance(0.25f);
Check(interruptedPopup.Closed && interruptedPopup.Amount == 0, "An early close still finishes.");
var neverShown = new MenuMotion();
neverShown.Close();
neverShown.Advance(1);
Check(neverShown.Closed && neverShown.Amount == 0, "A disposed-before-entry popup never flashes.");

// Page render snapshots must reject untracked rows during transitions and preserve exit endpoints.
page.ArmEntry();
Check(!page.CanDraw("d:practice"), "Waiting never permits deck rendering.");
page.Show(pageRows);
Check(pageRows.All(page.CanDraw) && !page.CanDraw("s:offscreen"), "Only staged rows render during page entry.");
page.Advance(1);
Check(page.CanDraw("s:offscreen"), "Normal browsing can reveal newly scrolled rows.");
page.Hide(pageRows, 1);
Check(!page.CanDraw("s:offscreen"), "Rows entering the vertical viewport during exit cannot flash at offset zero.");
float priorOffset = 0;
for (int frame = 0; frame < 30; frame++)
{
    page.Advance(1f / 60);
    float offset = page.Offset("d:practice");
    Check(offset >= priorOffset, "Exit position never jumps backwards when the animation finishes.");
    if (!page.Drawing) Check(!pageRows.Any(page.CanDraw), "No row remains drawable after exit.");
    priorOffset = offset;
}
Check(page.Offset("d:practice") == 1920, "Completed exit retains its offscreen endpoint.");
page.ArmEntry();
Check(!page.CanDraw("d:practice"), "Re-entry clears previous drawable state.");

// Search catalogs are independent of deck expansion and never renumber save identities.
var searchDoc = new DeckDocument();
searchDoc.Reconcile(new[] { 0, 1, 2, 9 });
var searchDeck = searchDoc.Create("练习", new[] { 1, 9 });
var searchNames = new Dictionary<int, string> { [0] = "Summit Practice", [1] = "Summit", [2] = "Farewell", [9] = "中文存档" };
var catalog = DeckSearch.Catalog(searchDoc, id => searchNames[id]);
Check(!searchDeck.Expanded && catalog.Count == 5, "Search includes every folded member plus the deck.");
Check(catalog.Select(e => e.Key).Distinct().Count() == 5, "Search catalog never duplicates saves.");
Check(catalog.Select(e => e.Key).SequenceEqual(new[] { "s:0", DeckDocument.DeckKey(searchDeck.Id), "s:1", "s:9", "s:2" }), "Search follows root order and member order.");
string BestKey(string query) { int i = DeckSearch.BestIndex(catalog, query); return i < 0 ? "" : catalog[i].Key; }
Check(BestKey("summit") == "s:1", "An exact save name outranks an earlier prefix.");
Check(BestKey("SUMMIT PRA") == "s:0", "Prefixes ignore case.");
Check(BestKey(" Ｓｕｍｍｉｔ ") == "s:1", "Full-width text and surrounding spaces normalize.");
Check(BestKey("中文") == "s:9", "Chinese substring queries find collapsed saves.");
Check(BestKey("练习") == DeckDocument.DeckKey(searchDeck.Id), "An exact deck label outranks member context.");
Check(BestKey("#9") == "s:9", "Slot identifiers distinguish duplicate names.");
Check(BestKey("practice summit") == "s:0", "Multiple keywords can appear in a different order.");
Check(BestKey("smmt") == "s:0", "Sparse abbreviations are a fallback with stable order.");
Check(BestKey("zzzz-not-present") == "", "No match is explicit, never an unrelated highlight.");
Check(BestKey("   ") == "s:0", "Empty search keeps a deterministic first entry.");
Check(DeckSearch.BestIndex(Array.Empty<SaveSearchEntry>(), "anything") == -1, "An empty catalog has no match.");
searchDeck.Expanded = true;
Check(DeckSearch.Catalog(searchDoc, id => searchNames[id]).SequenceEqual(catalog), "Expansion does not change catalog coverage or ranking.");
var duplicates = new[] { new SaveSearchEntry("s:5", "Same", "#5", 5, false), new SaveSearchEntry("s:6", "Same", "#6", 6, false) };
Check(DeckSearch.BestIndex(duplicates, "same") == 0, "Ties respect the user's order.");
Check(DeckSearch.BestIndex(duplicates, "#6") == 1, "Identifiers resolve identical display names.");
// Identically named saves remain distinguishable and searchable by their current map.
var mappedCatalog = DeckSearch.Catalog(searchDoc, _ => "Madeline", id => id == 1 ? "Strawberry Jam / Beginner Lobby" : id == 9 ? "春季联赛 / 专家大厅" : "Celeste / Summit");
Check(mappedCatalog.Single(e => e.Key == "s:1").Map == "Strawberry Jam / Beginner Lobby", "Grouped saves retain their own current map label.");
Check(mappedCatalog.Single(e => e.Key == "s:0").Map == "Celeste / Summit", "Ungrouped saves retain their own current map label.");
Check(mappedCatalog.Single(e => e.IsDeck).Map == "", "Deck headers do not inherit a member's map label.");
Check(mappedCatalog[DeckSearch.BestIndex(mappedCatalog, "strawberry jam")].Key == "s:1", "Map collection names distinguish identical save names.");
Check(mappedCatalog[DeckSearch.BestIndex(mappedCatalog, "专家大厅")].Key == "s:9", "Localized map names participate in matching.");
Check(mappedCatalog[DeckSearch.BestIndex(mappedCatalog, "Madeline Beginner")].Key == "s:1", "Queries can combine the save name and current map.");
Console.WriteLine("PASS: " + assertions + " assertions. Temporary fixtures: " + folder);

