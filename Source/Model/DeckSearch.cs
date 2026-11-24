using System.Text;

namespace Celeste.Mod.CompactSaveDeck.Model;

public sealed record SaveSearchEntry(string Key, string Name, string Context, int? Slot, bool IsDeck, string Map = "");

public static class DeckSearch
{
    // Include collapsed members, maintaining the user's deck and save order.
    public static List<SaveSearchEntry> Catalog(DeckDocument document, Func<int, string> saveName, Func<int, string>? saveMap = null)
    {
        var result = new List<SaveSearchEntry>();
        foreach (string key in document.RootOrder)
        {
            if (document.Find(key) is Deck deck)
            {
                result.Add(new(key, deck.Name, "", null, true));
                foreach (int slot in deck.Slots)
                    result.Add(new(DeckDocument.SaveKey(slot), saveName(slot), deck.Name + " #" + slot, slot, false, saveMap?.Invoke(slot) ?? ""));
            }
            else if (key.StartsWith("s:") && int.TryParse(key.AsSpan(2), out int slot))
                result.Add(new(key, saveName(slot), "#" + slot, slot, false, saveMap?.Invoke(slot) ?? ""));
        }
        return result;
    }

    public static int BestIndex(IReadOnlyList<SaveSearchEntry> entries, string query)
    {
        query = Normalize(query);
        int bestIndex = -1, bestScore = int.MaxValue;
        for (int i = 0; i < entries.Count; i++)
        {
            int nameScore = Score(Normalize(entries[i].Name), query);
            int mapScore = string.IsNullOrWhiteSpace(entries[i].Map) ? int.MaxValue : Score(Normalize(entries[i].Map), query);
            int contextScore = Score(Normalize(entries[i].Name + " " + entries[i].Map + " " + entries[i].Context), query);
            if (contextScore != int.MaxValue) contextScore += 1000;
            int score = Math.Min(Math.Min(nameScore, mapScore), contextScore);
            if (score < bestScore) { bestIndex = i; bestScore = score; }
        }
        return bestIndex;
    }

    private static string Normalize(string value) =>
        string.Join(" ", value.Normalize(NormalizationForm.FormKC).ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int Score(string name, string query)
    {
        if (query.Length == 0 || name == query) return 0;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 100;
        if (name.Contains(query, StringComparison.Ordinal)) return 200;
        string[] words = query.Split(' ');
        if (words.Length > 1 && words.All(word => name.Contains(word, StringComparison.Ordinal))) return 300;
        // Sparse abbreviations rank after exact, prefix, substring and keyword matches.
        int cursor = 0;
        foreach (char c in name)
            if (cursor < query.Length && query[cursor] == c) cursor++;
        return cursor == query.Length ? 400 : int.MaxValue;
    }
}
