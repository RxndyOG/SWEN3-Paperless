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
