using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssemblyCodePlugin.Services;

namespace AssemblyCodePlugin.UI
{
    public class ClassifierNode : INotifyPropertyChanged
    {
        public AssemblyCodeItem Item { get; set; }
        public ClassifierNode Parent { get; set; }
        public ObservableCollection<ClassifierNode> Children { get; set; } = new ObservableCollection<ClassifierNode>();
        public string DisplayText => Item.HasChildren ? $"🗂 {Item.Code} — {Item.Description}" : $"📄 {Item.Code} — {Item.Description}";
        
        private bool _isExpanded;
        public bool IsExpanded 
        { 
            get => _isExpanded; 
            set { _isExpanded = value; OnPropertyChanged(); } 
        }

        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { _isSelected = value; OnPropertyChanged(); } 
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ClassifierTreeDialog : Window
    {
        private List<ClassifierNode> _allNodes = new List<ClassifierNode>();
        public ObservableCollection<ClassifierNode> RootNodes { get; set; } = new ObservableCollection<ClassifierNode>();
        public ObservableCollection<ClassifierNode> RecommendedNodes { get; set; } = new ObservableCollection<ClassifierNode>();
        
        public AssemblyCodeItem SelectedItem { get; private set; }

        public ClassifierTreeDialog(List<AssemblyCodeItem> allItems, List<string> recommendedCodes, string currentSelectionCode = null)
        {
            InitializeComponent();
            
            BuildTree(allItems);
            BuildRecommended(allItems, recommendedCodes);
            
            TreeClassifier.ItemsSource = RootNodes;
            ListRecommended.ItemsSource = RecommendedNodes;
            
            if (RecommendedNodes.Count == 0)
            {
                PanelRecommended.Visibility = Visibility.Collapsed;
            }

            // Выделяем текущий элемент, если он передан
            if (!string.IsNullOrEmpty(currentSelectionCode))
            {
                // Очищаем от возможных эмодзи и описания (если была передана полная строка "⚠️ Код — Описание")
                string codeOnly = currentSelectionCode.Split(new[] { " — " }, StringSplitOptions.None)[0].Replace("⚠️", "").Trim();
                
                var targetNode = _allNodes.FirstOrDefault(n => string.Equals(n.Item.Code, codeOnly, StringComparison.OrdinalIgnoreCase));
                if (targetNode != null)
                {
                    targetNode.IsSelected = true;
                    // Раскрываем всех родителей
                    var p = targetNode.Parent;
                    while (p != null)
                    {
                        p.IsExpanded = true;
                        p = p.Parent;
                    }
                }
            }
        }

        private void BuildTree(List<AssemblyCodeItem> allItems)
        {
            var nodeDict = new Dictionary<string, ClassifierNode>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var item in allItems)
            {
                var node = new ClassifierNode { Item = item };
                nodeDict[item.Code] = node;
                _allNodes.Add(node);
            }

            foreach (var item in allItems)
            {
                var node = nodeDict[item.Code];
                if (!string.IsNullOrEmpty(item.ParentCode) && nodeDict.TryGetValue(item.ParentCode, out var parentNode))
                {
                    node.Parent = parentNode;
                    parentNode.Children.Add(node);
                }
                else
                {
                    RootNodes.Add(node);
                }
            }
        }
        
        private void BuildRecommended(List<AssemblyCodeItem> allItems, List<string> recommendedCodes)
        {
            if (recommendedCodes == null) return;
            var byCode = allItems.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
            
            foreach (var codeStr in recommendedCodes)
            {
                // Извлекаем только код из строки формата "Код — Описание"
                string code = codeStr.Split(new[] { " — " }, StringSplitOptions.None)[0].Trim();
                // Удаляем эмодзи
                code = code.Replace("⚠️", "").Trim();
                
                if (byCode.TryGetValue(code, out var item))
                {
                    RecommendedNodes.Add(new ClassifierNode { Item = item });
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = TxtSearch.Text.Trim();
            if (string.IsNullOrEmpty(q))
            {
                // Сброс фильтра
                foreach (var node in _allNodes)
                {
                    node.IsExpanded = false;
                }
                TreeClassifier.ItemsSource = RootNodes;
                return;
            }

            // Простой плоский поиск (показывать только совпадения плоским списком, чтобы не усложнять фильтрацию дерева)
            var filtered = _allNodes.Where(n => 
                n.Item.Code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Item.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            
            TreeClassifier.ItemsSource = filtered;
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
        }

        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                tvi.BringIntoView();
                e.Handled = true; // предотвращаем всплытие
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void ListRecommended_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void TreeClassifier_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            ClassifierNode selectedNode = null;
            
            if (ListRecommended.SelectedItem is ClassifierNode recNode)
            {
                selectedNode = recNode;
            }
            else if (TreeClassifier.SelectedItem is ClassifierNode treeNode)
            {
                selectedNode = treeNode;
            }

            if (selectedNode != null)
            {
                SelectedItem = selectedNode.Item;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите позицию из списка или дерева.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}