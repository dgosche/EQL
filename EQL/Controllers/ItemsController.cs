using EQL.Data;
using EQL.Models;
using EQL.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EQL.Controllers
{
    public class ItemsController : Controller
    {
        private readonly EqlDbContext _db;
        private readonly ItemCsvImporter _importer;

        public ItemsController(EqlDbContext db, ItemCsvImporter importer)
        {
            _db = db;
            _importer = importer;
        }

        // GET /Items?q=earring&slot=EAR&page=1
        public async Task<IActionResult> Index(string? q, string? slot, int page = 1)
        {
            const int pageSize = 50;
            var query = _db.Items.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var nq = Item.Normalize(q);
                query = query.Where(i => i.NormalizedName.Contains(nq));
            }
            if (!string.IsNullOrWhiteSpace(slot))
            {
                var s = slot.ToUpperInvariant();
                query = query.Where(i => i.Slot != null && i.Slot.ToUpper().Contains(s));
            }

            ViewBag.Total = await query.CountAsync();
            ViewBag.Q = q;
            ViewBag.Slot = slot;
            ViewBag.Page = Math.Max(1, page);
            ViewBag.PageSize = pageSize;
            ViewBag.ItemCount = await _db.Items.CountAsync();

            var items = await query.OrderBy(i => i.Name)
                                   .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
                                   .ToListAsync();
            return View(items);
        }

        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            return item is null ? NotFound() : View(item);
        }

        [HttpGet]
        public async Task<IActionResult> Import()
        {
            ViewBag.ItemCount = await _db.Items.CountAsync();
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Import(IFormFile? file)
        {
            if (file is null || file.Length == 0)
            {
                TempData["Error"] = "Choose the items.csv file made by EQL.Console.";
                return RedirectToAction(nameof(Import));
            }
            try
            {
                await using var stream = file.OpenReadStream();
                int count = await _importer.ImportAsync(stream);
                TempData["Message"] = $"Loaded {count:N0} items from {file.FileName}.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Import failed: " + ex.Message;
                return RedirectToAction(nameof(Import));
            }
        }
    }
}
