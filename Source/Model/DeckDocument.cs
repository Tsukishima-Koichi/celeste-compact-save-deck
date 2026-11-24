using System.Text.Json;

namespace Celeste.Mod.CompactSaveDeck.Model;

public sealed class Deck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<int> Slots { get; set; } = new();
    public bool Expanded { get; set; }
    public int? LastSlot { get; set; }
}

public sealed class DeckDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> RootOrder { get; set; } = new();
    public List<Deck> Decks { get; set; } = new();
    public string? Focus { get; set; }

    public static string SaveKey(int id) => "s:" + id;
    public static string DeckKey(string id) => "d:" + id;
    public Deck? GroupOf(int slot) => Decks.FirstOrDefault(d => d.Slots.Contains(slot));
    public Deck? Find(string key) => key.StartsWith("d:") ? Decks.FirstOrDefault(d => DeckKey(d.Id) == key) : null;
    public DeckDocument Clone() => JsonSerializer.Deserialize<DeckDocument>(JsonSerializer.Serialize(this))!;

    // The catalog includes orphan mod data, so those slots are never reused as empty slots.
    public void Reconcile(IEnumerable<int> catalog)
    {
        var present = catalog.Where(i => i >= 0).ToHashSet();
        var seen = new HashSet<int>();
        var ids = new HashSet<string>();
        Decks = (Decks ?? new()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id) && ids.Add(d.Id)).ToList();
        foreach (var deck in Decks)
        {
            deck.Name = string.IsNullOrWhiteSpace(deck.Name) ? "Deck" : deck.Name;
            deck.Slots = (deck.Slots ?? new()).Where(i => present.Contains(i) && seen.Add(i)).ToList();
            if (deck.LastSlot.HasValue && !deck.Slots.Contains(deck.LastSlot.Value))
                deck.LastSlot = null;
        }
        var allowed = Decks.Select(d => DeckKey(d.Id)).Concat(present.Except(seen).Select(SaveKey)).ToHashSet();
        RootOrder = (RootOrder ?? new()).Where(allowed.Contains).Distinct().ToList();
        foreach (var deck in Decks)
            if (!RootOrder.Contains(DeckKey(deck.Id))) RootOrder.Add(DeckKey(deck.Id));
        foreach (int slot in present.Except(seen).OrderBy(i => i))
            if (!RootOrder.Contains(SaveKey(slot))) RootOrder.Add(SaveKey(slot));
    }

    public Deck Create(string name, IEnumerable<int> slots)
    {
        var members = slots.Distinct().ToList();
        var oldOrder = RootOrder.ToList();
        int insert = oldOrder.FindIndex(k => members.Any(s => k == SaveKey(s) || GroupOf(s) is Deck d && k == DeckKey(d.Id)));
        var deck = new Deck { Name = name.Trim(), Expanded = false };
        foreach (int slot in members) RemoveMembership(slot);
        deck.Slots.AddRange(members);
        Decks.Add(deck);
        // Find the closest surviving anchor, so removing several items does not shift the insertion.
        int index = insert < 0 ? RootOrder.Count : oldOrder.Take(insert).Count(RootOrder.Contains);
        RootOrder.Insert(Math.Min(index, RootOrder.Count), DeckKey(deck.Id));
        return deck;
    }

    private void RemoveMembership(int slot)
    {
        foreach (var deck in Decks)
        {
            deck.Slots.Remove(slot);
            if (deck.LastSlot == slot) deck.LastSlot = null;
        }
        RootOrder.Remove(SaveKey(slot));
    }

    public void Assign(int slot, string? deckId)
    {
        Deck? old = GroupOf(slot);
        Deck? target = deckId == null ? null : Decks.FirstOrDefault(d => d.Id == deckId)
            ?? throw new ArgumentException("Unknown deck", nameof(deckId));
        if (old == target) return;
        int after = old == null ? RootOrder.Count : RootOrder.IndexOf(DeckKey(old.Id)) + 1;
        RemoveMembership(slot);
        if (target != null) target.Slots.Add(slot);
        else RootOrder.Insert(Math.Clamp(after, 0, RootOrder.Count), SaveKey(slot));
    }

    public void Dissolve(string id)
    {
        Deck deck = Decks.Single(d => d.Id == id);
        int index = RootOrder.IndexOf(DeckKey(id));
        RootOrder.Remove(DeckKey(id));
        Decks.Remove(deck);
        RootOrder.InsertRange(Math.Clamp(index, 0, RootOrder.Count), deck.Slots.Select(SaveKey));
    }

    public bool Move(string key, int direction)
    {
        direction = Math.Sign(direction);
        if (direction == 0) return false;
        if (key.StartsWith("s:") && int.TryParse(key.AsSpan(2), out int slot) && GroupOf(slot) is Deck deck)
        {
            int index = deck.Slots.IndexOf(slot), next = index + direction;
            if (next < 0 || next >= deck.Slots.Count) return false;
            (deck.Slots[index], deck.Slots[next]) = (deck.Slots[next], deck.Slots[index]);
            return true;
        }
        int root = RootOrder.IndexOf(key), to = root + direction;
        if (root < 0 || to < 0 || to >= RootOrder.Count) return false;
        (RootOrder[root], RootOrder[to]) = (RootOrder[to], RootOrder[root]);
        return true;
    }
}
