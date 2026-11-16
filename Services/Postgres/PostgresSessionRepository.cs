using System.Text.Json;
using gitorch.Models;
using gitorch.Services.Abstractions;
using Npgsql;

namespace gitorch.Services.Postgres;

public sealed class PostgresSessionRepository : ISessionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSessionRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS sessions (
                job_id          BIGINT PRIMARY KEY,
                installation_id BIGINT NOT NULL,
                owner           TEXT NOT NULL,
                repo            TEXT NOT NULL,
                is_org          BOOLEAN NOT NULL,
                labels_json     TEXT NOT NULL,
                runner_name     TEXT NOT NULL,
                container_id    TEXT NULL,
                status          TEXT NOT NULL,
                error_message   TEXT NULL,
                queued_at       TIMESTAMPTZ NOT NULL,
                started_at      TIMESTAMPTZ NULL,
                finished_at     TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS webhook_deliveries (
                delivery_id TEXT PRIMARY KEY,
                received_at TIMESTAMPTZ NOT NULL
            );
            """);
        cmd.ExecuteNonQuery();
    }

    public bool TryRecordDelivery(string deliveryId)
    {
        using var cmd = _dataSource.CreateCommand("""
            INSERT INTO webhook_deliveries (delivery_id, received_at)
            VALUES ($1, $2)
            ON CONFLICT (delivery_id) DO NOTHING;
            """);
        cmd.Parameters.AddWithValue(deliveryId);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void UpsertSession(RunnerSession s)
    {
        using var cmd = _dataSource.CreateCommand("""
            INSERT INTO sessions (
                job_id, installation_id, owner, repo, is_org, labels_json,
                runner_name, container_id, status, error_message,
                queued_at, started_at, finished_at
            )
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
            ON CONFLICT (job_id) DO UPDATE SET
                runner_name   = EXCLUDED.runner_name,
                container_id  = EXCLUDED.container_id,
                status        = EXCLUDED.status,
                error_message = EXCLUDED.error_message,
                started_at    = EXCLUDED.started_at,
                finished_at   = EXCLUDED.finished_at;
            """);

        cmd.Parameters.AddWithValue(s.JobId);
        cmd.Parameters.AddWithValue(s.InstallationId);
        cmd.Parameters.AddWithValue(s.Owner);
        cmd.Parameters.AddWithValue(s.Repo);
        cmd.Parameters.AddWithValue(s.IsOrg);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(s.Labels));
        cmd.Parameters.AddWithValue(s.RunnerName);
        cmd.Parameters.AddWithValue((object?)s.ContainerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(s.Status);
        cmd.Parameters.AddWithValue((object?)s.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue(s.QueuedAt);
        cmd.Parameters.AddWithValue((object?)s.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)s.FinishedAt ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void MarkInterruptedSessionsAsFailed()
    {
        // In multi-replica HA mode, another replica may own active jobs, so only stale rows are left to retention.
    }

    public IReadOnlyList<RunnerSession> LoadRecentSessions(int maxCompleted)
    {
        using var cmd = _dataSource.CreateCommand("""
            SELECT job_id, installation_id, owner, repo, is_org, labels_json,
                   runner_name, container_id, status, error_message,
                   queued_at, started_at, finished_at
            FROM sessions
            WHERE finished_at IS NULL
               OR job_id IN (
                   SELECT job_id FROM sessions
                   WHERE finished_at IS NOT NULL
                   ORDER BY finished_at DESC
                   LIMIT $1
               )
            ORDER BY queued_at DESC;
            """);
        cmd.Parameters.AddWithValue(maxCompleted);

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
                IsOrg = reader.GetBoolean(4),
                Labels = labels,
                RunnerName = reader.GetString(6),
                ContainerId = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = reader.GetString(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                QueuedAt = reader.GetFieldValue<DateTimeOffset>(10),
                StartedAt = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                FinishedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)
            });
        }

        return result;
    }

    public int PurgeExpiredRecords(TimeSpan deliveryRetention, TimeSpan sessionRetention)
    {
        var deliveryCutoff = DateTimeOffset.UtcNow.Subtract(deliveryRetention);
        var sessionCutoff = DateTimeOffset.UtcNow.Subtract(sessionRetention);

        using var cmd = _dataSource.CreateCommand("""
            WITH d1 AS (
                DELETE FROM webhook_deliveries WHERE received_at < $1 RETURNING 1
            ),
            d2 AS (
                DELETE FROM sessions WHERE finished_at IS NOT NULL AND finished_at < $2 RETURNING 1
            )
            SELECT (SELECT COUNT(*) FROM d1) + (SELECT COUNT(*) FROM d2);
            """);
        cmd.Parameters.AddWithValue(deliveryCutoff);
        cmd.Parameters.AddWithValue(sessionCutoff);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

