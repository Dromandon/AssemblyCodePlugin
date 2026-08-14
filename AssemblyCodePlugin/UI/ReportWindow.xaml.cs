using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AssemblyCodePlugin.Models;
using AssemblyCodePlugin.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AssemblyCodePlugin.UI
{
    public class FilterValue : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; }
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); } 
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public partial class ReportWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly ProcessingReport _report;
        private readonly RevitActionHandler _handler;
        private readonly ExternalEvent _exEvent;

        private bool _isHighlightActive = false;

        private Dictionary<string, HashSet<string>> _uncheckedFilters = new Dictionary<string, HashSet<string>>();
        private string _currentFilterColumn = "";
        private DataGrid _currentGrid;

        public ReportWindow(UIApplication uiApp, ProcessingReport report)
        {
            InitializeComponent();
            _uiApp = uiApp;
            _report = report;

            _handler = new RevitActionHandler();
            _exEvent = ExternalEvent.Create(_handler);

            Topmost = true;
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = _uiApp.MainWindowHandle;
            }
            catch { }

            DataContext = this;

            LoadReportData();
            LoadFilters();

            if (_uncheckedFilters.Count > 0)
            {
                ApplyFilters();
            }

            this.Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateFilterButtonsVisualState()), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            Closed += ReportWindow_Closed;
        }

        private void LoadReportData()
        {
            TxtSummaryTitle.Text = $"⏱ Время выполнения: {_report.ZoneCalcSeconds + _report.TypeChangeSeconds + _report.ParamSetSeconds:F2} с";
            TxtSummaryDetails.Text = $"Обработано элементов: {_report.TotalElements} | Обновлено: {_report.UpdatedElements} | Новых типов: {_report.CreatedTypes}";

            TabUnassigned.Header = $"❌ Без кода ({_report.UnassignedElementItems.Count})";
            GridUnassigned.ItemsSource = _report.UnassignedElementItems;

            TabSpanning.Header = $"⚠️ Пересекают ноль ({_report.SpanningElementItems.Count})";
            GridSpanning.ItemsSource = _report.SpanningElementItems;

            if (TabGrouped != null && GridGrouped != null)
            {
                TabGrouped.Header = $"📦 В группах ({_report.GroupedElementItems.Count})";
                GridGrouped.ItemsSource = _report.GroupedElementItems;
            }

            // Если есть элементы без кода, открываем первую вкладку, иначе если есть пересекающие ноль - вторую, иначе в группах
            if (_report.UnassignedElementItems.Count > 0)
                TabsMain.SelectedItem = TabUnassigned;
            else if (_report.SpanningElementItems.Count > 0)
                TabsMain.SelectedItem = TabSpanning;
            else if (_report.GroupedElementItems.Count > 0 && TabGrouped != null)
                TabsMain.SelectedItem = TabGrouped;
        }

        private string GetFiltersPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Apex", "AssemblyCodeReportFilters.txt");
        }

        private void LoadFilters()
        {
            try
            {
                string path = GetFiltersPath();
                if (!System.IO.File.Exists(path)) return;

                var lines = System.IO.File.ReadAllLines(path);
                if (lines.Length > 0 && bool.TryParse(lines[0], out bool remember))
                {
                    ChkRememberFilters.IsChecked = remember;
                    if (remember && lines.Length > 1)
                    {
                        var filterSettings = lines[1];
                        if (!string.IsNullOrWhiteSpace(filterSettings))
                        {
                            var entries = filterSettings.Split('|');
                            foreach (var entry in entries)
                            {
                                var parts = entry.Split(':');
                                if (parts.Length == 2)
                                {
                                    var col = parts[0];
                                    var vals = parts[1].Split(',').ToHashSet();
                                    _uncheckedFilters[col] = vals;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveFilters()
        {
            try
            {
                string path = GetFiltersPath();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                bool remember = ChkRememberFilters.IsChecked == true;
                string line1 = remember.ToString();
                string line2 = "";
                if (remember)
                {
                    var entries = _uncheckedFilters.Select(kvp => kvp.Key + ":" + string.Join(",", kvp.Value));
                    line2 = string.Join("|", entries);
                }
                System.IO.File.WriteAllLines(path, new[] { line1, line2 });
            }
            catch { }
        }

        private void ChkRememberFilters_Click(object sender, RoutedEventArgs e)
        {
            SaveFilters();
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var header = btn.Tag as System.Windows.Controls.Primitives.DataGridColumnHeader;
            _currentFilterColumn = header.Content.ToString();
            
            _currentGrid = FindParent<DataGrid>(btn);
            if (_currentGrid == null) return;

            var itemsSource = _currentGrid.ItemsSource as IEnumerable<ReportElementItem>;
            if (itemsSource == null) return;

            var allItems = itemsSource.ToList();
            var rawValues = allItems.Select(x => GetValueByColName(x, _currentFilterColumn)).ToList();
            
            var uniqueValues = new HashSet<string>();
            foreach(var val in rawValues)
            {
                uniqueValues.Add(string.IsNullOrEmpty(val) ? "(Пусто)" : val);
            }

            var sortedVals = uniqueValues.OrderBy(x => x).ToList();
            var filterList = sortedVals.Select(v => new FilterValue 
            { 
                Name = v, 
                IsSelected = _uncheckedFilters.ContainsKey(_currentFilterColumn) ? !_uncheckedFilters[_currentFilterColumn].Contains(v) : true 
            }).ToList();
            
            FilterListBox.ItemsSource = filterList;
            SelectAllCheckBox.IsChecked = filterList.All(x => x.IsSelected);
            
            FilterPopup.PlacementTarget = btn;
            FilterPopup.IsOpen = true;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) 
        { 
            bool isChecked = SelectAllCheckBox.IsChecked ?? false; 
            foreach (FilterValue item in FilterListBox.ItemsSource) item.IsSelected = isChecked; 
            UpdateFiltersFromList(); 
        }
        
        private void FilterCheckBox_Click(object sender, RoutedEventArgs e) => UpdateFiltersFromList();
        
        private void UpdateFiltersFromList()
        {
            var unselected = FilterListBox.ItemsSource.Cast<FilterValue>()
                .Where(x => !x.IsSelected)
                .Select(x => x.Name)
                .ToHashSet();

            if (unselected.Count == 0) _uncheckedFilters.Remove(_currentFilterColumn);
            else _uncheckedFilters[_currentFilterColumn] = unselected;
            
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            ApplyFilterToGrid(GridUnassigned);
            ApplyFilterToGrid(GridSpanning);
            if (GridGrouped != null) ApplyFilterToGrid(GridGrouped);
            UpdateFilterButtonsVisualState();
        }

        private void ApplyFilterToGrid(DataGrid grid)
        {
            if (grid.ItemsSource == null) return;
            System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (view == null) return;

            view.Filter = (obj) =>
            {
                var item = obj as ReportElementItem;
                if (item == null) return false;
                foreach (var filter in _uncheckedFilters)
                {
                    string val = GetValueByColName(item, filter.Key);
                    if (string.IsNullOrEmpty(val)) val = "(Пусто)";
                    if (filter.Value.Contains(val)) return false;
                }
                return true;
            };
        }

        private string GetValueByColName(ReportElementItem item, string colName)
        {
            switch (colName)
            {
                case "ID": return item.ElementId.ToString();
                case "Категория": return item.CategoryName;
                case "Тип / Семейство": return item.TypeName;
                case "Примечание":
                case "Причина": return item.Details;
                default: return "";
            }
        }

        private void ResetCurrentColumnFilter_Click(object sender, RoutedEventArgs e)
        {
            if (_uncheckedFilters.ContainsKey(_currentFilterColumn))
            {
                _uncheckedFilters.Remove(_currentFilterColumn);
                ApplyFilters();
                FilterPopup.IsOpen = false;
            }
        }

        private void ResetAllFilters_Click(object sender, RoutedEventArgs e)
        {
            _uncheckedFilters.Clear();
            ApplyFilters();
            FilterPopup.IsOpen = false;
        }

        private void UpdateFilterButtonsVisualState()
        {
            UpdateGridFiltersVisual(GridUnassigned);
            UpdateGridFiltersVisual(GridSpanning);
            if (GridGrouped != null) UpdateGridFiltersVisual(GridGrouped);
        }

        private void UpdateGridFiltersVisual(DataGrid grid)
        {
            if (grid == null) return;
            foreach (var header in FindVisualChildren<System.Windows.Controls.Primitives.DataGridColumnHeader>(grid))
            {
                string colName = header?.Content?.ToString() ?? "";
                bool isFiltered = !string.IsNullOrWhiteSpace(colName) && _uncheckedFilters.ContainsKey(colName);
                var btn = header.Template?.FindName("FilterDropDownBtn", header) as Button;
                if (btn == null) continue;

                if (isFiltered)
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(2, 132, 199)); // AccentBlue #0284C7
                else
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)); // TextSecondary #64748B
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T matched) yield return matched;
                foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
            }
        }

        private List<ElementId> GetTargetElementIds()
        {
            var selectedItems = new List<ReportElementItem>();

            var currentGrid = TabsMain.SelectedItem == TabUnassigned ? GridUnassigned :
                              TabsMain.SelectedItem == TabSpanning ? GridSpanning : GridGrouped;
            if (currentGrid != null && currentGrid.SelectedItems.Count > 0)
            {
                foreach (var item in currentGrid.SelectedItems.OfType<ReportElementItem>())
                {
                    selectedItems.Add(item);
                }
            }
            else
            {
                // Если строки не выбраны, берем все элементы текущей вкладки
                var itemsSource = currentGrid?.ItemsSource as IEnumerable<ReportElementItem>;
                if (itemsSource != null)
                {
                    selectedItems.AddRange(itemsSource);
                }
            }

            return selectedItems
                .Select(x => new ElementId(x.ElementId))
                .ToList();
        }

        private void BtnSelectInRevit_Click(object sender, RoutedEventArgs e)
        {
            var ids = GetTargetElementIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("Нет элементов для выбора.", "Выбор в Revit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.ActionToExecute = app =>
            {
                try
                {
                    app.ActiveUIDocument?.Selection.SetElementIds(ids);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось выбрать элементы: {ex.Message}");
                }
            };
            _exEvent.Raise();
        }

        private void BtnShowIn3D_Click(object sender, RoutedEventArgs e)
        {
            var ids = GetTargetElementIds();
            if (ids.Count == 0)
            {
                MessageBox.Show("Нет элементов для отображения.", "Показать на 3D", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.ActionToExecute = app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument.Document;
                    View3D view3D = Ensure3DViewActive(app, doc);
                    if (view3D == null) return;

                    app.ActiveUIDocument.Selection.SetElementIds(ids);
                    try { app.ActiveUIDocument.ShowElements(ids); } catch { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось показать элементы на 3D: {ex.Message}");
                }
            };
            _exEvent.Raise();
        }

        private void BtnToggleHighlight_Click(object sender, RoutedEventArgs e)
        {
            if (_isHighlightActive)
            {
                // Повторное нажатие -> сброс подсветки
                ResetHighlight();
            }
            else
            {
                // Включение подсветки
                var ids = GetTargetElementIds();
                if (ids.Count == 0)
                {
                    MessageBox.Show("Нет элементов для подсветки.", "Подсветка на 3D", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                HighlightIn3D(ids);
            }
        }

        private void HighlightIn3D(List<ElementId> targetIds)
        {
            _handler.ActionToExecute = app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument.Document;
                    View3D view3D = Ensure3DViewActive(app, doc);
                    if (view3D == null) return;

                    var targetSet = new HashSet<int>(targetIds.Select(id => id.IntegerValue));

                    var greyColor = new Autodesk.Revit.DB.Color(192, 192, 192);
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceTransparency(90);

                    ElementId solidPatternId = ElementId.InvalidElementId;
                    using (var fpeCol = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
                    {
                        var solidFpe = fpeCol.Cast<FillPatternElement>().FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                        if (solidFpe != null)
                        {
                            solidPatternId = solidFpe.Id;
                        }
                    }

                    try { ogs.SetSurfaceForegroundPatternColor(greyColor); } catch { }
                    try { ogs.SetCutForegroundPatternColor(greyColor); } catch { }
                    try { ogs.SetSurfaceBackgroundPatternColor(greyColor); } catch { }
                    try { ogs.SetCutBackgroundPatternColor(greyColor); } catch { }

                    if (solidPatternId != ElementId.InvalidElementId)
                    {
                        try { ogs.SetSurfaceForegroundPatternId(solidPatternId); } catch { }
                        try { ogs.SetCutForegroundPatternId(solidPatternId); } catch { }
                    }

                    using (Transaction t = new Transaction(doc, "Подсветка элементов отчета"))
                    {
                        t.Start();
                        var cleanOgs = new OverrideGraphicSettings();

                        using (var col = new FilteredElementCollector(doc, view3D.Id))
                        {
                            foreach (Element el in col.WhereElementIsNotElementType())
                            {
                                if (el.Category != null && el.Category.CategoryType == CategoryType.Model && el.Category.Id.IntegerValue != (int)BuiltInCategory.OST_SectionBox)
                                {
                                    try
                                    {
                                        if (targetSet.Contains(el.Id.IntegerValue))
                                        {
                                            view3D.SetElementOverrides(el.Id, cleanOgs);
                                        }
                                        else
                                        {
                                            view3D.SetElementOverrides(el.Id, ogs);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        t.Commit();
                    }

                    try { app.ActiveUIDocument.RefreshActiveView(); } catch { }

                    Dispatcher.Invoke(() =>
                    {
                        _isHighlightActive = true;
                        BtnToggleHighlight.Content = "🔄 Сбросить подсветку";
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось выполнить подсветку: {ex.Message}");
                }
            };
            _exEvent.Raise();
        }

        private void ResetHighlight()
        {
            _handler.ActionToExecute = app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument.Document;
                    View view = doc.ActiveView;
                    if (view == null || view.IsTemplate) return;

                    using (Transaction t = new Transaction(doc, "Сброс подсветки отчета"))
                    {
                        t.Start();
                        var cleanOgs = new OverrideGraphicSettings();
                        using (var col = new FilteredElementCollector(doc, view.Id))
                        {
                            foreach (Element el in col.WhereElementIsNotElementType())
                            {
                                if (el.Category != null && el.Category.CategoryType == CategoryType.Model && el.Category.Id.IntegerValue != (int)BuiltInCategory.OST_SectionBox)
                                {
                                    try
                                    {
                                        view.SetElementOverrides(el.Id, cleanOgs);
                                    }
                                    catch { }
                                }
                            }
                        }
                        t.Commit();
                    }

                    try { app.ActiveUIDocument.RefreshActiveView(); } catch { }

                    Dispatcher.Invoke(() =>
                    {
                        _isHighlightActive = false;
                        BtnToggleHighlight.Content = "💡 Подсветить на 3D";
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось сбросить подсветку: {ex.Message}");
                }
            };
            _exEvent.Raise();
        }

        private View3D Ensure3DViewActive(UIApplication app, Document doc)
        {
            View3D view3D = doc.ActiveView as View3D;
            if (view3D == null || view3D.IsTemplate)
            {
                using (var col = new FilteredElementCollector(doc))
                {
                    view3D = col.OfClass(typeof(View3D))
                        .Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && (v.Name == "{3D}" || v.Name.StartsWith("3D") || v.Name.Contains("3D")));
                }

                if (view3D != null)
                {
                    try { app.ActiveUIDocument.ActiveView = view3D; } catch { }
                }
            }

            if (view3D == null || view3D.IsTemplate)
            {
                MessageBox.Show("Для выполнения операции перейдите на 3D-вид (или откройте стандартный {3D} вид).", "3D-вид", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            return view3D;
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(PluginLogger.LogFilePath) && System.IO.File.Exists(PluginLogger.LogFilePath))
                {
                    Process.Start(PluginLogger.LogFilePath);
                }
                else
                {
                    MessageBox.Show("Файл лога не найден.", "Лог", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть лог: {ex.Message}");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ReportWindow_Closed(object sender, EventArgs e)
        {
            SaveFilters();
            if (_isHighlightActive)
            {
                ResetHighlight();
            }
        }
    }
}
