using gitorch.Models;

namespace gitorch.Services.Abstractions;

public interface ISessionRepository
{
    bool TryRecordDelivery(string deliveryId);
    void UpsertSession(RunnerSession session);
    void MarkInterruptedSessionsAsFailed();
    IReadOnlyList<RunnerSession> LoadRecentSessions(int maxCompleted);
    int PurgeExpiredRecords(TimeSpan deliveryRetention, TimeSpan sessionRetention);
}

