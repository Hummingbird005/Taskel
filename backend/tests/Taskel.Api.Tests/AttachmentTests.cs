using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Taskel.Api.Attachments;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class AttachmentTests(TaskelApiFactory factory)
{
    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, Guid? assigneeId = null, Guid? groupId = null)
    {
        var response = await client.PostApiAsync("/api/tasks", new CreateTaskRequest(title, null, AssigneeId: assigneeId, GroupId: groupId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    private static MultipartFormDataContent FileForm(byte[] content, string fileName, string contentType = "application/pdf", string part = "file")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, part, fileName } };
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid taskId, byte[] content, string fileName = "oferta.pdf",
        string contentType = "application/pdf") =>
        client.PostAsync($"/api/tasks/{taskId}/attachments", FileForm(content, fileName, contentType));

    private static async Task<AttachmentResponse> UploadedAsync(HttpClient client, Guid taskId, byte[] content, string fileName = "oferta.pdf")
    {
        var response = await UploadAsync(client, taskId, content, fileName);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadApiAsync<AttachmentResponse>())!;
    }

    private static byte[] Bytes(int length, byte seed = 7)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)(i * 31 + seed);
        return bytes;
    }

    private int FilesOnDisk() =>
        System.IO.Directory.Exists(factory.AttachmentsPath) ? System.IO.Directory.GetFiles(factory.AttachmentsPath).Length : 0;

    private async Task<HttpClient> CreateAdminAsync()
    {
        var (admin, user) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(user.Id, UserRole.Admin, null);
        return admin;
    }

    private async Task WithLimitAsync(int megabytes, Func<Task> body)
    {
        var admin = await CreateAdminAsync();
        var before = (await admin.GetApiAsync<AttachmentSettingsResponse>("/api/admin/settings/attachments"))!;
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutApiAsync("/api/admin/settings/attachments", new UpdateAttachmentSettingsRequest(megabytes))).StatusCode);
        try
        {
            await body();
        }
        finally
        {
            await admin.PutApiAsync("/api/admin/settings/attachments", new UpdateAttachmentSettingsRequest(before.MaxFileSizeMb));
        }
    }

    [Fact]
    public async Task A_File_Is_Uploaded_Listed_On_The_Task_And_Downloaded_Unchanged()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Cu fișier");
        Assert.Empty(task.Attachments);
        var content = Bytes(300_000);

        var attachment = await UploadedAsync(alice, task.Id, content, "Ofertă finală.pdf");

        Assert.Equal("Ofertă finală.pdf", attachment.FileName);
        Assert.Equal(content.Length, attachment.Size);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(aliceUser.Id, attachment.UploadedBy!.Id);

        var listed = Assert.Single((await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Attachments);
        Assert.Equal(attachment.Id, listed.Id);
        Assert.Equal(attachment.Id, Assert.Single(Assert.Single((await alice.GetApiAsync<List<TaskResponse>>("/api/tasks"))!, t => t.Id == task.Id).Attachments).Id);

        var download = await alice.GetAsync($"/api/tasks/{task.Id}/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Downloads_Are_Never_Shown_By_The_Browser_As_Part_Of_Taskel()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Pagină încărcată");
        var response = await UploadAsync(alice, task.Id, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "pagina.html", "text/html");
        var attachment = (await response.Content.ReadApiAsync<AttachmentResponse>())!;

        var download = await alice.GetAsync($"/api/tasks/{task.Id}/attachments/{attachment.Id}");

        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("pagina.html", download.Content.Headers.ContentDisposition.FileNameStar ?? download.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("sandbox", Assert.Single(download.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData(@"..\..\Windows\system32\evil.exe", "evil.exe")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:/Users/ion/Desktop/raport final.xlsx", "raport final.xlsx")]
    [InlineData("  .ascuns.  ", "ascuns")]
    [InlineData("", "fisier")]
    [InlineData("...", "fisier")]
    public void File_Names_Are_Reduced_To_A_Plain_Name(string raw, string expected) =>
        Assert.Equal(expected, AttachmentStore.CleanFileName(raw));

    [Fact]
    public void Long_File_Names_Are_Cut_But_Keep_Their_Extension()
    {
        var name = AttachmentStore.CleanFileName(new string('a', 400) + ".docx");
        Assert.Equal(255, name.Length);
        Assert.EndsWith(".docx", name);
        Assert.Equal("a\u0001b.txt".Replace("\u0001", ""), AttachmentStore.CleanFileName("a\u0001b.txt"));
    }

    [Fact]
    public async Task The_Uploaded_Name_Never_Decides_Where_The_File_Is_Stored()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Traversare");
        var before = FilesOnDisk();

        var attachment = await UploadedAsync(alice, task.Id, Bytes(10), "../../evil.txt");

        Assert.Equal("evil.txt", attachment.FileName);
        Assert.Equal(before + 1, FilesOnDisk());
        Assert.All(System.IO.Directory.GetFiles(factory.AttachmentsPath), path => Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(path)));
        Assert.False(File.Exists(Path.Combine(factory.AttachmentsPath, "..", "..", "evil.txt")));
    }

    [Fact]
    public async Task Only_People_Who_Can_See_The_Task_Reach_Its_Files()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Pentru Bob", bobUser.Id);
        var attachment = await UploadedAsync(alice, task.Id, Bytes(100));
        var url = $"/api/tasks/{task.Id}/attachments/{attachment.Id}";

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(carol, task.Id, Bytes(10))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(factory.CreateClient(), task.Id, Bytes(10))).StatusCode);
        // A file is reached only through its own task.
        var other = await CreateAsync(carol, "Alt task");
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/tasks/{other.Id}/attachments/{attachment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Assignee_Adds_And_Removes_Files_And_A_Forwarder_Only_Reads_Them()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (carol, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Trece mai departe", bobUser.Id);
        var fromBob = await UploadedAsync(bob, task.Id, Bytes(50), "de-la-bob.txt");
        await bob.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(carolUser.Id));

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/tasks/{task.Id}/attachments/{fromBob.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(bob, task.Id, Bytes(10))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/tasks/{task.Id}/attachments/{fromBob.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await carol.DeleteAsync($"/api/tasks/{task.Id}/attachments/{fromBob.Id}")).StatusCode);
        Assert.Empty((await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Attachments);
        Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync($"/api/tasks/{task.Id}/attachments/{fromBob.Id}")).StatusCode);
    }

    [Fact]
    public async Task Group_Members_Read_Files_Before_Taking_The_Task_But_Cannot_Add_Any()
    {
        var admin = await CreateAdminAsync();
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var created = await admin.PostApiAsync("/api/groups", new SaveGroupRequest($"Fișiere {Guid.NewGuid():N}"[..20], [bobUser.Id]));
        var group = (await created.Content.ReadApiAsync<GroupDetail>())!;
        var task = await CreateAsync(alice, "Pentru grup", groupId: group.Id);
        var attachment = await UploadedAsync(alice, task.Id, Bytes(80));

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/tasks/{task.Id}/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(bob, task.Id, Bytes(10))).StatusCode);

        await bob.PostAsync($"/api/tasks/{task.Id}/claim", null);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(bob, task.Id, Bytes(10))).StatusCode);
    }

    [Fact]
    public async Task A_File_Over_The_Limit_Is_Refused_And_Leaves_Nothing_Behind()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Prea mare");

        await WithLimitAsync(1, async () =>
        {
            var before = FilesOnDisk();
            Assert.Equal(1, (await alice.GetApiAsync<AttachmentLimitsResponse>("/api/attachments/limits"))!.MaxFileSizeMb);

            var exact = await UploadAsync(alice, task.Id, Bytes(1024 * 1024), "fix-1mb.bin");
            Assert.Equal(HttpStatusCode.Created, exact.StatusCode);

            var tooLarge = await UploadAsync(alice, task.Id, Bytes(1024 * 1024 + 1), "peste.bin");
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            Assert.Contains("1 MB", await tooLarge.Content.ReadAsStringAsync());

            Assert.Equal(before + 1, FilesOnDisk());
            Assert.Single((await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Attachments);
        });
    }

    [Fact]
    public async Task The_Default_Limit_Is_100_Megabytes_And_An_Administrator_Can_Change_It()
    {
        var admin = await CreateAdminAsync();
        var (user, _) = await factory.CreateUserAsync();

        var settings = (await admin.GetApiAsync<AttachmentSettingsResponse>("/api/admin/settings/attachments"))!;
        Assert.Equal(100, settings.DefaultMaxFileSizeMb);
        Assert.Equal(100, settings.MaxFileSizeMb);
        var limits = (await user.GetApiAsync<AttachmentLimitsResponse>("/api/attachments/limits"))!;
        Assert.Equal(100, limits.MaxFileSizeMb);
        Assert.Equal(100L * 1024 * 1024, limits.MaxFileSizeBytes);
        Assert.Equal(3, limits.MaxFilesPerTask);

        await WithLimitAsync(250, async () =>
        {
            Assert.Equal(250, (await user.GetApiAsync<AttachmentLimitsResponse>("/api/attachments/limits"))!.MaxFileSizeMb);
            Assert.Equal(250, (await admin.GetApiAsync<AttachmentSettingsResponse>("/api/admin/settings/attachments"))!.MaxFileSizeMb);
        });
    }

    [Fact]
    public async Task Only_Administrators_Change_The_Limit_And_Only_To_A_Sensible_Value()
    {
        var admin = await CreateAdminAsync();
        var (user, userInfo) = await factory.CreateUserAsync();
        var (manager, managerInfo) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerInfo.Id, UserRole.Manager, "Vânzări");
        _ = userInfo;

        foreach (var client in new[] { user, manager })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/settings/attachments")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutApiAsync("/api/admin/settings/attachments", new UpdateAttachmentSettingsRequest(5))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/settings/attachments")).StatusCode);

        foreach (var value in new[] { 0, -1, AttachmentOptions.MaxConfigurableMb + 1 })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PutApiAsync("/api/admin/settings/attachments", new UpdateAttachmentSettingsRequest(value))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync("/api/admin/settings/attachments", new { maxFileSizeMb = "mult" })).StatusCode);
        Assert.Equal(100, (await admin.GetApiAsync<AttachmentSettingsResponse>("/api/admin/settings/attachments"))!.MaxFileSizeMb);
    }

    [Fact]
    public async Task A_Task_Holds_A_Limited_Number_Of_Files()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Plin");
        AttachmentResponse? last = null;
        for (var i = 0; i < 3; i++) last = await UploadedAsync(alice, task.Id, Bytes(10), $"f{i}.txt");

        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(alice, task.Id, Bytes(10))).StatusCode);

        await alice.DeleteAsync($"/api/tasks/{task.Id}/attachments/{last!.Id}");
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(alice, task.Id, Bytes(10))).StatusCode);
    }

    [Fact]
    public async Task Malformed_Uploads_Are_Refused()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "Greșit");
        var url = $"/api/tasks/{task.Id}/attachments";
        var before = FilesOnDisk();

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(alice, task.Id, [])).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await alice.PostAsJsonAsync(url, new { file = "text" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await alice.PostAsync(url, new ByteArrayContent(Bytes(10)))).StatusCode);
        // A part with another name, and a plain form field named "file", are not the file.
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsync(url, FileForm(Bytes(10), "a.txt", part: "document"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.PostAsync(url, new MultipartFormDataContent { { new StringContent("doar text"), "file" } })).StatusCode);

        Assert.Equal(before, FilesOnDisk());
        Assert.Empty((await alice.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!.Attachments);
    }

    [Fact]
    public async Task Removing_A_File_Or_Its_Task_Removes_It_From_Disk()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, "De șters");
        var before = FilesOnDisk();
        var first = await UploadedAsync(alice, task.Id, Bytes(10), "a.txt");
        await UploadedAsync(alice, task.Id, Bytes(10), "b.txt");
        Assert.Equal(before + 2, FilesOnDisk());

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/tasks/{task.Id}/attachments/{first.Id}")).StatusCode);
        Assert.Equal(before + 1, FilesOnDisk());
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/tasks/{task.Id}/attachments/{first.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(before, FilesOnDisk());
    }

    [Fact]
    public async Task A_Manager_Downloads_The_Files_Of_Their_People_Only()
    {
        var department = $"Dept {Guid.NewGuid():N}"[..16];
        var (manager, managerUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(managerUser.Id, UserRole.Manager, department);
        var (member, memberUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(memberUser.Id, UserRole.User, department);
        var (outsider, outsiderUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(outsiderUser.Id, UserRole.User, "Altundeva");
        var task = await CreateAsync(member, "Al membrului");
        var attachment = await UploadedAsync(member, task.Id, Bytes(500), "intern.pdf");
        var foreign = await CreateAsync(outsider, "Din alt departament");
        var foreignFile = await UploadedAsync(outsider, foreign.Id, Bytes(10));

        // Not through the task itself, which the manager is not part of...
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/tasks/{task.Id}/attachments/{attachment.Id}")).StatusCode);
        // ...but through the team view, where the file is also listed.
        var listed = (await manager.GetApiAsync<List<TaskResponse>>($"/api/team/members/{memberUser.Id}/tasks"))!;
        Assert.Equal(attachment.Id, Assert.Single(Assert.Single(listed).Attachments).Id);
        var download = await manager.GetAsync($"/api/team/members/{memberUser.Id}/tasks/{task.Id}/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Bytes(500), await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/team/members/{outsiderUser.Id}/tasks/{foreign.Id}/attachments/{foreignFile.Id}")).StatusCode);
        // Another person's task cannot be reached by naming a member of the manager's own team.
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"/api/team/members/{memberUser.Id}/tasks/{foreign.Id}/attachments/{foreignFile.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.GetAsync($"/api/team/members/{memberUser.Id}/tasks/{task.Id}/attachments/{attachment.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_Outlook_Macro_Can_Attach_Files_With_Its_Token()
    {
        var (session, _) = await factory.CreateUserAsync();
        var (token, _) = await factory.CreateTokenClientAsync(session);
        var created = await token.PostApiAsync("/api/tasks/from-email", new CreateTaskFromEmailRequest(
            "Cerere ofertă", "Ana <ana@example.com>", DateTime.UtcNow, "Vezi atașamentul.", EmailImportance.Normal, null, "ana@example.com", "<m1@example.com>"));
        var task = (await created.Content.ReadApiAsync<TaskResponse>())!;

        var attachment = await UploadedAsync(token, task.Id, Bytes(2000), "cerere.docx");

        var seen = (await session.GetApiAsync<TaskResponse>($"/api/tasks/{task.Id}"))!;
        Assert.Equal("cerere.docx", Assert.Single(seen.Attachments).FileName);
        Assert.Equal(HttpStatusCode.OK, (await token.GetAsync($"/api/tasks/{task.Id}/attachments/{attachment.Id}")).StatusCode);
    }
}
