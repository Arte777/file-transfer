using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NexusBuilder
{
    public partial class ParamEditDialog : Window
    {
        public CustomProjectParam? Parameter { get; private set; }
        private readonly List<string> _existingKeys;
        private readonly bool _isEditing;
        private readonly string _origKey;

        public ParamEditDialog(CustomProjectParam? existingParam, List<string> existingKeys)
        {
            InitializeComponent();
            _existingKeys = existingKeys ?? new List<string>();
            _isEditing = existingParam != null;
            _origKey = existingParam?.Key ?? "";

            if (existingParam != null)
            {
                txtDialogTitle.Text = "Редактирование параметра";
                tbParamName.Text = existingParam.Name;
                tbParamKey.Text = existingParam.Key;
                tbParamDescription.Text = existingParam.Description;
                tbDefaultValue.Text = existingParam.DefaultValue;
                tbOptions.Text = existingParam.Options;
                chkIsRequired.IsChecked = existingParam.IsRequired;

                for (int i = 0; i < cbType.Items.Count; i++)
                {
                    if (cbType.Items[i] is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), existingParam.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        cbType.SelectedIndex = i;
                        break;
                    }
                }
            }
            else
            {
                txtDialogTitle.Text = "Новый пользовательский параметр";
                cbType.SelectedIndex = 0;
            }

            UpdateTypeVisibility();
        }

        private void CbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTypeVisibility();
        }

        private void UpdateTypeVisibility()
        {
            if (pnlOptions == null) return;
            string selectedType = (cbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "text";
            pnlOptions.Visibility = selectedType.Equals("select", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TbParamName_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-generate key from name if not editing and key wasn't manually customized
            if (!_isEditing && string.IsNullOrWhiteSpace(_origKey))
            {
                string raw = tbParamName.Text.Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    string gen = "";
                    foreach (char c in raw)
                    {
                        if (char.IsLetterOrDigit(c)) gen += c;
                        else if (c == ' ' || c == '_' || c == '-') gen += '_';
                    }
                    if (gen.Length > 0 && char.IsDigit(gen[0])) gen = "param_" + gen;
                    if (string.IsNullOrWhiteSpace(tbParamKey.Text) || tbParamKey.Tag?.ToString() == "auto")
                    {
                        tbParamKey.Text = gen;
                        tbParamKey.Tag = "auto";
                    }
                }
            }
        }

        private void TbParamKey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            tbParamKey.Tag = "custom";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = tbParamName.Text.Trim();
            string key = tbParamKey.Text.Trim();
            string desc = tbParamDescription.Text.Trim();
            string defVal = tbDefaultValue.Text.Trim();
            string options = tbOptions.Text.Trim();
            bool isReq = chkIsRequired.IsChecked == true;
            string type = (cbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "text";

            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Укажите название параметра.");
                tbParamName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                ShowError("Укажите уникальный ключ параметра.");
                tbParamKey.Focus();
                return;
            }

            if (!char.IsLetter(key[0]) && key[0] != '_')
            {
                ShowError("Ключ должен начинаться с буквы или знака подчеркивания.");
                tbParamKey.Focus();
                return;
            }

            if (key.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            {
                ShowError("Ключ может содержать только латинские буквы, цифры и знак подчеркивания.");
                tbParamKey.Focus();
                return;
            }

            if (!_origKey.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                _existingKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                ShowError($"Параметр с ключом '{key}' уже существует!");
                tbParamKey.Focus();
                return;
            }

            if (type.Equals("select", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(options))
            {
                ShowError("Для типа 'select' укажите варианты через запятую (например: normal, fast, high).");
                tbOptions.Focus();
                return;
            }

            var tempParam = new CustomProjectParam
            {
                Name = name,
                Key = key,
                Type = type,
                Description = desc,
                DefaultValue = defVal,
                Options = options,
                IsRequired = isReq
            };

            var (isValid, err) = tempParam.Validate(defVal);
            if (!isValid)
            {
                ShowError(err);
                tbDefaultValue.Focus();
                return;
            }

            Parameter = tempParam;
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            borderError.Visibility = Visibility.Visible;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
