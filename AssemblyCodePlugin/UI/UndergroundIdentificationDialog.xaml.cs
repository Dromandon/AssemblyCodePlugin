using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AssemblyCodePlugin.Services;

namespace AssemblyCodePlugin.UI
{
    public partial class UndergroundIdentificationDialog : Window
    {
        private readonly List<ParamInfo> _allParams;

        public string ResultParamName { get; private set; }
        public string ResultUndergroundValue { get; private set; }
        public string ResultAbovegroundValue { get; private set; }
        public bool ResultIsYesNo { get; private set; }
        public bool ResultNeverAddBglSuffix { get; private set; }

        public UndergroundIdentificationDialog(
            List<ParamInfo> allParams,
            string currentParamName,
            string currentUndergroundValue,
            string currentAbovegroundValue,
            bool currentIsYesNo,
            bool currentNeverAddBglSuffix)
        {
            InitializeComponent();

            _allParams = allParams ?? new List<ParamInfo>();



            // Заполняем список параметров
            CmbParamName.ItemsSource = _allParams;

            // Ищем текущий параметр или вставляем его текст
            var match = _allParams.FirstOrDefault(p => string.Equals(p.Name, currentParamName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                CmbParamName.SelectedItem = match;
            }
            else
            {
                CmbParamName.Text = currentParamName;
            }

            // Устанавливаем текущий режим и значения
            ResultIsYesNo = currentIsYesNo || (match != null && match.IsYesNo);

            TxtUndergroundValue.Text = currentUndergroundValue ?? "Подземная часть";
            TxtAbovegroundValue.Text = currentAbovegroundValue ?? "Надземная часть";

            bool underIsTrue = IsYesValue(currentUndergroundValue);
            ChkUndergroundYesNo.IsChecked = underIsTrue;
            ChkAbovegroundYesNo.IsChecked = !underIsTrue;

            if (ChkNeverAddBglSuffix != null)
                ChkNeverAddBglSuffix.IsChecked = currentNeverAddBglSuffix;

            UpdateInputMode(ResultIsYesNo);
        }

        private static bool IsYesValue(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return true;
            val = val.Trim();
            return string.Equals(val, "Да", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(val, "Да (Yes)", StringComparison.OrdinalIgnoreCase);
        }

        private void CmbParamName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbParamName.SelectedItem is ParamInfo info)
            {
                UpdateInputMode(info.IsYesNo);
            }
        }

        private void CmbParamName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = CmbParamName.Text?.Trim() ?? "";
            var info = _allParams.FirstOrDefault(p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase));
            if (info != null)
            {
                UpdateInputMode(info.IsYesNo);
            }
        }

        private void ChkUndergroundYesNo_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkAbovegroundYesNo == null || ChkUndergroundYesNo == null) return;
            // Автоматически переключаем противоположное значение для надземной части
            ChkAbovegroundYesNo.IsChecked = !(ChkUndergroundYesNo.IsChecked == true);
        }

        private void UpdateInputMode(bool isYesNo)
        {
            ResultIsYesNo = isYesNo;

            if (isYesNo)
            {
                if (TxtParamTypeHint != null) TxtParamTypeHint.Text = "Тип: Логический (Да/Нет)";

                if (TxtUndergroundValue != null) TxtUndergroundValue.Visibility = Visibility.Collapsed;
                if (ChkUndergroundYesNo != null) ChkUndergroundYesNo.Visibility = Visibility.Visible;

                if (TxtAbovegroundValue != null) TxtAbovegroundValue.Visibility = Visibility.Collapsed;
                if (ChkAbovegroundYesNo != null) ChkAbovegroundYesNo.Visibility = Visibility.Visible;
            }
            else
            {
                if (TxtParamTypeHint != null) TxtParamTypeHint.Text = "Тип: Текст / Число";

                if (TxtUndergroundValue != null) TxtUndergroundValue.Visibility = Visibility.Visible;
                if (ChkUndergroundYesNo != null) ChkUndergroundYesNo.Visibility = Visibility.Collapsed;

                if (TxtAbovegroundValue != null) TxtAbovegroundValue.Visibility = Visibility.Visible;
                if (ChkAbovegroundYesNo != null) ChkAbovegroundYesNo.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ResultParamName = CmbParamName.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(ResultParamName))
            {
                MessageBox.Show("Пожалуйста, укажите или выберите параметр идентификатора.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ResultIsYesNo)
            {
                ResultUndergroundValue = ChkUndergroundYesNo.IsChecked == true ? "Да" : "Нет";
                ResultAbovegroundValue = ChkAbovegroundYesNo.IsChecked == true ? "Да" : "Нет";
            }
            else
            {
                ResultUndergroundValue = TxtUndergroundValue.Text?.Trim() ?? "";
                ResultAbovegroundValue = TxtAbovegroundValue.Text?.Trim() ?? "";
            }

            if (ChkNeverAddBglSuffix != null)
                ResultNeverAddBglSuffix = ChkNeverAddBglSuffix.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
