using System.Net;
using System.Xml.Linq;
using Taskel.Api.Integrations;

namespace Taskel.Api.Tests;

/// <summary>The Outlook add-in's manifest, which points Outlook at this Taskel.</summary>
[Collection(ApiCollection.Name)]
public class OutlookAddInTests(TaskelApiFactory factory)
{
    private static readonly XNamespace App = "http://schemas.microsoft.com/office/appforoffice/1.1";
    private static readonly XNamespace Types = "http://schemas.microsoft.com/office/officeappbasictypes/1.0";

    [Fact]
    public async Task The_Manifest_Is_Served_Without_Signing_In_And_Points_At_This_Taskel()
    {
        var response = await factory.CreateClient().GetAsync("/api/outlook/manifest.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("taskel-outlook.xml", response.Content.Headers.ContentDisposition!.ToString());

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(OutlookManifest.AddInId, xml.Root!.Element(App + "Id")!.Value);
        Assert.Equal("ReadItem", xml.Root.Element(App + "Permissions")!.Value);
        var pane = xml.Descendants(Types + "Url").Select(u => u.Attribute("DefaultValue")!.Value).Distinct();
        // The public address set for emails, as in the links Taskel sends.
        Assert.Equal("https://taskel.test.invalid/outlook.html", Assert.Single(pane));
        // Every address in it is on this Taskel.
        var addresses = xml.Descendants().Attributes("DefaultValue").Select(a => a.Value).Where(v => v.StartsWith("http")).ToList();
        Assert.NotEmpty(addresses);
        Assert.All(addresses, a => Assert.StartsWith("https://taskel.test.invalid/", a));
    }

    [Fact]
    public void The_Public_Address_Is_Used_And_Escaped()
    {
        var xml = XDocument.Parse(OutlookManifest.For("https://taskel.firma.ro/app?x=1&y=<2>/"));

        Assert.Contains(xml.Descendants(Types + "Url"), u => u.Attribute("DefaultValue")!.Value == "https://taskel.firma.ro/app?x=1&y=<2>/outlook.html");
    }

    [Fact]
    public void The_Pinnable_Pane_Is_Declared_For_Outlook_Versions_That_Support_It()
    {
        var text = OutlookManifest.For("https://taskel.firma.ro");

        Assert.Equal(1, text.Split("<SupportsPinning>true</SupportsPinning>").Length - 1);
        Assert.Contains("VersionOverridesV1_1", text);
    }
}
