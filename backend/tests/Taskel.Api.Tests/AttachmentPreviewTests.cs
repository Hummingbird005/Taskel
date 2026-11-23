using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Taskel.Api.Attachments;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

/// <summary>PDF files and pictures shown in Taskel without being downloaded, and everything else kept as a download.</summary>
[Collection(ApiCollection.Name)]
public class AttachmentPreviewTests(TaskelApiFactory factory)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0;");
    private static readonly byte[] Webp = [.. Encoding.ASCII.GetBytes("RIFF"), 26, 0, 0, 0, .. Encoding.ASCII.GetBytes("WEBPVP8 ")];

    private static async Task<(TaskResponse Task, AttachmentResponse File)> TaskWithFileAsync(HttpClient client, byte[] content,
        string fileName, string declaredType = "application/octet-stream")
    {
        var created = await client.PostApiAsync("/api/tasks", new CreateTaskRequest("Cu fișier", null));
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        var uploaded = await client.PostAsync($"/api/tasks/{task.Id}/attachments", new MultipartFormDataContent { { file, "file", fileName } });
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        return (task, (await uploaded.Content.ReadApiAsync<AttachmentResponse>())!);
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("webp", "image/webp")]
    public async Task A_Pdf_Or_A_Picture_Is_Shown_With_The_Type_Its_Content_Proves(string kind, string expectedType)
    {
        var content = kind switch { "pdf" => Pdf, "png" => Png, "jpeg" => Jpeg, "gif" => Gif, _ => Webp };
        var (alice, _) = await factory.CreateUserAsync();
        // Whatever the uploader declared: the type comes from the bytes.
        var (task, file) = await TaskWithFileAsync(alice, content, $"fișier fără extensie", "text/plain");

        var shown = await alice.GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}?view=true");

        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        Assert.Equal(expectedType, shown.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", shown.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("fișier fără extensie", shown.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(content, await shown.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", Assert.Single(shown.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("same-origin", Assert.Single(shown.Headers.GetValues("Cross-Origin-Resource-Policy")));
        var policy = Assert.Single(shown.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'self'", policy);
        // Pictures are shown inside a sandbox; the browser's PDF viewer does not start in one.
        Assert.Equal(kind != "pdf", policy.Contains("sandbox"));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>", "poza.png", "image/png")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "desen.svg", "image/svg+xml")]
    [InlineData("doar text", "nota.pdf", "application/pdf")]
    public async Task Anything_Else_Is_Refused_For_Showing_And_Still_Downloaded(string text, string fileName, string declaredType)
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (task, file) = await TaskWithFileAsync(alice, Encoding.UTF8.GetBytes(text), fileName, declaredType);

        var shown = await alice.GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}?view=true");
        var downloaded = await alice.GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, shown.StatusCode);
        Assert.Equal("application/problem+json", shown.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task A_Large_Pdf_Is_Read_In_Parts()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (task, file) = await TaskWithFileAsync(alice, Pdf, "raport.pdf", "application/pdf");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tasks/{task.Id}/attachments/{file.Id}?view=true");
        request.Headers.Range = new RangeHeaderValue(0, 7);
        var part = await alice.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(await part.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task Only_Those_Who_Can_See_The_Task_Can_View_Its_Files()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, _) = await factory.CreateUserAsync();
        var (task, file) = await TaskWithFileAsync(alice, Png, "poza.png", "image/png");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}?view=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync($"/api/tasks/{task.Id}/attachments/{file.Id}?view=true")).StatusCode);
    }

    [Fact]
    public async Task A_Manager_Views_The_Files_Of_Their_People()
    {
        var department = $"Dept {Guid.NewGuid():N}"[..16];
        var (manager, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Manager, department);
        var (member, memberUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(memberUser.Id, UserRole.User, department);
        var (task, file) = await TaskWithFileAsync(member, Pdf, "intern.pdf", "application/pdf");

        var shown = await manager.GetAsync($"/api/team/members/{memberUser.Id}/tasks/{task.Id}/attachments/{file.Id}?view=true");

        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        Assert.Equal("application/pdf", shown.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", shown.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public void Recognises_Files_By_Their_First_Bytes_Only()
    {
        Assert.Equal(PreviewType.Pdf, PreviewType.Detect(Pdf));
        Assert.Equal("image/png", PreviewType.Detect(Png));
        Assert.Null(PreviewType.Detect([]));
        Assert.Null(PreviewType.Detect("RIFF\0\0\0\0WAVE"u8));
        Assert.Null(PreviewType.Detect(" %PDF-"u8));
    }
}
