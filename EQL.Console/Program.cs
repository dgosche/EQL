// EqlItems (.NET 10) — pull EverQuest Legends item stats from the EQL Wiki API (eqlwiki.com) into a CSV.
//
// Usage:
//   dotnet run                         # all items (Category:Items, ~11.4k pages)
//   dotnet run -- Ear                  # one category (Ear, Chest, Kunark_Era, ...)
//   dotnet run -- Items items.csv      # choose output file
//
// How it works:
//   - generator=categorymembers lists pages in a category, 50 at a time
//   - prop=revisions returns each page's wikitext in the same call
//   - each item page uses {{Itempage ... |statsblock = ...}}; the statsblock is plain text
//     ("AC: 5", "STR: +4 DEX: +9", "Slot: EAR") which ParseStats turns into fields
//   - follows the API "continue" token until the category is exhausted
//
// Be polite: one request at a time with a pause, a descriptive User-Agent, and cache the CSV.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string Api = "https://eqlwiki.com/api.php";
const string UserAgent = "MSACS506-EQL-GearOptimizer/1.0 (student project; contact: dpgosche@example.com)";
var delay = TimeSpan.FromMilliseconds(500);

string category = args.Length > 0 ? args[0] : "Items";
string outFile = args.Length > 1 ? args[1] : category.ToLowerInvariant() + ".csv";

string[] columns =
{
    "name","slot","class","race","size","AC","HP","MANA","END",
    "STR","STA","AGI","DEX","WIS","INT","CHA",
    "SV MAGIC","SV FIRE","SV COLD","SV POISON","SV DISEASE",
    "Haste","DMG","delay","skill","WT","effect","flags"
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

await using var writer = new StreamWriter(outFile, false, new UTF8Encoding(false));
await writer.WriteLineAsync(string.Join(",", columns.Select(Csv)));

var cont = new Dictionary<string, string>();
int count = 0;
do
{
    var query = new Dictionary<string, string>
    {
        ["action"] = "query",
        ["generator"] = "categorymembers",
        ["gcmtitle"] = "Category:" + category.Replace(' ', '_'),
        ["gcmnamespace"] = "0",
        ["gcmlimit"] = "50",
        ["prop"] = "revisions",
        ["rvprop"] = "content",
        ["rvslots"] = "main",
        ["format"] = "json",
        ["formatversion"] = "2",
        ["maxlag"] = "5",
    };
    foreach (var kv in cont) query[kv.Key] = kv.Value;

    using var doc = await ApiGet(http, query);
    var root = doc.RootElement;

    if (root.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var pages))
    {
        foreach (var page in pages.EnumerateArray())
        {
            string wt = page.TryGetProperty("revisions", out var revs)
                ? revs[0].GetProperty("slots").GetProperty("main").GetProperty("content").GetString() ?? ""
                : "";
            var block = ExtractStatsBlock(wt);
            if (block is null) continue; // not an item page

            var item = ParseStats(block);
            item["name"] = page.GetProperty("title").GetString() ?? "";
            await writer.WriteLineAsync(string.Join(",",
                columns.Select(c => Csv(item.TryGetValue(c, out var v) ? v : ""))));
            count++;
        }
    }
    Console.Error.Write($"\r{count} items...");

    cont = new Dictionary<string, string>();
    if (root.TryGetProperty("continue", out var c2))
        foreach (var p in c2.EnumerateObject()) cont[p.Name] = p.Value.ToString();

    await Task.Delay(delay);
} while (cont.Count > 0);

Console.Error.WriteLine($"\nWrote {count} items to {outFile}");

// ---------------- helpers ----------------

static async Task<JsonDocument> ApiGet(HttpClient http, Dictionary<string, string> query)
{
    var url = Api + "?" + string.Join("&",
        query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

    for (int attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("code", out var code) && code.GetString() == "maxlag")
                {
                    doc.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    continue;
                }
                return doc;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
    }
    throw new Exception("API request failed: " + url);
}

/// Pull the |statsblock = ... value out of the {{Itempage}} template.
static string? ExtractStatsBlock(string wikitext)
{
    var m = Regex.Match(wikitext, @"\|statsblock\s*=(.*?)\n\|", RegexOptions.Singleline);
    return m.Success ? m.Groups[1].Value : null;
}

/// "[[Spirit Tap|<span class='itemeff'>Spirit Tap</span>]]" -> "Spirit Tap"
static string CleanWiki(string s)
{
    s = Regex.Replace(s, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1");
    s = Regex.Replace(s, @"^[^|]*\|", "");
    s = Regex.Replace(s, "<[^>]+>", "");
    return WebUtility.HtmlDecode(s).Trim();
}

/// Parse the free-text stats block into field -> value.
static Dictionary<string, string> ParseStats(string block)
{
    var o = new Dictionary<string, string>(StringComparer.Ordinal);
    var text = Regex.Replace(block, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
    var labels = new (string Label, string Key)[] { ("Slot", "slot"), ("Class", "class"), ("Race", "race"), ("Effect", "effect") };

    foreach (var raw in text.Split('\n'))
    {
        var line = raw.Trim();
        if (line.Length == 0) continue;

        var lbl = labels.FirstOrDefault(l => line.StartsWith(l.Label + ":", StringComparison.OrdinalIgnoreCase));
        if (lbl.Label is not null)
        {
            o[lbl.Key] = CleanWiki(line[(lbl.Label.Length + 1)..]);
            continue;
        }

        var sk = Regex.Match(line, @"^Skill:\s*(.+?)\s+Atk Delay:\s*(\d+)", RegexOptions.IgnoreCase);
        if (sk.Success)
        {
            o["skill"] = sk.Groups[1].Value;
            o["delay"] = sk.Groups[2].Value;
            line = line[sk.Length..];
        }

        if (line.Contains("ITEM") && !line.Contains(':'))
        {
            o["flags"] = Regex.Replace(line, @"\s+", " ");
            continue;
        }

        // Size is a word, not a number — pull it out first so it doesn't glue onto the next label
        var size = Regex.Match(line, @"Size:\s*([A-Za-z]+)");
        if (size.Success)
        {
            o["size"] = size.Groups[1].Value.ToUpperInvariant();
            line = line.Remove(size.Index, size.Length);
        }

        // Numeric stats: "AC: 5", "STR: +4 DEX: +9", "SV MAGIC: +10", "Haste: +27%", "WT: 0.1"
        foreach (Match m in Regex.Matches(line, @"([A-Za-z][A-Za-z ]*?):\s*([+-]?\d+(?:\.\d+)?)%?"))
        {
            var val = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            o[m.Groups[1].Value.Trim()] = val.ToString(CultureInfo.InvariantCulture);
        }
    }
    return o;
}

static string Csv(string v) =>
    v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
