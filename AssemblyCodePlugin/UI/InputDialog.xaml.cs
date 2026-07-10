using System.Windows;

namespace AssemblyCodePlugin.UI
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }

        public InputDialog(string prompt, string defaultText = "")
        {
            InitializeComponent();
            TxtPrompt.Text = prompt;
            TxtInput.Text = defaultText;
            Loaded += (s, e) =>
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            InputText = TxtInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(InputText))
            {
                MessageBox.Show("Имя конфигурации не может быть пустым.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
