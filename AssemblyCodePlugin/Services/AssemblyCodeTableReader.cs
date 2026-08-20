using System;
using System.Collections.Generic;
using System.IO;
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

        public static (IReadOnlyList<AssemblyCodeItem> items,
                        IReadOnlyDictionary<string, AssemblyCodeItem> byCode)
            ReadClassifier(Document doc, string customPath = null)
        {
            string filePath = customPath;

            if (string.IsNullOrEmpty(filePath) && doc != null)
            {
                try
                {
                    var tableRef = AssemblyCodeTable.GetAssemblyCodeTable(doc)
                                                    .GetExternalFileReference();
                    if (tableRef != null)
                    {
                        var mp = tableRef.GetAbsolutePath();
                        filePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                    }
                }
                catch { /* не удалось — будем без файла */ }
            }

            var items = new List<AssemblyCodeItem>();
            var byCode = new Dictionary<string, AssemblyCodeItem>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;

                    var item = new AssemblyCodeItem
                    {
                        Code = parts[0].Trim(),
                        Description = parts[1].Trim(),
                        ParentCode = parts.Length > 2 ? parts[2].Trim() : ""
                    };
                    items.Add(item);
                    byCode[item.Code] = item;
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

            return (items, byCode);
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
                    // Если код кандидата короче нашего и наш код начинается с кода кандидата
                    if (code.Length > cCode.Length && code.StartsWith(cCode, StringComparison.OrdinalIgnoreCase))
                    {
                        // Проверяем, что после префикса идёт разделитель или иерархическое расширение
                        foundParents.Add(candidate);
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
