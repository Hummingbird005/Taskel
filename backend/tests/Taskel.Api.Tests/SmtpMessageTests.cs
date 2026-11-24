using Taskel.Api.Email;

namespace Taskel.Api.Tests;

public class SmtpMessageTests
{
    private static readonly SmtpOptions Smtp = new() { Host = "smtp.example.com", Username = "me@example.com", FromName = "Taskel" };

    [Fact]
    public void Reply_Threads_Under_The_Original_Email()
    {
        var message = SmtpEmailSender.BuildMessage(
            new ReplyEmail("ana@example.com", "Ofertă proiect", "Am rezolvat.\nMulțumesc!", "<abc@mail.example.com>"), Smtp);

        Assert.Equal("ana@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("me@example.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("Taskel", message.From.Mailboxes.Single().Name);
        Assert.Equal("Re: Ofertă proiect", message.Subject);
        Assert.Equal("abc@mail.example.com", message.InReplyTo);
        Assert.Equal(["abc@mail.example.com"], message.References);
        Assert.Equal("Am rezolvat.\nMulțumesc!", message.TextBody);
    }

    [Fact]
    public void A_Reply_On_Behalf_Of_Someone_Shows_Their_Name_And_Comes_Back_To_Them()
    {
        var message = SmtpEmailSender.BuildMessage(
            new ReplyEmail("ana@example.com", "Ofertă", "Gata.", null, OnBehalfOf: new Sender("Ion Popescu", "ion@firma.example")), Smtp);

        // Still from Taskel's address, which is the one allowed to send; under the person's name.
        Assert.Equal("me@example.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("Ion Popescu prin Taskel", message.From.Mailboxes.Single().Name);
        var replyTo = message.ReplyTo.Mailboxes.Single();
        Assert.Equal("ion@firma.example", replyTo.Address);
        Assert.Equal("Ion Popescu", replyTo.Name);

        var unnamed = SmtpEmailSender.BuildMessage(
            new ReplyEmail("ana@example.com", "Ofertă", "Gata.", null, OnBehalfOf: new Sender("Ion", "ion@firma.example")),
            new SmtpOptions { Host = "smtp.example.com", Username = "me@example.com" });
        Assert.Equal("Ion prin Taskel", unnamed.From.Mailboxes.Single().Name);
    }

    [Fact]
    public void A_Notification_With_Html_Carries_Both_Versions()
    {
        var message = SmtpEmailSender.BuildMessage(new EmailMessage("ana@example.com", "Task nou: X", "Text.", "<p>Card</p>"), Smtp);

        Assert.Equal("Text.", message.TextBody);
        Assert.Equal("<p>Card</p>", message.HtmlBody);
        Assert.Equal("auto-generated", message.Headers["Auto-Submitted"]);
        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public void A_Formatted_Reply_Carries_Text_Html_And_Files()
    {
        var reply = new ReplyEmail("ion@example.com", "Ofertă", "Gata.", "<abc@mail.example.com>", "<p>Gata.</p>",
            [
                new ReplyAttachment("oferta.pdf", "application/pdf", () => new MemoryStream("%PDF-1.7"u8.ToArray())),
                new ReplyAttachment("lipsa.txt", "text/plain", () => null),
                new ReplyAttachment("ciudat.bin", "nu e un tip", () => new MemoryStream([1, 2, 3])),
            ]);

        var message = SmtpEmailSender.BuildMessage(reply, Smtp);

        Assert.Equal("Gata.", message.TextBody);
        Assert.Equal("<p>Gata.</p>", message.HtmlBody);
        Assert.Equal(["oferta.pdf", "ciudat.bin"], message.Attachments.Select(a => a.ContentDisposition?.FileName));
        Assert.Equal("application/pdf", message.Attachments.First().ContentType.MimeType);
        // A type that cannot be read is sent as plain bytes.
        Assert.Equal("application/octet-stream", message.Attachments.Last().ContentType.MimeType);
        Assert.Equal("abc@mail.example.com", message.InReplyTo);
    }

    [Theory]
    [InlineData("Re: Already a reply", "Re: Already a reply")]
    [InlineData("RE: Shouting", "RE: Shouting")]
    [InlineData("  Padded  ", "Re: Padded")]
    [InlineData("Line\r\nBcc: victim@example.com", "Re: Line Bcc: victim@example.com")]
    [InlineData("", "Re:")]
    [InlineData(null, "Re:")]
    public void Subject_Gets_A_Single_Re_Prefix_On_One_Line(string? original, string expected)
    {
        var message = SmtpEmailSender.BuildMessage(new ReplyEmail("ana@example.com", original, "x", null), Smtp);

        Assert.Equal(expected, message.Subject);
        Assert.Null(message.InReplyTo);
        Assert.Empty(message.References);
        Assert.Empty(message.Bcc);
    }

    [Fact]
    public void From_Address_Overrides_The_Username()
    {
        var smtp = new SmtpOptions { Host = "h", Username = "login", FromAddress = "tasks@example.com" };

        var message = SmtpEmailSender.BuildMessage(new ReplyEmail("ana@example.com", "s", "b", null), smtp);

        Assert.Equal("tasks@example.com", message.From.Mailboxes.Single().Address);
    }

    [Theory]
    [InlineData(null, "me@example.com", false)]
    [InlineData("smtp.example.com", null, false)]
    [InlineData("smtp.example.com", "me@example.com", true)]
    public void Is_Configured_Needs_A_Host_And_A_From_Address(string? host, string? username, bool expected)
    {
        Assert.Equal(expected, new SmtpOptions { Host = host, Username = username }.IsConfigured);
    }

    [Fact]
    public async Task Sending_Without_Configuration_Fails_Clearly()
    {
        // The test send takes its settings as an argument, so no saved settings are needed here.
        var sender = new SmtpEmailSender(null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAsync<EmailNotConfiguredException>(
            () => sender.SendTestAsync(new EmailMessage("ana@example.com", "s", "b"), new SmtpOptions()));
    }
}
