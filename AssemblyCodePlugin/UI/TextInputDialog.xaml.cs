using System;
using System.Windows;

namespace AssemblyCodePlugin.UI
{
    public partial class TextInputDialog : Window
    {
        public string InputText { get; private set; }

        public TextInputDialog(string defaultText = "")
        {
            InitializeComponent();
            TxtInput.Text = defaultText;
            TxtInput.SelectAll();
            TxtInput.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtInput.Text))
            {
                MessageBox.Show(this, "Имя не может быть пустым", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            InputText = TxtInput.Text.Trim();
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
