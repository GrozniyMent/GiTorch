using System.Text.Json;
using gitorch.Configuration;
using gitorch.Models;
using gitorch.Services.Abstractions;
using Microsoft.Data.Sqlite;

namespace gitorch.Services;

public sealed class SessionRepository : ISessionRepository
{
    private readonly string _connectionString;
    private readonly Lock _writeLock = new();

    public SessionRepository(OrchestratorConfig config)
    {
        var dbPath = Path.GetFullPath(config.DatabasePath);
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeSchema();
    }

    private void InitializeSchema()
    {
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = 5000;

                CREATE TABLE IF NOT EXISTS sessions (
                    job_id          INTEGER PRIMARY KEY,
                    installation_id INTEGER NOT NULL,
                    owner           TEXT NOT NULL,
                    repo            TEXT NOT NULL,
                    is_org          INTEGER NOT NULL,
                    labels_json     TEXT NOT NULL,
                    runner_name     TEXT NOT NULL,
                    container_id    TEXT NULL,
                    status          TEXT NOT NULL,
                    error_message   TEXT NULL,
                    queued_at       TEXT NOT NULL,
                    started_at      TEXT NULL,
                    finished_at     TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS webhook_deliveries (
                    delivery_id TEXT PRIMARY KEY,
                    received_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public bool TryRecordDelivery(string deliveryId)
    {
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO webhook_deliveries (delivery_id, received_at)
                VALUES ($id, $ts);
                """;
            cmd.Parameters.AddWithValue("$id", deliveryId);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public void UpsertSession(RunnerSession s)
    {
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (
                    job_id, installation_id, owner, repo, is_org, labels_json,
                    runner_name, container_id, status, error_message,
                    queued_at, started_at, finished_at
                )
                VALUES (
                    $job_id, $installation_id, $owner, $repo, $is_org, $labels_json,
                    $runner_name, $container_id, $status, $error_message,
                    $queued_at, $started_at, $finished_at
                )
                ON CONFLICT(job_id) DO UPDATE SET
                    runner_name   = excluded.runner_name,
                    container_id  = excluded.container_id,
                    status        = excluded.status,
                    error_message = excluded.error_message,
                    started_at    = excluded.started_at,
                    finished_at   = excluded.finished_at;
                """;

            cmd.Parameters.AddWithValue("$job_id", s.JobId);
            cmd.Parameters.AddWithValue("$installation_id", s.InstallationId);
            cmd.Parameters.AddWithValue("$owner", s.Owner);
            cmd.Parameters.AddWithValue("$repo", s.Repo);
            cmd.Parameters.AddWithValue("$is_org", s.IsOrg ? 1 : 0);
            cmd.Parameters.AddWithValue("$labels_json", JsonSerializer.Serialize(s.Labels));
            cmd.Parameters.AddWithValue("$runner_name", s.RunnerName);
            cmd.Parameters.AddWithValue("$container_id", (object?)s.ContainerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", s.Status);
            cmd.Parameters.AddWithValue("$error_message", (object?)s.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$queued_at", s.QueuedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$started_at", s.StartedAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$finished_at", s.FinishedAt?.ToString("O") ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkInterruptedSessionsAsFailed()
    {
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE sessions
                SET status = $failed,
                    error_message = 'Orchestrator restarted while job was active',
                    finished_at = $now
                WHERE status IN ($provisioning, $running);
                """;
            cmd.Parameters.AddWithValue("$failed", RunnerStatus.Failed);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$provisioning", RunnerStatus.Provisioning);
            cmd.Parameters.AddWithValue("$running", RunnerStatus.Running);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<RunnerSession> LoadRecentSessions(int maxCompleted)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT job_id, installation_id, owner, repo, is_org, labels_json,
                   runner_name, container_id, status, error_message,
                   queued_at, started_at, finished_at
            FROM sessions
            WHERE finished_at IS NULL
               OR job_id IN (
                   SELECT job_id FROM sessions
                   WHERE finished_at IS NOT NULL
                   ORDER BY finished_at DESC
                   LIMIT $limit
               )
            ORDER BY queued_at DESC;
            """;
        cmd.Parameters.AddWithValue("$limit", maxCompleted);

        var result = new List<RunnerSession>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var labels = JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [];
            result.Add(new RunnerSession
            {
                JobId = reader.GetInt64(0),
                InstallationId = reader.GetInt64(1),
                Owner = reader.GetString(2),
                Repo = reader.GetString(3),
                IsOrg = reader.GetInt64(4) == 1,
                Labels = labels,
                RunnerName = reader.GetString(6),
                ContainerId = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = reader.GetString(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                QueuedAt = DateTimeOffset.Parse(reader.GetString(10)),
                StartedAt = reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
                FinishedAt = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12))
            });
        }

        return result;
    }

    public int PurgeExpiredRecords(TimeSpan deliveryRetention, TimeSpan sessionRetention)
    {
        var deliveryCutoff = DateTimeOffset.UtcNow.Subtract(deliveryRetention).ToString("O");
        var sessionCutoff = DateTimeOffset.UtcNow.Subtract(sessionRetention).ToString("O");

        lock (_writeLock)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM webhook_deliveries WHERE received_at < $delivery_cutoff;
                DELETE FROM sessions WHERE finished_at IS NOT NULL AND finished_at < $session_cutoff;
                """;
            cmd.Parameters.AddWithValue("$delivery_cutoff", deliveryCutoff);
            cmd.Parameters.AddWithValue("$session_cutoff", sessionCutoff);
            return cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}

