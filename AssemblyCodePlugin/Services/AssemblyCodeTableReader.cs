using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace AssemblyCodePlugin.Services
{
    public class AssemblyCodeItem
    {
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string ParentCode { get; set; } = "";
        public string CategoryPath { get; set; } = "";
        public bool HasChildren { get; set; } = false;
        public int Level { get; set; } = -1;
    }

    public class CrookedCodeInfo
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public int OriginalLevel { get; set; }
        public int CorrectedLevel { get; set; }
    }
    public static class AssemblyCodeTableReader
    {
        public static string GetClassifierFileName(Document doc)
        {
            if (doc == null) return "";
            try
            {
                var tableRef = AssemblyCodeTable.GetAssemblyCodeTable(doc)?.GetExternalFileReference();
                if (tableRef != null)
                {
                    var mp = tableRef.GetAbsolutePath();
                    string path = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                    if (!string.IsNullOrEmpty(path))
                        return Path.GetFileName(path);
                }
            }
            catch { }
            return "";
        }

        public static List<CrookedCodeInfo> CrookedCodes { get; set; } = new List<CrookedCodeInfo>();
        public static bool CrookedCodesExceedLevel5 { get; set; } = false;

        public static (IReadOnlyList<AssemblyCodeItem> items,
                        IReadOnlyDictionary<string, AssemblyCodeItem> byCode,
                        bool hasDeepClassifier)
            ReadClassifier(Document doc, string customPath = null, bool ignoreDeep = false)
        {
            string filePath = customPath;

            if (string.IsNullOrEmpty(filePath) && doc != null)
            {
                try
                {
                    var assemblyCodeTable = AssemblyCodeTable.GetAssemblyCodeTable(doc);
                    if (assemblyCodeTable != null)
                    {
                        var tableRef = assemblyCodeTable.GetExternalFileReference();
                        if (tableRef != null)
                        {
                            var mp = tableRef.GetAbsolutePath();
                            filePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        }
                    }
                }
                catch { /* не удалось — будем без файла */ }
            }

            var items = new List<AssemblyCodeItem>();
            bool hasDeepClassifier = false;
            var byCode = new Dictionary<string, AssemblyCodeItem>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                // Попытаемся прочитать файл в UTF-16, если это он, либо в системной кодировке, так как Revit Assembly Code бывает в 1251
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251)); // UniformatClassification.txt в СНГ часто 1251
                }
                catch
                {
                    lines = File.ReadAllLines(filePath, Encoding.UTF8);
                }

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;

                    string col3 = parts.Length > 2 ? parts[2].Trim() : "";
                    int.TryParse(col3, out int parsedLevel);

                    var item = new AssemblyCodeItem
                    {
                        Code = parts[0].Trim(),
                        Description = parts[1].Trim(),
                        ParentCode = col3,
                        Level = parsedLevel > 0 ? parsedLevel : -1
                    };
                    items.Add(item);
                    byCode[item.Code] = item;
                }

                hasDeepClassifier = items.Any(x => x.Level > 5);
                if (ignoreDeep)
                {
                    items.RemoveAll(x => x.Level > 5);
                    byCode = items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
                }

                CrookedCodes.Clear();
                CrookedCodesExceedLevel5 = false;
                foreach (var item in items)
                {
                    if (item.Level > 0)
                    {
                        int expectedLevel = item.Code.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (item.Level != expectedLevel && item.Code.Contains("."))
                        {
                            CrookedCodes.Add(new CrookedCodeInfo { Code = item.Code, Description = item.Description, OriginalLevel = item.Level, CorrectedLevel = expectedLevel });
                            if (expectedLevel > 5) CrookedCodesExceedLevel5 = true;
                            item.Level = expectedLevel;
                        }
                    }
                }
                bool useLevelHierarchy = items.Count > 0 && items.Where(x => x.Level > 0).Count() > items.Count * 0.5;
                if (useLevelHierarchy)
                {
                    var currentParents = new Dictionary<int, AssemblyCodeItem>();
                    foreach (var item in items)
                    {
                        if (item.Level > 0)
                        {
                            if (item.Level > 1 && currentParents.TryGetValue(item.Level - 1, out var parent))
                            {
                                item.ParentCode = parent.Code;
                            }
                            else if (item.Level == 1)
                            {
                                item.ParentCode = ""; // Корневой элемент
                            }
                            currentParents[item.Level] = item;
                        }
                    }
                }

                // Вычисляем иерархический путь (CategoryPath) для каждой позиции
                foreach (var item in items)
                {
                    item.CategoryPath = BuildPath(item, byCode, items);
                }

                // Вычисляем HasChildren
                foreach (var child in items)
                {
                    if (!string.IsNullOrEmpty(child.ParentCode) && byCode.TryGetValue(child.ParentCode, out var explicitParent))
                    {
                        explicitParent.HasChildren = true;
                    }
                    else
                    {
                        // Ищем родителя по иерархии кода (префикс)
                        int lastDot = child.Code.LastIndexOf('.');
                        if (lastDot > 0)
                        {
                            string parentCode = child.Code.Substring(0, lastDot);
                            if (byCode.TryGetValue(parentCode, out var implicitParent))
                            {
                                implicitParent.HasChildren = true;
                            }
                        }
                    }
                }
            }


            return (items, byCode, hasDeepClassifier);
        }

        private static string BuildPath(AssemblyCodeItem item,
            Dictionary<string, AssemblyCodeItem> byCode,
            List<AssemblyCodeItem> allItems)
        {
            var parents = new List<string>();

            // 1. Пытаемся по явному ParentCode из 3-го столбца (если это валидный код родителя)
            if (!string.IsNullOrEmpty(item.ParentCode) && byCode.ContainsKey(item.ParentCode))
            {
                var curr = byCode[item.ParentCode];
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (curr != null && !visited.Contains(curr.Code))
                {
                    visited.Add(curr.Code);
                    parents.Insert(0, curr.Description);
                    if (!string.IsNullOrEmpty(curr.ParentCode) && byCode.TryGetValue(curr.ParentCode, out var next))
                        curr = next;
                    else
                        break;
                }
            }
            else
            {
                // 2. Иначе ищем родительские позиции по иерархии кода (префиксы)
                string code = item.Code;
                var foundParents = new List<AssemblyCodeItem>();
                foreach (var candidate in allItems)
                {
                    if (candidate == item) continue;
                    string cCode = candidate.Code;
                    if (code.Length > cCode.Length && code.StartsWith(cCode, StringComparison.OrdinalIgnoreCase))
                    {
                        // Если код использует точки как разделители, следующий символ должен быть точкой
                        if (code.Contains("."))
                        {
                            if (cCode.EndsWith(".") || code[cCode.Length] == '.')
                            {
                                foundParents.Add(candidate);
                            }
                        }
                        else
                        {
                            // Для Uniformat без точек (например A1010 -> A1010100)
                            foundParents.Add(candidate);
                        }
                    }
                }
                foundParents.Sort((a, b) => a.Code.Length.CompareTo(b.Code.Length));
                foreach (var p in foundParents)
                {
                    parents.Add(p.Description);
                }
            }

            return string.Join(" > ", parents);
        }
    }
}
