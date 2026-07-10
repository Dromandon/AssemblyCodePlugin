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

        public UndergroundIdentificationDialog(
            List<ParamInfo> allParams,
            string currentParamName,
            string currentUndergroundValue,
            string currentAbovegroundValue,
            bool currentIsYesNo)
        {
            InitializeComponent();

            _allParams = allParams ?? new List<ParamInfo>();

            // Заполняем варианты Да / Нет
            var yesNoOptions = new[] { "Да (Yes)", "Нет (No)" };
            CmbUndergroundYesNo.ItemsSource = yesNoOptions;
            CmbAbovegroundYesNo.ItemsSource = yesNoOptions;

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
            CmbUndergroundYesNo.SelectedIndex = underIsTrue ? 0 : 1;
            CmbAbovegroundYesNo.SelectedIndex = underIsTrue ? 1 : 0;

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

        private void CmbUndergroundYesNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbAbovegroundYesNo == null) return;
            // Автоматически переключаем противоположное значение для надземной части
            if (CmbUndergroundYesNo.SelectedIndex == 0)
                CmbAbovegroundYesNo.SelectedIndex = 1;
            else
                CmbAbovegroundYesNo.SelectedIndex = 0;
        }

        private void UpdateInputMode(bool isYesNo)
        {
            ResultIsYesNo = isYesNo;

            if (isYesNo)
            {
                if (TxtParamTypeHint != null) TxtParamTypeHint.Text = "Тип: Логический (Да/Нет)";

                if (TxtUndergroundValue != null) TxtUndergroundValue.Visibility = Visibility.Collapsed;
                if (CmbUndergroundYesNo != null) CmbUndergroundYesNo.Visibility = Visibility.Visible;

                if (TxtAbovegroundValue != null) TxtAbovegroundValue.Visibility = Visibility.Collapsed;
                if (CmbAbovegroundYesNo != null) CmbAbovegroundYesNo.Visibility = Visibility.Visible;
            }
            else
            {
                if (TxtParamTypeHint != null) TxtParamTypeHint.Text = "Тип: Текст / Число";

                if (TxtUndergroundValue != null) TxtUndergroundValue.Visibility = Visibility.Visible;
                if (CmbUndergroundYesNo != null) CmbUndergroundYesNo.Visibility = Visibility.Collapsed;

                if (TxtAbovegroundValue != null) TxtAbovegroundValue.Visibility = Visibility.Visible;
                if (CmbAbovegroundYesNo != null) CmbAbovegroundYesNo.Visibility = Visibility.Collapsed;
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
                ResultUndergroundValue = CmbUndergroundYesNo.SelectedIndex == 0 ? "Да" : "Нет";
                ResultAbovegroundValue = CmbAbovegroundYesNo.SelectedIndex == 0 ? "Да" : "Нет";
            }
            else
            {
                ResultUndergroundValue = TxtUndergroundValue.Text?.Trim() ?? "";
                ResultAbovegroundValue = TxtAbovegroundValue.Text?.Trim() ?? "";
            }

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
