using System.Text;
using EQL.Data;
using EQL.Models;
using EQL.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EQL.Controllers
{
    public class InventoryController : Controller
    {
        private readonly EqlDbContext _db;
        public InventoryController(EqlDbContext db) => _db = db;

        // GET /Inventory — characters loaded so far, plus the upload form
        public async Task<IActionResult> Index()
        {
            // Group and sort in SQL using plain values, then build the records in memory
            // (EF Core can't translate OrderBy on a record created with a constructor).
            var rows = await _db.InventoryEntries
                .GroupBy(e => e.Character)
                .Select(g => new
                {
                    Character = g.Key,
                    Lines = g.Count(),
                    Matched = g.Sum(e => e.ItemId != null ? 1 : 0),
                })
                .OrderBy(x => x.Character)
                .ToListAsync();
            var chars = rows.Select(x => new CharacterSummary(x.Character, x.Lines, x.Matched)).ToList();
            ViewBag.ItemCount = await _db.Items.CountAsync();
            return View(chars);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile? file, string? character)
        {
            if (file is null || file.Length == 0)
            {
                TempData["Error"] = "Choose an inventory file.";
                return RedirectToAction(nameof(Index));
            }
            // "Dave-Inventory.txt" -> "Dave" when no name was typed
            if (string.IsNullOrWhiteSpace(character))
                character = Path.GetFileNameWithoutExtension(file.FileName).Split('-', '_')[0];

            string text;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                text = await reader.ReadToEndAsync();

            var (lines, matched) = await InventoryImporter.ImportAsync(_db, character!, text);
            TempData["Message"] = $"{character}: {lines} lines read, {matched} matched to items, {lines - matched} not found.";
            return RedirectToAction(nameof(Details), new { character });
        }

        // GET /Inventory/Details?character=Dave
        public async Task<IActionResult> Details(string character)
        {
            var entries = await _db.InventoryEntries.AsNoTracking()
                .Include(e => e.Item)
                .Where(e => e.Character == character)
                .OrderBy(e => e.ItemId == null ? 0 : 1).ThenBy(e => e.Location).ThenBy(e => e.RawName)
                .ToListAsync();
            ViewBag.Character = character;
            return View(entries);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string character)
        {
            await _db.InventoryEntries.Where(e => e.Character == character).ExecuteDeleteAsync();
            TempData["Message"] = $"Removed {character}'s inventory.";
            return RedirectToAction(nameof(Index));
        }

        // GET/POST /Inventory/Optimize?character=Beolen&classCodes=SHD&classCodes=SHM&classCodes=MNK
        [HttpGet, HttpPost]
        public async Task<IActionResult> Optimize(string character, string[]? classCodes, Dictionary<string, double>? weights)
        {
            // Up to three classes. If none were sent, use the ones last chosen for this character (cookie).
            var cookieName = "eql-classes-" + Uri.EscapeDataString(character ?? "");
            var classes = (classCodes ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant())
                .Where(c => GameRules.Classes.Any(k => k.Code == c))
                .Distinct().Take(3).ToList();
            if (classes.Count == 0 && Request.Cookies.TryGetValue(cookieName, out var saved))
                classes = saved.Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Where(c => GameRules.Classes.Any(k => k.Code == c)).Take(3).ToList();
            if (classes.Count == 0) classes.Add("WAR");
            Response.Cookies.Append(cookieName, string.Join(",", classes),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true });

            var w = GameRules.DefaultWeights(classes);
            // Use the posted weights unless a class was just changed (then load the combined defaults).
            bool resetWeights = Request.HasFormContentType && Request.Form.ContainsKey("resetWeights");
            if (HttpMethods.IsPost(Request.Method) && weights is not null && !resetWeights)
                foreach (var kv in weights.Where(kv => w.ContainsKey(kv.Key))) w[kv.Key] = kv.Value;

            var owned = await _db.InventoryEntries.AsNoTracking()
                .Include(e => e.Item)
                .Where(e => e.Character == character && e.ItemId != null)
                .ToListAsync();
            var ownedGroups = owned
                .GroupBy(e => (e.ItemId, e.Tier))
                .Select(g => (Item: g.First().Item!, Tier: g.Key.Tier, Count: g.Sum(e => e.Count)));

            var gear = await _db.Items.AsNoTracking().Where(i => i.Slot != null && i.Slot != "").ToListAsync();

            var vm = new OptimizeViewModel
            {
                Character = character ?? "",
                ClassCodes = classes,
                Weights = w,
                Results = GearOptimizer.Build(ownedGroups, gear, classes, w),
            };
            return View(vm);
        }
    }

    public record CharacterSummary(string Character, int Lines, int Matched);

    public class OptimizeViewModel
    {
        public string Character { get; set; } = "";
        public List<string> ClassCodes { get; set; } = new() { "WAR" };
        public Dictionary<string, double> Weights { get; set; } = new();
        public List<GearOptimizer.SlotResult> Results { get; set; } = new();
    }
}
