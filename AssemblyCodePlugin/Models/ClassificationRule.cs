using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AssemblyCodePlugin.Models
{
    public class FilterConditionRule
    {
        public string ParamName { get; set; } = "Имя типа";
        public string Comparator { get; set; } = "Содержит"; // "Содержит", "Не содержит", "Равно", "Не равно", "Начинается с", "Заканчивается на", "Больше", "Меньше", "Больше или равно", "Меньше или равно"
        public string ValueString { get; set; } = "";
    }

    // ─── Фильтр для определения типа элемента ───────────────────────────────────
    public class RevitElementFilterRule
    {
        // Категория Revit (строковое имя BuiltInCategory, например "OST_Walls")
        public string TargetCategory { get; set; } = "OST_Walls";
        public string CategoryDisplayName { get; set; } = "Стены";

        // Логика объединения: "AND" (Все условия должны выполняться) или "OR" (Хотя бы одно)
        public string LogicalOperator { get; set; } = "AND";

        // Список гибких условий фильтрации
        public List<FilterConditionRule> Conditions { get; set; } = new List<FilterConditionRule>();

        // Устаревшие поля для обратной совместимости (сохраняются, если Conditions пуст)
        public List<string> FamilyNameContainsAny { get; set; } = new List<string>();
        public List<string> FamilyNameNotContains { get; set; } = new List<string>();
        public List<string> TypeNameContainsAny { get; set; } = new List<string>();
        public List<string> TypeNameNotContains { get; set; } = new List<string>();

        public List<FilterConditionRule> GetEffectiveConditions()
        {
            if (Conditions != null && Conditions.Count > 0)
                return Conditions;

            var list = new List<FilterConditionRule>();
            if (FamilyNameContainsAny != null)
            {
                foreach (var s in FamilyNameContainsAny)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(new FilterConditionRule { ParamName = "Имя семейства", Comparator = "Содержит", ValueString = s.Trim() });
                }
            }
            if (FamilyNameNotContains != null)
            {
                foreach (var s in FamilyNameNotContains)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(new FilterConditionRule { ParamName = "Имя семейства", Comparator = "Не содержит", ValueString = s.Trim() });
                }
            }
            if (TypeNameContainsAny != null)
            {
                foreach (var s in TypeNameContainsAny)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(new FilterConditionRule { ParamName = "Имя типа", Comparator = "Содержит", ValueString = s.Trim() });
                }
            }
            if (TypeNameNotContains != null)
            {
                foreach (var s in TypeNameNotContains)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(new FilterConditionRule { ParamName = "Имя типа", Comparator = "Не содержит", ValueString = s.Trim() });
                }
            }
            return list;
        }

        [IgnoreDataMember]
        public string DisplaySummary
        {
            get
            {
                var conds = GetEffectiveConditions();
                var parts = new List<string>();
                parts.Add($"Кат.: {CategoryDisplayName}");
                if (conds != null && conds.Count > 0)
                {
                    string joinOp = (LogicalOperator == "OR" || LogicalOperator == "ИЛИ") ? " ИЛИ " : " И ";
                    var condDescs = conds.Select(c => $"{c.ParamName} {c.Comparator} \"{c.ValueString}\"");
                    parts.Add(string.Join(joinOp, condDescs));
                }
                return string.Join("; ", parts);
            }
        }
    }


    // ─── Правило рейтингового поиска в классификаторе ───────────────────────────
    public class ClassifierSearchRule
    {
        // Хотя бы одно из слов должно быть в описании классификатора
        public List<string> AnyTrue { get; set; } = new List<string>();
        // Все слова обязательно должны быть в описании классификатора
        public List<string> AllTrue { get; set; } = new List<string>();
        // Ни одно из слов не должно быть в описании классификатора
        public List<string> Exceptions { get; set; } = new List<string>();
        // Дополнительные бонусные баллы: слово → приоритет
        public Dictionary<string, int> ExtraTrue { get; set; } = new Dictionary<string, int>();

        [IgnoreDataMember]
        public string DisplaySummary
        {
            get
            {
                var parts = new List<string>();
                if (AnyTrue != null && AnyTrue.Count > 0)
                    parts.Add($"Любое: [{string.Join(", ", AnyTrue)}]");
                if (AllTrue != null && AllTrue.Count > 0)
                    parts.Add($"Все: [{string.Join(", ", AllTrue)}]");
                if (Exceptions != null && Exceptions.Count > 0)
                    parts.Add($"Искл: [{string.Join(", ", Exceptions.Take(4))}]");
                if (ExtraTrue != null && ExtraTrue.Count > 0)
                    parts.Add($"Бонусы: {string.Join(", ", ExtraTrue.Select(kv => $"{kv.Key}(+{kv.Value})"))}");
                return parts.Count > 0 ? string.Join("\n", parts) : "—";
            }
        }
    }

    // ─── Полное правило классификации одного типа элемента ──────────────────────
    public class ClassificationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ElementTypeName { get; set; } = "";
        public bool IsEnabled { get; set; } = true;

        // Если true — тип НЕ получает суффикс _BGL (для свай и фундаментных плит)
        public bool ExcludeFromBglRename { get; set; } = false;

        // Если true — для этого типа не применяется классификация в надземной или подземной части
        public bool SkipAboveGround { get; set; } = false;
        public bool SkipUnderground { get; set; } = false;

        // Фильтр определения элементов этого типа
        public RevitElementFilterRule RevitFilter { get; set; } = new RevitElementFilterRule();

        // Правило поиска кода для надземных элементов (exceptionWords включает "Подзем")
        public ClassifierSearchRule AboveGroundSearchRule { get; set; } = new ClassifierSearchRule();

        // Правило поиска кода для подземных элементов (extraTrue содержит "Подзем":100)
        public ClassifierSearchRule UndergroundSearchRule { get; set; } = new ClassifierSearchRule();

        // Проверенный или выбранный пользователем код (если кандидатов несколько)
        public string VerifiedAboveCode { get; set; }
        public string VerifiedBelowCode { get; set; }

        // История выбранных кодов для разных файлов классификатора (ИмяФайла -> ВыбранноеЗначение)
        public Dictionary<string, string> SavedAboveByClassifier { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SavedBelowByClassifier { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [IgnoreDataMember]
        public string FilterSummary => RevitFilter?.DisplaySummary ?? "";

        [IgnoreDataMember]
        public string SearchSummary =>
            $"[▲] {AboveGroundSearchRule?.DisplaySummary ?? "—"}\n" +
            $"[▼] {UndergroundSearchRule?.DisplaySummary ?? "—"}";

        public ClassificationRule Clone()
        {
            return new ClassificationRule
            {
                Id = Guid.NewGuid().ToString(),
                ElementTypeName = ElementTypeName + " (копия)",
                IsEnabled = true,
                ExcludeFromBglRename = ExcludeFromBglRename,
                SkipAboveGround = SkipAboveGround,
                SkipUnderground = SkipUnderground,
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = RevitFilter?.TargetCategory ?? "OST_Walls",
                    CategoryDisplayName = RevitFilter?.CategoryDisplayName ?? "",
                    FamilyNameContainsAny = RevitFilter?.FamilyNameContainsAny?.ToList() ?? new List<string>(),
                    FamilyNameNotContains = RevitFilter?.FamilyNameNotContains?.ToList() ?? new List<string>(),
                    TypeNameContainsAny = RevitFilter?.TypeNameContainsAny?.ToList() ?? new List<string>(),
                    TypeNameNotContains = RevitFilter?.TypeNameNotContains?.ToList() ?? new List<string>()
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = AboveGroundSearchRule?.AnyTrue?.ToList() ?? new List<string>(),
                    AllTrue = AboveGroundSearchRule?.AllTrue?.ToList() ?? new List<string>(),
                    Exceptions = AboveGroundSearchRule?.Exceptions?.ToList() ?? new List<string>(),
                    ExtraTrue = AboveGroundSearchRule?.ExtraTrue?.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, int>()
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = UndergroundSearchRule?.AnyTrue?.ToList() ?? new List<string>(),
                    AllTrue = UndergroundSearchRule?.AllTrue?.ToList() ?? new List<string>(),
                    Exceptions = UndergroundSearchRule?.Exceptions?.ToList() ?? new List<string>(),
                    ExtraTrue = UndergroundSearchRule?.ExtraTrue?.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, int>()
                },
                VerifiedAboveCode = VerifiedAboveCode,
                VerifiedBelowCode = VerifiedBelowCode,
                SavedAboveByClassifier = SavedAboveByClassifier?.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                SavedBelowByClassifier = SavedBelowByClassifier?.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
