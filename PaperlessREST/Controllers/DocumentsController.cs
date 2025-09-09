using Microsoft.AspNetCore.Mvc;
using PaperlessREST.Data;
using PaperlessREST.Models;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DocumentsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_db.Documents.ToList());

    [HttpPost]
    public IActionResult Create([FromBody] Document doc)
    {
        _db.Documents.Add(doc);
        _db.SaveChanges();
        return CreatedAtAction(nameof(GetAll), new { id = doc.Id }, doc);
    }
}