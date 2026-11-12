using System.Net;
using System.Net.Http.Json;
using Taskel.Api.Email;
using Taskel.Api.Models;

namespace Taskel.Api.Tests;

[Collection(ApiCollection.Name)]
public class NotificationTests(TaskelApiFactory factory)
{
    private static async Task<TaskResponse> CreateAsync(HttpClient client, CreateTaskRequest request)
    {
        var response = await client.PostApiAsync("/api/tasks", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadApiAsync<TaskResponse>())!;
    }

    /// <summary>Notifications leave in the background: waits until the expected number arrived.</summary>
    private async Task<IReadOnlyList<EmailMessage>> WaitForAsync(string address, int count)
    {
        for (var i = 0; i < 100 && factory.Emails.NotificationsTo(address).Count < count; i++) await Task.Delay(30);
        return factory.Emails.NotificationsTo(address);
    }

    /// <summary>
    /// For "nothing was sent": the queue is in order, so once a later message to someone else has
    /// arrived, anything queued before it would have arrived too.
    /// </summary>
    private async Task<IReadOnlyList<EmailMessage>> NoneForAsync(string address)
    {
        var (sender, _) = await factory.CreateUserAsync();
        var (_, marker) = await factory.CreateUserAsync();
        await CreateAsync(sender, new CreateTaskRequest("Marker", null, AssigneeId: marker.Id));
        Assert.Single(await WaitForAsync(marker.Email, 1));
        return factory.Emails.NotificationsTo(address);
    }

    private async Task<Guid> CreateGroupAsync(params Guid[] memberIds)
    {
        var (admin, adminUser) = await factory.CreateUserAsync();
        await factory.SetRoleAsync(adminUser.Id, UserRole.Admin, null);
        var response = await admin.PostApiAsync("/api/groups", new SaveGroupRequest($"Grup {Guid.NewGuid():N}"[..20], [.. memberIds]));
        return (await response.Content.ReadApiAsync<GroupDetail>())!.Id;
    }

    [Fact]
    public async Task Assignee_Is_Emailed_When_A_Task_Is_Created_For_Them()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();

        await CreateAsync(alice, new CreateTaskRequest("Pregătește oferta", "Note care nu pleacă pe email",
            DueDate: new DateOnly(2031, 3, 14), Priority: TaskPriority.High, AssigneeId: bobUser.Id));

        var mail = Assert.Single(await WaitForAsync(bobUser.Email, 1));
        Assert.Equal("Task nou: Pregătește oferta", mail.Subject);
        Assert.Contains($"Bună, {bobUser.DisplayName},", mail.Body);
        Assert.Contains($"{aliceUser.DisplayName} ți-a alocat un task în Taskel.", mail.Body);
        Assert.Contains("Task: Pregătește oferta", mail.Body);
        Assert.Contains("Termen: 14 martie 2031", mail.Body);
        Assert.Contains("Prioritate: ridicată", mail.Body);
        Assert.Contains("Deschide Taskel: https://taskel.test.invalid", mail.Body);
        Assert.Contains("Poți opri notificările", mail.Body);
        Assert.DoesNotContain("Note care nu pleacă", mail.Body);
        Assert.Empty(factory.Emails.NotificationsTo(aliceUser.Email));
    }

    [Fact]
    public async Task An_Interval_Is_Shown_As_A_Period_And_Medium_Priority_Is_Left_Out()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();

        await CreateAsync(alice, new CreateTaskRequest("Inventar", null,
            DueDate: new DateOnly(2031, 3, 14), AssigneeId: bobUser.Id, StartDate: new DateOnly(2031, 3, 10)));

        var mail = Assert.Single(await WaitForAsync(bobUser.Email, 1));
        Assert.Contains("Interval: 10 martie 2031 – 14 martie 2031", mail.Body);
        Assert.DoesNotContain("Prioritate", mail.Body);
    }

    [Fact]
    public async Task A_Task_You_Keep_For_Yourself_Sends_Nothing()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();

        await CreateAsync(alice, new CreateTaskRequest("Al meu", null));
        await CreateAsync(alice, new CreateTaskRequest("Tot al meu", null, AssigneeId: aliceUser.Id));

        Assert.Empty(await NoneForAsync(aliceUser.Email));
    }

    [Fact]
    public async Task Reassigning_Emails_The_New_Person_Once()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (_, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, new CreateTaskRequest("Mutat", null, AssigneeId: bobUser.Id));

        await alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(carolUser.Id));
        // The same person again, and back to the creator: no news for anyone.
        await alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest(carolUser.Id));
        await alice.PutApiAsync($"/api/tasks/{task.Id}/assignee", new AssignTaskRequest());

        Assert.Single(await WaitForAsync(carolUser.Email, 1));
        Assert.Empty(await NoneForAsync(aliceUser.Email));
        Assert.Single(factory.Emails.NotificationsTo(carolUser.Email));
        Assert.Single(factory.Emails.NotificationsTo(bobUser.Email));
    }

    [Fact]
    public async Task Forwarding_Emails_The_New_Assignee_With_The_Note()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, new CreateTaskRequest("Raport", null, AssigneeId: bobUser.Id));

        await bob.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(carolUser.Id, Note: "Datele sunt în CRM."));

        var mail = Assert.Single(await WaitForAsync(carolUser.Email, 1));
        Assert.Equal("Task nou: Raport", mail.Subject);
        Assert.Contains($"{bobUser.DisplayName} ți-a direcționat un task în Taskel.", mail.Body);
        Assert.Contains($"Mesaj de la {bobUser.DisplayName}: Datele sunt în CRM.", mail.Body);
    }

    [Fact]
    public async Task Forwarding_Back_To_The_Creator_Emails_Them()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var task = await CreateAsync(alice, new CreateTaskRequest("Înapoi", null, AssigneeId: bobUser.Id));

        await bob.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(aliceUser.Id));

        var mail = Assert.Single(await WaitForAsync(aliceUser.Email, 1));
        Assert.Contains("ți-a direcționat un task", mail.Body);
    }

    [Fact]
    public async Task A_Group_Task_Emails_Every_Member_Except_Whoever_Gave_It()
    {
        var (alice, aliceUser) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var groupId = await CreateGroupAsync(aliceUser.Id, bobUser.Id, carolUser.Id);

        var task = await CreateAsync(alice, new CreateTaskRequest("Pentru echipă", null, GroupId: groupId));

        var toBob = Assert.Single(await WaitForAsync(bobUser.Email, 1));
        Assert.StartsWith("Task pentru grupul Grup ", toBob.Subject);
        Assert.EndsWith(": Pentru echipă", toBob.Subject);
        Assert.Contains("a dat un task grupului Grup ", toBob.Body);
        Assert.Contains("până când unul dintre voi îl preia", toBob.Body);
        Assert.Single(await WaitForAsync(carolUser.Email, 1));

        // Taking it and handing it back is not announced.
        await bob.PostAsync($"/api/tasks/{task.Id}/claim", null);
        await bob.PostAsync($"/api/tasks/{task.Id}/release", null);
        Assert.Empty(await NoneForAsync(aliceUser.Email));
        Assert.Single(factory.Emails.NotificationsTo(bobUser.Email));
        Assert.Single(factory.Emails.NotificationsTo(carolUser.Email));
    }

    [Fact]
    public async Task Forwarding_To_A_Group_Emails_Its_Members()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        var groupId = await CreateGroupAsync(bobUser.Id, carolUser.Id);
        var task = await CreateAsync(alice, new CreateTaskRequest("Către grup", null, AssigneeId: bobUser.Id));

        await bob.PostApiAsync($"/api/tasks/{task.Id}/forward", new ForwardTaskRequest(GroupId: groupId));

        var mail = Assert.Single(await WaitForAsync(carolUser.Email, 1));
        Assert.Contains("a direcționat un task către grupul", mail.Body);
        // Bob got the first assignment, but not his own forward.
        Assert.Single(factory.Emails.NotificationsTo(bobUser.Email));
    }

    [Fact]
    public async Task Someone_Who_Turned_Notifications_Off_Gets_None()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();

        var before = (await bob.GetApiAsync<NotificationSettingsResponse>("/api/notifications"))!;
        Assert.True(before.EmailOnNewTask);
        Assert.True(before.Available);
        Assert.Equal(bobUser.Email, before.Address);

        var off = await bob.PutApiAsync("/api/notifications", new UpdateNotificationSettingsRequest(false));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await bob.GetApiAsync<NotificationSettingsResponse>("/api/notifications"))!.EmailOnNewTask);
        await CreateAsync(alice, new CreateTaskRequest("Fără email", null, AssigneeId: bobUser.Id));
        Assert.Empty(await NoneForAsync(bobUser.Email));

        await bob.PutApiAsync("/api/notifications", new UpdateNotificationSettingsRequest(true));
        await CreateAsync(alice, new CreateTaskRequest("Cu email", null, AssigneeId: bobUser.Id));
        Assert.Equal("Task nou: Cu email", Assert.Single(await WaitForAsync(bobUser.Email, 1)).Subject);
    }

    [Fact]
    public async Task A_Failing_Mail_Server_Does_Not_Fail_The_Assignment_Or_Later_Emails()
    {
        var (alice, _) = await factory.CreateUserAsync();
        var (bob, bobUser) = await factory.CreateUserAsync();
        var (_, carolUser) = await factory.CreateUserAsync();
        factory.Emails.FailFor(bobUser.Email, () => new EmailSendException("down", new IOException()));

        var task = await CreateAsync(alice, new CreateTaskRequest("Oricum alocat", null, AssigneeId: bobUser.Id));
        await CreateAsync(alice, new CreateTaskRequest("După eroare", null, AssigneeId: carolUser.Id));

        Assert.Equal(bobUser.Id, task.Assignee!.Id);
        Assert.Single((await bob.GetApiAsync<List<TaskResponse>>("/api/tasks"))!);
        Assert.Single(await WaitForAsync(carolUser.Email, 1));
        Assert.Empty(factory.Emails.NotificationsTo(bobUser.Email));
    }

    [Fact]
    public async Task Notification_Settings_Need_A_Session_And_A_Valid_Body()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications")).StatusCode);
        var (bob, _) = await factory.CreateUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bob.PutAsJsonAsync("/api/notifications", new { emailOnNewTask = "da" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bob.PutAsJsonAsync("/api/notifications", new { emailOnNewTask = true, address = "altul@example.com" })).StatusCode);
    }

    [Fact]
    public void Notification_Message_Is_Marked_As_Automatic_And_Keeps_The_Subject_On_One_Line()
    {
        var smtp = new SmtpOptions { Host = "smtp.example.com", FromAddress = "taskel@example.com", FromName = "Taskel" };

        var message = SmtpEmailSender.BuildMessage(new EmailMessage("ion@example.com", "Task nou: Ofertă\r\nBcc: x@example.com", "Corp"), smtp);

        Assert.Equal("Task nou: Ofertă Bcc: x@example.com", message.Subject);
        Assert.Equal("auto-generated", message.Headers["Auto-Submitted"]);
        Assert.Equal("ion@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Empty(message.Bcc);
        Assert.Equal("taskel@example.com", Assert.Single(message.From.Mailboxes).Address);
    }

    [Fact]
    public void Without_A_Public_Url_The_Email_Has_No_Link()
    {
        var task = new TaskItem { Title = "Fără link" };
        var mail = TaskNotifier.Compose(task, new User { Email = "a@example.com" }, new User { Email = "b@example.com" },
            null, TaskNotificationReason.Assigned, null, publicUrl: "  ");

        Assert.DoesNotContain("Deschide Taskel", mail.Body);
        // Accounts without a display name are addressed by email.
        Assert.Contains("Bună, a@example.com,", mail.Body);
        Assert.Contains("b@example.com ți-a alocat", mail.Body);
    }
}
