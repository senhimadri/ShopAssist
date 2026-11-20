using Microsoft.Data.Sqlite;

namespace ShopAssist.Core.Memory;


/// <summary>
/// Long-term memory: durable facts about a customer, stored in SQLite.
/// Think of it as a small CRM table: one row per fact, keyed by customer.
/// </summary>
public sealed class CustomerMemoryStore : IDisposable
{
    private readonly SqliteConnection _db;

    public CustomerMemoryStore(string dbPath = "shopassist.db")
    {
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();

        using var create = _db.CreateCommand();

        create.CommandText = """
            CREATE TABLE IF NOT EXISTS CustomerFacts (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId  TEXT    NOT NULL,
                FactKey     TEXT    NOT NULL,
                FactValue   TEXT    NOT NULL,
                UpdatedUtc  TEXT    NOT NULL,
                UNIQUE (CustomerId, FactKey)
            );
            """;

        create.ExecuteNonQuery();
    }

    /// <summary>Save or update one fact. A new value replaces the old one for the same key.</summary>
    public void Remember(string customerId, string key, string value)
    {
        using var cmd = _db.CreateCommand();

        cmd.CommandText = """
            INSERT INTO CustomerFacts (CustomerId, FactKey, FactValue, UpdatedUtc)
            VALUES ($c, $k, $v, $t)
            ON CONFLICT (CustomerId, FactKey)
            DO UPDATE SET FactValue = excluded.FactValue, UpdatedUtc = excluded.UpdatedUtc;
            """;

        cmd.Parameters.AddWithValue("$c", customerId);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Return all remembered facts for one customer, as key/value pairs.</summary>
    public IReadOnlyList<(string Key, string Value)> Recall(string customerId)
    {
        using var cmd = _db.CreateCommand();

        cmd.CommandText = "SELECT FactKey, FactValue FROM CustomerFacts WHERE CustomerId = $c ORDER BY FactKey;";
        cmd.Parameters.AddWithValue("$c", customerId);

        var facts = new List<(string, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            facts.Add((reader.GetString(0), reader.GetString(1)));
        return facts;
    }
    public void Dispose() => _db.Dispose();
}
