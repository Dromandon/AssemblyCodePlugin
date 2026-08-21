using System;
using System.Collections.Generic;
using System.Linq;
using AssemblyCodePlugin.Models;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.Services
{
    public struct FloorZoneData
    {
        public ElementId FloorId { get; set; }
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
    }

    /// <summary>
    /// Контекст нулевого уровня для проекта с предвычисленными границами плит (для максимального быстродействия).
    /// </summary>
    public class ZeroZoneContext
    {
        public double LowElevation { get; }
        public double HighElevation { get; }
        public List<FloorZoneData> ZeroLevelFloors { get; }

        public ZeroZoneContext(double lowElev, double highElev, List<FloorZoneData> zeroFloors)
        {
            LowElevation = lowElev;
            HighElevation = highElev;
            ZeroLevelFloors = zeroFloors ?? new List<FloorZoneData>();
        }
    }

    public static class ZoneDeterminator
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double ToleranceFeet = 50.0 * MmToFeet; // погрешность 50 мм

        public static ZeroZoneContext BuildContext(Document doc, ZeroLevelSettings settings)
        {
            Dictionary<string, double> levelElevations;
            using (var collector = new FilteredElementCollector(doc).OfClass(typeof(Level)))
            {
                levelElevations = collector.Cast<Level>().ToDictionary(l => l.Name, l => l.Elevation, StringComparer.OrdinalIgnoreCase);
            }

            double GetElevation(string name) =>
                !string.IsNullOrEmpty(name) && levelElevations.TryGetValue(name, out var e) ? e : 0.0;

            double lowElev = GetElevation(settings.LowLevelName)
                             + settings.LowLevelOffsetMm * MmToFeet;
            double highElev = GetElevation(settings.HighLevelName)
                              + settings.HighLevelOffsetMm * MmToFeet;

            if (lowElev > highElev)
            {
                double tmp = lowElev;
                lowElev = highElev;
                highElev = tmp;
            }

            // Находим все плиты перекрытий и сразу предвычисляем их BoundingBox в память C#
            List<Element> allFloors;
            using (var collector = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType())
            {
                allFloors = collector.ToList();
            }

            var zeroFloors = new List<FloorZoneData>();
            foreach (var f in allFloors)
            {
                var box = f.get_BoundingBox(null);
                if (box != null)
                {
                    double topZ = box.Max.Z;
                    if (topZ >= lowElev - ToleranceFeet && topZ <= highElev + ToleranceFeet)
                    {
                        zeroFloors.Add(new FloorZoneData
                        {
                            FloorId = f.Id,
                            MinX = box.Min.X,
                            MaxX = box.Max.X,
                            MinY = box.Min.Y,
                            MaxY = box.Max.Y,
                            MinZ = box.Min.Z,
                            MaxZ = box.Max.Z
                        });
                    }
                }
            }

            return new ZeroZoneContext(lowElev, highElev, zeroFloors);
        }

        public static ZoneResult DetermineZone(Element elem, ZeroZoneContext ctx)
        {
            if (elem == null || ctx == null) return ZoneResult.Unknown;

            var cat = elem.Category?.Id;
            if (cat == null) return ZoneResult.Unknown;

            int catId = cat.IntegerValue;

            if (catId == (int)BuiltInCategory.OST_StructuralFoundation)
                return ZoneResult.BelowZero;

            // Плиты перекрытия на отметке нуля (или ниже верхнего уровня нуля) всегда относятся к ПОДЗЕМНОЙ части (BelowZero)
            if (catId == (int)BuiltInCategory.OST_Floors || catId == (int)BuiltInCategory.OST_EdgeSlab)
            {
                return CheckFloorZoneFast(elem, ctx.HighElevation);
            }

            // 1. Мгновенная проверка по уровню для стен, колонн и других элементов
            var fastZone = FastCheckByLevel(elem, ctx.LowElevation, ctx.HighElevation);
            if (fastZone != ZoneResult.Unknown)
                return fastZone;

            var box = elem.get_BoundingBox(null);
            if (box == null)
                return FallbackDetermineZone(elem, ctx.LowElevation, ctx.HighElevation);

            double minZ = box.Min.Z;
            double maxZ = box.Max.Z;
            double centerZ = (minZ + maxZ) / 2.0;

            if (minZ >= ctx.HighElevation - ToleranceFeet)
                return ZoneResult.AboveZero;

            if (maxZ <= ctx.LowElevation + ToleranceFeet)
                return ZoneResult.BelowZero;

            // Для лестниц (включая обобщённые модели лестниц) оставляем старый алгоритм по центру BoundingBox
            if (catId == (int)BuiltInCategory.OST_Stairs ||
                catId == (int)BuiltInCategory.OST_StairsRuns ||
                catId == (int)BuiltInCategory.OST_StairsLandings ||
                catId == (int)BuiltInCategory.OST_Ramps ||
                IsStairGenericModel(elem, catId))
            {
                return centerZ >= ctx.HighElevation ? ZoneResult.AboveZero : ZoneResult.BelowZero;
            }

            // Для остальных элементов, пересекающих отметки нуля:
            // проверяем плиты нуля, которые он пересекает по XY, и сравниваем чистые отметки верха/низа
            return DetermineElementInZeroZone(box, ctx);
        }

        private static bool IsStairGenericModel(Element elem, int catId)
        {
            if (catId != (int)BuiltInCategory.OST_GenericModel || elem == null) return false;
            try
            {
                string famName = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                if (string.IsNullOrEmpty(famName))
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                    {
                        var et = elem.Document.GetElement(typeId) as ElementType;
                        famName = et?.FamilyName ?? "";
                    }
                }

                if (famName.IndexOf("Landing", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                return famName.IndexOf("_STA_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       famName.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static ZoneResult CheckFloorZoneFast(Element elem, double highElev)
        {
            try
            {
                // Сначала пробуем узнать отметку через уровень и смещение
                var levelId = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId();
                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    var lv = elem.Document.GetElement(levelId) as Level;
                    if (lv != null)
                    {
                        double elev = lv.Elevation;
                        double offset = 0.0;
                        var offsetParam = elem.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                        if (offsetParam != null) offset = offsetParam.AsDouble();

                        double topZ = elev + offset;
                        // Если верх плиты <= отметке нуля (например 0.000 + допуск) -> подземная
                        if (topZ <= highElev + ToleranceFeet)
                            return ZoneResult.BelowZero;
                        return ZoneResult.AboveZero;
                    }
                }

                // Резерв через BoundingBox
                var box = elem.get_BoundingBox(null);
                if (box != null)
                {
                    return box.Max.Z <= highElev + ToleranceFeet ? ZoneResult.BelowZero : ZoneResult.AboveZero;
                }
            }
            catch { }
            return ZoneResult.Unknown;
        }

        private static ZoneResult DetermineElementInZeroZone(BoundingBoxXYZ elemBox, ZeroZoneContext ctx)
        {
            double minZ = elemBox.Min.Z;
            double maxZ = elemBox.Max.Z;
            double centerZ = (minZ + maxZ) / 2.0;

            double minX = elemBox.Min.X;
            double maxX = elemBox.Max.X;
            double minY = elemBox.Min.Y;
            double maxY = elemBox.Max.Y;

            // Быстрая проверка пересечения XY с плитами из кэша в памяти C#
            bool foundOverlap = false;
            double maxFloorTopZ = double.MinValue;

            int count = ctx.ZeroLevelFloors.Count;
            for (int i = 0; i < count; i++)
            {
                var f = ctx.ZeroLevelFloors[i];
                bool overlapXY = !(maxX < f.MinX || minX > f.MaxX || maxY < f.MinY || minY > f.MaxY);
                if (overlapXY)
                {
                    foundOverlap = true;
                    if (f.MaxZ > maxFloorTopZ) maxFloorTopZ = f.MaxZ;
                }
            }

            if (foundOverlap)
            {
                // 1. Если верх элемента совпадает с верхом наивысшей пересекаемой плиты нуля
                // (или находится ниже верха плиты) — это касание снизу, элемент под плитой -> BelowZero
                if (maxZ <= maxFloorTopZ + ToleranceFeet)
                    return ZoneResult.BelowZero;

                // 2. Если низ элемента совпадает с верхом плиты (или находится выше) -> AboveZero
                if (minZ >= maxFloorTopZ - ToleranceFeet)
                    return ZoneResult.AboveZero;

                return ZoneResult.Spanning;
            }

            return centerZ >= ctx.HighElevation ? ZoneResult.AboveZero : ZoneResult.BelowZero;
        }

        private static ZoneResult FallbackDetermineZone(Element elem, double zeroLowElev, double zeroHighElev)
        {
            try
            {
                var levelId = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId()
                           ?? elem.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId()
                           ?? elem.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();

                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    var lv = elem.Document.GetElement(levelId) as Level;
                    if (lv != null)
                    {
                        return lv.Elevation >= zeroHighElev ? ZoneResult.AboveZero : ZoneResult.BelowZero;
                    }
                }
            }
            catch { }
            return ZoneResult.Unknown;
        }

        private static ZoneResult FastCheckByLevel(Element elem, double lowElev, double highElev)
        {
            try
            {
                var levelId = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId()
                           ?? elem.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId()
                           ?? elem.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();

                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    var lv = elem.Document.GetElement(levelId) as Level;
                    if (lv != null)
                    {
                        double elev = lv.Elevation;

                        // Учитываем отступ (смещение) от уровня
                        double offset = 0.0;
                        var offsetParam = elem.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)
                                       ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM)
                                       ?? elem.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
                                       ?? elem.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);

                        if (offsetParam != null)
                        {
                            offset = offsetParam.AsDouble();
                        }

                        double baseZ = elev + offset;

                        // Также пытаемся оценить высоту элемента (у стен/колонн)
                        double height = 0.0;
                        var heightParam = elem.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)
                                       ?? elem.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM);
                        if (heightParam != null) height = heightParam.AsDouble();

                        double topZ = height > 0 ? baseZ + height : baseZ;

                        // Сравниваем чистые цифры (без обращения к 3D геометрии)
                        if (baseZ >= highElev - ToleranceFeet)
                            return ZoneResult.AboveZero;

                        if (topZ <= lowElev + ToleranceFeet && topZ != baseZ)
                            return ZoneResult.BelowZero;
                        if (baseZ <= lowElev - 10.0 * MmToFeet && height == 0)
                            return ZoneResult.BelowZero;
                    }
                }
            }
            catch { }
            return ZoneResult.Unknown;
        }


    }
}
