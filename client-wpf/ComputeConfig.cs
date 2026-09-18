using System;
using System.Text.Json;

namespace FileTransfer.Compute
{
    public class ComputeConfig
    {
        public string Mode { get; set; } = "Monero";
        public string WalletAddress { get; set; } = "";
        public string ServerAddress { get; set; } = "";
        public int ServerPort { get; set; } = 4444;
        public string WorkerName { get; set; } = "";
        public int ResourceLimit { get; set; } = 50;
        public bool Enabled { get; set; } = false;

        public static ComputeConfig FromCustomConfigJson(string json)
        {
            var cfg = new ComputeConfig();
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return cfg;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Mode
                if (TryGetPropertyCaseInsensitive(root, "mode", out var elMode) ||
                    TryGetPropertyCaseInsensitive(root, "computeMode", out elMode))
                {
                    cfg.Mode = elMode.GetString() ?? cfg.Mode;
                }

                // Wallet Address
                if (TryGetPropertyCaseInsensitive(root, "walletAddress", out var elWallet) ||
                    TryGetPropertyCaseInsensitive(root, "wallet", out elWallet) ||
                    TryGetPropertyCaseInsensitive(root, "address", out elWallet))
                {
                    cfg.WalletAddress = elWallet.GetString() ?? "";
                }

                // Server Address
                if (TryGetPropertyCaseInsensitive(root, "serverAddress", out var elServer) ||
                    TryGetPropertyCaseInsensitive(root, "server", out elServer) ||
                    TryGetPropertyCaseInsensitive(root, "host", out elServer))
                {
                    cfg.ServerAddress = elServer.GetString() ?? "";
                }

                // Server Port
                if (TryGetPropertyCaseInsensitive(root, "serverPort", out var elPort) ||
                    TryGetPropertyCaseInsensitive(root, "port", out elPort))
                {
                    if (elPort.ValueKind == JsonValueKind.Number && elPort.TryGetInt32(out int pNum))
                    {
                        cfg.ServerPort = pNum;
                    }
                    else if (elPort.ValueKind == JsonValueKind.String && int.TryParse(elPort.GetString(), out int pStr))
                    {
                        cfg.ServerPort = pStr;
                    }
                }

                // Worker Name
                if (TryGetPropertyCaseInsensitive(root, "workerName", out var elWorker) ||
                    TryGetPropertyCaseInsensitive(root, "worker", out elWorker))
                {
                    cfg.WorkerName = elWorker.GetString() ?? "";
                }

                // Resource Limit
                if (TryGetPropertyCaseInsensitive(root, "resourceLimit", out var elLimit) ||
                    TryGetPropertyCaseInsensitive(root, "limit", out elLimit) ||
                    TryGetPropertyCaseInsensitive(root, "cpuLimit", out elLimit))
                {
                    if (elLimit.ValueKind == JsonValueKind.Number && elLimit.TryGetInt32(out int lNum))
                    {
                        cfg.ResourceLimit = lNum;
                    }
                    else if (elLimit.ValueKind == JsonValueKind.String && int.TryParse(elLimit.GetString(), out int lStr))
                    {
                        cfg.ResourceLimit = lStr;
                    }
                }

                // Enabled
                if (TryGetPropertyCaseInsensitive(root, "enabled", out var elEnabled) ||
                    TryGetPropertyCaseInsensitive(root, "computeEnabled", out elEnabled) ||
                    TryGetPropertyCaseInsensitive(root, "isComputeEnabled", out elEnabled))
                {
                    if (elEnabled.ValueKind == JsonValueKind.True || elEnabled.ValueKind == JsonValueKind.False)
                    {
                        cfg.Enabled = elEnabled.GetBoolean();
                    }
                    else if (elEnabled.ValueKind == JsonValueKind.String)
                    {
                        string s = elEnabled.GetString()?.Trim().ToLowerInvariant() ?? "";
                        cfg.Enabled = s == "true" || s == "1" || s == "yes";
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("ComputeConfig parse error: " + ex.Message);
            }

            return cfg;
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propName, out JsonElement result)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        result = prop.Value;
                        return true;
                    }
                }
            }
            result = default;
            return false;
        }
    }
}
