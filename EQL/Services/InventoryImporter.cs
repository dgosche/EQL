using System.Text.RegularExpressions;
using EQL.Data;
using EQL.Models;
using Microsoft.EntityFrameworkCore;

namespace EQL.Services
{
    /// Reads a player's inventory file and links each line to an Item by name.
    ///
    /// Supported formats:
    ///  - EverQuest "/outputfile inventory" (tab-separated, header "Location  Name  ID  Count  Slots")
    ///  - any CSV/tab file with a header that has a "Name" column (optional Location / Count columns)
    ///  - a plain list with one item name per line
    /// Upgrade tiers are read from a trailing " +N" on the name ("Mithril Breastplate +3").
    public static partial class InventoryImporter
    {
        [GeneratedRegex(@"^(?<base>.*?)\s*\+(?<tier>\d{1,2})$")]
        private static partial Regex TierSuffix();

        public record ParsedLine(string? Location, string RawName, string BaseName, int Tier, int Count);

        public static (string BaseName, int Tier) SplitTier(string name)
        {
            var m = TierSuffix().Match(name.Trim());
            return m.Success ? (m.Groups["base"].Value.Trim(), int.Parse(m.Groups["tier"].Value)) : (name.Trim(), 0);
        }

        public static List<ParsedLine> Parse(string text)
        {
            var lines = text.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            var result = new List<ParsedLine>();
            if (lines.Count == 0) return result;

            char sep = lines[0].Contains('\t') ? '\t' : (lines[0].Contains(',') ? ',' : '\0');
            string[] Split(string l) => sep == ',' ? Csv.Read(new StringReader(l)).FirstOrDefault() ?? new[] { l }
                                      : sep == '\t' ? l.Split('\t') : new[] { l };

            var header = Split(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
            int nameCol = Array.IndexOf(header, "name");
            int locCol = Array.IndexOf(header, "location");
            int countCol = Array.IndexOf(header, "count");
            bool hasHeader = nameCol >= 0;
            if (!hasHeader) nameCol = 0;

            foreach (var line in lines.Skip(hasHeader ? 1 : 0))
            {
                var f = Split(line);
                if (nameCol >= f.Length) continue;
                var raw = f[nameCol].Trim();
                if (raw.Length == 0 || raw.Equals("Empty", StringComparison.OrdinalIgnoreCase)) continue;

                int count = 1;
                if (countCol >= 0 && countCol < f.Length && int.TryParse(f[countCol].Trim(), out var n) && n > 0) count = n;
                string? loc = locCol >= 0 && locCol < f.Length ? f[locCol].Trim() : null;

                var (baseName, tier) = SplitTier(raw);
                result.Add(new ParsedLine(loc, raw, baseName, tier, count));
            }
            return result;
        }

        /// Replaces the character's inventory with the file contents. Returns (lines, matched).
        public static async Task<(int Lines, int Matched)> ImportAsync(EqlDbContext db, string character, string text)
        {
            character = character.Trim();
            var parsed = Parse(text);

            await db.InventoryEntries.Where(e => e.Character == character).ExecuteDeleteAsync();

            var lookup = await LookupAsync(db, parsed.Select(p => p.RawName).Concat(parsed.Select(p => p.BaseName)));
            int matched = 0;
            foreach (var p in parsed)
            {
                // Prefer an exact match on the full name (some real items end in "+N"), else the base name.
                int? id = lookup.TryGetValue(Item.Normalize(p.RawName), out var a) ? a
                        : lookup.TryGetValue(Item.Normalize(p.BaseName), out var b) ? b : null;
                bool exact = id.HasValue && lookup.ContainsKey(Item.Normalize(p.RawName)) && p.Tier > 0;
                if (id.HasValue) matched++;

                db.InventoryEntries.Add(new InventoryEntry
                {
                    Character = character,
                    Location = p.Location is { Length: > 64 } ? p.Location[..64] : p.Location,
                    RawName = Trunc(p.RawName),
                    BaseName = Trunc(exact ? p.RawName : p.BaseName),
                    Tier = exact ? 0 : p.Tier,
                    Count = p.Count,
                    ItemId = id,
                });
            }
            await db.SaveChangesAsync();
            return (parsed.Count, matched);
        }

        /// Re-links every inventory line after the Items table has been reloaded.
        public static async Task RematchAllAsync(EqlDbContext db)
        {
            var entries = await db.InventoryEntries.ToListAsync();
            if (entries.Count == 0) return;
            var lookup = await LookupAsync(db, entries.Select(e => e.BaseName));
            foreach (var e in entries)
                e.ItemId = lookup.TryGetValue(Item.Normalize(e.BaseName), out var id) ? id : null;
            await db.SaveChangesAsync();
        }

        private static async Task<Dictionary<string, int>> LookupAsync(EqlDbContext db, IEnumerable<string> names)
        {
            var wanted = names.Select(Item.Normalize).Distinct().ToList();
            var dict = new Dictionary<string, int>();
            foreach (var chunk in wanted.Chunk(500))
            {
                var hits = await db.Items.Where(i => chunk.Contains(i.NormalizedName))
                                         .Select(i => new { i.NormalizedName, i.Id }).ToListAsync();
                foreach (var h in hits) dict.TryAdd(h.NormalizedName, h.Id); // first wins on duplicate names
            }
            return dict;
        }

        private static string Trunc(string s) => s.Length > 200 ? s[..200] : s;
    }
}
