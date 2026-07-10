using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssemblyCodePlugin.Models;

namespace AssemblyCodePlugin.UI
{
    public class BonusWordRow
    {
        public string Word { get; set; }
        public int Points { get; set; }
    }

    public partial class RuleEditDialog : Window
    {
        private readonly ClassificationRule _source;
        public ClassificationRule Result { get; private set; }

        // Коллекции тегов для Вкладки 1 (Фильтры Revit)
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

        private static readonly (string Display, string BIC, string DisplayName)[] Categories =
        {
            ("OST_Walls — Стены", "OST_Walls", "Стены"),
            ("OST_Floors — Перекрытия", "OST_Floors", "Перекрытия"),
            ("OST_StructuralFraming — Каркас несущих конструкций", "OST_StructuralFraming", "Каркас несущих конструкций"),
            ("OST_StructuralColumns — Несущие колонны", "OST_StructuralColumns", "Несущие колонны"),
            ("OST_GenericModel — Обобщённые модели", "OST_GenericModel", "Обобщённые модели"),
            ("OST_StructuralFoundation — Фундаменты", "OST_StructuralFoundation", "Фундаменты"),
            ("OST_EdgeSlab — Монолитный пояс (EdgeSlab)", "OST_EdgeSlab", "Монолитный пояс"),
        };

        public RuleEditDialog(ClassificationRule rule)
        {
            InitializeComponent();
            _source = rule;
            DataContext = this;
            LoadFromRule(rule);
        }

        private void LoadFromRule(ClassificationRule r)
        {
            TxtTypeName.Text = r.ElementTypeName;
            ChkSkipAboveGround.IsChecked = r.SkipAboveGround;
            ChkSkipUnderground.IsChecked = r.SkipUnderground;

            var f = r.RevitFilter ?? new RevitElementFilterRule();
            CmbCategory.SelectedIndex = Array.FindIndex(Categories, c => c.BIC == f.TargetCategory);
            if (CmbCategory.SelectedIndex < 0) CmbCategory.SelectedIndex = 0;

            LoadTags(FamilyContainsTags, f.FamilyNameContainsAny);
            LoadTags(FamilyNotContainsTags, f.FamilyNameNotContains);
            LoadTags(TypeContainsTags, f.TypeNameContainsAny);
            LoadTags(TypeNotContainsTags, f.TypeNameNotContains);

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

        // Кнопки добавления чипов: Вкладка 1
        private void BtnAddFamContains_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddFamContains, FamilyContainsTags);
        private void TxtAddFamContains_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddFamContains, FamilyContainsTags); }
        private void RemoveFamContains_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, FamilyContainsTags);

        private void BtnAddFamNotContains_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddFamNotContains, FamilyNotContainsTags);
        private void TxtAddFamNotContains_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddFamNotContains, FamilyNotContainsTags); }
        private void RemoveFamNotContains_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, FamilyNotContainsTags);

        private void BtnAddTypeContains_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddTypeContains, TypeContainsTags);
        private void TxtAddTypeContains_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddTypeContains, TypeContainsTags); }
        private void RemoveTypeContains_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, TypeContainsTags);

        private void BtnAddTypeNotContains_Click(object sender, RoutedEventArgs e) => AddTag(TxtAddTypeNotContains, TypeNotContainsTags);
        private void TxtAddTypeNotContains_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddTag(TxtAddTypeNotContains, TypeNotContainsTags); }
        private void RemoveTypeNotContains_Click(object sender, RoutedEventArgs e) => RemoveTag(sender, TypeNotContainsTags);

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

        private ClassificationRule BuildRule()
        {
            int catIdx = CmbCategory.SelectedIndex;
            string bic = catIdx >= 0 && catIdx < Categories.Length ? Categories[catIdx].BIC : "OST_Walls";
            string dispName = catIdx >= 0 && catIdx < Categories.Length ? Categories[catIdx].DisplayName : "";

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
