using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PixFix.Infrastructure.Communication;

/// <summary>
/// Öffnet einen E-Mail-Entwurf (SPEC F6) wie PixFix 69: mit Outlook Desktop über die Outlook-Automatisierung,
/// sonst über <c>mailto:</c>. Der Entwurf wird nur angezeigt, nie automatisch gesendet.
/// </summary>
public static class MailComposer
{
    private const int OlMailItem = 0;

    public static void OpenDraft(string to, string subject, string body, bool useOutlook, IReadOnlyList<string>? attachments = null)
    {
        to = (to ?? string.Empty).Trim();
        subject = (subject ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        body = (body ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);

        if (useOutlook)
        {
            OpenInOutlook(to, subject, body, attachments ?? []);
            return;
        }

        var uri = "mailto:" + to + (subject.Length == 0 && body.Length == 0
            ? string.Empty
            : "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body));
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }

    private static void OpenInOutlook(string to, string subject, string body, IReadOnlyList<string> attachments)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Outlook (Desktop) wurde auf diesem PC nicht gefunden.");
        var app = Activator.CreateInstance(outlookType) ?? throw new InvalidOperationException("Outlook konnte nicht gestartet werden.");
        object? mail = null;
        try
        {
            mail = Invoke(app, "CreateItem", BindingFlags.InvokeMethod, OlMailItem)
                ?? throw new InvalidOperationException("Outlook hat keinen Entwurf angelegt.");
            Invoke(mail, "To", BindingFlags.SetProperty, to);
            Invoke(mail, "Subject", BindingFlags.SetProperty, subject);
            Invoke(mail, "Body", BindingFlags.SetProperty, body);
            if (attachments.Count > 0)
            {
                var items = Invoke(mail, "Attachments", BindingFlags.GetProperty);
                foreach (var file in attachments)
                {
                    Invoke(items!, "Add", BindingFlags.InvokeMethod, file);
                }
            }

            Invoke(mail, "Display", BindingFlags.InvokeMethod, false);
        }
        finally
        {
            if (mail is not null && Marshal.IsComObject(mail))
            {
                Marshal.ReleaseComObject(mail);
            }

            if (Marshal.IsComObject(app))
            {
                Marshal.ReleaseComObject(app);
            }
        }
    }

    private static object? Invoke(object target, string member, BindingFlags flags, params object[] args) =>
        target.GetType().InvokeMember(member, flags, null, target, args, System.Globalization.CultureInfo.InvariantCulture);
}
