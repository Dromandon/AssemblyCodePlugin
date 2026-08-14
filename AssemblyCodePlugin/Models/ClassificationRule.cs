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

    // ─── Узел дерева фильтрации (Группа или конкретное условие) ────────────────
    public class FilterNode
    {
        // true = логическая группа (AND/OR), false = конкретное правило (лист)
        public bool IsGroup { get; set; } = false;

        // Поля для Группы
        public string LogicalOperator { get; set; } = "AND"; // "AND" или "OR"
        public List<FilterNode> Children { get; set; } = new List<FilterNode>();

        // Поля для Условия
        public string ParamName { get; set; } = "Имя типа";
        public string Comparator { get; set; } = "Содержит";
        public string ValueString { get; set; } = "";

        // Удобные методы для рекурсивного клонирования
        public FilterNode Clone()
        {
            var node = new FilterNode
            {
                IsGroup = this.IsGroup,
                LogicalOperator = this.LogicalOperator,
                ParamName = this.ParamName,
                Comparator = this.Comparator,
                ValueString = this.ValueString
            };
            if (this.Children != null)
            {
                foreach (var child in this.Children)
                {
                    node.Children.Add(child.Clone());
                }
            }
            return node;
        }

        public string GetDisplayString()
        {
            if (!IsGroup)
            {
                return $"{ParamName} {Comparator} \"{ValueString}\"";
            }
            else
            {
                if (Children == null || Children.Count == 0) return "(Пусто)";
                string op = (LogicalOperator == "OR" || LogicalOperator == "ИЛИ") ? " ИЛИ " : " И ";
                var parts = Children.Select(c => c.GetDisplayString()).ToList();
                if (parts.Count == 1) return parts[0];
                return "(" + string.Join(op, parts) + ")";
            }
        }
    }

    // ─── Фильтр для определения типа элемента ───────────────────────────────────
    public class RevitElementFilterRule
    {
        // Категория Revit (строковое имя BuiltInCategory, например "OST_Walls")
        public string TargetCategory { get; set; } = "OST_Walls";
        public string CategoryDisplayName { get; set; } = "Стены";

        // Добавлять суффикс к имени типа при классификации (для авто-дублирования типов)
        public bool AppendTypeSuffix { get; set; } = false;
        public string TypeSuffix { get; set; } = "";

        // Логика объединения: "AND" (Все условия должны выполняться) или "OR" (Хотя бы одно)
        public string LogicalOperator { get; set; } = "AND";

        // Дерево условий (вместо плоского списка)
        public FilterNode RootNode { get; set; }

        // Список гибких условий фильтрации (СОХРАНЕН ДЛЯ ОБРАТНОЙ СОВМЕСТИМОСТИ)
        public List<FilterConditionRule> Conditions { get; set; } = new List<FilterConditionRule>();

        // Устаревшие поля для обратной совместимости
        public List<string> FamilyNameContainsAny { get; set; } = new List<string>();
        public List<string> FamilyNameNotContains { get; set; } = new List<string>();
        public List<string> TypeNameContainsAny { get; set; } = new List<string>();
        public List<string> TypeNameNotContains { get; set; } = new List<string>();

        /// <summary>
        /// Возвращает корневой узел, автоматически выполняя миграцию из старых форматов (Conditions или массивы).
        /// </summary>
        public FilterNode GetEffectiveRootNode()
        {
            if (RootNode != null)
                return RootNode;

            // Если RootNode еще нет, создаем его на основе старых данных
            var root = new FilterNode { IsGroup = true, LogicalOperator = this.LogicalOperator ?? "AND" };

            // Сначала пробуем старый список Conditions
            if (Conditions != null && Conditions.Count > 0)
            {
                foreach (var c in Conditions)
                {
                    root.Children.Add(new FilterNode
                    {
                        IsGroup = false,
                        ParamName = c.ParamName,
                        Comparator = c.Comparator,
                        ValueString = c.ValueString
                    });
                }
                RootNode = root;
                return root;
            }

            // Иначе пробуем совсем старые массивы
            if (FamilyNameContainsAny != null)
                foreach (var s in FamilyNameContainsAny)
                    if (!string.IsNullOrWhiteSpace(s))
                        root.Children.Add(new FilterNode { IsGroup = false, ParamName = "Имя семейства", Comparator = "Содержит", ValueString = s.Trim() });
            
            if (FamilyNameNotContains != null)
                foreach (var s in FamilyNameNotContains)
                    if (!string.IsNullOrWhiteSpace(s))
                        root.Children.Add(new FilterNode { IsGroup = false, ParamName = "Имя семейства", Comparator = "Не содержит", ValueString = s.Trim() });
            
            if (TypeNameContainsAny != null)
                foreach (var s in TypeNameContainsAny)
                    if (!string.IsNullOrWhiteSpace(s))
                        root.Children.Add(new FilterNode { IsGroup = false, ParamName = "Имя типа", Comparator = "Содержит", ValueString = s.Trim() });
            
            if (TypeNameNotContains != null)
                foreach (var s in TypeNameNotContains)
                    if (!string.IsNullOrWhiteSpace(s))
                        root.Children.Add(new FilterNode { IsGroup = false, ParamName = "Имя типа", Comparator = "Не содержит", ValueString = s.Trim() });

            RootNode = root;
            return root;
        }

        public List<FilterNode> GetAllConditions()
        {
            var result = new List<FilterNode>();
            var root = GetEffectiveRootNode();
            if (root != null)
                CollectConditions(root, result);
            return result;
        }
        
        private void CollectConditions(FilterNode node, List<FilterNode> result)
        {
            if (!node.IsGroup) result.Add(node);
            else if (node.Children != null)
                foreach(var c in node.Children) CollectConditions(c, result);
        }

        [IgnoreDataMember]
        public string DisplaySummary
        {
            get
            {
                var root = GetEffectiveRootNode();
                var parts = new List<string>();
                parts.Add($"Кат.: {CategoryDisplayName}");
                
                if (root != null && root.Children.Count > 0)
                {
                    parts.Add(root.GetDisplayString());
                }
                if (AppendTypeSuffix && !string.IsNullOrWhiteSpace(TypeSuffix))
                {
                    parts.Add($"Суффикс типа: \"{TypeSuffix}\"");
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
                    TargetCategory = this.RevitFilter?.TargetCategory ?? "OST_Walls",
                    CategoryDisplayName = this.RevitFilter?.CategoryDisplayName ?? "",
                    AppendTypeSuffix = this.RevitFilter?.AppendTypeSuffix ?? false,
                    TypeSuffix = this.RevitFilter?.TypeSuffix ?? "",
                    LogicalOperator = this.RevitFilter?.LogicalOperator ?? "AND",
                    RootNode = this.RevitFilter?.RootNode?.Clone(),
                    // Сохраняем пустые старые коллекции, так как мы их уже мигрировали в RootNode
                    Conditions = new List<FilterConditionRule>(),
                    FamilyNameContainsAny = new List<string>(),
                    FamilyNameNotContains = new List<string>(),
                    TypeNameContainsAny = new List<string>(),
                    TypeNameNotContains = new List<string>()
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
