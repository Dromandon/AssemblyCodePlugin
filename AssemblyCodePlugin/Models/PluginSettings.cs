using System.Collections.Generic;

namespace AssemblyCodePlugin.Models
{
    public class PluginSettings
    {
        // Имена параметров Revit (UNIFORMAT_CODE и FAM_Underground)
        public string AssemblyCodeParamName { get; set; } = "Код по классификатору";
        public string AssemblyDescriptionParamName { get; set; } = "";
        public string UndergroundParamName { get; set; } = "FAM_Underground";
        public string UndergroundValueText { get; set; } = "Подземная часть";
        public string AbovegroundValueText { get; set; } = "Надземная часть";
        public bool IsUndergroundParamYesNo { get; set; } = false;

        // Общая настройка: не добавлять суффикс _BGL для подземной части
        public bool NeverAddBglSuffix { get; set; } = false;
        
        // Отключить разделение на надземную и подземную части
        public bool DisableZoneSplit { get; set; } = false;

        // Настройки нулевого уровня
        public ZeroLevelSettings ZeroLevel { get; set; } = new ZeroLevelSettings();

        // Имя последнего использованного файла классификатора
        public string LastClassifierFileName { get; set; } = "";

        // Габариты окна главного интерфейса
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 720;
        public bool IsWindowMaximized { get; set; } = false;

        // Габариты окна настройки правила классификации
        public double RuleEditDialogWidth { get; set; } = 820;
        public double RuleEditDialogHeight { get; set; } = 640;
        public bool IsRuleEditDialogMaximized { get; set; } = false;

        // Ширины столбцов таблицы (в пикселях)
        public double ColTypeWidth { get; set; } = 160;
        public double ColAboveWidth { get; set; } = 340;
        public double ColBelowWidth { get; set; } = 340;

        // Правила классификации (порядок = приоритет)
        public List<ClassificationRule> Rules { get; set; } = new List<ClassificationRule>();
    }
}
