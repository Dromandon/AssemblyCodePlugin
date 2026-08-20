using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AssemblyCodePlugin.Models;
using AssemblyCodePlugin.Services;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.UI
{
    public class BonusWordRow
    {
        public string Word { get; set; }
        public int Points { get; set; }
    }

    public class CategoryItem
    {
        public string Display { get; set; }
        public string BIC { get; set; }
        public string DisplayName { get; set; }
    }

    public partial class RuleEditDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ClassificationRule _source;
        private readonly Document _doc;
        public ClassificationRule Result { get; private set; }

        private FilterNode _rootFilterNode;

        // Коллекции тегов для Вкладки 1 (Фильтры Revit - устаревшие для совместимости)
        public ObservableCollection<string> FamilyContainsTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> FamilyNotContainsTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> TypeContainsTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> TypeNotContainsTags { get; } = new ObservableCollection<string>();

        // Коллекции тегов и баллов для Вкладки 2 (Надземная часть ▲)
        public ObservableCollection<string> AboveAnyTrueTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AboveAllTrueTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AboveExceptionsTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<BonusWordRow> AboveBonusRows { get; } = new ObservableCollection<BonusWordRow>();

        // Коллекции тегов и баллов для Вкладки 3 (Подземная часть ▼)
        public ObservableCollection<string> BelowAnyTrueTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> BelowAllTrueTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> BelowExceptionsTags { get; } = new ObservableCollection<string>();
        public ObservableCollection<BonusWordRow> BelowBonusRows { get; } = new ObservableCollection<BonusWordRow>();

        private readonly Dictionary<string, string> _previewPathToCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<string> AbovePreviewMatches { get; } = new ObservableCollection<string>();

        private string _selectedAbovePreviewMatch;
        public string SelectedAbovePreviewMatch
        {
            get => _selectedAbovePreviewMatch;
            set
            {
                if (_selectedAbovePreviewMatch != value)
                {
                    _selectedAbovePreviewMatch = value;
                    OnPropertyChanged(nameof(SelectedAbovePreviewMatch));
                    OnPropertyChanged(nameof(SelectedAbovePreviewPath));
                }
            }
        }

        public string SelectedAbovePreviewPath =>
            (_selectedAbovePreviewMatch != null && _previewPathToCodeMap.TryGetValue(_selectedAbovePreviewMatch, out var p) && !string.IsNullOrEmpty(p))
            ? $"📁 {p}" : "";

        public ObservableCollection<string> BelowPreviewMatches { get; } = new ObservableCollection<string>();

        private string _selectedBelowPreviewMatch;
        public string SelectedBelowPreviewMatch
        {
            get => _selectedBelowPreviewMatch;
            set
            {
                if (_selectedBelowPreviewMatch != value)
                {
                    _selectedBelowPreviewMatch = value;
                    OnPropertyChanged(nameof(SelectedBelowPreviewMatch));
                    OnPropertyChanged(nameof(SelectedBelowPreviewPath));
                }
            }
        }

        public string SelectedBelowPreviewPath =>
            (_selectedBelowPreviewMatch != null && _previewPathToCodeMap.TryGetValue(_selectedBelowPreviewMatch, out var p) && !string.IsNullOrEmpty(p))
            ? $"📁 {p}" : "";

        public CategoryItem[] Categories { get; private set; }
        private ICollectionView _categoryView;

        private void AddCatAndSubcats(Category cat, Dictionary<int, CategoryItem> list)
        {
            if (cat == null) return;
            int id = cat.Id.IntegerValue;
            
            if (Enum.IsDefined(typeof(BuiltInCategory), id))
            {
                if (!list.ContainsKey(id))
                {
                    string bicName = Enum.GetName(typeof(BuiltInCategory), (BuiltInCategory)id);
                    if (bicName != null && !bicName.StartsWith("INVALID"))
                    {
                        list[id] = new CategoryItem { Display = $"{bicName} — {cat.Name}", BIC = bicName, DisplayName = cat.Name };
                    }
                }
            }
            
            try
            {
                foreach (Category sub in cat.SubCategories)
                {
                    AddCatAndSubcats(sub, list);
                }
            }
            catch { }
        }

        private void InitializeCategories()
        {
            if (_doc != null)
            {
                var list = new Dictionary<int, CategoryItem>();
                foreach (Category cat in _doc.Settings.Categories)
                {
                    AddCatAndSubcats(cat, list);
                }
                
                foreach (BuiltInCategory bic in Enum.GetValues(typeof(BuiltInCategory)))
                {
                    int id = (int)bic;
                    if (!list.ContainsKey(id))
                    {
                        try
                        {
                            Category cat = Category.GetCategory(_doc, bic);
                            if (cat != null) AddCatAndSubcats(cat, list);
                        }
                        catch { }
                    }
                }

                Categories = list.Values.OrderBy(c => c.DisplayName).ToArray();
            }
            else
            {
                Categories = new[] { new CategoryItem { Display = "OST_Walls — Стены", BIC = "OST_Walls", DisplayName = "Стены" } };
            }
        }

        public RuleEditDialog(ClassificationRule rule, Document doc = null, bool disableZoneSplit = false)
        {
            InitializeComponent();
            _source = rule;
            _doc = doc;
            InitializeCategories();
            DataContext = this;
            LoadFromRule(rule);
            
            if (disableZoneSplit && TabBelowGround != null)
            {
                TabBelowGround.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void LoadFromRule(ClassificationRule r)
        {
            TxtTypeName.Text = r.ElementTypeName;
            ChkSkipAboveGround.IsChecked = r.SkipAboveGround;
            ChkSkipUnderground.IsChecked = r.SkipUnderground;

            var f = r.RevitFilter ?? new RevitElementFilterRule();
            
            _categoryView = CollectionViewSource.GetDefaultView(Categories);
            _categoryView.Filter = CategoryFilter;
            CmbCategory.ItemsSource = _categoryView;
            
            var selCat = Categories.FirstOrDefault(c => c.BIC == f.TargetCategory);
            if (selCat != null) CmbCategory.SelectedItem = selCat;
            else CmbCategory.SelectedIndex = 0;

            if (ChkAppendTypeSuffix != null) ChkAppendTypeSuffix.IsChecked = f.AppendTypeSuffix;
            if (TxtTypeSuffix != null) TxtTypeSuffix.Text = f.TypeSuffix;
            UpdateTypeSuffixPanelVisibility();

            RefreshAvailableParametersForSelectedCategory();

            // Создаем корневой узел из правила (если он пуст, метод сам создаст пустую группу)
            _rootFilterNode = f.GetEffectiveRootNode();
            
            // Если он не группа, оборачиваем в группу, чтобы в корне всегда была Группа
            if (!_rootFilterNode.IsGroup)
            {
                var newRoot = new FilterNode { IsGroup = true, LogicalOperator = "AND" };
                newRoot.Children.Add(_rootFilterNode);
                _rootFilterNode = newRoot;
            }

            var rootControl = new FilterNodeControl(_rootFilterNode, CurrentCategoryParams, isRoot: true);
            RootFilterContainer.Content = rootControl;

            var ab = r.AboveGroundSearchRule ?? new ClassifierSearchRule();
            LoadTags(AboveAnyTrueTags, ab.AnyTrue);
            LoadTags(AboveAllTrueTags, ab.AllTrue);
            LoadTags(AboveExceptionsTags, ab.Exceptions);
            LoadBonuses(AboveBonusRows, ab.ExtraTrue);

            var bel = r.UndergroundSearchRule ?? new ClassifierSearchRule();
            LoadTags(BelowAnyTrueTags, bel.AnyTrue);
            LoadTags(BelowAllTrueTags, bel.AllTrue);
            LoadTags(BelowExceptionsTags, bel.Exceptions);
            LoadBonuses(BelowBonusRows, bel.ExtraTrue);
        }

        public System.Collections.ObjectModel.ObservableCollection<string> CurrentCategoryParams { get; } = new System.Collections.ObjectModel.ObservableCollection<string>();

        private bool CategoryFilter(object item)
        {
            if (string.IsNullOrWhiteSpace(CmbCategory.Text)) return true;
            var cat = (CategoryItem)item;
            return cat.Display.IndexOf(CmbCategory.Text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CmbCategory_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = e.OriginalSource as TextBox;
            if (tb != null && CmbCategory.IsKeyboardFocusWithin)
            {
                _categoryView.Refresh();
                CmbCategory.IsDropDownOpen = true;
            }
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategory.SelectedItem != null)
                RefreshAvailableParametersForSelectedCategory();
        }

        private void RefreshAvailableParametersForSelectedCategory()
        {
            string bicStr = "OST_Walls";
            if (CmbCategory.SelectedItem is CategoryItem selCat)
            {
                bicStr = selCat.BIC;
            }
            else
            {
                var match = Categories.FirstOrDefault(c => c.Display.Equals(CmbCategory.Text, StringComparison.OrdinalIgnoreCase));
                if (match != null) bicStr = match.BIC;
            }

            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Имя типа", "Имя семейства" };

            if (_doc != null && Enum.TryParse<BuiltInCategory>(bicStr, out var bic))
            {
                try
                {
                    var types = new FilteredElementCollector(_doc).OfCategory(bic).WhereElementIsElementType().Take(15);
                    foreach (var t in types)
                    {
                        foreach (Parameter p in t.Parameters)
                        {
                            if (p.Definition != null && !string.IsNullOrWhiteSpace(p.Definition.Name))
                                list.Add(p.Definition.Name);
                        }
                    }

                    var insts = new FilteredElementCollector(_doc).OfCategory(bic).WhereElementIsNotElementType().Take(15);
                    foreach (var inst in insts)
                    {
                        foreach (Parameter p in inst.Parameters)
                        {
                            if (p.Definition != null && !string.IsNullOrWhiteSpace(p.Definition.Name))
                                list.Add(p.Definition.Name);
                        }
                    }

                    var bindingMap = _doc.ParameterBindings;
                    var it = bindingMap.ForwardIterator();
                    while (it.MoveNext())
                    {
                        if (it.Key is Definition def && !string.IsNullOrWhiteSpace(def.Name))
                            list.Add(def.Name);
                    }
                }
                catch { }
            }

            CurrentCategoryParams.Clear();
            CurrentCategoryParams.Add("Имя типа");
            CurrentCategoryParams.Add("Имя семейства");
            foreach (var p in list.Where(x => !string.Equals(x, "Имя типа", StringComparison.OrdinalIgnoreCase) && !string.Equals(x, "Имя семейства", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x))
            {
                CurrentCategoryParams.Add(p);
            }
        }

        private void LoadTags(ObservableCollection<string> col, List<string> source)
        {
            col.Clear();
            if (source != null)
            {
                foreach (var s in source)
                {
                    if (!string.IsNullOrWhiteSpace(s)) col.Add(s.Trim());
                }
            }
        }

        private void LoadBonuses(ObservableCollection<BonusWordRow> col, Dictionary<string, int> dict)
        {
            col.Clear();
            if (dict != null)
            {
                foreach (var kv in dict)
                {
                    col.Add(new BonusWordRow { Word = kv.Key, Points = kv.Value });
                }
            }
        }

        // ─── Обработчики добавления и удаления тегов ────────────────────────────────
        private void AddTag(TextBox textBox, ObservableCollection<string> targetCollection)
        {
            string val = textBox.Text?.Trim();
            if (!string.IsNullOrEmpty(val))
            {
                if (!targetCollection.Any(t => string.Equals(t, val, StringComparison.OrdinalIgnoreCase)))
                {
                    targetCollection.Add(val);
                }
                textBox.Text = "";
            }
        }

        private void RemoveTag(object sender, ObservableCollection<string> targetCollection)
        {
            if (sender is Button btn && btn.Tag is string tagText)
            {
                targetCollection.Remove(tagText);
            }
        }

        // Кнопки добавления чипов: Вкладка 2 (Надземная часть ▲)
        private void BtnAddAboveAny_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddAboveAny, AboveAnyTrueTags);
        private void TxtAddAboveAny_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddAboveAny, AboveAnyTrueTags); }
        private void RemoveAboveAny_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, AboveAnyTrueTags);

        private void BtnAddAboveAll_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddAboveAll, AboveAllTrueTags);
        private void TxtAddAboveAll_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddAboveAll, AboveAllTrueTags); }
        private void RemoveAboveAll_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, AboveAllTrueTags);

        private void BtnAddAboveExc_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddAboveExc, AboveExceptionsTags);
        private void TxtAddAboveExc_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddAboveExc, AboveExceptionsTags); }
        private void RemoveAboveExc_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, AboveExceptionsTags);

        private void BtnAddAboveBonus_Click(object sender, RoutedEventArgs e)
        {
            string word = TxtAboveBonusWord.Text?.Trim();
            if (int.TryParse(TxtAboveBonusPoints.Text?.Trim(), out int pts) && !string.IsNullOrEmpty(word))
            {
                var existing = AboveBonusRows.FirstOrDefault(r => string.Equals(r.Word, word, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Points = pts;
                else AboveBonusRows.Add(new BonusWordRow { Word = word, Points = pts });
                TxtAboveBonusWord.Text = "";
                TxtAboveBonusPoints.Text = "1";
            }
        }
        private void RemoveAboveBonus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BonusWordRow row)
                AboveBonusRows.Remove(row);
        }

        // Кнопки добавления чипов: Вкладка 3 (Подземная часть ▼)
        private void BtnAddBelowAny_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddBelowAny, BelowAnyTrueTags);
        private void TxtAddBelowAny_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddBelowAny, BelowAnyTrueTags); }
        private void RemoveBelowAny_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, BelowAnyTrueTags);

        private void BtnAddBelowAll_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddBelowAll, BelowAllTrueTags);
        private void TxtAddBelowAll_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddBelowAll, BelowAllTrueTags); }
        private void RemoveBelowAll_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, BelowAllTrueTags);

        private void BtnAddBelowExc_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddBelowExc, BelowExceptionsTags);
        private void TxtAddBelowExc_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddBelowExc, BelowExceptionsTags); }
        private void RemoveBelowExc_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, BelowExceptionsTags);

        private void BtnAddBelowBonus_Click(object sender, RoutedEventArgs e)
        {
            string word = TxtBelowBonusWord.Text?.Trim();
            if (int.TryParse(TxtBelowBonusPoints.Text?.Trim(), out int pts) && !string.IsNullOrEmpty(word))
            {
                var existing = BelowBonusRows.FirstOrDefault(r => string.Equals(r.Word, word, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Points = pts;
                else BelowBonusRows.Add(new BonusWordRow { Word = word, Points = pts });
                TxtBelowBonusWord.Text = "";
                TxtBelowBonusPoints.Text = "1";
            }
        }
        private void RemoveBelowBonus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BonusWordRow row)
                BelowBonusRows.Remove(row);
        }

        // ─── Сохранение и выход ───────────────────────────────────────────────────
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = BuildRule();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CopyRuleCollection(ObservableCollection<string> source, ObservableCollection<string> target)
        {
            target.Clear();
            foreach (var item in source)
                target.Add(item);
        }

        private void CopyBonusRows(ObservableCollection<BonusWordRow> source, ObservableCollection<BonusWordRow> target)
        {
            target.Clear();
            foreach (var item in source)
                target.Add(new BonusWordRow { Word = item.Word, Points = item.Points });
        }

        private void BtnCopyAboveToBelow_Click(object sender, RoutedEventArgs e)
        {
            CopyRuleCollection(AboveAnyTrueTags, BelowAnyTrueTags);
            CopyRuleCollection(AboveAllTrueTags, BelowAllTrueTags);
            CopyRuleCollection(AboveExceptionsTags, BelowExceptionsTags);
            CopyBonusRows(AboveBonusRows, BelowBonusRows);
        }

        private void BtnCopyBelowToAbove_Click(object sender, RoutedEventArgs e)
        {
            CopyRuleCollection(BelowAnyTrueTags, AboveAnyTrueTags);
            CopyRuleCollection(BelowAllTrueTags, AboveAllTrueTags);
            CopyRuleCollection(BelowExceptionsTags, AboveExceptionsTags);
            CopyBonusRows(BelowBonusRows, AboveBonusRows);
        }

        private void BtnHelpClassifierSearch_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ClassifierSearchHelpDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnCheckAboveMatches_Click(object sender, RoutedEventArgs e)
        {
            CheckMatchesPreview(isUnderground: false);
        }

        private void BtnCheckBelowMatches_Click(object sender, RoutedEventArgs e)
        {
            CheckMatchesPreview(isUnderground: true);
        }

        private void CheckMatchesPreview(bool isUnderground)
        {
            if (_doc == null)
            {
                MessageBox.Show("Документ Revit не подключен, невозможно прочитать классификатор.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var (classifierItems, byCode) = AssemblyCodeTableReader.ReadClassifier(_doc);
                if (classifierItems == null || classifierItems.Count == 0)
                {
                    MessageBox.Show("Файл классификатора Assembly Code не задан или пуст в документе Revit.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _previewPathToCodeMap.Clear();
                foreach (var item in classifierItems)
                {
                    if (item == null || string.IsNullOrEmpty(item.Code)) continue;
                    string str = item.HasChildren ? $"⚠️ {item.Code} — {item.Description}" : $"{item.Code} — {item.Description}";
                    _previewPathToCodeMap[str] = item.CategoryPath ?? "";
                }

                var rule = isUnderground
                    ? new ClassifierSearchRule
                    {
                        AnyTrue = BelowAnyTrueTags.ToList(),
                        AllTrue = BelowAllTrueTags.ToList(),
                        Exceptions = BelowExceptionsTags.ToList(),
                        ExtraTrue = BelowBonusRows.ToDictionary(r => r.Word, r => r.Points, StringComparer.OrdinalIgnoreCase)
                    }
                    : new ClassifierSearchRule
                    {
                        AnyTrue = AboveAnyTrueTags.ToList(),
                        AllTrue = AboveAllTrueTags.ToList(),
                        Exceptions = AboveExceptionsTags.ToList(),
                        ExtraTrue = AboveBonusRows.ToDictionary(r => r.Word, r => r.Points, StringComparer.OrdinalIgnoreCase)
                    };

                var matches = ClassifierRatingEngine.FindPositiveMatches(classifierItems, byCode, rule, isUnderground);

                var targetCollection = isUnderground ? BelowPreviewMatches : AbovePreviewMatches;
                targetCollection.Clear();

                if (matches != null && matches.Count > 0)
                {
                    foreach (var m in matches)
                    {
                        string str = m.HasChildren ? $"⚠️ {m.Code} — {m.Description}" : $"{m.Code} — {m.Description}";
                        targetCollection.Add(str);
                    }
                    if (isUnderground)
                        SelectedBelowPreviewMatch = targetCollection.First();
                    else
                        SelectedAbovePreviewMatch = targetCollection.First();
                }
                else
                {
                    targetCollection.Add("⚠️ Не удалось автоматически найти подходящее значение");
                    if (isUnderground)
                        SelectedBelowPreviewMatch = targetCollection.First();
                    else
                        SelectedAbovePreviewMatch = targetCollection.First();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при проверке классификатора:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChkAppendTypeSuffix_Click(object sender, RoutedEventArgs e)
        {
            UpdateTypeSuffixPanelVisibility();
        }

        private void UpdateTypeSuffixPanelVisibility()
        {
            if (PanelTypeSuffix != null && ChkAppendTypeSuffix != null)
            {
                PanelTypeSuffix.Visibility = (ChkAppendTypeSuffix.IsChecked == true) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        private ClassificationRule BuildRule()
        {
            string bic = "OST_Walls";
            string dispName = "";

            if (CmbCategory.SelectedItem is CategoryItem selCat)
            {
                bic = selCat.BIC;
                dispName = selCat.DisplayName;
            }
            else
            {
                var match = Categories.FirstOrDefault(c => c.Display.Equals(CmbCategory.Text, StringComparison.OrdinalIgnoreCase) || c.DisplayName.Equals(CmbCategory.Text, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    bic = match.BIC;
                    dispName = match.DisplayName;
                }
            }

            return new ClassificationRule
            {
                Id = _source.Id,
                ElementTypeName = TxtTypeName.Text?.Trim() ?? "",
                IsEnabled = true,
                ExcludeFromBglRename = _source.ExcludeFromBglRename,
                SkipAboveGround = ChkSkipAboveGround.IsChecked == true,
                SkipUnderground = ChkSkipUnderground.IsChecked == true,
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = bic,
                    CategoryDisplayName = dispName,
                    AppendTypeSuffix = ChkAppendTypeSuffix != null && ChkAppendTypeSuffix.IsChecked == true,
                    TypeSuffix = TxtTypeSuffix?.Text?.Trim() ?? "",
                    LogicalOperator = "AND", // Корневой логический оператор теперь хранится в самом RootNode
                    RootNode = _rootFilterNode?.Clone(),
                    Conditions = new List<FilterConditionRule>(),
                    FamilyNameContainsAny = FamilyContainsTags.ToList(),
                    FamilyNameNotContains = FamilyNotContainsTags.ToList(),
                    TypeNameContainsAny = TypeContainsTags.ToList(),
                    TypeNameNotContains = TypeNotContainsTags.ToList()
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = AboveAnyTrueTags.ToList(),
                    AllTrue = AboveAllTrueTags.ToList(),
                    Exceptions = AboveExceptionsTags.ToList(),
                    ExtraTrue = AboveBonusRows.ToDictionary(r => r.Word, r => r.Points, StringComparer.OrdinalIgnoreCase)
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = BelowAnyTrueTags.ToList(),
                    AllTrue = BelowAllTrueTags.ToList(),
                    Exceptions = BelowExceptionsTags.ToList(),
                    ExtraTrue = BelowBonusRows.ToDictionary(r => r.Word, r => r.Points, StringComparer.OrdinalIgnoreCase)
                }
            };
        }
    }
}
