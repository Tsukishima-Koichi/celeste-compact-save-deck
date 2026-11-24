using System.Text.Json;

namespace Celeste.Mod.CompactSaveDeck.Model;

public sealed class DeckStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Path { get; }
    public string? Warning { get; private set; }
    public bool ReadOnly { get; private set; }
    private bool preserveInvalid;
    public DeckStore(string path) => Path = path;

    public DeckDocument Load()
    {
        if (!File.Exists(Path)) return new();
        try { return Read(Path); }
        catch (NotSupportedException ex) { Warning = ex.Message; ReadOnly = true; return new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Warning = ex.Message;
            preserveInvalid = true;
            try { if (File.Exists(Path + ".bak")) return Read(Path + ".bak"); }
            catch (Exception backup) when (backup is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
            { Warning += "; backup: " + backup.Message; }
            return new();
        }
    }

    private static DeckDocument Read(string path)
    {
        DeckDocument doc = JsonSerializer.Deserialize<DeckDocument>(File.ReadAllText(path))
            ?? throw new JsonException("Empty deck document");
        if (doc.SchemaVersion != 1) throw new NotSupportedException("Unsupported deck schema: " + doc.SchemaVersion);
        return doc;
    }

    public void Save(DeckDocument doc)
    {
        if (ReadOnly) throw new IOException("A newer deck schema is present; refusing to overwrite it.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, doc, JsonOptions);
                stream.Flush(true);
            }
            if (preserveInvalid && File.Exists(Path))
                File.Copy(Path, Path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak");
            else File.Move(temp, Path);
            preserveInvalid = false;
            Warning = null;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
