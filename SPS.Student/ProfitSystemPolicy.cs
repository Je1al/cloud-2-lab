using SPS.Abstractions;

namespace SPS.Student;

/// <summary>
/// Adaptive profit-oriented policy for Lab 2.
/// The policy is deliberately self-contained and depends only on SPS.Abstractions.
/// </summary>
public sealed class ProfitSystemPolicy : ISystemPolicy
{
    private readonly AdaptiveRpcPolicy rpcPolicy = new();
    private int plannedApis = 2;
    private int plannedWorkers = 2;
    private TimeSpan? lastCapacityChange;

    public IRequestPolicy RpcPolicy => rpcPolicy;

    public TimeSpan? GetProcessingBudget(RequestInfo request, Random random) => null;

    public EndpointSelectionDecision? SelectEndpoint(RoutingContext context, Random random)
    {
        if (context.Endpoints.Count == 0) return null;

        // Prefer the endpoint with the smallest normalized load.  OutstandingCalls
        // includes calls in flight, while Slots accounts for different capacities.
        var endpoint = context.Endpoints
            .OrderBy(e => (double)e.OutstandingCalls / Math.Max(1, e.Slots))
            .ThenBy(e => e.OutstandingCalls)
            .ThenByDescending(e => e.Speed)
            .ThenBy(e => e.Id)
            .First();

        return EndpointSelectionDecision.Assign(endpoint.Id);
    }

    public EndpointDecision ReviewEndpoint(EndpointContext context, Random random)
    {
        var recent = context.RecentResults
            .Where(r => !r.IsProbe)
            .OrderByDescending(r => r.Time)
            .Take(5)
            .ToArray();

        var latest = context.RecentResults
            .OrderByDescending(r => r.Time)
            .FirstOrDefault();
        if (latest is { IsProbe: true, Outcome: RequestOutcome.Success })
            return EndpointDecision.Allow();
        if (latest?.IsProbe == true && IsBadResult(latest) &&
            context.Now - latest.Time < TimeSpan.FromSeconds(2))
            return EndpointDecision.Block();

        // A short circuit prevents repeatedly sending traffic to an endpoint that
        // is currently failing, while Probe lets the simulator test recovery.
        if (recent.Length >= 2 && recent.Take(2).All(IsBadResult))
        {
            if (!context.Endpoint.ProbeOutstanding) return EndpointDecision.Probe();
            return EndpointDecision.Block();
        }

        return EndpointDecision.Allow();
    }

    public CacheReadDecision OnCacheRead(CacheReadContext context, Random random)
    {
        var entry = context.Entry;
        if (entry != null && context.Now - entry.SourceObservedAt <= context.Request.MaxDataAge)
            return CacheReadDecision.ServeCached();

        // Coalesce concurrent requests for the same key.  This is especially
        // valuable for hot keys because one storage read can pay many customers.
        var pending = context.PendingFetches
            .OrderByDescending(f => f.WaitingCustomers)
            .ThenBy(f => f.StartedAt)
            .FirstOrDefault();
        return pending != null
            ? CacheReadDecision.JoinFetch(pending.Id)
            : CacheReadDecision.StartFetch();
    }

    public CacheWriteDecision OnCacheFill(CacheFillContext context, Random random)
    {
        if (context.Value.SizeBytes > context.CapacityBytes) return CacheWriteDecision.Skip();

        // Keep data long enough to amortize storage work, but never let the cache
        // become permanently pinned. Freshness is checked per customer on delivery.
        var ttl = context.Value.Key.StartsWith("Metadata:", StringComparison.Ordinal)
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(45);

        long retained = context.Entries
            .Where(e => e.Key != context.Value.Key)
            .Sum(e => e.SizeBytes);
        var evict = new List<string>();

        // Evict the entries closest to expiration first (a deterministic and
        // effective approximation of LRU with the public cache API).
        foreach (var old in context.Entries
                     .Where(e => e.Key != context.Value.Key)
                     .OrderBy(e => e.ExpiresAt)
                     .ThenBy(e => e.SizeBytes)
                     .ThenBy(e => e.Key))
        {
            if (retained + context.Value.SizeBytes <= context.CapacityBytes) break;
            retained -= old.SizeBytes;
            evict.Add(old.Key);
        }

        return retained + context.Value.SizeBytes <= context.CapacityBytes
            ? CacheWriteDecision.Retain(ttl, evict.ToArray())
            : CacheWriteDecision.Skip();
    }

    public JobAssignmentDecision? SelectJobAssignment(JobSchedulingContext context, Random random)
    {
        if (context.QueuedJobs.Count == 0 || context.AvailableWorkers.Count == 0) return null;

        // Revenue density first, then age. This protects expensive Generate
        // requests without starving older cheap requests indefinitely.
        var job = context.QueuedJobs
            .OrderByDescending(j =>
                j.Request.Revenue / (decimal)Math.Max(.05, j.Request.EstimatedWorkSeconds) +
                (decimal)Math.Min(3, Math.Max(0, (context.Now - j.QueuedAt).TotalSeconds)) * .5m)
            .ThenBy(j => j.QueuedAt)
            .ThenBy(j => j.Id)
            .First();

        var worker = context.AvailableWorkers
            .OrderBy(w => w.AssignedJobs)
            .ThenByDescending(w => w.Speed)
            .ThenBy(w => w.Id)
            .First();

        return JobAssignmentDecision.Assign(job.Id, worker.Id);
    }

    public JobRecoveryDecision ReviewJob(JobRecoveryContext context, Random random)
    {
        var job = context.Job;
        var age = context.Now - (job.AssignedAt ?? job.QueuedAt);
        var lastHeartbeatAge = job.LastHeartbeat is { } hb
            ? context.Now - hb
            : age;

        // A healthy worker sends heartbeats every 500 ms in the public scenario.
        // Allow startup/network jitter, but recover a crashed assignment quickly.
        var suspectedDead = age >= TimeSpan.FromSeconds(4.0) &&
                            lastHeartbeatAge >= TimeSpan.FromSeconds(2.5);
        if (suspectedDead)
            return JobRecoveryDecision.RequeueAfter(TimeSpan.FromSeconds(.75));

        return JobRecoveryDecision.KeepWaiting();
    }

    public CapacityPlan ReviewCapacity(CapacityContext context, Random random)
    {
        int api = Math.Min(2, context.Settings.MaximumApis);
        int workers = Math.Min(2, context.Settings.MaximumWorkers);

        // Scale only when there is evidence of demand; idle instances are billed
        // for their complete lifetime, including startup and draining.
        if (context.PendingCalls >= 8) api = Math.Min(context.Settings.MaximumApis, 3);
        if (context.PendingCalls >= 18) api = Math.Min(context.Settings.MaximumApis, 4);
        if (context.PendingCalls >= 30) api = Math.Min(context.Settings.MaximumApis, 5);

        if (context.QueuedJobs.Count >= 1) workers = Math.Min(context.Settings.MaximumWorkers, 3);
        if (context.QueuedJobs.Count >= 4) workers = Math.Min(context.Settings.MaximumWorkers, 4);
        if (context.QueuedJobs.Count >= 8) workers = Math.Min(context.Settings.MaximumWorkers, 5);

        // Respect the shared compute-unit limit, preferably retaining workers for
        // Generate requests because they have the longest customer patience.
        while ((long)api * context.Settings.Api.ComputeUnits +
               (long)workers * context.Settings.Worker.ComputeUnits > context.Settings.ComputeUnitLimit)
        {
            if (api > 2) api--;
            else if (workers > 1) workers--;
            else break;
        }

        // Do not drain or start instances for a one-second fluctuation. Startup
        // and drain are billable, so a short cooldown improves profit materially.
        bool coolingDown = lastCapacityChange is { } changedAt &&
                           context.Now - changedAt < TimeSpan.FromSeconds(8);
        if (coolingDown)
        {
            api = Math.Max(api, plannedApis);
            workers = Math.Max(workers, plannedWorkers);
        }
        api = Math.Min(api, context.Settings.MaximumApis);
        workers = Math.Min(workers, context.Settings.MaximumWorkers);
        while ((long)api * context.Settings.Api.ComputeUnits +
               (long)workers * context.Settings.Worker.ComputeUnits > context.Settings.ComputeUnitLimit)
        {
            if (api > 2) api--;
            else if (workers > 1) workers--;
            else break;
        }
        if (api != plannedApis || workers != plannedWorkers)
        {
            plannedApis = api;
            plannedWorkers = workers;
            lastCapacityChange = context.Now;
        }
        return new CapacityPlan(plannedApis, plannedWorkers);
    }

    public void Observe(SystemObservation observation) { }

    private static bool IsBadResult(CallResultInfo result) => result.Outcome is
        RequestOutcome.Timeout or RequestOutcome.Unavailable or RequestOutcome.Rejected;

    private sealed class AdaptiveRpcPolicy : IRequestPolicy
    {
        private readonly Dictionary<long, int> attempts = new();

        public TimeSpan GetTimeout(RequestContext context, Random random)
        {
            // Long enough for the normal API/storage path; short enough to
            // recover from a lost network or crashed endpoint.
            return TimeSpan.FromMilliseconds(750);
        }

        public RetryDecision OnResult(RequestObservation observation, Random random)
        {
            int attempt = attempts.GetValueOrDefault(observation.RequestId) + 1;
            attempts[observation.RequestId] = attempt;

            if (observation.Outcome == RequestOutcome.Success)
            {
                attempts.Remove(observation.RequestId);
                return RetryDecision.NoRetry();
            }

            bool transient = observation.Outcome is RequestOutcome.Timeout or
                RequestOutcome.Unavailable or RequestOutcome.Rejected;
            if (!transient || attempt >= 3)
            {
                attempts.Remove(observation.RequestId);
                return RetryDecision.NoRetry();
            }

            var delay = attempt == 1
                ? TimeSpan.FromMilliseconds(120)
                : TimeSpan.FromMilliseconds(260);
            return RetryDecision.RetryAfter(delay);
        }
    }
}
