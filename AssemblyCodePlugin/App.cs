using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace AssemblyCodePlugin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "Antigravity";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // Вкладка уже существует
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Кодификатор");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                PushButtonData buttonData = new PushButtonData(
                    "AssemblyCodeBtn",
                    "Заполнить\nкодификатор",
                    assemblyPath,
                    "AssemblyCodePlugin.Command")
                {
                    ToolTip = "Автоматическая классификация и назначение кодов элементам по правилам Dynamo",
                    LongDescription = "Открывает окно настроек с таблицей из 3 столбцов (Тип элемента, Фильтр Revit, Рейтинговый поиск в классификаторе)."
                };

                panel.AddItem(buttonData);
                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
