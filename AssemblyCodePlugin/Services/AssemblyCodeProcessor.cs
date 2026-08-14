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

            PluginLogger.Log("3. Сбор элементов целевых категорий из модели...");
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.Category != null && e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_SectionBox)
                .Where(e => HasParameter(e, doc, settings.AssemblyCodeParamName))
                .ToList();

            report.TotalElements = allElements.Count;
            PluginLogger.Log($"   -> Всего собранных элементов: {allElements.Count}");

            // Запоминаем элементы в группах для последующей кластеризации
            var groupedElements = allElements.Where(e => e.GroupId != ElementId.InvalidElementId).ToList();
            PluginLogger.Log($"   -> Элементов в группах: {groupedElements.Count}");

            // ─── ЭТАП 1: ГРУППИРОВКА ПО ТИПОРАЗМЕРАМ ────────────────────────────────
            var elementsByTypeId = allElements
                .GroupBy(e => e.GetTypeId())
                .ToList();

            PluginLogger.Log($"   -> Сгруппировано по типам: {elementsByTypeId.Count} групп типоразмеров.");

            var pendingGroupMutations = new Dictionary<ElementId, ElementMutation>();

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

                    // Разделяем элементы группы по правилу и зоне
                    var swZone = System.Diagnostics.Stopwatch.StartNew();
                    
                    var elementsByRuleAndZone = new Dictionary<Tuple<ClassificationRule, ZoneResult>, List<Element>>();
                    var unassignedElements = new List<Element>();

                    foreach (var elem in group)
                    {
                        var rule = ElementTypeDetector.FindMatchingRule(elem, elemType, settings.Rules);
                        if (rule == null)
                        {
                            unassignedElements.Add(elem);
                            continue;
                        }

                        var zone = settings.DisableZoneSplit ? ZoneResult.AboveZero : ZoneDeterminator.DetermineZone(elem, zeroCtx);
                        if (!settings.DisableZoneSplit && zone == ZoneResult.Spanning)
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
                        
                        var key = Tuple.Create(rule, zone);
                        if (!elementsByRuleAndZone.TryGetValue(key, out var list))
                        {
                            list = new List<Element>();
                            elementsByRuleAndZone[key] = list;
                        }
                        list.Add(elem);
                    }
                    swZone.Stop();
                    report.ZoneCalcSeconds += swZone.Elapsed.TotalSeconds;

                    if (unassignedElements.Count > 0)
                    {
                        if (unassignedElements.Count == group.Count())
                            PluginLogger.Log($"       -> Правило не найдено для всех ({unassignedElements.Count}) экз. Пропуск.");
                        else
                            PluginLogger.Log($"       -> Для {unassignedElements.Count} экз. правило не найдено. Пропуск.");
                        
                        foreach (var elem in unassignedElements)
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
                    }

                    foreach (var kvp in elementsByRuleAndZone)
                    {
                        var rule = kvp.Key.Item1;
                        var zone = kvp.Key.Item2;
                        var elems = kvp.Value;
                        bool isUnderground = zone == ZoneResult.BelowZero;

                        bool canRenameSourceType = (elems.Count == countInGroup);

                        PluginLogger.Log($"       -> Правило '{rule.ElementTypeName}': {(isUnderground ? "подземных" : "надземных")}={elems.Count}");
                        
                        ProcessSubgroup(
                            doc, elems, elemType, isUnderground,
                            rule, classifierItems, byCode, settings, typesByNameAndClass, report, canRenameSourceType, pendingGroupMutations);
                    }
                }

                // После обработки всех элементов по типам, запускаем обработку сгруппированных элементов
                if (pendingGroupMutations.Count > 0)
                {
                    PluginLogger.Log("4.5. Обработка мутаций для сгруппированных элементов (Phantom Group Pipeline)...");
                    var clusters = ClusterizeGroupMutations(doc, groupedElements, pendingGroupMutations);
                    GroupMutationManager.ProcessGroups(doc, clusters, report);
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

        private static bool HasParameter(Element e, Document doc, string paramName)
        {
            if (e.LookupParameter(paramName) != null) return true;
            var type = doc.GetElement(e.GetTypeId()) as ElementType;
            if (type != null && type.LookupParameter(paramName) != null) return true;
            return false;
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
            ProcessingReport report,
            bool canRenameSourceType,
            Dictionary<ElementId, ElementMutation> pendingGroupMutations)
        {
            bool excludeFromBgl = (settings != null && settings.DisableZoneSplit) || rule.ExcludeFromBglRename || (settings != null && settings.NeverAddBglSuffix);
            string ruleSuffix = (rule.RevitFilter != null && rule.RevitFilter.AppendTypeSuffix) ? rule.RevitFilter.TypeSuffix : "";
            ElementType targetType = EnsureCorrectTypeNameFast(
                sourceType, isUnderground, excludeFromBgl, ruleSuffix, typeIndex, report, canRenameSourceType);

            PluginLogger.Log($"          -> [{(isUnderground ? "ПОДЗЕМНАЯ" : "НАДЗЕМНАЯ")} ({elements.Count} экз.)] Целевой тип: '{targetType.Name}'");

            // 2. Назначаем Assembly Code и Описание В ТИПОРАЗМЕР (1 раз на тип!)
            bool skipClassification = isUnderground ? rule.SkipUnderground : rule.SkipAboveGround;
            if (!skipClassification)
            {
                string verifiedRaw = isUnderground ? rule.VerifiedBelowCode : rule.VerifiedAboveCode;
                string code = ExtractCodeFromPreview(verifiedRaw);
                AssemblyCodeItem matchedItem = null;

                if (string.IsNullOrEmpty(code))
                {
                    var searchRule = isUnderground
                        ? rule.UndergroundSearchRule
                        : rule.AboveGroundSearchRule;

                    matchedItem = ClassifierRatingEngine.FindBestMatch(
                        classifierItems, byCode, searchRule, isUnderground);

                    code = matchedItem != null ? matchedItem.Code : "";
                }
                else if (byCode != null && byCode.TryGetValue(code, out var itemByCode))
                {
                    matchedItem = itemByCode;
                }

                if (!string.IsNullOrEmpty(code))
                {
                    // 1. Записываем код в типоразмер
                    TrySetStringParamIfChanged(targetType, settings.AssemblyCodeParamName, code);

                    // Если параметр кода является параметром экземпляра — записываем во все элементы
                    bool isCodeInstanceParam = elements.Count > 0 && IsWritableInstanceParam(elements[0], settings.AssemblyCodeParamName);
                    if (isCodeInstanceParam)
                    {
                        foreach (var elem in elements)
                        {
                            SetInstanceParamSafe(elem, settings.AssemblyCodeParamName, code, doc, pendingGroupMutations);
                        }
                    }

                    // 2. Записываем описание, если настроен параметр описания
                    if (!string.IsNullOrWhiteSpace(settings.AssemblyDescriptionParamName) && matchedItem != null && !string.IsNullOrEmpty(matchedItem.Description))
                    {
                        TrySetStringParamIfChanged(targetType, settings.AssemblyDescriptionParamName, matchedItem.Description);

                        bool isDescInstanceParam = elements.Count > 0 && IsWritableInstanceParam(elements[0], settings.AssemblyDescriptionParamName);
                        if (isDescInstanceParam)
                        {
                            foreach (var elem in elements)
                            {
                                SetInstanceParamSafe(elem, settings.AssemblyDescriptionParamName, matchedItem.Description, doc, pendingGroupMutations);
                            }
                        }
                    }
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

            bool isInstanceParam = false;
            string undergroundText = "";
            if (settings == null || !settings.DisableZoneSplit)
            {
                // 3. Записываем FAM_Underground в ТИПОРАЗМЕР (если это параметр типа)
                undergroundText = isUnderground
                    ? settings.UndergroundValueText
                    : settings.AbovegroundValueText;

                TrySetStringParamIfChanged(targetType, settings.UndergroundParamName, undergroundText);

                // Проверяем 1 раз для подгруппы: является ли FAM_Underground параметром ЭКЗЕМПЛЯРА
                isInstanceParam = elements.Count > 0 && IsWritableInstanceParam(elements[0], settings.UndergroundParamName);
            }

            // 4. Обходим элементы подгруппы:
            var swChange = System.Diagnostics.Stopwatch.StartNew();
            int changedTypeCount = 0;
            foreach (var elem in elements)
            {
                if (elem.GetTypeId() != targetType.Id)
                {
                    ChangeTypeIdSafe(elem, targetType.Id, pendingGroupMutations);
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
                    SetInstanceParamSafe(elem, settings.UndergroundParamName, undergroundText, doc, pendingGroupMutations);
                }
                swParam.Stop();
                report.ParamSetSeconds += swParam.Elapsed.TotalSeconds;
            }
            report.UpdatedElements += elements.Count;
        }

        private static void ChangeTypeIdSafe(Element elem, ElementId targetTypeId, Dictionary<ElementId, ElementMutation> pending)
        {
            if (elem == null || targetTypeId == null || targetTypeId == ElementId.InvalidElementId) return;
            if (elem.GetTypeId() == targetTypeId) return;

            if (elem.GroupId != ElementId.InvalidElementId)
            {
                if (!pending.TryGetValue(elem.Id, out var mut))
                {
                    mut = new ElementMutation { ElementId = elem.Id, OriginalTypeId = elem.GetTypeId() };
                    pending[elem.Id] = mut;
                }
                mut.TargetTypeId = targetTypeId;
            }
            else
            {
                FastChangeTypeId(elem, targetTypeId);
            }
        }

        private static void SetInstanceParamSafe(Element elem, string paramName, string value, Document doc, Dictionary<ElementId, ElementMutation> pending)
        {
            if (elem == null || string.IsNullOrEmpty(paramName)) return;

            if (elem.GroupId != ElementId.InvalidElementId)
            {
                var param = elem.LookupParameter(paramName);
                if (param == null || param.IsReadOnly) return;
                
                // Проверяем VariesAcrossGroups
                bool varies = false;
                if (param.Definition is InternalDefinition id)
                {
                    try { varies = id.VariesAcrossGroups; } catch { }
                }

                if (varies)
                {
                    TrySetStringParamIfChanged(elem, paramName, value);
                }
                else
                {
                    if (param.AsString() == value || (string.IsNullOrEmpty(param.AsString()) && string.IsNullOrEmpty(value))) return;

                    if (!pending.TryGetValue(elem.Id, out var mut))
                    {
                        mut = new ElementMutation { ElementId = elem.Id, OriginalTypeId = elem.GetTypeId() };
                        pending[elem.Id] = mut;
                    }
                    mut.StringParameters[paramName] = value;
                }
            }
            else
            {
                TrySetStringParamIfChanged(elem, paramName, value);
            }
        }

        private static List<GroupCluster> ClusterizeGroupMutations(
            Document doc, 
            List<Element> groupedElements, 
            Dictionary<ElementId, ElementMutation> pendingGroupMutations)
        {
            var groupIds = groupedElements.Select(e => e.GroupId).Distinct().ToList();
            var clusters = new List<GroupCluster>();

            var groupsByType = groupIds
                .Select(id => doc.GetElement(id) as Group)
                .Where(g => g != null)
                .GroupBy(g => g.GroupType.Id)
                .ToList();

            foreach (var typeGroup in groupsByType)
            {
                var groupType = doc.GetElement(typeGroup.Key) as GroupType;
                if (groupType == null) continue;

                // Быстрая проверка: есть ли вообще мутации для этой группы?
                bool hasMutations = false;
                foreach (var groupInst in typeGroup)
                {
                    foreach (var mId in groupInst.GetMemberIds())
                    {
                        if (pendingGroupMutations.TryGetValue(mId, out var mut) && mut.HasChanges())
                        {
                            hasMutations = true;
                            break;
                        }
                    }
                    if (hasMutations) break;
                }

                if (!hasMutations) continue;

                var firstInst = typeGroup.FirstOrDefault();
                bool has2DElements = false;
                if (firstInst != null)
                {
                    foreach (var mId in firstInst.GetMemberIds())
                    {
                        var mElem = doc.GetElement(mId);
                        if (mElem != null && mElem.ViewSpecific)
                        {
                            has2DElements = true;
                            break;
                        }
                    }
                }

                if (has2DElements)
                {
                    PluginLogger.Log($"       [Внимание] Группа '{groupType.Name}' пропущена, так как содержит 2D-элементы (ViewSpecific).");
                    continue;
                }

                var clustersForType = new Dictionary<string, GroupCluster>();
                
                // Создаем эталонный экземпляр в 0,0,0 для вычисления точного угла поворота
                Group referenceGroup = doc.Create.PlaceGroup(XYZ.Zero, groupType);

                foreach (var groupInst in typeGroup)
                {
                    var memberIds = groupInst.GetMemberIds();
                    
                    double rotAngle = GroupMutationManager.CalculateGroupRotation(groupInst, referenceGroup);
                    Transform groupTransform = Transform.Identity;
                    groupTransform.Origin = (groupInst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                    groupTransform.BasisX = new XYZ(Math.Cos(rotAngle), Math.Sin(rotAngle), 0);
                    groupTransform.BasisY = new XYZ(-Math.Sin(rotAngle), Math.Cos(rotAngle), 0);
                    
                    var instMutations = new Dictionary<string, ElementMutation>(); // Ключ - локальные координаты
                    var hashParts = new List<string>();
                    
                    for (int i = 0; i < memberIds.Count; i++)
                    {
                        if (pendingGroupMutations.TryGetValue(memberIds[i], out var mut) && mut.HasChanges())
                        {
                            var elem = doc.GetElement(memberIds[i]);
                            if (elem == null) continue;
                            
                            XYZ globalCenter = GroupMutationManager.GetElementCenter(elem);
                            XYZ localCenter = groupTransform.Inverse.OfPoint(globalCenter);
                            
                            // Округляем до миллиметра для надежного сравнения (1 фут = 304.8 мм)
                            string localKey = $"{Math.Round(localCenter.X * 304.8)}:{Math.Round(localCenter.Y * 304.8)}:{Math.Round(localCenter.Z * 304.8)}";
                            
                            string mutStr = $"{localKey}:T={mut.TargetTypeId?.IntegerValue ?? 0};";
                            foreach (var p in mut.StringParameters.OrderBy(k => k.Key))
                            {
                                mutStr += $"{p.Key}={p.Value};";
                            }
                            hashParts.Add(mutStr);
                            instMutations[localKey] = mut;
                        }
                    }

                    if (hashParts.Count == 0) continue;

                    hashParts.Sort(); // Сортируем, чтобы порядок элементов не имел значения
                    string hash = string.Join("|", hashParts);

                    if (!clustersForType.TryGetValue(hash, out var cluster))
                    {
                        cluster = new GroupCluster
                        {
                            OriginalGroupType = groupType,
                            PatternMutations = instMutations, // Мутации теперь по локальному ключу
                            TargetGroupTypeName = groupType.Name
                        };
                        clustersForType[hash] = cluster;
                    }
                    
                    cluster.Instances.Add(groupInst);
                }
                
                doc.Delete(referenceGroup.Id); // Удаляем эталон после обработки типа

                foreach (var kvp in clustersForType)
                {
                    var cluster = kvp.Value;
                    string suffix = "";
                    foreach (var mut in cluster.PatternMutations.Values)
                    {
                        if (mut.TargetTypeId != null)
                        {
                            var t = doc.GetElement(mut.TargetTypeId) as ElementType;
                            if (t != null && t.Name.Contains("_"))
                            {
                                int idx = t.Name.LastIndexOf('_');
                                if (idx > 0)
                                {
                                    string potentialSuffix = t.Name.Substring(idx);
                                    if (potentialSuffix == "_BGL" || potentialSuffix.StartsWith("_L") || potentialSuffix.StartsWith("_S"))
                                    {
                                        suffix = potentialSuffix;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(suffix)) cluster.TargetGroupTypeName += suffix;
                    
                    clusters.Add(cluster);
                }
            }

            return clusters;
        }

        private static void FastChangeTypeId(Element elem, ElementId targetTypeId)
        {
            if (elem == null || targetTypeId == null || targetTypeId == ElementId.InvalidElementId) return;

            // Если элемент внутри группы, и мы дошли сюда, значит мы пытаемся изменить тип напрямую (допустим, для свободных).
            // Если он в группе - пропустить.
            if (elem.GroupId != ElementId.InvalidElementId) return;

            try
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
            catch (Exception ex)
            {
                PluginLogger.Log($"          [Внимание] Не удалось изменить TypeId у элемента {elem.Id.IntegerValue}: {ex.Message}");
            }
        }

        private static ElementType EnsureCorrectTypeNameFast(
            ElementType source, bool isUnderground, bool excludeFromRename, string ruleTypeSuffix,
            Dictionary<(Type classType, string name), ElementType> typeIndex,
            ProcessingReport report,
            bool canRenameSourceType)
        {
            if (source == null) return source;

            string name = source.Name?.TrimEnd() ?? "";
            string cleanName = name;
            while (cleanName.EndsWith("_BGL", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = cleanName.Substring(0, cleanName.Length - 4).TrimEnd();
            }

            string suffixToAdd = "";
            if (!string.IsNullOrWhiteSpace(ruleTypeSuffix))
            {
                string ts = ruleTypeSuffix.Trim();
                if (!cleanName.EndsWith(ts, StringComparison.OrdinalIgnoreCase))
                {
                    suffixToAdd = ts;
                }
            }

            string targetName = cleanName + suffixToAdd;
            if (isUnderground && !excludeFromRename)
            {
                targetName += "_BGL";
            }

            if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            var lookupKey = (source.GetType(), targetName);
            if (typeIndex.TryGetValue(lookupKey, out var existingType))
            {
                return existingType;
            }

            // Если все обрабатываемые элементы исходного типа идут в этот новый тип,
            // мы можем не создавать дубликат, а безопасно переименовать сам тип!
            // Это спасёт сгруппированные элементы от разгруппировки (ибо мы меняем имя типа, а не TypeId у экземпляров).
            if (canRenameSourceType)
            {
                try
                {
                    source.Name = targetName;
                    typeIndex[lookupKey] = source;
                    return source;
                }
                catch
                {
                    // Скорее всего имя уже занято. Идём дальше и пытаемся создать дубликат.
                }
            }

            // Типоразмера ещё нет в проекте (или не смогли переименовать) — создаём дубликат
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
            try
            {
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
            }
            catch (Exception ex)
            {
                if (elem.GroupId != ElementId.InvalidElementId)
                {
                    PluginLogger.Log($"          [Внимание] Параметр '{paramName}' у элемента {elem.Id.IntegerValue} в группе {elem.GroupId.IntegerValue} не может быть изменён напрямую: {ex.Message}");
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
