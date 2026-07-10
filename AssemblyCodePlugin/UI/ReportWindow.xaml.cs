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
    public partial class ReportWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly ProcessingReport _report;
        private readonly RevitActionHandler _handler;
        private readonly ExternalEvent _exEvent;

        private bool _isHighlightActive = false;

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
            if (_isHighlightActive)
            {
                ResetHighlight();
            }
        }
    }
}
