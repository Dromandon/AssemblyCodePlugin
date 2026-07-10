using System.Windows;

namespace AssemblyCodePlugin.UI
{
    public partial class ClassifierSearchHelpDialog : Window
    {
        public ClassifierSearchHelpDialog()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
