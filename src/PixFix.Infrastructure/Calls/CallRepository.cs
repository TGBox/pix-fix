using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixFix.Core.Calls;
using PixFix.Core.Legacy;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Calls;

/// <summary>Anruf im Papierkorb.</summary>
public sealed record CallTrashItem(string TrashId, string CustomerNumber, string Reason, DateTimeOffset DeletedUtc);

/// <summary>Speichern, Laden und Papierkorb der Anrufe (SPEC F4).</summary>
public sealed class CallRepository(PixFixDatabase database, TimeProvider? timeProvider = null)
{
    private const string TrashKind = "call";
    private const string SnapshotFormat = "pixfix-call-1";

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    /// <summary>Neueste Anrufe zuerst (wie in PixFix 69, wo neue Karten oben eingefügt werden).</summary>
    public IReadOnlyList<Call> LoadAll()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT * FROM calls ORDER BY created_utc DESC, id;", null);
        using var reader = command.ExecuteReader();
        var result = new List<Call>();
        while (reader.Read())
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public Call Create()
    {
        var now = time.GetUtcNow();
        var call = new Call { Id = Guid.NewGuid().ToString("N"), ReceivedUtc = now, CreatedUtc = now, UpdatedUtc = now };
        Save(call);
        return call;
    }

    /// <summary>Legt den Anruf an oder überschreibt ihn vollständig.</summary>
    public void Save(Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        using var db = database.Open();
        Write(db, null, call);
    }

    /// <summary>Verschiebt den Anruf in den Papierkorb.</summary>
    public void MoveToTrash(string callId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        Call? call = null;
        using (var command = SqliteExtensions.Create(db, "SELECT * FROM calls WHERE id = $id;", tx, ("$id", callId)))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                call = Read(reader);
            }
        }

        if (call is null)
        {
            return;
        }

        db.Execute(
            "INSERT INTO trash(id, kind, original_id, deleted_utc, snapshot_json) VALUES($id, $kind, $original, $now, $json);",
            tx,
            ("$id", Guid.NewGuid().ToString("N")),
            ("$kind", TrashKind),
            ("$original", callId),
            ("$now", time.GetUtcNow().ToDb()),
            ("$json", JsonSerializer.Serialize(new CallSnapshot(SnapshotFormat, call))));
        db.Execute("DELETE FROM calls WHERE id = $id;", tx, ("$id", callId));
        tx.Commit();
    }

    public IReadOnlyList<CallTrashItem> LoadTrash()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db, "SELECT id, deleted_utc, snapshot_json FROM trash WHERE kind = $kind ORDER BY deleted_utc DESC;", null, ("$kind", TrashKind));
        using var reader = command.ExecuteReader();
        var result = new List<CallTrashItem>();
        while (reader.Read())
        {
            var call = ParseSnapshot(reader.GetText("snapshot_json"));
            result.Add(new CallTrashItem(
                reader.GetText("id"),
                call.CustomerNumber,
                call.ReasonText,
                DateTimeOffset.TryParse(reader.GetText("deleted_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.MinValue));
        }

        return result;
    }

    /// <summary>Stellt den Anruf wieder her; ist die Id inzwischen vergeben, bekommt er eine neue.</summary>
    public Call Restore(string trashId)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var json = db.Scalar("SELECT snapshot_json FROM trash WHERE id = $id AND kind = $kind;", tx, ("$id", trashId), ("$kind", TrashKind)) as string
            ?? throw new InvalidOperationException("Papierkorb-Eintrag nicht gefunden.");
        var call = ParseSnapshot(json);
        var exists = Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM calls WHERE id = $id;", tx, ("$id", call.Id)), CultureInfo.InvariantCulture) > 0;
        if (exists || string.IsNullOrEmpty(call.Id))
        {
            call = call with { Id = Guid.NewGuid().ToString("N") };
        }

        call = call with { UpdatedUtc = time.GetUtcNow() };
        Write(db, tx, call);
        db.Execute("DELETE FROM trash WHERE id = $id;", tx, ("$id", trashId));
        tx.Commit();
        return call;
    }

    public void DeletePermanently(string trashId)
    {
        using var db = database.Open();
        db.Execute("DELETE FROM trash WHERE id = $id AND kind = $kind;", null, ("$id", trashId), ("$kind", TrashKind));
    }

    /// <summary>Cobra-Schnelltexte (aus PixFix 69 übernommen).</summary>
    public IReadOnlyList<CobraQuickText> LoadCobraQuickTexts()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(db, "SELECT * FROM cobra_quick_texts ORDER BY sort_order, name;", null);
        using var reader = command.ExecuteReader();
        var result = new List<CobraQuickText>();
        while (reader.Read())
        {
            result.Add(new CobraQuickText(
                reader.GetText("name"),
                reader.GetText("cobra_text"),
                reader.GetText("notes_text"),
                reader.GetText("work_text"),
                reader.GetFlag("complete_call"),
                reader.GetFlag("schedule_recall")));
        }

        return result;
    }

    // ------------------------------------------------------------------ Hilfsfunktionen

    private sealed record CallSnapshot(string Format, Call Call);

    /// <summary>Neue Snapshots und die aus PixFix 69 übernommenen (Feldliste der alten CallEntry-Klasse).</summary>
    private static Call ParseSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var format = root.TryGetProperty("Format", out var f) ? f.GetString() ?? string.Empty : string.Empty;
        if (format == SnapshotFormat)
        {
            return JsonSerializer.Deserialize<CallSnapshot>(json)?.Call ?? throw new JsonException("Leerer Snapshot.");
        }

        var v = root.TryGetProperty("Values", out var values)
            ? values.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        string Value(string key) => v.TryGetValue(key, out var value) ? value : string.Empty;
        bool Flag(string key) => string.Equals(Value(key), "true", StringComparison.OrdinalIgnoreCase);
        var zone = TimeZoneInfo.Local;
        DateTimeOffset? LegacyTime(string key) => LegacyTimestamp.ToUtc(Value(key), zone);

        var created = LegacyTime("CreatedAt") ?? DateTimeOffset.UtcNow;
        return new Call
        {
            Id = Value("Id"),
            CustomerNumber = Value("CustomerNumber"),
            CustomerId = Value("CallIdentifier"),
            Phone1 = Value("Phone1"),
            Phone2 = Value("Phone2"),
            Phone3 = Value("Phone3"),
            PhonePrivate = Value("PhonePrivate"),
            PhoneDirect = Value("PhoneDirect"),
            Mobile = Value("Mobile"),
            AlternativePhone = Value("AlternativePhone"),
            Email1 = Value("Email1"),
            Email2 = Value("Email2"),
            EmailTarget = Value("EmailTarget"),
            Fax = Value("Fax"),
            Notes = Value("Notes"),
            ReasonText = Value("Reason"),
            ReasonRtf = Value("ReasonRtf"),
            SolutionText = Value("WorkPerformed"),
            SolutionRtf = Value("WorkPerformedRtf"),
            Status = CallStatusText.ParseLegacy(Value("Status")),
            CallbackTarget = Value("CallbackTargets").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty,
            IsBillable = Flag("IsBillable"),
            BillingStartUtc = LegacyTime("BillingStart"),
            BillingEndUtc = LegacyTime("BillingEnd"),
            ReceivedUtc = LegacyTime("ReceivedAt"),
            CreatedUtc = created,
            UpdatedUtc = LegacyTime("UpdatedAt") ?? created,
            CompletedUtc = LegacyTime("CompletedAt"),
            ReminderUtc = LegacyTime("ReminderAt"),
            ReminderActive = false, // Erinnerungen gelöschter Anrufe nicht wieder aufleben lassen
            IsCollapsed = Flag("IsCollapsed"),
        };
    }

    private static Call Read(SqliteDataReader r) => new()
    {
        Id = r.GetText("id"),
        CustomerNumber = r.GetText("customer_number"),
        CustomerId = r.GetText("customer_id"),
        Phone1 = r.GetText("phone1"),
        Phone2 = r.GetText("phone2"),
        Phone3 = r.GetText("phone3"),
        PhonePrivate = r.GetText("phone_private"),
        PhoneDirect = r.GetText("phone_direct"),
        Mobile = r.GetText("mobile"),
        AlternativePhone = r.GetText("alternative_phone"),
        Email1 = r.GetText("email1"),
        Email2 = r.GetText("email2"),
        EmailTarget = r.GetText("email_target"),
        Fax = r.GetText("fax"),
        Notes = r.GetText("notes"),
        ReasonText = r.GetText("reason_text"),
        ReasonRtf = r.GetText("reason_rtf"),
        SolutionText = r.GetText("solution_text"),
        SolutionRtf = r.GetText("solution_rtf"),
        Status = Enum.TryParse<CallStatus>(r.GetText("status"), out var status) ? status : CallStatus.Open,
        CallbackTarget = r.GetText("callback_target"),
        IsBillable = r.GetFlag("is_billable"),
        BillingStartUtc = Time(r.GetText("billing_start_utc")),
        BillingEndUtc = Time(r.GetText("billing_end_utc")),
        ReceivedUtc = Time(r.GetText("received_utc")),
        CreatedUtc = Time(r.GetText("created_utc")) ?? DateTimeOffset.MinValue,
        UpdatedUtc = Time(r.GetText("updated_utc")) ?? DateTimeOffset.MinValue,
        CompletedUtc = Time(r.GetText("completed_utc")),
        ReminderUtc = Time(r.GetText("reminder_utc")),
        ReminderActive = r.GetFlag("reminder_active"),
        ReminderLastShownUtc = Time(r.GetText("reminder_last_shown_utc")),
        IsCollapsed = r.GetFlag("is_collapsed"),
    };

    private static DateTimeOffset? Time(string value) =>
        value.Length > 0 && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static void Write(SqliteConnection db, SqliteTransaction? tx, Call c) => db.Execute(
        """
        INSERT INTO calls(id, customer_number, customer_id, phone1, phone2, phone3, phone_private, phone_direct, mobile, alternative_phone,
            email1, email2, email_target, fax, notes, reason_text, reason_rtf, solution_text, solution_rtf, status, callback_target,
            is_billable, billing_start_utc, billing_end_utc, received_utc, created_utc, updated_utc, completed_utc, reminder_utc,
            reminder_active, reminder_last_shown_utc, is_collapsed)
        VALUES($id, $customer, $customerId, $p1, $p2, $p3, $pp, $pd, $mobile, $alt, $e1, $e2, $et, $fax, $notes, $reason, $reasonRtf,
            $solution, $solutionRtf, $status, $callback, $billable, $bs, $be, $received, $created, $updated, $completed, $reminder,
            $reminderActive, $reminderShown, $collapsed)
        ON CONFLICT(id) DO UPDATE SET
            customer_number = excluded.customer_number, customer_id = excluded.customer_id, phone1 = excluded.phone1,
            phone2 = excluded.phone2, phone3 = excluded.phone3, phone_private = excluded.phone_private,
            phone_direct = excluded.phone_direct, mobile = excluded.mobile, alternative_phone = excluded.alternative_phone,
            email1 = excluded.email1, email2 = excluded.email2, email_target = excluded.email_target, fax = excluded.fax,
            notes = excluded.notes, reason_text = excluded.reason_text, reason_rtf = excluded.reason_rtf,
            solution_text = excluded.solution_text, solution_rtf = excluded.solution_rtf, status = excluded.status,
            callback_target = excluded.callback_target, is_billable = excluded.is_billable,
            billing_start_utc = excluded.billing_start_utc, billing_end_utc = excluded.billing_end_utc,
            received_utc = excluded.received_utc, updated_utc = excluded.updated_utc, completed_utc = excluded.completed_utc,
            reminder_utc = excluded.reminder_utc, reminder_active = excluded.reminder_active,
            reminder_last_shown_utc = excluded.reminder_last_shown_utc, is_collapsed = excluded.is_collapsed;
        """,
        tx,
        ("$id", c.Id),
        ("$customer", c.CustomerNumber),
        ("$customerId", c.CustomerId),
        ("$p1", c.Phone1),
        ("$p2", c.Phone2),
        ("$p3", c.Phone3),
        ("$pp", c.PhonePrivate),
        ("$pd", c.PhoneDirect),
        ("$mobile", c.Mobile),
        ("$alt", c.AlternativePhone),
        ("$e1", c.Email1),
        ("$e2", c.Email2),
        ("$et", c.EmailTarget),
        ("$fax", c.Fax),
        ("$notes", c.Notes),
        ("$reason", c.ReasonText),
        ("$reasonRtf", c.ReasonRtf),
        ("$solution", c.SolutionText),
        ("$solutionRtf", c.SolutionRtf),
        ("$status", c.Status.ToString()),
        ("$callback", c.CallbackTarget),
        ("$billable", c.IsBillable.ToDb()),
        ("$bs", c.BillingStartUtc.ToDb()),
        ("$be", c.BillingEndUtc.ToDb()),
        ("$received", c.ReceivedUtc.ToDb()),
        ("$created", c.CreatedUtc.ToDb()),
        ("$updated", c.UpdatedUtc.ToDb()),
        ("$completed", c.CompletedUtc.ToDb()),
        ("$reminder", c.ReminderUtc.ToDb()),
        ("$reminderActive", c.ReminderActive.ToDb()),
        ("$reminderShown", c.ReminderLastShownUtc.ToDb()),
        ("$collapsed", c.IsCollapsed.ToDb()));
}
