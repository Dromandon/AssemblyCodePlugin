using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.Services
{
    public class ParamInfo
    {
        public string Name { get; set; }
        public StorageType StorageType { get; set; }
        public bool IsYesNo { get; set; }

        public override string ToString() => Name;
    }

    public static class ParameterCollector
    {
        public static List<ParamInfo> CollectParameters(Document doc)
        {
            var result = new Dictionary<string, ParamInfo>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return new List<ParamInfo>();

            try
            {
                // 1. Собираем параметры с типоразмеров и экземпляров
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => e.Category != null)
                    .Take(150);

                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements()
                    .Where(e => e.Category != null)
                    .Take(150);

                foreach (var elem in instances.Concat(types))
                {
                    if (elem == null) continue;
                    foreach (Parameter p in elem.Parameters)
                    {
                        if (p == null || p.Definition == null) continue;
                        string name = p.Definition.Name;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (!result.ContainsKey(name))
                        {
                            bool isYesNo = IsYesNoParameter(p);
                            result[name] = new ParamInfo
                            {
                                Name = name,
                                StorageType = p.StorageType,
                                IsYesNo = isYesNo
                            };
                        }
                    }
                }

                // 2. Добавляем параметры из привязок проекта (ParameterBindings)
                var bindingIter = doc.ParameterBindings.ForwardIterator();
                while (bindingIter.MoveNext())
                {
                    if (bindingIter.Key is Definition def && !string.IsNullOrWhiteSpace(def.Name))
                    {
                        string name = def.Name;
                        if (!result.ContainsKey(name))
                        {
                            bool isYesNo = IsYesNoDefinition(def);
                            result[name] = new ParamInfo
                            {
                                Name = name,
                                StorageType = isYesNo ? StorageType.Integer : StorageType.String,
                                IsYesNo = isYesNo
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"Ошибка сбора параметров: {ex.Message}");
            }

            return result.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static bool IsYesNoParameter(Parameter p)
        {
            if (p == null || p.Definition == null) return false;
            if (p.StorageType != StorageType.Integer) return false;

            return IsYesNoDefinition(p.Definition);
        }

        private static bool IsYesNoDefinition(Definition def)
        {
            if (def == null) return false;
            try
            {
#if REVIT2023_OR_GREATER
                var spec = def.GetDataType();
                if (spec == SpecTypeId.Boolean.YesNo) return true;
#else
#pragma warning disable CS0618
                if (def.ParameterType == ParameterType.YesNo) return true;
#pragma warning restore CS0618
#endif
            }
            catch { }

            return false;
        }
    }
}
