using PixFix.Infrastructure.Assets;
using PixFix.Infrastructure.Data;

namespace PixFix.Infrastructure.Communication;

/// <summary>Fax-Vorlage (z. B. „TELEFAX Vorlage nicht erreicht.doc“) mit Datei in der Ablage.</summary>
public sealed record FaxDocument(long Id, string Name, string File);

/// <summary>Fax-Dokumente (SPEC F6): werden wie in PixFix 69 mit dem Standardprogramm geöffnet, ausgefüllt und gefaxt.</summary>
public sealed class FaxDocumentRepository(PixFixDatabase database, AssetStore assets, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<FaxDocument> LoadAll()
    {
        using var db = database.Open();
        using var command = SqliteExtensions.Create(
            db,
            "SELECT f.id, f.name, a.relative_path FROM fax_documents f JOIN assets a ON a.id = f.asset_id ORDER BY f.sort_order, f.name;",
            null);
        using var reader = command.ExecuteReader();
        var result = new List<FaxDocument>();
        while (reader.Read())
        {
            result.Add(new FaxDocument(reader.GetInteger("id"), reader.GetText("name"), assets.GetFullPath(reader.GetText("relative_path"))));
        }

        return result;
    }

    public void Add(string file)
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var asset = assets.Import(file, db, tx, time.GetUtcNow());
        var order = Convert.ToInt32(db.Scalar("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM fax_documents;", tx), System.Globalization.CultureInfo.InvariantCulture);
        db.Execute(
            "INSERT INTO fax_documents(name, asset_id, sort_order) VALUES($name, $asset, $order);",
            tx,
            ("$name", Path.GetFileName(file)),
            ("$asset", asset.Id),
            ("$order", order));
        tx.Commit();
    }

    public void Remove(long id)
    {
        using var db = database.Open();
        db.Execute("DELETE FROM fax_documents WHERE id = $id;", null, ("$id", id));
    }
}
