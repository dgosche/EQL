using System.Globalization;
using System.Text;
using EQL.Data;
using EQL.Models;
using Microsoft.EntityFrameworkCore;

namespace EQL.Services
{
    /// Loads the items.csv produced by EQL.Console into the Items table.
    public class ItemCsvImporter
    {
        private readonly EqlDbContext _db;
        public ItemCsvImporter(EqlDbContext db) => _db = db;

        /// Replaces every item with the contents of the CSV. Returns the number of items loaded.
        public async Task<int> ImportAsync(Stream csvStream)
        {
            var items = ParseItems(csvStream);

            // Replace the whole table. Inventory links are cleared and re-matched afterwards.
            await _db.InventoryEntries.ExecuteUpdateAsync(s => s.SetProperty(e => e.ItemId, (int?)null));
            await _db.Items.ExecuteDeleteAsync();

            _db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var batch in items.Chunk(2000))
            {
                _db.Items.AddRange(batch);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
            _db.ChangeTracker.AutoDetectChangesEnabled = true;

            await InventoryImporter.RematchAllAsync(_db);
            return items.Count;
        }

        /// Reads items.csv into Item objects (no database work).
        public static List<Item> ParseItems(Stream csvStream)
        {
            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            var rows = Csv.Read(reader).ToList();
            if (rows.Count < 2) return new List<Item>();

            var header = rows[0].Select(h => h.Trim()).ToArray();
            int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (Col("name") < 0) throw new InvalidDataException("items.csv has no 'name' column.");

            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in header) cols[h] = Col(h);

            string? S(string[] r, string c) =>
                cols.TryGetValue(c, out var i) && i >= 0 && i < r.Length && r[i].Trim().Length > 0 ? r[i].Trim() : null;
            int? I(string[] r, string c) =>
                double.TryParse(S(r, c), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
            double? D(string[] r, string c) =>
                double.TryParse(S(r, c), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            string? Cut(string? v, int max) => v is null || v.Length <= max ? v : v[..max];

            var items = new List<Item>(rows.Count);
            foreach (var r in rows.Skip(1))
            {
                var name = S(r, "name");
                if (name is null) continue;
                items.Add(new Item
                {
                    Name = Cut(name, 200)!,
                    NormalizedName = Cut(Item.Normalize(name), 200)!,
                    Slot = Cut(S(r, "slot"), 100),
                    Class = Cut(S(r, "class"), 200),
                    Race = Cut(S(r, "race"), 200),
                    Size = Cut(S(r, "size"), 20),
                    AC = I(r, "AC"), HP = I(r, "HP"), Mana = I(r, "MANA"), End = I(r, "END"),
                    Str = I(r, "STR"), Sta = I(r, "STA"), Agi = I(r, "AGI"), Dex = I(r, "DEX"),
                    Wis = I(r, "WIS"), Int = I(r, "INT"), Cha = I(r, "CHA"),
                    SvMagic = I(r, "SV MAGIC"), SvFire = I(r, "SV FIRE"), SvCold = I(r, "SV COLD"),
                    SvPoison = I(r, "SV POISON"), SvDisease = I(r, "SV DISEASE"),
                    Haste = I(r, "Haste"), Dmg = I(r, "DMG"), Delay = I(r, "delay"),
                    Skill = Cut(S(r, "skill"), 50),
                    Weight = D(r, "WT"),
                    Effect = Cut(S(r, "effect"), 500),
                    Flags = Cut(S(r, "flags"), 100),
                });
            }
            return items;
        }
    }

    /// Minimal RFC 4180 CSV reader (quoted fields, doubled quotes, newlines inside quotes).
    public static class Csv
    {
        public static IEnumerable<string[]> Read(TextReader reader)
        {
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int c;
            while ((c = reader.Read()) != -1)
            {
                char ch = (char)c;
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (ch == '\r') { /* handled by \n */ }
                else if (ch == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    yield return row.ToArray(); row.Clear();
                }
                else field.Append(ch);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                yield return row.ToArray();
            }
        }
    }
}
