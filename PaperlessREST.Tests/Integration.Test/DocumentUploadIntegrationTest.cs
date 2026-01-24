using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaperlessREST.Data;
using PaperlessREST.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

public class DocumentUploadIntegrationTest : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DocumentUploadIntegrationTest(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadSameFileTwice_CreatesNewVersion_AndLinksDiffBaseVersion()
    {
        // Arrange: ensure DB schema is applied
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var client = _factory.CreateClient();

        // Two different PDF byte contents, but SAME filename (important for your versioning behavior)
        var pdfBytesV1 = Encoding.ASCII.GetBytes("%PDF-1.4\n% v1\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");
        var pdfBytesV2 = Encoding.ASCII.GetBytes("%PDF-1.4\n% v2 - changed\n1 0 obj\n<< /Producer (test) >>\nendobj\ntrailer\n<<>>\n%%EOF");

        // Act: upload v1
        var resp1 = await PostPdfAsync(client, pdfBytesV1, fileName: "versioned.pdf");
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var (docId1, currentVersionId1, versionNumber1) = await ReadUploadResponseAsync(resp1);

        // Act: upload v2 (same filename)
        var resp2 = await PostPdfAsync(client, pdfBytesV2, fileName: "versioned.pdf");
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);

        var (docId2, currentVersionId2, versionNumber2) = await ReadUploadResponseAsync(resp2);

        // Assert: same document id, version increments
        Assert.Equal(docId1, docId2);
        Assert.Equal(1, versionNumber1);
        Assert.Equal(2, versionNumber2);
        Assert.NotEqual(currentVersionId1, currentVersionId2);

        // Assert DB: one Document, two Versions, diff-base relationship is correct
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var doc = await db.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.Id == docId1);

            Assert.NotNull(doc);
            Assert.Equal("versioned.pdf", doc!.FileName);
            Assert.Equal(currentVersionId2, doc.CurrentVersionId);

            // should have 2 versions
            Assert.Equal(2, doc.Versions.Count);

            var v1 = doc.Versions.Single(v => v.Id == currentVersionId1);
            var v2 = doc.Versions.Single(v => v.Id == currentVersionId2);

            Assert.Equal(1, v1.VersionNumber);
            Assert.Equal(2, v2.VersionNumber);

            // This is your "additional use case" linkage
            Assert.Equal(v1.Id, v2.DiffBaseVersionId);

            // sanity: object keys should differ
            Assert.False(string.IsNullOrWhiteSpace(v1.ObjectKey));
            Assert.False(string.IsNullOrWhiteSpace(v2.ObjectKey));
            Assert.NotEqual(v1.ObjectKey, v2.ObjectKey);
        }
    }

    private static async Task<HttpResponseMessage> PostPdfAsync(HttpClient client, byte[] pdfBytes, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName); // must match your controller: [FromForm] IFormFile file

        return await client.PostAsync("/api/documents", form);
    }

    private static async Task<(int DocId, int CurrentVersionId, int VersionNumber)> ReadUploadResponseAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // Your controller returns:
        // { id, fileName, currentVersionId, versionCreated, versionNumber }
        var docId = root.GetProperty("id").GetInt32();
        var currentVersionId = root.GetProperty("currentVersionId").GetInt32();
        var versionNumber = root.GetProperty("versionNumber").GetInt32();

        return (docId, currentVersionId, versionNumber);
    }

    [Fact]
    public async Task UploadPdf_PersistsDocumentAndVersion_AndStoresObject()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var client = _factory.CreateClient();

        // Minimal valid PDF bytes
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "test.pdf");

        // Act
        var resp = await client.PostAsync("/api/documents", form);

        // Assert HTTP
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var docId = json.RootElement.GetProperty("id").GetInt32();
        var currentVersionId = json.RootElement.GetProperty("currentVersionId").GetInt32();

        // Assert DB rows + version relationship
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId);
            Assert.NotNull(doc);
            Assert.Equal("test.pdf", doc!.FileName);
            Assert.Equal(currentVersionId, doc.CurrentVersionId);

            var version = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == currentVersionId);
            Assert.NotNull(version);
            Assert.Equal(docId, version!.DocumentId);
            Assert.Equal("application/pdf", version.ContentType);
            Assert.True(version.SizeBytes > 0);

            // Assert object exists in storage by fetching it
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            await using var stream = await storage.GetObjectAsync(version.ObjectKey);

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            Assert.True(ms.Length > 0);
        }
    }
}
