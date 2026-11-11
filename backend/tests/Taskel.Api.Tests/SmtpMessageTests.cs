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
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new SmtpOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAsync<EmailNotConfiguredException>(
            () => sender.SendReplyAsync(new ReplyEmail("ana@example.com", "s", "b", null)));
    }
}
