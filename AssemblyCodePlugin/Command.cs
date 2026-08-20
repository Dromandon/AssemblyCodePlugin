using System;
using System.Linq;
using AssemblyCodePlugin.Services;
using AssemblyCodePlugin.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AssemblyCodePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            if (uiApp.ActiveUIDocument == null)
            {
                TaskDialog.Show("Кодификатор", "Нет открытого документа Revit.");
                return Result.Cancelled;
            }
            var doc = uiApp.ActiveUIDocument.Document;

            try
            {
                // 1. Загружаем настройки
                var settings = ConfigService.Load();

                // 2. Получаем все уровни из документа (отсортированные по высоте)
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => l.Name)
                    .ToList();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Кодификатор", "В документе не найдено ни одного уровня.");
                    return Result.Cancelled;
                }

                // 3. Открываем окно настроек
                var window = new MainWindow(doc, uiApp, settings, levels);
                bool? dlgResult = window.ShowDialog();

                if (dlgResult != true || !window.RunRequested)
                    return Result.Cancelled;

                PluginLogger.Initialize();
                PluginLogger.Log("Запуск команды AssemblyCode...");

                // 4. Запускаем обработку
                var resultSettings = window.ResultSettings;

                // Проверяем, указаны ли уровни нуля
                if (string.IsNullOrEmpty(resultSettings.ZeroLevel.LowLevelName) ||
                    string.IsNullOrEmpty(resultSettings.ZeroLevel.HighLevelName))
                {
                    TaskDialog.Show("Кодификатор",
                        "Не заданы уровни нуля.\nПожалуйста, укажите нижний и верхний уровень нуля в настройках.");
                    return Result.Cancelled;
                }

                PluginLogger.Log($"Начало AssemblyCodeProcessor.Process (Уровни нуля: {resultSettings.ZeroLevel.LowLevelName} -> {resultSettings.ZeroLevel.HighLevelName})...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ProcessingReport report = null;

                try
                {
                    ProgressManager.Show("Чтение классификатора и анализ элементов...");
                    report = AssemblyCodeProcessor.Process(doc, resultSettings);
                }
                finally
                {
                    ProgressManager.Close();
                }

                sw.Stop();
                PluginLogger.Log($"Завершение AssemblyCodeProcessor.Process за {sw.Elapsed.TotalSeconds:F2} с.");

                // 5. Отображаем интерактивное немодальное окно отчёта
                var reportWindow = new ReportWindow(uiApp, report);
                reportWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Кодификатор — Ошибка", ex.Message + "\n\n" + ex.StackTrace);
                message = ex.Message + "\n\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
