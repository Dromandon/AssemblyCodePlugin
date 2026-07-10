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

                // 2. Проверка условий фильтрации
                var conds = f.GetEffectiveConditions();
                if (conds == null || conds.Count == 0)
                {
                    return rule;
                }

                bool isOr = (f.LogicalOperator == "OR" || f.LogicalOperator == "ИЛИ");
                bool matchResult = isOr ? false : true;

                foreach (var cond in conds)
                {
                    string valStr = GetParameterValueString(elem, elemType, cond.ParamName);
                    bool condEval = EvaluateCondition(cond, valStr);

                    if (isOr)
                    {
                        if (condEval)
                        {
                            matchResult = true;
                            break;
                        }
                    }
                    else
                    {
                        if (!condEval)
                        {
                            matchResult = false;
                            break;
                        }
                    }
                }

                if (matchResult)
                    return rule;
            }

            return null;
        }

        private static string GetParameterValueString(Element elem, ElementType elemType, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return "";

            string pName = paramName.Trim();
            if (string.Equals(pName, "Имя семейства", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pName, "FamilyName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pName, "Family Name", StringComparison.OrdinalIgnoreCase))
            {
                return (elemType as FamilySymbol)?.Family?.Name ?? "";
            }

            if (string.Equals(pName, "Имя типа", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pName, "TypeName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pName, "Type Name", StringComparison.OrdinalIgnoreCase))
            {
                return elemType?.Name ?? "";
            }

            Parameter param = elemType?.LookupParameter(pName);
            if (param == null && elem != null)
                param = elem.LookupParameter(pName);
            if (param == null)
                param = (elemType as FamilySymbol)?.Family?.LookupParameter(pName);

            if (param == null) return "";

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? "";
                case StorageType.Double:
                case StorageType.Integer:
                    string vs = param.AsValueString();
                    if (!string.IsNullOrEmpty(vs)) return vs;
                    return param.StorageType == StorageType.Double
                        ? param.AsDouble().ToString()
                        : param.AsInteger().ToString();
                case StorageType.ElementId:
                    return param.AsElementId()?.IntegerValue.ToString() ?? "";
                default:
                    return "";
            }
        }

        private static bool EvaluateCondition(FilterConditionRule cond, string valStr)
        {
            if (cond == null) return true;
            string target = cond.ValueString ?? "";
            valStr = valStr ?? "";

            switch (cond.Comparator)
            {
                case "Содержит":
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Не содержит":
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0;
                case "Равно":
                    return string.Equals(valStr, target, StringComparison.OrdinalIgnoreCase);
                case "Не равно":
                    return !string.Equals(valStr, target, StringComparison.OrdinalIgnoreCase);
                case "Начинается с":
                    return valStr.StartsWith(target, StringComparison.OrdinalIgnoreCase);
                case "Заканчивается на":
                    return valStr.EndsWith(target, StringComparison.OrdinalIgnoreCase);
                case "Больше (>)":
                case "Больше":
                    if (double.TryParse(valStr, out double v1) && double.TryParse(target, out double t1))
                        return v1 > t1;
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) > 0;
                case "Меньше (<)":
                case "Меньше":
                    if (double.TryParse(valStr, out double v2) && double.TryParse(target, out double t2))
                        return v2 < t2;
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) < 0;
                case "Больше или равно (>=)":
                case "Больше или равно":
                    if (double.TryParse(valStr, out double v3) && double.TryParse(target, out double t3))
                        return v3 >= t3;
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Меньше или равно (<=)":
                case "Меньше или равно":
                    if (double.TryParse(valStr, out double v4) && double.TryParse(target, out double t4))
                        return v4 <= t4;
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) <= 0;
                default:
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            }
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
