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
                var rootNode = f.GetEffectiveRootNode();
                if (rootNode == null || (!rootNode.IsGroup && string.IsNullOrWhiteSpace(rootNode.ParamName)) || (rootNode.IsGroup && rootNode.Children.Count == 0))
                {
                    return rule;
                }

                if (EvaluateNode(rootNode, elem, elemType))
                    return rule;
            }

            return null;
        }

        private static bool EvaluateNode(FilterNode node, Element elem, ElementType elemType)
        {
            if (node == null) return true;

            if (!node.IsGroup)
            {
                string valStr = GetParameterValueString(elem, elemType, node.ParamName, node.Comparator);
                return EvaluateCondition(node.Comparator, node.ValueString, valStr);
            }
            else
            {
                if (node.Children == null || node.Children.Count == 0) return true;

                bool isOr = (node.LogicalOperator == "OR" || node.LogicalOperator == "ИЛИ");
                bool result = !isOr; // Для AND начальное значение true (все должны выполниться), для OR - false (хотя бы одно)

                foreach (var child in node.Children)
                {
                    bool childEval = EvaluateNode(child, elem, elemType);

                    if (isOr)
                    {
                        if (childEval) return true; // При OR достаточно одного true
                    }
                    else
                    {
                        if (!childEval) return false; // При AND достаточно одного false
                    }
                }

                return result;
            }
        }

        private static string GetParameterValueString(Element elem, ElementType elemType, string paramName, string comparator)
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
                    if (param.Element != null && param.Element.Document != null)
                    {
                        Element linkedElem = param.Element.Document.GetElement(param.AsElementId());
                        if (linkedElem is Level level)
                        {
                            bool isMathOp = comparator != null && (
                                comparator.Contains("Больше") || 
                                comparator.Contains("Меньше") || 
                                comparator.Contains(">") || 
                                comparator.Contains("<"));
                            
                            if (isMathOp)
                            {
                                // Возвращаем отметку в миллиметрах
                                double mm = level.Elevation * 304.8;
                                return mm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                return level.Name;
                            }
                        }
                        return linkedElem?.Name ?? param.AsElementId().IntegerValue.ToString();
                    }
                    return param.AsElementId().IntegerValue.ToString();
                default:
                    return "";
            }
        }

        private static bool TryParseDouble(string input, out double result)
        {
            if (string.IsNullOrWhiteSpace(input)) { result = 0; return false; }
            input = input.Replace(',', '.');
            return double.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private static bool EvaluateCondition(string comparator, string targetValue, string valStr)
        {
            if (string.IsNullOrWhiteSpace(comparator)) return true;
            string target = targetValue ?? "";
            valStr = valStr ?? "";

            switch (comparator)
            {
                case "Содержит":
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Не содержит":
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0;
                case "Равно":
                    // Числовое сравнение (чтобы "3" было равно "3.000")
                    if (TryParseDouble(valStr, out double vEq) && TryParseDouble(target, out double tEq))
                    {
                        if (tEq >= 100 || tEq <= -100) tEq = tEq / 1000.0;
                        return Math.Abs(vEq - tEq) < 0.001; // Учитываем погрешность
                    }
                    return string.Equals(valStr, target, StringComparison.OrdinalIgnoreCase);
                case "Не равно":
                    if (TryParseDouble(valStr, out double vNeq) && TryParseDouble(target, out double tNeq))
                    {
                        return Math.Abs(vNeq - tNeq) >= 0.001;
                    }
                    return !string.Equals(valStr, target, StringComparison.OrdinalIgnoreCase);
                case "Начинается с":
                    return valStr.StartsWith(target, StringComparison.OrdinalIgnoreCase);
                case "Заканчивается на":
                    return valStr.EndsWith(target, StringComparison.OrdinalIgnoreCase);
                case "Больше (>)":
                case "Больше":
                    if (TryParseDouble(valStr, out double v1) && TryParseDouble(target, out double t1))
                    {
                        return v1 > t1;
                    }
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) > 0;
                case "Меньше (<)":
                case "Меньше":
                    if (TryParseDouble(valStr, out double v2) && TryParseDouble(target, out double t2))
                    {
                        return v2 < t2;
                    }
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) < 0;
                case "Больше или равно (>=)":
                case "Больше или равно":
                    if (TryParseDouble(valStr, out double v3) && TryParseDouble(target, out double t3))
                    {
                        return v3 >= t3;
                    }
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) >= 0;
                case "Меньше или равно (<=)":
                case "Меньше или равно":
                    if (TryParseDouble(valStr, out double v4) && TryParseDouble(target, out double t4))
                    {
                        return v4 <= t4;
                    }
                    return string.Compare(valStr, target, StringComparison.OrdinalIgnoreCase) <= 0;

                default:
                    return valStr.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static string GetCategoryBicName(Element elem)
        {
            var cat = elem.Category;
            if (cat == null) return "";
            int id = cat.Id.IntegerValue;
            var name = Enum.GetName(typeof(BuiltInCategory), id);
            return name ?? cat.Name;
        }
    }
}
