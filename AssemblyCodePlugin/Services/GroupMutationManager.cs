using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using AssemblyCodePlugin.Models;

namespace AssemblyCodePlugin.Services
{
    public class ElementMutation
    {
        public ElementId ElementId { get; set; }
        public ElementId OriginalTypeId { get; set; }
        public ElementId TargetTypeId { get; set; }
        public Dictionary<string, string> StringParameters { get; set; } = new Dictionary<string, string>();

        public bool HasChanges()
        {
            return TargetTypeId != null || StringParameters.Count > 0;
        }
    }

    public class GroupCluster
    {
        public GroupType OriginalGroupType { get; set; }
        public List<Group> Instances { get; set; } = new List<Group>();
        // Ключ - локальные координаты элемента внутри группы (формат "X:Y:Z" в мм), Значение - мутация
        public Dictionary<string, ElementMutation> PatternMutations { get; set; } = new Dictionary<string, ElementMutation>();
        public string TargetGroupTypeName { get; set; }
    }

    public class JoinRecord
    {
        public XYZ Center { get; set; }
        public ElementId CategoryId { get; set; }
        public ElementId ExternalElementId { get; set; }
        public bool IsCutting { get; set; }
    }

    public static class GroupMutationManager
    {
        public static double CalculateGroupRotation(Group originalGroup, Group referenceGroup)
        {
            var origIds = originalGroup.GetMemberIds();
            var refIds = referenceGroup.GetMemberIds();
            
            var origOrigin = (originalGroup.Location as LocationPoint)?.Point ?? XYZ.Zero;
            var refOrigin = (referenceGroup.Location as LocationPoint)?.Point ?? XYZ.Zero;
            
            // Найти все LocationCurve в referenceGroup
            var refCurves = new List<Tuple<ElementId, double, double>>(); // ID, Length, DistToOrigin
            foreach (var id in refIds)
            {
                var elem = referenceGroup.Document.GetElement(id);
                if (elem != null && elem.Location is LocationCurve lc && lc.Curve != null && lc.Curve.IsBound)
                {
                    XYZ center = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
                    double dist = center.DistanceTo(refOrigin);
                    refCurves.Add(Tuple.Create(id, lc.Curve.Length, dist));
                }
            }
            
            foreach (var id in origIds)
            {
                var origElem = originalGroup.Document.GetElement(id);
                if (origElem != null && origElem.Location is LocationCurve lcOrig && lcOrig.Curve != null && lcOrig.Curve.IsBound)
                {
                    XYZ centerOrig = (lcOrig.Curve.GetEndPoint(0) + lcOrig.Curve.GetEndPoint(1)) / 2.0;
                    double distOrig = centerOrig.DistanceTo(origOrigin);
                    
                    var match = refCurves.FirstOrDefault(r => 
                        Math.Abs(r.Item2 - lcOrig.Curve.Length) < 1e-4 && 
                        Math.Abs(r.Item3 - distOrig) < 1e-4 &&
                        referenceGroup.Document.GetElement(r.Item1).GetTypeId() == origElem.GetTypeId());
                        
                    if (match != null)
                    {
                        var refElem = referenceGroup.Document.GetElement(match.Item1);
                        var lcRef = refElem.Location as LocationCurve;
                        if (lcRef != null && lcRef.Curve != null && lcRef.Curve.IsBound)
                        {
                            var dirOrig = (lcOrig.Curve.GetEndPoint(1) - lcOrig.Curve.GetEndPoint(0)).Normalize();
                            var dirRef = (lcRef.Curve.GetEndPoint(1) - lcRef.Curve.GetEndPoint(0)).Normalize();
                            
                            double angleOrig = Math.Atan2(dirOrig.Y, dirOrig.X);
                            double angleRef = Math.Atan2(dirRef.Y, dirRef.X);
                            
                            return angleOrig - angleRef;
                        }
                    }
                }
            }
            return 0.0;
        }

        public static Transform GetAccurateGroupTransform(Group group)
        {
            var t = Transform.Identity;
            var loc = group.Location as LocationPoint;
            if (loc != null)
            {
                t.Origin = loc.Point;
            }

            var geomElem = group.get_Geometry(new Options());
            if (geomElem != null)
            {
                foreach (var geomObj in geomElem)
                {
                    if (geomObj is GeometryInstance geomInst)
                    {
                        t.BasisX = geomInst.Transform.BasisX;
                        t.BasisY = geomInst.Transform.BasisY;
                        t.BasisZ = geomInst.Transform.BasisZ;
                        break;
                    }
                }
            }
            return t;
        }

        public static XYZ GetElementCenter(Element elem)
        {
            if (elem.Location is LocationCurve lc && lc.Curve != null && lc.Curve.IsBound)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
            if (elem.Location is LocationPoint lp)
                return lp.Point;
            
            var box = elem.get_BoundingBox(null);
            if (box != null) return (box.Min + box.Max) / 2.0;
            
            return XYZ.Zero;
        }

        public static void ProcessGroups(Document doc, List<GroupCluster> clusters, ProcessingReport report)
        {
            if (clusters == null || clusters.Count == 0) return;

            HashSet<string> existingGroupTypeNames;
            using (var collector = new FilteredElementCollector(doc).OfClass(typeof(GroupType)))
            {
                existingGroupTypeNames = new HashSet<string>(collector.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            }

            int cIdx = 0;
            foreach (var cluster in clusters)
            {
                cIdx++;
                PluginLogger.Log($"   -> Обработка кластера групп {cIdx}/{clusters.Count} (Тип: {cluster.OriginalGroupType.Name}, Экз: {cluster.Instances.Count})");

                if (cluster.PatternMutations.Count == 0) continue;

                var templateGroup = cluster.Instances.First();
                var templateMemberIds = templateGroup.GetMemberIds();

                Group refGroup = null;
                Group phantomGroup = null;
                Group newGroup = null;

                try
                {
                    // Двигаем песочницу ТОЛЬКО по X и Y. Если сдвинуть по Z, элементы получат Z-оффсет,
                    // и при смене типа этот оффсет (например 10000 футов) перенесется на боевые группы!
                    XYZ offset = new XYZ(10000, 10000, 0);
                    var copiedIds = ElementTransformUtils.CopyElement(doc, templateGroup.Id, offset);
                    if (copiedIds == null || copiedIds.Count == 0) continue;
                    
                    phantomGroup = doc.GetElement(copiedIds.First()) as Group;
                    if (phantomGroup == null) continue;

                    // Сбрасываем поворот у копии, чтобы она была в нулевой ориентации.
                    // Создаем временный эталон для вычисления угла поворота
                    refGroup = doc.Create.PlaceGroup(XYZ.Zero, cluster.OriginalGroupType);
                    double rotAngle = CalculateGroupRotation(phantomGroup, refGroup);
                    
                    var phantomLoc = phantomGroup.Location as LocationPoint;
                    XYZ originalPhantomOrigin = phantomLoc != null ? phantomLoc.Point : XYZ.Zero;
                    
                    if (Math.Abs(rotAngle) > 1e-6)
                    {
                        Line axis = Line.CreateBound(originalPhantomOrigin, originalPhantomOrigin + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, phantomGroup.Id, axis, -rotAngle);
                    }

                    // Формируем Идеальный Transform для phantomGroup (угол = 0)
                    var phantomTransform = Transform.Identity;
                    phantomTransform.Origin = originalPhantomOrigin;
                    
                    var elementsForNewGroup = phantomGroup.UngroupMembers();

                    // Применяем мутации на основе локальных координат
                    foreach (var newId in elementsForNewGroup)
                    {
                        var newElem = doc.GetElement(newId);
                        if (newElem == null) continue;

                        XYZ globalCenter = GetElementCenter(newElem);
                        XYZ localCenter = phantomTransform.Inverse.OfPoint(globalCenter);
                        
                        string localKey = $"{Math.Round(localCenter.X * 304.8)}:{Math.Round(localCenter.Y * 304.8)}:{Math.Round(localCenter.Z * 304.8)}";

                        if (cluster.PatternMutations.TryGetValue(localKey, out var mutation))
                        {
                            try
                            {
                                if (mutation.TargetTypeId != null && mutation.TargetTypeId != ElementId.InvalidElementId && newElem.GetTypeId() != mutation.TargetTypeId)
                                {
                                    var typeParam = newElem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM);
                                    if (typeParam != null && !typeParam.IsReadOnly)
                                        typeParam.Set(mutation.TargetTypeId);
                                    else
                                        newElem.ChangeTypeId(mutation.TargetTypeId);
                                }

                                foreach (var kvp in mutation.StringParameters)
                                {
                                    var param = newElem.LookupParameter(kvp.Key);
                                    if (param != null && !param.IsReadOnly)
                                    {
                                        param.Set(kvp.Value);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                PluginLogger.Log($"      [Ошибка] Сбой мутации элемента в группе: {ex.Message}");
                            }
                        }
                    }

                    newGroup = doc.Create.NewGroup(elementsForNewGroup);
                    GroupType newGroupType = newGroup.GroupType;
                    
                    // Вычисляем вектор смещения новой базовой точки в локальных координатах.
                    var newGroupLoc = newGroup.Location as LocationPoint;
                    XYZ newOrigin = newGroupLoc != null ? newGroupLoc.Point : XYZ.Zero;
                    XYZ localDelta = newOrigin - originalPhantomOrigin;

                    string finalName = cluster.TargetGroupTypeName;
                    int nameCounter = 1;
                    while (existingGroupTypeNames.Contains(finalName) && newGroupType.Name != finalName)
                    {
                        finalName = $"{cluster.TargetGroupTypeName}_{nameCounter}";
                        nameCounter++;
                    }
                    newGroupType.Name = finalName;
                    existingGroupTypeNames.Add(finalName);

                    // Запоминаем соединения со внешними элементами до смены типа
                    var joinsByGroup = new Dictionary<ElementId, List<JoinRecord>>();
                    var oldTransforms = new Dictionary<ElementId, Transform>();
                    foreach (var inst in cluster.Instances)
                    {
                        joinsByGroup[inst.Id] = GetExternalJoins(doc, inst);
                        
                        double instAngle = CalculateGroupRotation(inst, refGroup);
                        var instTransform = Transform.Identity;
                        instTransform.Origin = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        instTransform.BasisX = new XYZ(Math.Cos(instAngle), Math.Sin(instAngle), 0);
                        instTransform.BasisY = new XYZ(-Math.Sin(instAngle), Math.Cos(instAngle), 0);
                        
                        oldTransforms[inst.Id] = instTransform;
                    }
                    
                    doc.Delete(refGroup.Id); // Удаляем эталон
                    refGroup = null;

                    foreach (var inst in cluster.Instances)
                    {
                        inst.GroupType = newGroupType;
                        
                        // Компенсируем сдвиг базовой точки, чтобы геометрия осталась на месте
                        if (oldTransforms.TryGetValue(inst.Id, out var oldTransform))
                        {
                            // Преобразуем локальный сдвиг в глобальный вектор с учетом поворота экземпляра
                            XYZ rotatedDelta = oldTransform.OfVector(localDelta);
                            ElementTransformUtils.MoveElement(doc, inst.Id, rotatedDelta);
                        }

                        report.UpdatedElements += inst.GetMemberIds().Count;
                    }

                    // Удаляем песочный экземпляр ТОЛЬКО ПОСЛЕ ТОГО, как тип назначен боевым экземплярам,
                    // чтобы Revit не очистил новый GroupType из-за отсутствия ссылок на него.
                    doc.Delete(newGroup.Id);
                    newGroup = null;

                    // Восстанавливаем соединения
                    foreach (var inst in cluster.Instances)
                    {
                        if (joinsByGroup.TryGetValue(inst.Id, out var joins) && joins.Count > 0)
                        {
                            RestoreExternalJoins(doc, inst, joins);
                        }
                    }

                    PluginLogger.Log($"      -> Создан новый тип группы: {newGroupType.Name}. Обновлено экземпляров: {cluster.Instances.Count}");
                }
                catch (Exception ex)
                {
                    PluginLogger.Log($"      [Ошибка] Не удалось собрать новую группу: {ex.Message}");
                    foreach (var inst in cluster.Instances)
                    {
                        ReportGroupError(inst, $"Сбой сборки новой группы ({ex.Message})", report, doc);
                    }
                }
                finally
                {
                    // Гарантированная очистка временных элементов
                    if (phantomGroup != null && phantomGroup.IsValidObject)
                        try { doc.Delete(phantomGroup.Id); } catch { }
                    if (refGroup != null && refGroup.IsValidObject)
                        try { doc.Delete(refGroup.Id); } catch { }
                }
            }
        }

        private static void ReportGroupError(Group group, string errorMessage, ProcessingReport report, Document doc)
        {
            if (group == null) return;
            var memberIds = group.GetMemberIds();
            foreach (var id in memberIds)
            {
                var elem = doc.GetElement(id);
                if (elem == null) continue;
                var elemType = doc.GetElement(elem.GetTypeId()) as ElementType;
                
                report.GroupedElementItems.Add(new ReportElementItem
                {
                    ElementId = elem.Id.IntegerValue,
                    ElementName = elem.Name ?? "",
                    CategoryName = elem.Category?.Name ?? "",
                    TypeName = elemType?.Name ?? "",
                    Details = $"Сбой в группе: {errorMessage}"
                });
            }
        }

        private static bool IsGroupTypeNameInUse(Document doc, string name)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return existing != null;
        }

        private static List<JoinRecord> GetExternalJoins(Document doc, Group group)
        {
            var records = new List<JoinRecord>();
            if (group == null) return records;

            var memberIds = group.GetMemberIds();
            foreach (var mId in memberIds)
            {
                var elem = doc.GetElement(mId);
                if (elem == null) continue;

                List<ElementId> joinedIds;
                try
                {
                    joinedIds = JoinGeometryUtils.GetJoinedElements(doc, elem).ToList();
                }
                catch
                {
                    continue; // Элемент не поддерживает Join
                }

                var externalIds = joinedIds.Where(id => !memberIds.Contains(id)).ToList();
                if (externalIds.Count == 0) continue;

                var box = elem.get_BoundingBox(null);
                if (box == null) continue;
                XYZ center = (box.Min + box.Max) / 2.0;

                foreach (var extId in externalIds)
                {
                    var extElem = doc.GetElement(extId);
                    if (extElem == null) continue;

                    bool isCutting = false;
                    try
                    {
                        isCutting = JoinGeometryUtils.IsCuttingElementInJoin(doc, elem, extElem);
                    }
                    catch { }

                    records.Add(new JoinRecord
                    {
                        Center = center,
                        CategoryId = elem.Category?.Id,
                        ExternalElementId = extId,
                        IsCutting = isCutting
                    });
                }
            }
            return records;
        }

        private static void RestoreExternalJoins(Document doc, Group group, List<JoinRecord> records)
        {
            if (records == null || records.Count == 0 || group == null) return;

            var newMembers = group.GetMemberIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();

            foreach (var record in records)
            {
                var extElem = doc.GetElement(record.ExternalElementId);
                if (extElem == null) continue;

                Element bestMatch = null;
                double minDist = double.MaxValue;
                
                foreach (var newElem in newMembers)
                {
                    if (newElem.Category?.Id != record.CategoryId) continue;
                    var box = newElem.get_BoundingBox(null);
                    if (box == null) continue;
                    
                    XYZ center = (box.Min + box.Max) / 2.0;
                    double dist = center.DistanceTo(record.Center);
                    if (dist < minDist && dist < 1.0) // Допуск ~30 см (1 фут)
                    {
                        minDist = dist;
                        bestMatch = newElem;
                    }
                }

                if (bestMatch != null)
                {
                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, bestMatch, extElem))
                        {
                            JoinGeometryUtils.JoinGeometry(doc, bestMatch, extElem);
                        }
                        
                        bool currentIsCutting = JoinGeometryUtils.IsCuttingElementInJoin(doc, bestMatch, extElem);
                        if (currentIsCutting != record.IsCutting)
                        {
                            JoinGeometryUtils.SwitchJoinOrder(doc, bestMatch, extElem);
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLogger.Log($"      [Внимание] Не удалось восстановить Join для элемента {bestMatch.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }
        }
    }
}
