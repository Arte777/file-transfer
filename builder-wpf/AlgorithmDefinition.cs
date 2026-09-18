using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusBuilder
{
    /// <summary>
    /// Describes a single mining algorithm / coin that the Compute Module can target.
    /// </summary>
    public class AlgorithmDefinition
    {
        public string Id { get; set; } = "";              // e.g. "etc", "xmr", "kas"
        public string Name { get; set; } = "";             // Short name, e.g. "ETC"
        public string DisplayName { get; set; } = "";      // User-facing, e.g. "Ethereum Classic (ETC)"
        public string Algorithm { get; set; } = "";         // Internal algo name, e.g. "etchash", "randomx"
        public string Backend { get; set; } = "";           // Worker backend id, e.g. "lolMiner", "xmrig"
        public string WorkerExecutable { get; set; } = "";  // e.g. "lolMiner.exe", "xmrig.exe"

        // Capability flags
        public bool WalletRequired { get; set; } = true;
        public bool PoolRequired { get; set; } = true;
        public bool PortRequired { get; set; } = true;
        public bool WorkerSupported { get; set; } = true;
        public bool SupportsDualMining { get; set; } = false;

        /// <summary>
        /// Algorithm IDs that this algorithm can be paired with as a secondary in dual mining.
        /// Empty if dual mining is not supported.
        /// </summary>
        public List<string> CompatibleDualSecondaries { get; set; } = new List<string>();

        // Default connection presets (user can override)
        public string DefaultPool { get; set; } = "";
        public int DefaultPort { get; set; } = 0;
        public string WalletPlaceholder { get; set; } = "0x...";
    }

    /// <summary>
    /// Central registry of all supported algorithms.
    /// </summary>
    public static class AlgorithmRegistry
    {
        private static readonly List<AlgorithmDefinition> _algorithms = new List<AlgorithmDefinition>
        {
            new AlgorithmDefinition
            {
                Id = "etc",
                Name = "ETC",
                DisplayName = "Ethereum Classic (ETC)",
                Algorithm = "etchash",
                Backend = "lolMiner",
                WorkerExecutable = "lolMiner.exe",
                SupportsDualMining = true,
                CompatibleDualSecondaries = new List<string> { "kas", "rvn" },
                DefaultPool = "etc.2miners.com",
                DefaultPort = 1010,
                WalletPlaceholder = "0x..."
            },
            new AlgorithmDefinition
            {
                Id = "xmr",
                Name = "XMR",
                DisplayName = "Monero (XMR)",
                Algorithm = "randomx",
                Backend = "xmrig",
                WorkerExecutable = "xmrig.exe",
                SupportsDualMining = false,
                DefaultPool = "pool.supportxmr.com",
                DefaultPort = 3333,
                WalletPlaceholder = "4..."
            },
            new AlgorithmDefinition
            {
                Id = "kas",
                Name = "KAS",
                DisplayName = "Kaspa (KAS)",
                Algorithm = "karlsenhash",
                Backend = "lolMiner",
                WorkerExecutable = "lolMiner.exe",
                SupportsDualMining = true,
                CompatibleDualSecondaries = new List<string> { "etc" },
                DefaultPool = "pool.woolypooly.com",
                DefaultPort = 3112,
                WalletPlaceholder = "kaspa:..."
            },
            new AlgorithmDefinition
            {
                Id = "rvn",
                Name = "RVN",
                DisplayName = "Ravencoin (RVN)",
                Algorithm = "kawpow",
                Backend = "lolMiner",
                WorkerExecutable = "lolMiner.exe",
                SupportsDualMining = false,
                DefaultPool = "rvn.2miners.com",
                DefaultPort = 6060,
                WalletPlaceholder = "R..."
            },
            new AlgorithmDefinition
            {
                Id = "ergo",
                Name = "ERGO",
                DisplayName = "Ergo (ERG)",
                Algorithm = "autolykos2",
                Backend = "lolMiner",
                WorkerExecutable = "lolMiner.exe",
                SupportsDualMining = false,
                DefaultPool = "ergo.2miners.com",
                DefaultPort = 8888,
                WalletPlaceholder = "9f..."
            }
        };

        /// <summary>Returns all registered algorithms.</summary>
        public static IReadOnlyList<AlgorithmDefinition> All => _algorithms.AsReadOnly();

        /// <summary>Finds an algorithm by its ID.</summary>
        public static AlgorithmDefinition? GetById(string id)
        {
            return _algorithms.FirstOrDefault(a =>
                string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks whether the given primary + secondary dual-mining pair is valid.
        /// Returns (isValid, userMessage).
        /// </summary>
        public static (bool IsValid, string Message) CheckDualMiningSupport(string primaryId, string secondaryId)
        {
            if (string.IsNullOrEmpty(primaryId) || string.IsNullOrEmpty(secondaryId))
                return (false, "Не выбран алгоритм.");

            if (string.Equals(primaryId, secondaryId, StringComparison.OrdinalIgnoreCase))
                return (false, "Primary и Secondary не могут быть одним и тем же алгоритмом.");

            var primary = GetById(primaryId);
            var secondary = GetById(secondaryId);

            if (primary == null)
                return (false, $"Неизвестный Primary алгоритм: {primaryId}");
            if (secondary == null)
                return (false, $"Неизвестный Secondary алгоритм: {secondaryId}");

            if (!primary.SupportsDualMining)
                return (false, $"⚠ {primary.DisplayName} не поддерживает Dual Mining.");

            if (!secondary.SupportsDualMining)
                return (false, $"⚠ {secondary.DisplayName} не поддерживает Dual Mining.");

            // Both must use the same backend (same worker executable)
            if (!string.Equals(primary.Backend, secondary.Backend, StringComparison.OrdinalIgnoreCase))
                return (false, $"⚠ Разные backend: {primary.Backend} и {secondary.Backend}. Dual mining невозможен.");

            // Check explicit compatibility list
            if (!primary.CompatibleDualSecondaries.Any(s =>
                    string.Equals(s, secondaryId, StringComparison.OrdinalIgnoreCase)))
                return (false, $"⚠ Эта комбинация не поддерживается выбранным worker/backend.");

            return (true, $"✓ {primary.DisplayName} + {secondary.DisplayName} — совместимы.");
        }

        /// <summary>
        /// Returns the set of unique backend executables required by the given algorithm IDs.
        /// </summary>
        public static HashSet<string> GetRequiredBackends(IEnumerable<string> algorithmIds)
        {
            var backends = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in algorithmIds)
            {
                var algo = GetById(id);
                if (algo != null)
                    backends.Add(algo.WorkerExecutable);
            }
            return backends;
        }
    }
}
