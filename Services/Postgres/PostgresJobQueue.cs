using System.Runtime.CompilerServices;
using System.Text.Json;
using gitorch.Models;
using gitorch.Services.Abstractions;
using Npgsql;

namespace gitorch.Services.Postgres;

public sealed class PostgresJobQueue : IJobQueue
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _nodeId = $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
    private readonly SemaphoreSlim _localSignal = new(0);
    private volatile bool _completed;

    public PostgresJobQueue(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS job_queue (
                job_id       BIGINT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                created_at   TIMESTAMPTZ NOT NULL,
                claimed_at   TIMESTAMPTZ NULL,
                claimed_by   TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_job_queue_unclaimed
                ON job_queue (created_at) WHERE claimed_at IS NULL;
            """);
        cmd.ExecuteNonQuery();
    }

    public async ValueTask<bool> EnqueueAsync(JobRequest job, CancellationToken ct = default)
    {
        if (_completed)
            return false;

        await using var cmd = _dataSource.CreateCommand("""
            INSERT INTO job_queue (job_id, payload_json, created_at)
            VALUES ($1, $2, $3)
            ON CONFLICT (job_id) DO NOTHING;
            """);
        cmd.Parameters.AddWithValue(job.JobId);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(job));
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);

        _localSignal.Release();
        return true;
    }

    public async IAsyncEnumerable<JobRequest> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (!_completed && !ct.IsCancellationRequested)
        {
            var claimed = await TryClaimNextAsync(ct);
            if (claimed != null)
            {
                yield return claimed;
                continue;
            }

            try
            {
                await _localSignal.WaitAsync(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    public void Complete()
    {
        _completed = true;
        _localSignal.Release();
    }

    private async Task<JobRequest?> TryClaimNextAsync(CancellationToken ct)
    {
        await using var cmd = _dataSource.CreateCommand("""
            UPDATE job_queue
            SET claimed_at = NOW(), claimed_by = $1
            WHERE job_id = (
                SELECT job_id
                FROM job_queue
                WHERE claimed_at IS NULL
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            RETURNING payload_json;
            """);
        cmd.Parameters.AddWithValue(_nodeId);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        return json == null ? null : JsonSerializer.Deserialize<JobRequest>(json);
    }
}

