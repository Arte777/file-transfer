using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexusBuilder
{
    /// <summary>
    /// Configuration for a single mining endpoint (Primary or Secondary).
    /// </summary>
    public class ComputeEngineEndpoint
    {
        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "";

        [JsonPropertyName("wallet")]
        public string Wallet { get; set; } = "";

        [JsonPropertyName("pool")]
        public string Pool { get; set; } = "";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 0;

        [JsonPropertyName("worker")]
        public string Worker { get; set; } = "";

        public ComputeEngineEndpoint Clone()
        {
            return new ComputeEngineEndpoint
            {
                Algorithm = Algorithm,
                Wallet = Wallet,
                Pool = Pool,
                Port = Port,
                Worker = Worker
            };
        }
    }

    /// <summary>
    /// Configuration for a single Compute Module instance.
    /// </summary>
    public class ComputeModuleConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Compute Module";

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "single"; // "single" or "dual"

        [JsonPropertyName("primary")]
        public ComputeEngineEndpoint Primary { get; set; } = new ComputeEngineEndpoint();

        [JsonPropertyName("secondary")]
        public ComputeEngineEndpoint Secondary { get; set; } = new ComputeEngineEndpoint();

        [JsonPropertyName("resourceLimit")]
        public int ResourceLimit { get; set; } = 30;

        public bool IsDualMode => string.Equals(Mode, "dual", StringComparison.OrdinalIgnoreCase);

        /// <summary>Creates a deep copy of this module config.</summary>
        public ComputeModuleConfig Clone()
        {
            return new ComputeModuleConfig
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8), // New unique ID
                Name = Name + " (копия)",
                Enabled = Enabled,
                Mode = Mode,
                Primary = Primary.Clone(),
                Secondary = Secondary.Clone(),
                ResourceLimit = ResourceLimit
            };
        }

        /// <summary>Swaps Primary and Secondary endpoints.</summary>
        public void SwapEndpoints()
        {
            var temp = Primary;
            Primary = Secondary;
            Secondary = temp;
        }

        /// <summary>
        /// Validates this module configuration.
        /// Returns (isValid, list of error messages).
        /// </summary>
        public (bool IsValid, List<string> Errors) Validate(int moduleIndex)
        {
            var errors = new List<string>();
            string prefix = $"Compute Module #{moduleIndex + 1}";

            // Validate Primary
            ValidateEndpoint(Primary, "Primary", prefix, errors);

            // Validate Secondary if dual mode
            if (IsDualMode)
            {
                ValidateEndpoint(Secondary, "Secondary", prefix, errors);

                // Check dual-mining compatibility
                if (!string.IsNullOrEmpty(Primary.Algorithm) && !string.IsNullOrEmpty(Secondary.Algorithm))
                {
                    var (isValid, message) = AlgorithmRegistry.CheckDualMiningSupport(
                        Primary.Algorithm, Secondary.Algorithm);
                    if (!isValid)
                        errors.Add($"{prefix}: {message}");
                }
            }

            // ResourceLimit
            if (ResourceLimit < 1 || ResourceLimit > 100)
                errors.Add($"{prefix}: Resource Limit должен быть от 1 до 100%.");

            return (errors.Count == 0, errors);
        }

        private void ValidateEndpoint(ComputeEngineEndpoint ep, string endpointName, string prefix, List<string> errors)
        {
            var algo = AlgorithmRegistry.GetById(ep.Algorithm);

            if (string.IsNullOrWhiteSpace(ep.Algorithm))
            {
                errors.Add($"{prefix}: Не выбран алгоритм {endpointName}.");
                return; // Can't validate further without algorithm
            }

            if (algo == null)
            {
                errors.Add($"{prefix}: Неизвестный алгоритм {endpointName}: {ep.Algorithm}.");
                return;
            }

            if (algo.WalletRequired && string.IsNullOrWhiteSpace(ep.Wallet))
                errors.Add($"{prefix}: Не указан кошелёк {endpointName}.");

            if (algo.PoolRequired && string.IsNullOrWhiteSpace(ep.Pool))
                errors.Add($"{prefix}: Не указан пул {endpointName}.");

            if (algo.PortRequired)
            {
                if (ep.Port < 1 || ep.Port > 65535)
                    errors.Add($"{prefix}: Порт {endpointName} должен быть от 1 до 65535.");
            }
        }

        /// <summary>
        /// Returns the set of algorithm IDs used by this module (1 for single, up to 2 for dual).
        /// </summary>
        public IEnumerable<string> GetUsedAlgorithmIds()
        {
            if (!string.IsNullOrEmpty(Primary.Algorithm))
                yield return Primary.Algorithm;
            if (IsDualMode && !string.IsNullOrEmpty(Secondary.Algorithm))
                yield return Secondary.Algorithm;
        }
    }

    /// <summary>
    /// Root configuration object for the Compute system.
    /// </summary>
    public class ComputeRootConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("modules")]
        public List<ComputeModuleConfig> Modules { get; set; } = new List<ComputeModuleConfig>();

        /// <summary>
        /// Serializes this config to a Dictionary suitable for JSON embedding in build output.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var modulesList = new List<object>();
            foreach (var mod in Modules)
            {
                var modDict = new Dictionary<string, object>
                {
                    ["id"] = mod.Id,
                    ["name"] = mod.Name,
                    ["enabled"] = mod.Enabled,
                    ["mode"] = mod.Mode,
                    ["primary"] = new Dictionary<string, object>
                    {
                        ["algorithm"] = mod.Primary.Algorithm,
                        ["wallet"] = mod.Primary.Wallet,
                        ["pool"] = mod.Primary.Pool,
                        ["port"] = mod.Primary.Port,
                        ["worker"] = mod.Primary.Worker
                    },
                    ["resourceLimit"] = mod.ResourceLimit
                };

                if (mod.IsDualMode)
                {
                    modDict["secondary"] = new Dictionary<string, object>
                    {
                        ["algorithm"] = mod.Secondary.Algorithm,
                        ["wallet"] = mod.Secondary.Wallet,
                        ["pool"] = mod.Secondary.Pool,
                        ["port"] = mod.Secondary.Port,
                        ["worker"] = mod.Secondary.Worker
                    };
                }

                modulesList.Add(modDict);
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = Enabled,
                ["modules"] = modulesList
            };
        }

        /// <summary>
        /// Migrates legacy 7-key configuration to the new module format.
        /// </summary>
        public static ComputeRootConfig MigrateFromLegacy(Dictionary<string, object> legacyKeys)
        {
            var config = new ComputeRootConfig();

            // Try to extract legacy keys
            string mode = GetStringValue(legacyKeys, "mode", "etc");
            string wallet = GetStringValue(legacyKeys, "walletAddress", "");
            string pool = GetStringValue(legacyKeys, "serverAddress", "");
            int port = GetIntValue(legacyKeys, "serverPort", 0);
            string worker = GetStringValue(legacyKeys, "workerName", "");
            int resourceLimit = GetIntValue(legacyKeys, "resourceLimit", 30);
            bool enabled = GetBoolValue(legacyKeys, "enabled", false);

            config.Enabled = enabled;

            // Map legacy mode to algorithm ID
            string algorithmId = MapLegacyModeToAlgorithmId(mode);

            var module = new ComputeModuleConfig
            {
                Name = "Compute Module #1",
                Enabled = true,
                Mode = "single",
                Primary = new ComputeEngineEndpoint
                {
                    Algorithm = algorithmId,
                    Wallet = wallet,
                    Pool = pool,
                    Port = port,
                    Worker = worker
                },
                ResourceLimit = resourceLimit
            };

            config.Modules.Add(module);
            return config;
        }

        private static string MapLegacyModeToAlgorithmId(string legacyMode)
        {
            switch (legacyMode?.ToLowerInvariant())
            {
                case "etc":
                case "ethereum_classic":
                    return "etc";
                case "xmr":
                case "monero":
                    return "xmr";
                case "kas":
                case "kaspa":
                    return "kas";
                case "rvn":
                case "ravencoin":
                    return "rvn";
                case "ergo":
                    return "ergo";
                default:
                    return "etc"; // Default fallback
            }
        }

        private static string GetStringValue(Dictionary<string, object> dict, string key, string defaultValue)
        {
            if (dict.TryGetValue(key, out var val))
            {
                if (val is JsonElement je)
                    return je.ValueKind == JsonValueKind.String ? je.GetString() ?? defaultValue : je.ToString();
                return val?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        private static int GetIntValue(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (dict.TryGetValue(key, out var val))
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out int n)) return n;
                    if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out int ns)) return ns;
                }
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (int.TryParse(val?.ToString(), out int parsed)) return parsed;
            }
            return defaultValue;
        }

        private static bool GetBoolValue(Dictionary<string, object> dict, string key, bool defaultValue)
        {
            if (dict.TryGetValue(key, out var val))
            {
                if (val is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.True) return true;
                    if (je.ValueKind == JsonValueKind.False) return false;
                }
                if (val is bool b) return b;
                if (bool.TryParse(val?.ToString(), out bool parsed)) return parsed;
            }
            return defaultValue;
        }
    }
}
