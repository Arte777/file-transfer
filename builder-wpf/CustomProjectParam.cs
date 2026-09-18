using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace NexusBuilder
{
    public class CustomProjectParam : INotifyPropertyChanged
    {
        private string _name = "";
        private string _key = "";
        private string _type = "text"; // "text", "number", "boolean", "select"
        private string _defaultValue = "";
        private string _options = ""; // For "select" type: comma-separated values
        private string _description = "";
        private bool _isRequired = false;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
        }

        public string Key
        {
            get => _key;
            set { if (_key != value) { _key = value; OnPropertyChanged(nameof(Key)); } }
        }

        public string Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(nameof(Type)); } }
        }

        public string DefaultValue
        {
            get => _defaultValue;
            set { if (_defaultValue != value) { _defaultValue = value; OnPropertyChanged(nameof(DefaultValue)); } }
        }

        public string Options
        {
            get => _options;
            set { if (_options != value) { _options = value; OnPropertyChanged(nameof(Options)); } }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(nameof(Description)); } }
        }

        public bool IsRequired
        {
            get => _isRequired;
            set { if (_isRequired != value) { _isRequired = value; OnPropertyChanged(nameof(IsRequired)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        public (bool isValid, string error) Validate(string? val = null)
        {
            string v = val ?? DefaultValue ?? "";
            if (IsRequired && string.IsNullOrWhiteSpace(v))
            {
                return (false, $"Параметр '{Name}' ({Key}) обязателен для заполнения.");
            }

            if (!string.IsNullOrWhiteSpace(v))
            {
                switch (Type.ToLowerInvariant())
                {
                    case "number":
                        if (!double.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                        {
                            return (false, $"Значение параметра '{Name}' ({Key}) должно быть числом.");
                        }
                        break;

                    case "boolean":
                        string vLower = v.Trim().ToLowerInvariant();
                        if (vLower != "true" && vLower != "false" && vLower != "1" && vLower != "0")
                        {
                            return (false, $"Значение параметра '{Name}' ({Key}) должно быть логическим (true / false).");
                        }
                        break;

                    case "select":
                        var opts = Options.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(o => o.Trim())
                                          .ToList();
                        if (opts.Count > 0 && !opts.Contains(v.Trim(), StringComparer.OrdinalIgnoreCase))
                        {
                            return (false, $"Значение '{v}' параметра '{Name}' отсутствует в списке допустимых опций: {string.Join(", ", opts)}");
                        }
                        break;
                }
            }

            return (true, "");
        }

        public object? GetTypedValue(string? val = null)
        {
            string v = val ?? DefaultValue ?? "";
            switch (Type.ToLowerInvariant())
            {
                case "number":
                    if (double.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double num))
                    {
                        if (num == Math.Floor(num) && num >= long.MinValue && num <= long.MaxValue)
                        {
                            return (long)num;
                        }
                        return num;
                    }
                    return 0;

                case "boolean":
                    string vLower = v.Trim().ToLowerInvariant();
                    return vLower == "true" || vLower == "1";

                default:
                    return v;
            }
        }
    }
}
