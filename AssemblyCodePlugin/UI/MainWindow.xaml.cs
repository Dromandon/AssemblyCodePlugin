using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AssemblyCodePlugin.Models;
using AssemblyCodePlugin.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AssemblyCodePlugin.UI
{
    public class FilterTokenViewModel
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public class RuleRowViewModel : INotifyPropertyChanged
    {
        public ClassificationRule Rule { get; }
        public ObservableCollection<FilterTokenViewModel> FilterTokens { get; } = new ObservableCollection<FilterTokenViewModel>();

        public RuleRowViewModel(ClassificationRule rule)
        {
            Rule = rule;
            AboveMatches = new ObservableCollection<string> { rule.VerifiedAboveCode ?? "— (нажмите Проверить)" };
            SelectedAboveMatch = rule.VerifiedAboveCode ?? AboveMatches.First();
            BelowMatches = new ObservableCollection<string> { rule.VerifiedBelowCode ?? "— (нажмите Проверить)" };
            SelectedBelowMatch = rule.VerifiedBelowCode ?? BelowMatches.First();
            UpdateFilterTokens();
        }

        public void UpdateFilterTokens()
        {
            FilterTokens.Clear();
            if (Rule?.RevitFilter == null) return;
            FilterTokens.Add(new FilterTokenViewModel { Label = "Кат.: ", Value = Rule.RevitFilter.CategoryDisplayName });

            var conds = Rule.RevitFilter.GetAllConditions();
            if (conds != null && conds.Count > 0)
            {
                string joinOp = "; ";
                bool first = true;
                foreach (var cond in conds)
                {
                    string prefix = first ? "; " : joinOp;
                    first = false;
                    FilterTokens.Add(new FilterTokenViewModel
                    {
                        Label = $"{prefix}{cond.ParamName} ({cond.Comparator}): ",
                        Value = cond.ValueString
                    });
                }
            }
        }

        public bool IsEnabled
        {
            get => Rule.IsEnabled;
            set { if (Rule.IsEnabled != value) { Rule.IsEnabled = value; OnPropertyChanged(); } }
        }

        public string ElementTypeName => Rule.ElementTypeName;
        public string FilterSummary => Rule.FilterSummary;
        public string SearchSummary => Rule.SearchSummary;
        public bool IsAboveActive => !Rule.SkipAboveGround;
        public bool IsBelowActive => !Rule.SkipUnderground;

        public ObservableCollection<string> AboveMatches { get; }
        public ObservableCollection<string> BelowMatches { get; }

        private readonly Dictionary<string, string> _pathToCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string _selectedAboveMatch;
        public string SelectedAboveMatch
        {
            get => _selectedAboveMatch;
            set
            {
                if (_selectedAboveMatch != value)
                {
                    _selectedAboveMatch = value;
                    Rule.VerifiedAboveCode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAboveMatchNotFound));
                    OnPropertyChanged(nameof(SelectedAbovePath));
                }
            }
        }

        private string _selectedBelowMatch;
        public string SelectedBelowMatch
        {
            get => _selectedBelowMatch;
            set
            {
                if (_selectedBelowMatch != value)
                {
                    _selectedBelowMatch = value;
                    Rule.VerifiedBelowCode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBelowMatchNotFound));
                    OnPropertyChanged(nameof(SelectedBelowPath));
                }
            }
        }

        public string SelectedAbovePath =>
            (_selectedAboveMatch != null && _pathToCodeMap.TryGetValue(_selectedAboveMatch, out var p) && !string.IsNullOrEmpty(p))
            ? $"📁 {p}" : "";

        public string SelectedBelowPath =>
            (_selectedBelowMatch != null && _pathToCodeMap.TryGetValue(_selectedBelowMatch, out var p) && !string.IsNullOrEmpty(p))
            ? $"📁 {p}" : "";

        public bool IsAboveMatchNotFound => IsAboveActive && SelectedAboveMatch != null && SelectedAboveMatch.IndexOf("Не удалось автоматически найти", StringComparison.OrdinalIgnoreCase) >= 0;
        public bool IsBelowMatchNotFound => IsBelowActive && SelectedBelowMatch != null && SelectedBelowMatch.IndexOf("Не удалось автоматически найти", StringComparison.OrdinalIgnoreCase) >= 0;

        public void SetMatches(List<AssemblyCodeItem> above, List<AssemblyCodeItem> below, IReadOnlyList<AssemblyCodeItem> allClassifierItems = null)
        {
            string prevAbove = Rule.VerifiedAboveCode;
            string prevBelow = Rule.VerifiedBelowCode;

            _pathToCodeMap.Clear();
            if (allClassifierItems != null)
            {
                foreach (var item in allClassifierItems)
                {
                    if (item == null || string.IsNullOrEmpty(item.Code)) continue;
                    string str = $"{item.Code} — {item.Description}";
                    _pathToCodeMap[str] = item.CategoryPath ?? "";
                }
            }

            AboveMatches.Clear();
            if (Rule.SkipAboveGround)
            {
                AboveMatches.Add("— Не классифицировать надземную часть —");
                SelectedAboveMatch = AboveMatches.First();
            }
            else
            {
                var aboveAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (above != null && above.Count > 0)
                {
                    foreach (var item in above)
                    {
                        string str = $"{item.Code} — {item.Description}";
                        _pathToCodeMap[str] = item.CategoryPath ?? "";
                        AboveMatches.Add(str);
                        aboveAdded.Add(item.Code);
                    }
                }
                else
                {
                    AboveMatches.Add("⚠️ Не удалось автоматически найти подходящее значение");
                }

                if (allClassifierItems != null && allClassifierItems.Count > 0)
                {
                    if (aboveAdded.Count > 0 && allClassifierItems.Count > aboveAdded.Count)
                    {
                        AboveMatches.Add("─── Все остальные позиции классификатора (рейтинг 0) ───");
                    }
                    foreach (var item in allClassifierItems)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Code)) continue;
                        if (!aboveAdded.Contains(item.Code))
                        {
                            AboveMatches.Add($"{item.Code} — {item.Description}");
                        }
                    }
                }

                string bestAbove = null;
                if (!string.IsNullOrEmpty(prevAbove) && prevAbove != "— (нажмите Проверить)" && prevAbove != "⚠️ Файл классификатора не найден")
                {
                    bestAbove = AboveMatches.FirstOrDefault(x => string.Equals(x, prevAbove, StringComparison.OrdinalIgnoreCase))
                             ?? AboveMatches.FirstOrDefault(x => x.StartsWith(prevAbove + " ", StringComparison.OrdinalIgnoreCase));
                    if (bestAbove == null)
                    {
                        AboveMatches.Insert(0, prevAbove);
                        bestAbove = prevAbove;
                    }
                }
                SelectedAboveMatch = bestAbove ?? AboveMatches.First();
            }

            BelowMatches.Clear();
            if (Rule.SkipUnderground)
            {
                BelowMatches.Add("— Не классифицировать подземную часть —");
                SelectedBelowMatch = BelowMatches.First();
            }
            else
            {
                var belowAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (below != null && below.Count > 0)
                {
                    foreach (var item in below)
                    {
                        string str = $"{item.Code} — {item.Description}";
                        _pathToCodeMap[str] = item.CategoryPath ?? "";
                        BelowMatches.Add(str);
                        belowAdded.Add(item.Code);
                    }
                }
                else
                {
                    BelowMatches.Add("⚠️ Не удалось автоматически найти подходящее значение");
                }

                if (allClassifierItems != null && allClassifierItems.Count > 0)
                {
                    if (belowAdded.Count > 0 && allClassifierItems.Count > belowAdded.Count)
                    {
                        BelowMatches.Add("─── Все остальные позиции классификатора (рейтинг 0) ───");
                    }
                    foreach (var item in allClassifierItems)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Code)) continue;
                        if (!belowAdded.Contains(item.Code))
                        {
                            BelowMatches.Add($"{item.Code} — {item.Description}");
                        }
                    }
                }

                string bestBelow = null;
                if (!string.IsNullOrEmpty(prevBelow) && prevBelow != "— (нажмите Проверить)" && prevBelow != "⚠️ Файл классификатора не найден")
                {
                    bestBelow = BelowMatches.FirstOrDefault(x => string.Equals(x, prevBelow, StringComparison.OrdinalIgnoreCase))
                             ?? BelowMatches.FirstOrDefault(x => x.StartsWith(prevBelow + " ", StringComparison.OrdinalIgnoreCase));
                    if (bestBelow == null)
                    {
                        BelowMatches.Insert(0, prevBelow);
                        bestBelow = prevBelow;
                    }
                }
                SelectedBelowMatch = bestBelow ?? BelowMatches.First();
            }

            OnPropertyChanged(nameof(IsAboveActive));
            OnPropertyChanged(nameof(IsBelowActive));
            OnPropertyChanged(nameof(SelectedAbovePath));
            OnPropertyChanged(nameof(SelectedBelowPath));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window
    {
        private readonly Document _doc;
        private readonly UIApplication _uiApp;
        private PluginSettings _settings;
        private readonly ObservableCollection<RuleRowViewModel> _rows;
        private bool _runRequested = false;
        private List<ParamInfo> _availableParams = new List<ParamInfo>();

        public bool RunRequested => _runRequested;
        public PluginSettings ResultSettings => _settings;

        public MainWindow(Document doc, UIApplication uiApp, PluginSettings settings,
            IEnumerable<string> levelNames)
        {
            InitializeComponent();
            _doc = doc;
            _uiApp = uiApp;
            _settings = settings;

            _availableParams = ParameterCollector.CollectParameters(_doc);
            if (CmbAssemblyCodeParam != null)
            {
                CmbAssemblyCodeParam.ItemsSource = _availableParams;
            }
            if (CmbAssemblyDescParam != null)
            {
                CmbAssemblyDescParam.ItemsSource = _availableParams;
            }

            _rows = new ObservableCollection<RuleRowViewModel>(
                settings.Rules.Select(r => new RuleRowViewModel(r)));
            GridRules.ItemsSource = _rows;

            // Автосохранение при закрытии окна отключено - сохранение только по кнопке "Сохранить настройки" или "ЗАПУСТИТЬ"

            foreach (var name in levelNames)
            {
                CmbLowLevel.Items.Add(name);
                CmbHighLevel.Items.Add(name);
            }

            LoadSettingsToUi(settings);
            RefreshConfigsList();

            Loaded += (s, e) =>
            {
                UpdateAllMatchesInUi();
            };
        }

        private bool _isSuppressingConfigChanged = false;

        private void RefreshConfigsList(string selectName = null)
        {
            try
            {
                _isSuppressingConfigChanged = true;
                if (CmbConfigs == null) return;
                CmbConfigs.Items.Clear();
                var list = ConfigService.GetAvailableConfigs();
                foreach (var c in list)
                    CmbConfigs.Items.Add(c);

                string target = selectName ?? ConfigService.GetActiveConfigName();
                CmbConfigs.SelectedItem = target;
                if (CmbConfigs.SelectedItem == null && CmbConfigs.Items.Count > 0)
                    CmbConfigs.SelectedIndex = 0;
            }
            finally
            {
                _isSuppressingConfigChanged = false;
            }
        }

        private void CmbConfigs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSuppressingConfigChanged || CmbConfigs.SelectedItem == null) return;
            string newConfig = CmbConfigs.SelectedItem.ToString();
            string oldConfig = ConfigService.GetActiveConfigName();
            if (newConfig == oldConfig) return;

            ConfigService.SetActiveConfigName(newConfig);
            _settings = ConfigService.LoadConfig(newConfig);

            LoadSettingsToUi(_settings);
            _rows.Clear();
            foreach (var r in _settings.Rules)
                _rows.Add(new RuleRowViewModel(r));

            UpdateAllMatchesInUi();
        }

        private void BtnNewConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Введите имя новой конфигурации:", "Новая конфигурация") { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                string name = dlg.InputText;
                SaveUiToSettings();
                ConfigService.SaveConfig(name, _settings);
                RefreshConfigsList(name);
            }
        }

        private void BtnRenameConfig_Click(object sender, RoutedEventArgs e)
        {
            string oldName = ConfigService.GetActiveConfigName();
            if (string.Equals(oldName, "По умолчанию", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Базовую конфигурацию «По умолчанию» нельзя переименовывать.", "Уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new InputDialog("Введите новое имя конфигурации:", oldName) { Owner = this };
            if (dlg.ShowDialog() == true && !string.Equals(oldName, dlg.InputText, StringComparison.OrdinalIgnoreCase))
            {
                SaveUiToSettings();
                if (ConfigService.RenameConfig(oldName, dlg.InputText))
                {
                    RefreshConfigsList(dlg.InputText);
                }
            }
        }

        private void BtnDeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            string active = ConfigService.GetActiveConfigName();
            if (string.Equals(active, "По умолчанию", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Базовую конфигурацию «По умолчанию» нельзя удалить.", "Уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"Удалить конфигурацию «{active}»?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ConfigService.DeleteConfig(active);
                string first = ConfigService.GetAvailableConfigs().FirstOrDefault() ?? "По умолчанию";
                ConfigService.SetActiveConfigName(first);
                _settings = ConfigService.LoadConfig(first);
                LoadSettingsToUi(_settings);
                _rows.Clear();
                foreach (var r in _settings.Rules)
                    _rows.Add(new RuleRowViewModel(r));
                RefreshConfigsList(first);
                UpdateAllMatchesInUi();
            }
        }

        private void BtnImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Импорт конфигурации",
                Filter = "JSON конфигурации (*.json)|*.json|Все файлы (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                var loaded = ConfigService.ImportFromFile(ofd.FileName);
                string name = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
                var dlg = new InputDialog("Имя для импортированной конфигурации:", name) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    name = dlg.InputText;
                }
                ConfigService.SaveConfig(name, loaded);
                _settings = loaded;
                LoadSettingsToUi(_settings);
                _rows.Clear();
                foreach (var r in _settings.Rules)
                    _rows.Add(new RuleRowViewModel(r));
                RefreshConfigsList(name);
                UpdateAllMatchesInUi();
            }
        }

        private void BtnExportConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveUiToSettings();
            string active = ConfigService.GetActiveConfigName();
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Экспорт конфигурации",
                Filter = "JSON конфигурации (*.json)|*.json",
                FileName = active + ".json"
            };
            if (sfd.ShowDialog() == true)
            {
                ConfigService.ExportToFile(sfd.FileName, _settings);
                MessageBox.Show($"Конфигурация «{active}» успешно экспортирована.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateAllMatchesInUi()
        {
            try
            {
                string currentClassifierFile = AssemblyCodeTableReader.GetClassifierFileName(_doc);
                bool isNewClassifier = !string.IsNullOrEmpty(currentClassifierFile) &&
                                       !string.Equals(currentClassifierFile, _settings.LastClassifierFileName, StringComparison.OrdinalIgnoreCase);

                foreach (var row in _rows)
                {
                    if (!string.IsNullOrEmpty(currentClassifierFile) && row.Rule.SavedAboveByClassifier != null &&
                        row.Rule.SavedAboveByClassifier.TryGetValue(currentClassifierFile, out var savedAbove))
                    {
                        row.Rule.VerifiedAboveCode = savedAbove;
                    }
                    else if (isNewClassifier)
                    {
                        row.Rule.VerifiedAboveCode = null;
                    }

                    if (!string.IsNullOrEmpty(currentClassifierFile) && row.Rule.SavedBelowByClassifier != null &&
                        row.Rule.SavedBelowByClassifier.TryGetValue(currentClassifierFile, out var savedBelow))
                    {
                        row.Rule.VerifiedBelowCode = savedBelow;
                    }
                    else if (isNewClassifier)
                    {
                        row.Rule.VerifiedBelowCode = null;
                    }
                }

                if (!string.IsNullOrEmpty(currentClassifierFile))
                {
                    _settings.LastClassifierFileName = currentClassifierFile;
                }

                var (classifierItems, byCode) = AssemblyCodeTableReader.ReadClassifier(_doc);
                if (classifierItems != null && classifierItems.Count > 0)
                {
                    TxtStatus.Text = $"Классификатор подключен ({classifierItems.Count} позиций). Готов к работе.";
                    foreach (var row in _rows)
                    {
                        UpdateMatchesForRow(row);
                    }
                }
                else
                {
                    TxtStatus.Text = "⚠️ Файл классификатора не найден в проекте Revit (Управление -> Код по классификатору)";
                    TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
                    foreach (var row in _rows)
                    {
                        row.AboveMatches.Clear();
                        row.AboveMatches.Add("⚠️ Файл классификатора не найден");
                        row.SelectedAboveMatch = row.AboveMatches.First();

                        row.BelowMatches.Clear();
                        row.BelowMatches.Add("⚠️ Файл классификатора не найден");
                        row.SelectedBelowMatch = row.BelowMatches.First();
                    }
                }
            }
            catch { }
        }

        private void LoadSettingsToUi(PluginSettings s)
        {
            if (s.WindowWidth > 400) Width = s.WindowWidth;
            if (s.WindowHeight > 300) Height = s.WindowHeight;
            if (s.IsWindowMaximized) WindowState = WindowState.Maximized;

            if (s.ColTypeWidth > 50 && ColType != null) ColType.Width = s.ColTypeWidth;
            if (s.ColAboveWidth > 100 && ColAbove != null) ColAbove.Width = s.ColAboveWidth;
            if (s.ColBelowWidth > 100 && ColBelow != null) ColBelow.Width = s.ColBelowWidth;

            CmbLowLevel.SelectedItem = s.ZeroLevel.LowLevelName;
            CmbHighLevel.SelectedItem = s.ZeroLevel.HighLevelName;
            TxtLowOffset.Text = s.ZeroLevel.LowLevelOffsetMm.ToString();
            TxtHighOffset.Text = s.ZeroLevel.HighLevelOffsetMm.ToString();

            if (CmbAssemblyCodeParam != null)
            {
                var match = _availableParams.FirstOrDefault(p => string.Equals(p.Name, s.AssemblyCodeParamName, StringComparison.OrdinalIgnoreCase));
                if (match != null) CmbAssemblyCodeParam.SelectedItem = match;
                else CmbAssemblyCodeParam.Text = s.AssemblyCodeParamName;
            }
            if (CmbAssemblyDescParam != null)
            {
                var matchDesc = _availableParams.FirstOrDefault(p => string.Equals(p.Name, s.AssemblyDescriptionParamName, StringComparison.OrdinalIgnoreCase));
                if (matchDesc != null) CmbAssemblyDescParam.SelectedItem = matchDesc;
                else CmbAssemblyDescParam.Text = s.AssemblyDescriptionParamName;
            }
            UpdateDescParamVisibility();

            if (ChkDisableZoneSplit != null) ChkDisableZoneSplit.IsChecked = s.DisableZoneSplit;
            UpdateZoneSplitVisibility();
        }

        private void ChkDisableZoneSplit_Click(object sender, RoutedEventArgs e)
        {
            _settings.DisableZoneSplit = ChkDisableZoneSplit.IsChecked == true;
            UpdateZoneSplitVisibility();
        }

        private void UpdateZoneSplitVisibility()
        {
            bool disabled = _settings.DisableZoneSplit;
            if (ZeroLevelSettingsPanel != null)
                ZeroLevelSettingsPanel.Visibility = disabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                
            if (BtnConfigureUndergroundId != null)
                BtnConfigureUndergroundId.Visibility = disabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                
            if (ColBelow != null)
                ColBelow.Visibility = disabled ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void CmbAssemblyCodeParam_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDescParamVisibility();
        }

        private void CmbAssemblyCodeParam_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDescParamVisibility();
        }

        private void UpdateDescParamVisibility()
        {
            if (PanelAssemblyDescParam == null || CmbAssemblyCodeParam == null) return;
            string codeParam = CmbAssemblyCodeParam.Text?.Trim() ?? "";
            bool isSystemCode = string.Equals(codeParam, "Assembly Code", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(codeParam, "Код по классификатору", StringComparison.OrdinalIgnoreCase);

            PanelAssemblyDescParam.Visibility = isSystemCode ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void BtnConfigureUndergroundId_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UndergroundIdentificationDialog(
                _availableParams,
                _settings.UndergroundParamName,
                _settings.UndergroundValueText,
                _settings.AbovegroundValueText,
                _settings.IsUndergroundParamYesNo,
                _settings.NeverAddBglSuffix
            ) { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                _settings.UndergroundParamName = dlg.ResultParamName;
                _settings.UndergroundValueText = dlg.ResultUndergroundValue;
                _settings.AbovegroundValueText = dlg.ResultAbovegroundValue;
                _settings.IsUndergroundParamYesNo = dlg.ResultIsYesNo;
                _settings.NeverAddBglSuffix = dlg.ResultNeverAddBglSuffix;
                SetStatus($"Идентификатор зоны: {_settings.UndergroundParamName} ({_settings.UndergroundValueText} / {_settings.AbovegroundValueText})");
            }
        }

        private void SaveUiToSettings()
        {
            _settings.IsWindowMaximized = (WindowState == WindowState.Maximized);
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
            if (ColType != null && ColType.ActualWidth > 50) _settings.ColTypeWidth = ColType.ActualWidth;
            if (ColAbove != null && ColAbove.ActualWidth > 100) _settings.ColAboveWidth = ColAbove.ActualWidth;
            if (ColBelow != null && ColBelow.ActualWidth > 100) _settings.ColBelowWidth = ColBelow.ActualWidth;

            string currentClassifier = AssemblyCodeTableReader.GetClassifierFileName(_doc);
            _settings.ZeroLevel.LowLevelName = CmbLowLevel.SelectedItem as string ?? "";
            _settings.ZeroLevel.HighLevelName = CmbHighLevel.SelectedItem as string ?? "";
            _settings.ZeroLevel.LowLevelOffsetMm = TryParseDouble(TxtLowOffset.Text);
            _settings.ZeroLevel.HighLevelOffsetMm = TryParseDouble(TxtHighOffset.Text);
            _settings.AssemblyCodeParamName = CmbAssemblyCodeParam?.Text?.Trim() ?? "Код по классификатору";
            _settings.AssemblyDescriptionParamName = CmbAssemblyDescParam?.Text?.Trim() ?? "";
            _settings.DisableZoneSplit = ChkDisableZoneSplit?.IsChecked == true;

            if (PanelAssemblyDescParam != null && PanelAssemblyDescParam.Visibility == System.Windows.Visibility.Visible)
            {
                _settings.AssemblyDescriptionParamName = CmbAssemblyDescParam.Text?.Trim() ?? "";
            }
            else
            {
                _settings.AssemblyDescriptionParamName = "";
            }
            _settings.LastClassifierFileName = currentClassifier;

            foreach (var row in _rows)
            {
                if (!string.IsNullOrEmpty(currentClassifier))
                {
                    if (row.Rule.SavedAboveByClassifier == null)
                        row.Rule.SavedAboveByClassifier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (row.Rule.SavedBelowByClassifier == null)
                        row.Rule.SavedBelowByClassifier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(row.Rule.VerifiedAboveCode))
                        row.Rule.SavedAboveByClassifier[currentClassifier] = row.Rule.VerifiedAboveCode;
                    if (!string.IsNullOrEmpty(row.Rule.VerifiedBelowCode))
                        row.Rule.SavedBelowByClassifier[currentClassifier] = row.Rule.VerifiedBelowCode;
                }
            }

            _settings.Rules = _rows.Select(r => r.Rule).ToList();
        }

        // ─── Автоматический пересчёт классификатора для строки ────────────────────
        private void UpdateMatchesForRow(RuleRowViewModel row)
        {
            try
            {
                var (classifierItems, byCode) = AssemblyCodeTableReader.ReadClassifier(_doc);
                if (classifierItems != null && classifierItems.Count > 0)
                {
                    var aboveCandidates = ClassifierRatingEngine.FindPositiveMatches(
                        classifierItems, byCode, row.Rule.AboveGroundSearchRule, isUnderground: false);
                    var belowCandidates = ClassifierRatingEngine.FindPositiveMatches(
                        classifierItems, byCode, row.Rule.UndergroundSearchRule, isUnderground: true);

                    row.SetMatches(aboveCandidates, belowCandidates, classifierItems);
                }
            }
            catch { }
        }

        // ─── Кнопки управления таблицей ──────────────────────────────────────────

        private bool? ConfigureAndShowRuleEditDialog(RuleEditDialog dlg)
        {
            if (_settings != null)
            {
                if (_settings.RuleEditDialogWidth > 400) dlg.Width = _settings.RuleEditDialogWidth;
                if (_settings.RuleEditDialogHeight > 300) dlg.Height = _settings.RuleEditDialogHeight;
                if (_settings.IsRuleEditDialogMaximized) dlg.WindowState = WindowState.Maximized;
            }

            bool? result = dlg.ShowDialog();

            if (_settings != null)
            {
                _settings.IsRuleEditDialogMaximized = (dlg.WindowState == WindowState.Maximized);
                if (dlg.WindowState == WindowState.Normal)
                {
                    _settings.RuleEditDialogWidth = dlg.Width;
                    _settings.RuleEditDialogHeight = dlg.Height;
                }
            }

            return result;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var newRule = new ClassificationRule { ElementTypeName = "Новый тип" };
            var dlg = new RuleEditDialog(newRule, _doc, _settings.DisableZoneSplit) { Owner = this };
            if (ConfigureAndShowRuleEditDialog(dlg) == true)
            {
                var row = new RuleRowViewModel(dlg.Result);
                _rows.Add(row);
                UpdateMatchesForRow(row);
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is RuleRowViewModel row)
            {
                int idx = _rows.IndexOf(row);
                var copyRule = row.Rule.Clone();
                var copyRow = new RuleRowViewModel(copyRule);
                _rows.Insert(idx + 1, copyRow);
                GridRules.SelectedItem = copyRow;
                UpdateMatchesForRow(copyRow);
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e) => EditSelected();
        private void GridRules_DoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

        private void EditSelected()
        {
            if (GridRules.SelectedItem is not RuleRowViewModel row) return;
            var dlg = new RuleEditDialog(row.Rule, _doc, _settings.DisableZoneSplit) { Owner = this };
            if (ConfigureAndShowRuleEditDialog(dlg) == true)
            {
                int idx = _rows.IndexOf(row);
                if (idx >= 0)
                {
                    var updatedRow = new RuleRowViewModel(dlg.Result);
                    _rows[idx] = updatedRow;
                    UpdateMatchesForRow(updatedRow);
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (GridRules.SelectedItem is RuleRowViewModel row)
                DeleteSelectedRow(row);
        }

        private void GridRules_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                if (GridRules.SelectedItem is RuleRowViewModel row)
                    DeleteSelectedRow(row);
            }
        }

        private void DeleteSelectedRow(RuleRowViewModel row)
        {
            if (row == null) return;
            int idx = _rows.IndexOf(row);
            if (idx >= 0)
            {
                _rows.RemoveAt(idx);
                if (_rows.Count > 0)
                {
                    int nextIdx = Math.Min(idx, _rows.Count - 1);
                    GridRules.SelectedIndex = nextIdx;
                    GridRules.Focus();
                }
            }
        }

        private void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridRules.SelectedIndex;
            if (idx <= 0) return;
            var item = _rows[idx];
            _rows.RemoveAt(idx);
            _rows.Insert(idx - 1, item);
            GridRules.SelectedIndex = idx - 1;
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = GridRules.SelectedIndex;
            if (idx < 0 || idx >= _rows.Count - 1) return;
            var item = _rows[idx];
            _rows.RemoveAt(idx);
            _rows.Insert(idx + 1, item);
            GridRules.SelectedIndex = idx + 1;
        }

        // ─── Сохранение и запуск ──────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveUiToSettings();
            ConfigService.Save(_settings);
            SetStatus("Настройки сохранены");
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            SaveUiToSettings();
            if (!CheckAndWarnIfRulesCollideOnType())
            {
                return;
            }
            ConfigService.Save(_settings);
            _runRequested = true;
            DialogResult = true;
            Close();
        }

        private bool CheckAndWarnIfRulesCollideOnType()
        {
            var activeRules = _settings.Rules?.Where(r => r.IsEnabled && r.RevitFilter != null).ToList();
            if (activeRules == null || activeRules.Count < 2) return true;

            var byCategory = activeRules.GroupBy(r => r.RevitFilter.TargetCategory);
            foreach (var group in byCategory)
            {
                var rulesInCat = group.ToList();
                for (int i = 0; i < rulesInCat.Count; i++)
                {
                    for (int j = i + 1; j < rulesInCat.Count; j++)
                    {
                        var r1 = rulesInCat[i];
                        var r2 = rulesInCat[j];

                        bool r1HasSuffix = r1.RevitFilter.AppendTypeSuffix && !string.IsNullOrWhiteSpace(r1.RevitFilter.TypeSuffix);
                        bool r2HasSuffix = r2.RevitFilter.AppendTypeSuffix && !string.IsNullOrWhiteSpace(r2.RevitFilter.TypeSuffix);

                        if (!r1HasSuffix && !r2HasSuffix)
                        {
                            var c1Type = r1.RevitFilter.GetAllConditions().Where(c => string.Equals(c.ParamName, "Имя семейства", StringComparison.OrdinalIgnoreCase) || string.Equals(c.ParamName, "Имя типа", StringComparison.OrdinalIgnoreCase)).Select(c => $"{c.ParamName}:{c.Comparator}:{c.ValueString}").OrderBy(s => s);
                            var c2Type = r2.RevitFilter.GetAllConditions().Where(c => string.Equals(c.ParamName, "Имя семейства", StringComparison.OrdinalIgnoreCase) || string.Equals(c.ParamName, "Имя типа", StringComparison.OrdinalIgnoreCase)).Select(c => $"{c.ParamName}:{c.Comparator}:{c.ValueString}").OrderBy(s => s);

                            bool sameTypeFilters = c1Type.SequenceEqual(c2Type);
                            bool hasInstanceFilters = r1.RevitFilter.GetAllConditions().Any(c => !string.Equals(c.ParamName, "Имя семейства", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.ParamName, "Имя типа", StringComparison.OrdinalIgnoreCase)) ||
                                                      r2.RevitFilter.GetAllConditions().Any(c => !string.Equals(c.ParamName, "Имя семейства", StringComparison.OrdinalIgnoreCase) && !string.Equals(c.ParamName, "Имя типа", StringComparison.OrdinalIgnoreCase));

                            if (sameTypeFilters && hasInstanceFilters)
                            {
                                var result = MessageBox.Show(
                                    $"⚠️ Предупреждение о коллизии правил!\n\n" +
                                    $"Правила «{r1.ElementTypeName}» и «{r2.ElementTypeName}» фильтруют одну категорию ({r1.RevitFilter.CategoryDisplayName}) и имеют одинаковые условия по типам/семействам, но различаются параметрами экземпляра.\n\n" +
                                    $"Если параметр кода по классификатору является параметром типа, элементы могут перезаписывать код друг друга.\n" +
                                    $"Рекомендуется открыть настройки этих правил и включить опцию «Добавлять суффикс к имени типа».\n\n" +
                                    $"Продолжить запуск плагина?",
                                    "Предупреждение о правилах",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Warning);
                                return result == MessageBoxResult.Yes;
                            }
                        }
                    }
                }
            }
            return true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOpenApexMessenger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://msgr.apex-soft.ru/apex-project-bureau/messages/@mikhail.davtyan",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть ссылку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenTelegram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://t.me/Dromandon",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть ссылку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetStatus(string text) => TxtStatus.Text = text;

        private static double TryParseDouble(string text) =>
            double.TryParse(text?.Trim(), out double val) ? val : 0;
    }
}
