using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    [HttpPut("{id:int}")]
    public IActionResult UpdateDocument(int id, [FromBody] Document newDoc)
    {
        var doc = _db.Documents.Find(id);
        if (doc == null)
            return NotFound();

        doc.FileName = newDoc.FileName;
        doc.Content = newDoc.Content;

        int changes = _db.SaveChanges();

        if (changes == 0)
            return BadRequest("Error occured while updating a document");
        else
            return NoContent();
    }

    [HttpGet("{id:int}")]
    public IActionResult GetDocById(int id) => Ok(_db.Documents.Find(id));

    [HttpDelete("{id:int")]
    public IActionResult DeleteDocById(int id)
    {
        var doc = _db.Documents.Find(id);
        if (doc == null)
            return NotFound();
        _db.Documents.Remove(doc);

        int changes = _db.SaveChanges();
        if (changes == 0)
            return BadRequest("error occured while deleting a document");

        return Ok("successfully removed");
    }
}