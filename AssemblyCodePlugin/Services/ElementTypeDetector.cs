using System;
using System.Collections.Generic;
using System.Linq;
using AssemblyCodePlugin.Models;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.Services
{
    /// <summary>
    /// Определяет, к какому правилу классификации относится элемент Revit.
    /// Воспроизводит логику Dynamo: сначала проверяется имя семейства (Family.Name),
    /// если не задано — имя типоразмера (ElementType.Name).
    /// </summary>
    public static class ElementTypeDetector
    {
        public static ClassificationRule FindMatchingRule(
            Element elem,
            ElementType elemType,
            IEnumerable<ClassificationRule> rules)
        {
            if (elem == null || elemType == null || rules == null)
                return null;

            string categoryBicName = GetCategoryBicName(elem);
            string familyName = (elemType as FamilySymbol)?.Family?.Name ?? "";
            string typeName = elemType.Name ?? "";

            foreach (var rule in rules)
            {
                if (!rule.IsEnabled) continue;

                var f = rule.RevitFilter;

                // 1. Категория должна совпасть
                if (!string.IsNullOrEmpty(f.TargetCategory) &&
                    !string.Equals(categoryBicName, f.TargetCategory, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 2. Проверка имени семейства (если задано)
                if (f.FamilyNameContainsAny != null && f.FamilyNameContainsAny.Count > 0)
                {
                    bool anyFamilyMatch = f.FamilyNameContainsAny.Any(pattern =>
                        familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!anyFamilyMatch) continue;

                    // Проверяем NOT Contains для семейства
                    if (f.FamilyNameNotContains != null && f.FamilyNameNotContains.Count > 0)
                    {
                        bool hasExcluded = f.FamilyNameNotContains.Any(pattern =>
                            familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (hasExcluded) continue;
                    }

                    return rule;
                }

                // 3. Если имя семейства не задано — проверяем имя типоразмера
                if (f.TypeNameContainsAny != null && f.TypeNameContainsAny.Count > 0)
                {
                    bool anyTypeMatch = f.TypeNameContainsAny.Any(pattern =>
                        typeName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!anyTypeMatch) continue;

                    if (f.TypeNameNotContains != null && f.TypeNameNotContains.Count > 0)
                    {
                        bool hasExcluded = f.TypeNameNotContains.Any(pattern =>
                            typeName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (hasExcluded) continue;
                    }

                    return rule;
                }
            }

            return null;
        }

        private static readonly Dictionary<int, string> _bicMap = new Dictionary<int, string>
        {
            { (int)BuiltInCategory.OST_Walls, "OST_Walls" },
            { (int)BuiltInCategory.OST_Floors, "OST_Floors" },
            { (int)BuiltInCategory.OST_StructuralFraming, "OST_StructuralFraming" },
            { (int)BuiltInCategory.OST_StructuralColumns, "OST_StructuralColumns" },
            { (int)BuiltInCategory.OST_GenericModel, "OST_GenericModel" },
            { (int)BuiltInCategory.OST_EdgeSlab, "OST_EdgeSlab" },
            { (int)BuiltInCategory.OST_StructuralFoundation, "OST_StructuralFoundation" },
        };

        private static string GetCategoryBicName(Element elem)
        {
            var cat = elem.Category;
            if (cat == null) return "";
            int id = cat.Id.IntegerValue;
            return _bicMap.TryGetValue(id, out var name) ? name : cat.Name;
        }
    }
}
