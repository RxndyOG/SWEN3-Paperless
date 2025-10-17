using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaperlessREST.Data;
using PaperlessREST.Models;
using PaperlessREST.Services;
using Microsoft.Extensions.Logging;

namespace PaperlessREST.Controllers;

public interface IDocumentsController
{
    IActionResult GetAll();
    IActionResult Create(Document doc, MessageQueueService mq);
    IActionResult UpdateDocument(int id, Document newDoc);
    IActionResult GetDocById(int id);
    IActionResult DeleteDocById(int id);
}


[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase, IDocumentsController
{
    private readonly AppDbContext _db;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(AppDbContext db, ILogger<DocumentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetAll() => Ok(_db.Documents.ToList());


    [HttpPost]
    public IActionResult Create([FromBody] Document doc, [FromServices] MessageQueueService mq)
    {
        _db.Documents.Add(doc);
        _db.SaveChanges();

        // Send message to RabbitMQ
        mq.Publish($"New document uploaded: {doc.FileName} (ID: {doc.Id})");
        _logger.LogInformation($"New document uploaded: {doc.FileName} (ID: {doc.Id}");

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
        {
            return BadRequest("Error occured while updating a document");
            _logger.LogError("Error occured while updating a document");
        }

        else
        {
            return NoContent();
            _logger.LogInformation($"Change {changes}");
        }
    }

    [HttpGet("{id:int}")]
    public IActionResult GetDocById(int id) => Ok(_db.Documents.Find(id));

    [HttpDelete("{id:int}")]
    public IActionResult DeleteDocById(int id)
    {
        var doc = _db.Documents.Find(id);
        if (doc == null)
            return NotFound();
        _db.Documents.Remove(doc);

        int changes = _db.SaveChanges();
        if (changes == 0)
        {
            return BadRequest("error occured while deleting a document");
            _logger.LogError($"{nameof(DeleteDocById)} failed to delete");
        }

        _logger.LogInformation($"Removed {doc.FileName} successfully");
        return Ok("successfully removed");
    }
}