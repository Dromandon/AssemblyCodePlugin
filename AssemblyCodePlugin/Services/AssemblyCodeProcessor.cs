using System;
using System.Collections.Generic;
using System.Linq;
using AssemblyCodePlugin.Models;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.Services
{
    public class ProcessingReport
    {
        public int TotalElements { get; set; }
        public int UpdatedElements { get; set; }
        public int CreatedTypes { get; set; }
        public double ZoneCalcSeconds { get; set; }
        public double TypeChangeSeconds { get; set; }
        public double ParamSetSeconds { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> SpanningElements { get; } = new List<string>();
        public List<ReportElementItem> UnassignedElementItems { get; } = new List<ReportElementItem>();
        public List<ReportElementItem> SpanningElementItems { get; } = new List<ReportElementItem>();
        public List<ReportElementItem> GroupedElementItems { get; } = new List<ReportElementItem>();

        public string GetSummary() =>
            $"Обработано элементов: {TotalElements}\n" +
            $"Обновлено: {UpdatedElements}\n" +
            $"Создано новых типоразмеров: {CreatedTypes}\n" +
            $"─── Телеметрия времени ───\n" +
            $"Определение зон: {ZoneCalcSeconds:F2} с\n" +
            $"Смена типоразмеров: {TypeChangeSeconds:F2} с\n" +
            $"Запись параметров: {ParamSetSeconds:F2} с\n" +
            $"Предупреждений: {Warnings.Count}\n" +
            $"Элементов, пересекающих уровень нуля: {SpanningElements.Count}\n" +
            $"Элементов в группах: {GroupedElementItems.Count}";
    }

    public static class AssemblyCodeProcessor
    {
        public static ProcessingReport Process(Document doc, PluginSettings settings)
        {
            var report = new ProcessingReport();
            if (doc == null || settings == null) return report;

            // 1. Загружаем классификатор
            PluginLogger.Log("1. Чтение таблицы классификатора...");
            var (classifierItems, byCode) = AssemblyCodeTableReader.ReadClassifier(doc);
            if (classifierItems.Count == 0)
                PluginLogger.Log("   -> ⚠️ ВНИМАНИЕ: Файл классификатора не найден или пуст! Пожалуйста, подключите файл классификатора в настройках Revit (Управление -> Дополнительные параметры -> Код по классификатору).");
            else
                PluginLogger.Log($"   -> Классификатор прочитан. Позиций: {classifierItems.Count}");

            // 2. Строим контекст нулевой зоны (предвычисленный BoundingBox плит)
            PluginLogger.Log("2. Построение контекста нуля (поиск плит перекрытия на отметке нуля)...");
            var zeroCtx = ZoneDeterminator.BuildContext(doc, settings.ZeroLevel);
            PluginLogger.Log($"   -> Найдено плит на отметке нуля: {zeroCtx.ZeroLevelFloors.Count}");

            // 3. Предварительно собираем индекс всех типов в документе по имени (O(1) поиск без try/catch)
            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .ToList();

            var typesByNameAndClass = new Dictionary<(Type classType, string name), ElementType>();
            var elementTypeCache = new Dictionary<ElementId, ElementType>();

            foreach (var t in allTypes)
            {
                elementTypeCache[t.Id] = t;
                var key = (t.GetType(), t.Name);
                if (!typesByNameAndClass.ContainsKey(key))
                {
                    typesByNameAndClass[key] = t;
                }
            }

            // 4. Собираем целевые элементы
            var categories = new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_EdgeSlab
            };

            PluginLogger.Log("3. Сбор элементов целевых категорий из модели...");
            var allElements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(categories))
                .WhereElementIsNotElementType()
                .ToElements();

            report.TotalElements = allElements.Count;
            PluginLogger.Log($"   -> Всего элементов в целевых категориях: {allElements.Count}");

            // Записываем элементы в группах в отчет
            var groupedElements = allElements.Where(e => e.GroupId != ElementId.InvalidElementId).ToList();
            foreach (var elem in groupedElements)
            {
                var elemType = doc.GetElement(elem.GetTypeId()) as ElementType;
                report.GroupedElementItems.Add(new ReportElementItem
                {
                    ElementId = elem.Id.IntegerValue,
                    ElementName = elem.Name ?? "",
                    CategoryName = elem.Category?.Name ?? "",
                    TypeName = elemType?.Name ?? "",
                    Details = "Элемент в группе (пропущен)"
                });
            }
            PluginLogger.Log($"   -> Элементов в группах (пропущено): {groupedElements.Count}");

            // ─── ЭТАП 1: ГРУППИРОВКА ПО ТИПОРАЗМЕРАМ ────────────────────────────────
            var elementsByTypeId = allElements
                .Where(e => e.GroupId == ElementId.InvalidElementId)
                .GroupBy(e => e.GetTypeId())
                .ToList();

            PluginLogger.Log($"   -> Сгруппировано по типам: {elementsByTypeId.Count} групп типоразмеров.");

            using (var tx = new Transaction(doc, "Заполнение кодификатора (Antigravity)"))
            {
                PluginLogger.Log("4. Старт транзакции Revit...");
                tx.Start();

                var failOpt = tx.GetFailureHandlingOptions();
                failOpt.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                tx.SetFailureHandlingOptions(failOpt);

                int idx = 0;
                int totalGroups = elementsByTypeId.Count;
                foreach (var group in elementsByTypeId)
                {
                    idx++;
                    if (idx % 20 == 1 || idx == totalGroups)
                    {
                        double prog = 10.0 + (idx / (double)totalGroups) * 40.0;
                        ProgressManager.UpdateStatus($"Расчет зон и назначение типов ({idx}/{totalGroups})...", prog, $"Обработано групп: {idx} из {totalGroups}");
                    }

                    var sourceTypeId = group.Key;
                    if (!elementTypeCache.TryGetValue(sourceTypeId, out var elemType))
                    {
                        elemType = doc.GetElement(sourceTypeId) as ElementType;
                        if (elemType == null) continue;
                        elementTypeCache[sourceTypeId] = elemType;
                    }

                    int countInGroup = group.Count();
                    PluginLogger.Log($"   [{idx}/{totalGroups}] Тип '{elemType.Name}' ({countInGroup} экз.). Поиск правила...");

                    // Находим правило классификации ОДИН РАЗ НА ТИП
                    var firstElem = group.First();
                    var rule = ElementTypeDetector.FindMatchingRule(firstElem, elemType, settings.Rules);
                    if (rule == null)
                    {
                        PluginLogger.Log($"       -> Правило не найдено. Пропуск.");
                        foreach (var elem in group)
                        {
                            report.UnassignedElementItems.Add(new ReportElementItem
                            {
                                ElementId = elem.Id.IntegerValue,
                                ElementName = elem.Name ?? "",
                                CategoryName = elem.Category?.Name ?? "",
                                TypeName = elemType.Name ?? "",
                                Details = "Правило классификации не найдено"
                            });
                        }
                        continue;
                    }

                    // Разделяем элементы группы на надземные и подземные
                    var aboveElements = new List<Element>();
                    var belowElements = new List<Element>();

                    var swZone = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var elem in group)
                    {
                        var zone = ZoneDeterminator.DetermineZone(elem, zeroCtx);
                        if (zone == ZoneResult.Spanning)
                        {
                            report.SpanningElements.Add($"Id={elem.Id} | Тип={elemType.Name}");
                            report.SpanningElementItems.Add(new ReportElementItem
                            {
                                ElementId = elem.Id.IntegerValue,
                                ElementName = elem.Name ?? "",
                                CategoryName = elem.Category?.Name ?? "",
                                TypeName = elemType.Name ?? "",
                                Details = "Элемент пересекает нулевую зону"
                            });
                            var box = elem.get_BoundingBox(null);
                            double centerZ = box != null ? (box.Min.Z + box.Max.Z) / 2.0 : zeroCtx.LowElevation;
                            zone = centerZ >= zeroCtx.HighElevation ? ZoneResult.AboveZero : ZoneResult.BelowZero;
                        }

                        if (zone == ZoneResult.BelowZero)
                            belowElements.Add(elem);
                        else if (zone == ZoneResult.AboveZero)
                            aboveElements.Add(elem);
                    }
                    swZone.Stop();
                    report.ZoneCalcSeconds += swZone.Elapsed.TotalSeconds;

                    PluginLogger.Log($"       -> Зоны: надземных={aboveElements.Count}, подземных={belowElements.Count} (расчет за {swZone.Elapsed.TotalMilliseconds:F1} мс)");

                    // Обрабатываем надземную подгруппу
                    if (aboveElements.Count > 0)
                    {
                        ProcessSubgroup(
                            doc, aboveElements, elemType, isUnderground: false,
                            rule, classifierItems, byCode, settings, typesByNameAndClass, report);
                    }

                    // Обрабатываем подземную подгруппу
                    if (belowElements.Count > 0)
                    {
                        ProcessSubgroup(
                            doc, belowElements, elemType, isUnderground: true,
                            rule, classifierItems, byCode, settings, typesByNameAndClass, report);
                    }
                }

                PluginLogger.Log("5. Коммит транзакции (Revit применяет изменения)...");
                ProgressManager.StartCommitPhase(report.UpdatedElements);
                var swCommit = System.Diagnostics.Stopwatch.StartNew();
                tx.Commit();
                swCommit.Stop();
                PluginLogger.Log($"   -> Транзакция зафиксирована за {swCommit.Elapsed.TotalSeconds:F2} с.");
            }

            PluginLogger.Log($"=== ОБРАБОТКА ЗАВЕРШЕНА. Всего элементов: {report.TotalElements}, Обновлено: {report.UpdatedElements} ===");
            return report;
        }

        private static void ProcessSubgroup(
            Document doc,
            List<Element> elements,
            ElementType sourceType,
            bool isUnderground,
            ClassificationRule rule,
            IReadOnlyList<AssemblyCodeItem> classifierItems,
            IReadOnlyDictionary<string, AssemblyCodeItem> byCode,
            PluginSettings settings,
            Dictionary<(Type classType, string name), ElementType> typeIndex,
            ProcessingReport report)
        {
            bool excludeFromBgl = rule.ExcludeFromBglRename || (settings != null && settings.NeverAddBglSuffix);
            ElementType targetType = EnsureCorrectTypeNameFast(
                sourceType, isUnderground, excludeFromBgl, typeIndex, report);

            PluginLogger.Log($"          -> [{(isUnderground ? "ПОДЗЕМНАЯ" : "НАДЗЕМНАЯ")} ({elements.Count} экз.)] Целевой тип: '{targetType.Name}'");

            // 2. Назначаем Assembly Code В ТИПОРАЗМЕР (1 раз на тип!)
            bool skipClassification = isUnderground ? rule.SkipUnderground : rule.SkipAboveGround;
            if (!skipClassification)
            {
                string verifiedRaw = isUnderground ? rule.VerifiedBelowCode : rule.VerifiedAboveCode;
                string code = ExtractCodeFromPreview(verifiedRaw);

                if (string.IsNullOrEmpty(code))
                {
                    var searchRule = isUnderground
                        ? rule.UndergroundSearchRule
                        : rule.AboveGroundSearchRule;

                    var bestItem = ClassifierRatingEngine.FindBestMatch(
                        classifierItems, byCode, searchRule, isUnderground);

                    code = bestItem != null ? bestItem.Code : "";
                }

                if (!string.IsNullOrEmpty(code))
                {
                    TrySetStringParamIfChanged(targetType, settings.AssemblyCodeParamName, code);
                }
                else
                {
                    AddWarning(report.Warnings,
                        $"Код не найден для «{rule.ElementTypeName}» " +
                        $"({(isUnderground ? "подземная" : "надземная")} часть)");
                    foreach (var elem in elements)
                    {
                        report.UnassignedElementItems.Add(new ReportElementItem
                        {
                            ElementId = elem.Id.IntegerValue,
                            ElementName = elem.Name ?? "",
                            CategoryName = elem.Category?.Name ?? "",
                            TypeName = sourceType.Name ?? "",
                            Details = $"Код не найден ({(isUnderground ? "подземная" : "надземная")} часть)"
                        });
                    }
                }
            }
            else
            {
                PluginLogger.Log($"          * Пропущено назначение кода (отключено в правиле для {(isUnderground ? "подземной" : "надземной")} части)");
                foreach (var elem in elements)
                {
                    report.UnassignedElementItems.Add(new ReportElementItem
                    {
                        ElementId = elem.Id.IntegerValue,
                        ElementName = elem.Name ?? "",
                        CategoryName = elem.Category?.Name ?? "",
                        TypeName = sourceType.Name ?? "",
                        Details = $"Классификация отключена ({(isUnderground ? "подземная" : "надземная")} часть)"
                    });
                }
            }

            // 3. Записываем FAM_Underground в ТИПОРАЗМЕР (если это параметр типа)
            string undergroundText = isUnderground
                ? settings.UndergroundValueText
                : settings.AbovegroundValueText;

            TrySetStringParamIfChanged(targetType, settings.UndergroundParamName, undergroundText);

            // Проверяем 1 раз для подгруппы: является ли FAM_Underground параметром ЭКЗЕМПЛЯРА
            bool isInstanceParam = elements.Count > 0 && IsWritableInstanceParam(elements[0], settings.UndergroundParamName);

            // 4. Обходим элементы подгруппы:
            var swChange = System.Diagnostics.Stopwatch.StartNew();
            int changedTypeCount = 0;
            foreach (var elem in elements)
            {
                if (elem.GetTypeId() != targetType.Id)
                {
                    FastChangeTypeId(elem, targetType.Id);
                    changedTypeCount++;
                }
            }
            swChange.Stop();
            report.TypeChangeSeconds += swChange.Elapsed.TotalSeconds;

            if (changedTypeCount > 0)
            {
                PluginLogger.Log($"             * Изменен TypeId у {changedTypeCount} экз. за {swChange.Elapsed.TotalMilliseconds:F1} мс");
            }

            if (isInstanceParam)
            {
                var swParam = System.Diagnostics.Stopwatch.StartNew();
                foreach (var elem in elements)
                {
                    TrySetStringParamIfChanged(elem, settings.UndergroundParamName, undergroundText);
                }
                swParam.Stop();
                report.ParamSetSeconds += swParam.Elapsed.TotalSeconds;
            }
            report.UpdatedElements += elements.Count;
        }

        private static void FastChangeTypeId(Element elem, ElementId targetTypeId)
        {
            var typeParam = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM);
            if (typeParam != null && !typeParam.IsReadOnly)
            {
                typeParam.Set(targetTypeId);
            }
            else
            {
                elem.ChangeTypeId(targetTypeId);
            }
        }

        private static ElementType EnsureCorrectTypeNameFast(
            ElementType source, bool isUnderground, bool excludeFromRename,
            Dictionary<(Type classType, string name), ElementType> typeIndex,
            ProcessingReport report)
        {
            if (excludeFromRename || source == null) return source;

            string name = source.Name?.TrimEnd() ?? "";
            bool endsWithBgl = name.EndsWith("_BGL", StringComparison.OrdinalIgnoreCase);
            bool endsWithDoubleBgl = name.EndsWith("_BGL_BGL", StringComparison.OrdinalIgnoreCase);

            // Быстрая проверка: если элемент уже назван верно, мгновенно возвращаем его без изменений
            if (isUnderground && endsWithBgl && !endsWithDoubleBgl)
            {
                return source;
            }
            if (!isUnderground && !endsWithBgl)
            {
                return source;
            }

            // Иначе очищаем суффиксы и формируем целевое имя
            string cleanName = name;
            while (cleanName.EndsWith("_BGL", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = cleanName.Substring(0, cleanName.Length - 4).TrimEnd();
            }

            string targetName = isUnderground ? cleanName + "_BGL" : cleanName;

            if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            var lookupKey = (source.GetType(), targetName);
            if (typeIndex.TryGetValue(lookupKey, out var existingType))
            {
                return existingType;
            }

            // Типоразмера ещё нет в проекте — создаём дубликат
            try
            {
                var dup = source.Duplicate(targetName);
                typeIndex[lookupKey] = dup;
                report.CreatedTypes++;
                return dup;
            }
            catch
            {
                return source;
            }
        }

        private static bool IsWritableInstanceParam(Element elem, string paramName)
        {
            if (elem == null || string.IsNullOrEmpty(paramName)) return false;
            var param = elem.LookupParameter(paramName);
            return param != null && !param.IsReadOnly;
        }

        private static bool TrySetStringParamIfChanged(Element elem, string paramName, string value)
        {
            if (elem == null || string.IsNullOrEmpty(paramName)) return false;
            var param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;

            if (param.StorageType == StorageType.String)
            {
                string currentVal = param.AsString();
                if (!string.Equals(currentVal, value, StringComparison.Ordinal))
                {
                    param.Set(value ?? "");
                    return true;
                }
            }
            else if (param.StorageType == StorageType.Integer)
            {
                int targetInt = 0;
                if (string.Equals(value, "Да", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "1", StringComparison.Ordinal))
                {
                    targetInt = 1;
                }
                else
                {
                    int.TryParse(value, out targetInt);
                }

                if (param.AsInteger() != targetInt)
                {
                    param.Set(targetInt);
                    return true;
                }
            }
            else if (param.StorageType == StorageType.Double)
            {
                if (double.TryParse(value, out double targetDouble))
                {
                    if (Math.Abs(param.AsDouble() - targetDouble) > 1e-6)
                    {
                        param.Set(targetDouble);
                        return true;
                    }
                }
            }
            return false;
        }

        private static void AddWarning(List<string> list, string text)
        {
            if (!list.Contains(text)) list.Add(text);
        }

        private static string ExtractCodeFromPreview(string preview)
        {
            if (string.IsNullOrWhiteSpace(preview) || preview.StartsWith("—") || preview.StartsWith("─") || preview.StartsWith("⚠️") || preview.Contains("Не удалось") || preview.StartsWith("Не найдено"))
                return null;
            int dashIdx = preview.IndexOf(" — ");
            if (dashIdx > 0)
                return preview.Substring(0, dashIdx).Trim();
            return preview.Trim();
        }
    }

    public class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            bool hasResolvedErrors = false;

            foreach (var failure in failures)
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error)
                {
                    if (failure.HasResolutions())
                    {
                        failuresAccessor.ResolveFailure(failure);
                        hasResolvedErrors = true;
                    }
                }
            }

            return hasResolvedErrors
                ? FailureProcessingResult.ProceedWithCommit
                : FailureProcessingResult.Continue;
        }
    }
}
