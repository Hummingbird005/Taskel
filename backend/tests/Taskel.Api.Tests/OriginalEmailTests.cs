using Taskel.Api.Email;

namespace Taskel.Api.Tests;

public class OriginalEmailTests
{
    [Fact]
    public void The_Sender_And_The_Date_Are_Read_From_The_First_Lines()
    {
        var email = OriginalEmail.Parse("De la: Ana Popa <ana@client.example>\nPrimit: 2026-11-23 09:12\n\nBună ziua,\nofertă.", null)!;

        Assert.Equal("Ana Popa", email.SenderName);
        Assert.Equal("ana@client.example", email.SenderAddress);
        Assert.Equal(new DateTime(2026, 11, 23, 9, 12, 0), email.Received);
        Assert.Equal("Bună ziua,\nofertă.", email.Body);
        Assert.Equal("Ana Popa <ana@client.example>", email.Who);
    }

    [Theory]
    [InlineData("Doar textul.", "ana@client.example", "ana@client.example", "Doar textul.")]
    [InlineData("De la: ana@client.example\n\nText", null, "ana@client.example", "Text")]
    [InlineData("De la: Departamentul X\n\nText", "x@client.example", "Departamentul X <x@client.example>", "Text")]
    public void Missing_Parts_Fall_Back(string description, string? fallback, string who, string body)
    {
        var email = OriginalEmail.Parse(description, fallback)!;

        Assert.Equal(who, email.Who);
        Assert.Equal(body, email.Body);
        Assert.Null(email.Received);
    }

    [Fact]
    public void No_Message_Is_No_Email() => Assert.Null(OriginalEmail.Parse("  ", "a@b.example"));
}
