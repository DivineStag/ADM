using System.Collections.Immutable;
using System.Text.Json;
using ADM.Application;
using ADM.Contracts;
using ADM.Domain;
using Microsoft.Data.Sqlite;

namespace ADM.Infrastructure;

/// <summary>Checkpoint and send reservation share a transaction. A recorded attempt is never silently repeated.</summary>
public sealed class SqliteDispatchStore(string path) : IDispatchStore, IFlightPlanCache, IDutyStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _ownership;
    private bool _initialized;
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _ownership ??= new FileStream(path + ".owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (_initialized) return connection;
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                command.CommandText = "SELECT version FROM schema_version LIMIT 2";
                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetValue(0) is not long version || version != 1 || await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new InvalidDataException("Unsupported or ambiguous ADM database version; no schema was changed.");
            }
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS schema_version(version INTEGER NOT NULL);
                INSERT INTO schema_version SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_version);
                CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, checkpoint TEXT NOT NULL, flight TEXT NOT NULL, aircraft TEXT NOT NULL, archived INTEGER NOT NULL, saved TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS command_journal(id TEXT PRIMARY KEY, session TEXT NOT NULL, kind TEXT NOT NULL, service TEXT NOT NULL, version INTEGER NOT NULL, intent TEXT NOT NULL, outcome TEXT NOT NULL,
                    UNIQUE(session,kind,service,version));
                CREATE UNIQUE INDEX IF NOT EXISTS one_active_session ON sessions((1)) WHERE archived=0;
                CREATE TABLE IF NOT EXISTS flight_cache(id INTEGER PRIMARY KEY CHECK(id=1), flight TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS checkpoint_history(sequence INTEGER PRIMARY KEY AUTOINCREMENT, session TEXT NOT NULL, checkpoint TEXT NOT NULL, saved TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS duty_sessions(id TEXT PRIMARY KEY, progress TEXT NOT NULL, saved TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS sector_legs(id TEXT PRIMARY KEY, duty TEXT NOT NULL, session TEXT NOT NULL UNIQUE, number INTEGER NOT NULL, UNIQUE(duty,number));
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            command.CommandText = "SELECT version FROM schema_version";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("Unsupported ADM database version; no session was dispatched.");
            _initialized = true;
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async Task<SavedDispatch?> LoadActiveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT checkpoint,flight,aircraft FROM sessions WHERE archived=0 ORDER BY saved DESC LIMIT 1";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new(DispatchCheckpointCodec.Deserialize(reader.GetString(0)), JsonSerializer.Deserialize<FlightPlanSnapshot>(reader.GetString(1))
                ?? throw new InvalidDataException("Saved flight is missing."), reader.GetString(2));
        }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(SavedDispatch operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await WriteAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> TryBeginSendAsync(SavedDispatch operation, OperationalCommand intent, CancellationToken cancellationToken)
    {
        if (!operation.Checkpoint.State.Commands.Contains(intent) || intent.SessionId != operation.Checkpoint.State.Session.Id)
            throw new ArgumentException("Command must belong to the persisted session journal.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await WriteAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO command_journal VALUES($id,$session,$kind,$service,$version,$intent,'Unknown')";
            command.Parameters.AddWithValue("$id", intent.Id.ToString("D")); command.Parameters.AddWithValue("$session", intent.SessionId.ToString("D"));
            command.Parameters.AddWithValue("$kind", intent.Kind.ToString()); command.Parameters.AddWithValue("$service", intent.Service?.ToString() ?? intent.Release?.ToString() ?? "");
            command.Parameters.AddWithValue("$version", intent.IntentVersion); command.Parameters.AddWithValue("$intent", JsonSerializer.Serialize(intent));
            bool reserved = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            transaction.Commit(); return reserved;
        }
        finally { _gate.Release(); }
    }
    public async Task RecordReceiptAsync(SavedDispatch operation, Guid commandId, ReceiptStatus status, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await WriteAsync(connection, transaction, operation, cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE command_journal SET outcome=$outcome WHERE id=$id AND session=$session";
            command.Parameters.AddWithValue("$outcome", status.ToString()); command.Parameters.AddWithValue("$id", commandId.ToString("D"));
            command.Parameters.AddWithValue("$session", operation.Checkpoint.State.Session.Id.ToString("D"));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw new InvalidDataException("Receipt has no reserved command.");
            transaction.Commit();
        }
        finally { _gate.Release(); }
    }
    public async Task<ImmutableHashSet<Guid>> AttemptedAsync(Guid sessionId, CancellationToken cancellationToken)
        => await JournalIdsAsync(sessionId, false, cancellationToken).ConfigureAwait(false);
    public async Task<ImmutableHashSet<Guid>> RejectedAsync(Guid sessionId, CancellationToken cancellationToken)
        => await JournalIdsAsync(sessionId, true, cancellationToken).ConfigureAwait(false);
    private async Task<ImmutableHashSet<Guid>> JournalIdsAsync(Guid sessionId, bool rejectedOnly, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.CommandText = "SELECT id FROM command_journal WHERE session=$session" + (rejectedOnly ? " AND outcome='Rejected'" : "");
            command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var result = ImmutableHashSet.CreateBuilder<Guid>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Guid.Parse(reader.GetString(0)));
            return result.ToImmutable();
        }
        finally { _gate.Release(); }
    }
    private static async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, SavedDispatch operation, CancellationToken ct)
    {
        DispatchCheckpointCodec.Validate(operation.Checkpoint);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions VALUES($id,$checkpoint,$flight,$aircraft,$archived,$saved)
            ON CONFLICT(id) DO UPDATE SET checkpoint=$checkpoint,flight=$flight,aircraft=$aircraft,archived=$archived,saved=$saved
            """;
        command.Parameters.AddWithValue("$id", operation.Checkpoint.State.Session.Id.ToString("D"));
        command.Parameters.AddWithValue("$checkpoint", DispatchCheckpointCodec.Serialize(operation.Checkpoint));
        command.Parameters.AddWithValue("$flight", JsonSerializer.Serialize(operation.Flight)); command.Parameters.AddWithValue("$aircraft", operation.AircraftBinding);
        command.Parameters.AddWithValue("$archived", operation.Archived ? 1 : 0); command.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        command.CommandText = "INSERT INTO checkpoint_history(session,checkpoint,saved) VALUES($id,$checkpoint,$saved)";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (operation.Duty is { } duty) await WriteDutyAsync(connection, transaction, duty, ct).ConfigureAwait(false);
    }
    private static async Task WriteDutyAsync(SqliteConnection connection, SqliteTransaction transaction, DutyProgress duty, CancellationToken ct)
    {
        DutyTracking.Validate(duty);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO duty_sessions VALUES($id,$progress,$saved) ON CONFLICT(id) DO UPDATE SET progress=$progress,saved=$saved";
        command.Parameters.AddWithValue("$id", duty.Id.ToString("D")); command.Parameters.AddWithValue("$progress", JsonSerializer.Serialize(duty));
        command.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var sector in duty.Sectors)
        {
            command.Parameters.Clear();
            command.CommandText = "INSERT OR IGNORE INTO sector_legs VALUES($id,$duty,$session,$number)";
            command.Parameters.AddWithValue("$id", sector.Id.ToString("D")); command.Parameters.AddWithValue("$duty", duty.Id.ToString("D"));
            command.Parameters.AddWithValue("$session", sector.SessionId.ToString("D")); command.Parameters.AddWithValue("$number", sector.Number);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
    public async Task<DutyProgress?> LoadDutyAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.CommandText = "SELECT progress FROM duty_sessions ORDER BY saved DESC LIMIT 1";
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string json) return null;
            var duty = JsonSerializer.Deserialize<DutyProgress>(json) ?? throw new InvalidDataException("Saved duty is empty.");
            DutyTracking.Validate(duty); return duty;
        }
        finally { _gate.Release(); }
    }
    public async Task SaveDutyAsync(DutyProgress duty, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false); using var transaction = connection.BeginTransaction();
            await WriteDutyAsync(connection, transaction, duty, ct).ConfigureAwait(false); transaction.Commit();
        }
        finally { _gate.Release(); }
    }
    public async Task<ImmutableArray<SavedDispatch>> HistoryAsync(Guid dutyId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT s.checkpoint,s.flight,s.aircraft FROM sessions s JOIN sector_legs l ON l.session=s.id WHERE l.duty=$duty AND s.archived=1 ORDER BY l.number";
            command.Parameters.AddWithValue("$duty", dutyId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var result = ImmutableArray.CreateBuilder<SavedDispatch>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(new(DispatchCheckpointCodec.Deserialize(reader.GetString(0)),
                JsonSerializer.Deserialize<FlightPlanSnapshot>(reader.GetString(1)) ?? throw new InvalidDataException("History flight is missing."), reader.GetString(2), true));
            return result.ToImmutable();
        }
        finally { _gate.Release(); }
    }
    async Task IFlightPlanCache.SaveAsync(FlightPlanSnapshot flight, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO flight_cache VALUES(1,$flight) ON CONFLICT(id) DO UPDATE SET flight=$flight";
            command.Parameters.AddWithValue("$flight", JsonSerializer.Serialize(flight)); await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    async Task<FlightPlanSnapshot?> IFlightPlanCache.LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand(); command.CommandText = "SELECT flight FROM flight_cache WHERE id=1";
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string json ? JsonSerializer.Deserialize<FlightPlanSnapshot>(json) : null;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { _ownership?.Dispose(); _gate.Dispose(); }
}
