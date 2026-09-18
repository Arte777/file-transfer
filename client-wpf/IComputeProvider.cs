using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    public class ProviderStatusInfo
    {
        public bool IsActive { get; set; }
        public int ProcessId { get; set; }
        public string Algorithm { get; set; } = "";
        public string PoolUrl { get; set; } = "";
        public string Worker { get; set; } = "";
        public int ResourceLimitPercent { get; set; }
        public int AllocatedCores { get; set; }
        public string Hashrate { get; set; } = "N/A";
        public int AcceptedShares { get; set; }
        public int RejectedShares { get; set; }
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        public string LastMessage { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public override string ToString()
        {
            return $"Algo: {Algorithm}, Pool: {PoolUrl}, Worker: {Worker}, PID: {ProcessId}, Limit: {ResourceLimitPercent}% ({AllocatedCores} cores), Hashrate: {Hashrate}, Shares: {AcceptedShares}/{RejectedShares}, Uptime: {Uptime:hh\\:mm\\:ss}, Msg: {LastMessage}";
        }
    }

    /// <summary>
    /// Универсальный интерфейс вычислительного провайдера.
    /// </summary>
    public interface IComputeProvider
    {
        string Name { get; }
        string Algorithm { get; }
        bool IsRunning { get; }

        (bool isValid, string error) Validate(ComputeConfig config);
        Task<bool> StartAsync(ComputeConfig config, CancellationToken cancellationToken);
        Task StopAsync();
        ProviderStatusInfo GetStatus();
    }
}
