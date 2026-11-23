using System.Security;

namespace Taskel.Api.Integrations;

/// <summary>
/// The XML manifest of the Outlook add-in: a "Taskel" button on a message being read, which opens the add-in's
/// pane (<c>outlook.html</c> of the interface) next to it. Version 1.1 asks to change the open email, to mark it
/// with the category "Taskel"; an add-in installed from version 1.0 must be installed again for that. The pane can be pinned, so it follows the selected
/// message. Works in classic Outlook for Windows, the new Outlook, Outlook on the web and Outlook for Mac.
/// </summary>
public static class OutlookManifest
{
    /// <summary>The add-in's identity. It stays the same across versions, so an update replaces the add-in.</summary>
    public const string AddInId = "5b0f3c2e-8f4a-4d1e-9c6b-7a2d1e4f9b30";

    /// <summary>Raised when the manifest changes, so Outlook picks up the new one.</summary>
    public const string Version = "1.1.0.0";

    /// <summary>
    /// Read the email, and change only it: the pane adds the category "Taskel" to an email it sent. Nothing else in
    /// the mailbox is read or changed.
    /// </summary>
    public const string Permission = "ReadWriteItem";

    /// <summary>The manifest for a Taskel reached at <paramref name="baseUrl"/> (no trailing slash).</summary>
    public static string For(string baseUrl)
    {
        var b = SecurityElement.Escape(baseUrl.TrimEnd('/'));
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
           xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
           xmlns:mailappor="http://schemas.microsoft.com/office/mailappversionoverrides/1.0"
           xsi:type="MailApp">
  <Id>{AddInId}</Id>
  <Version>{Version}</Version>
  <ProviderName>Taskel</ProviderName>
  <DefaultLocale>ro-RO</DefaultLocale>
  <DisplayName DefaultValue="Taskel" />
  <Description DefaultValue="Trimite emailul în Taskel: către tine, către colegi sau către un grup, cu indicații." />
  <IconUrl DefaultValue="{b}/outlook/icon-64.png" />
  <HighResolutionIconUrl DefaultValue="{b}/outlook/icon-128.png" />
  <SupportUrl DefaultValue="{b}/" />
  <Hosts>
    <Host Name="Mailbox" />
  </Hosts>
  <Requirements>
    <Sets>
      <Set Name="Mailbox" MinVersion="1.5" />
    </Sets>
  </Requirements>
  <FormSettings>
    <Form xsi:type="ItemRead">
      <DesktopSettings>
        <SourceLocation DefaultValue="{b}/outlook.html" />
        <RequestedHeight>450</RequestedHeight>
      </DesktopSettings>
    </Form>
  </FormSettings>
  <Permissions>{Permission}</Permissions>
  <Rule xsi:type="RuleCollection" Mode="Or">
    <Rule xsi:type="ItemIs" ItemType="Message" FormType="Read" />
  </Rule>
  <DisableEntityHighlighting>false</DisableEntityHighlighting>
  <VersionOverrides xmlns="http://schemas.microsoft.com/office/mailappversionoverrides" xsi:type="VersionOverridesV1_0">
    <Requirements>
      <bt:Sets DefaultMinVersion="1.5">
        <bt:Set Name="Mailbox" />
      </bt:Sets>
    </Requirements>
{Hosts(pinnable: false)}
{Resources(b)}
    <VersionOverrides xmlns="http://schemas.microsoft.com/office/mailappversionoverrides/1.1" xsi:type="VersionOverridesV1_1">
      <Requirements>
        <bt:Sets DefaultMinVersion="1.5">
          <bt:Set Name="Mailbox" />
        </bt:Sets>
      </Requirements>
{Hosts(pinnable: true)}
{Resources(b)}
    </VersionOverrides>
  </VersionOverrides>
</OfficeApp>
""";
    }

    // The button on a message being read. Pinning needs the 1.1 overrides, which Outlook uses where it supports
    // them; older versions read the 1.0 ones.
    private static string Hosts(bool pinnable) => $"""
    <Hosts>
      <Host xsi:type="MailHost">
        <DesktopFormFactor>
          <ExtensionPoint xsi:type="MessageReadCommandSurface">
            <OfficeTab id="TabDefault">
              <Group id="taskelGroup">
                <Label resid="groupLabel" />
                <Control xsi:type="Button" id="taskelOpenPane">
                  <Label resid="buttonLabel" />
                  <Supertip>
                    <Title resid="buttonLabel" />
                    <Description resid="buttonTip" />
                  </Supertip>
                  <Icon>
                    <bt:Image size="16" resid="icon16" />
                    <bt:Image size="32" resid="icon32" />
                    <bt:Image size="80" resid="icon80" />
                  </Icon>
                  <Action xsi:type="ShowTaskpane">
                    <SourceLocation resid="paneUrl" />{(pinnable ? "\n                    <SupportsPinning>true</SupportsPinning>" : "")}
                  </Action>
                </Control>
              </Group>
            </OfficeTab>
          </ExtensionPoint>
        </DesktopFormFactor>
      </Host>
    </Hosts>
""";

    private static string Resources(string b) => $"""
    <Resources>
      <bt:Images>
        <bt:Image id="icon16" DefaultValue="{b}/outlook/icon-16.png" />
        <bt:Image id="icon32" DefaultValue="{b}/outlook/icon-32.png" />
        <bt:Image id="icon80" DefaultValue="{b}/outlook/icon-80.png" />
      </bt:Images>
      <bt:Urls>
        <bt:Url id="paneUrl" DefaultValue="{b}/outlook.html" />
      </bt:Urls>
      <bt:ShortStrings>
        <bt:String id="groupLabel" DefaultValue="Taskel" />
        <bt:String id="buttonLabel" DefaultValue="Trimite în Taskel" />
      </bt:ShortStrings>
      <bt:LongStrings>
        <bt:String id="buttonTip" DefaultValue="Fă din acest email un task: pentru tine, pentru colegi sau pentru un grup." />
      </bt:LongStrings>
    </Resources>
""";
}
