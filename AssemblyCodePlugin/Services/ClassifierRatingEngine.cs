using System;
using System.Collections.Generic;
using System.Linq;
using AssemblyCodePlugin.Models;

namespace AssemblyCodePlugin.Services
{
    public static class ClassifierRatingEngine
    {
        private static readonly string[] BelowZeroKeywords = { "подзем", "ниже" };

        /// <summary>
        /// Рейтинговый поиск кода в классификаторе — воспроизводит логику Python-узлов
        /// Dynamo #23/#24 (надземные) и #25 (подземные с иерархической проверкой).
        /// </summary>
        public static AssemblyCodeItem FindBestMatch(
            IReadOnlyList<AssemblyCodeItem> items,
            IReadOnlyDictionary<string, AssemblyCodeItem> itemsByCode,
            ClassifierSearchRule rule,
            bool isUnderground)
        {
            var top = FindTopMatches(items, itemsByCode, rule, isUnderground);
            return top.FirstOrDefault();
        }

        public static List<AssemblyCodeItem> FindTopMatches(
            IReadOnlyList<AssemblyCodeItem> items,
            IReadOnlyDictionary<string, AssemblyCodeItem> itemsByCode,
            ClassifierSearchRule rule,
            bool isUnderground)
        {
            var empty = new List<AssemblyCodeItem>();
            if (items == null || rule == null || items.Count == 0)
                return empty;

            int maxRating = 0;
            var bestMatches = new List<AssemblyCodeItem>();

            foreach (var item in items)
            {
                string desc = string.IsNullOrEmpty(item.CategoryPath) 
                    ? (item.Description ?? "") 
                    : (item.CategoryPath + " " + (item.Description ?? ""));

                // bool1: anyTrue
                bool bool1 = rule.AnyTrue == null || rule.AnyTrue.Count == 0 ||
                             rule.AnyTrue.Any(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                // bool2: allTrue
                bool bool2 = rule.AllTrue == null || rule.AllTrue.Count == 0 ||
                             rule.AllTrue.All(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                // bool3: exceptions
                bool bool3 = rule.Exceptions != null && rule.Exceptions.Count > 0 &&
                             rule.Exceptions.Any(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!bool1 || !bool2 || bool3)
                    continue;

                // Подсчёт рейтинга (базовый = 1)
                int rating = 1;
                if (rule.ExtraTrue != null)
                {
                    foreach (var kv in rule.ExtraTrue)
                    {
                        if (desc.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                            rating += kv.Value;
                    }
                }

                if (rating > maxRating)
                {
                    maxRating = rating;
                    bestMatches.Clear();
                    bestMatches.Add(item);
                }
                else if (rating == maxRating)
                {
                    bestMatches.Add(item);
                }
            }

            if (bestMatches.Count == 0)
                return empty;

            // ─── Иерархическая проверка по родительскому разделу ─────────────────
            if (isUnderground && maxRating < 100)
            {
                var hierarchyResult = FilterAllByParentSection(bestMatches, itemsByCode, wantUnderground: true);
                if (hierarchyResult.Count > 0) return hierarchyResult;
            }

            if (!isUnderground)
            {
                var hierarchyResult = FilterAllByParentSection(bestMatches, itemsByCode, wantUnderground: false);
                if (hierarchyResult.Count > 0) return hierarchyResult.OrderBy(x => x.Code?.Length ?? 0).ThenBy(x => x.Code).ToList();
            }

            return bestMatches.OrderBy(x => x.Code?.Length ?? 0).ThenBy(x => x.Code).ToList();
        }

        /// <summary>
        /// Возвращает все позиции с рейтингом > 0, отсортированные по убыванию рейтинга.
        /// На первом месте находится позиция с наивысшим рейтингом.
        /// </summary>
        public static List<AssemblyCodeItem> FindPositiveMatches(
            IReadOnlyList<AssemblyCodeItem> items,
            IReadOnlyDictionary<string, AssemblyCodeItem> itemsByCode,
            ClassifierSearchRule rule,
            bool isUnderground)
        {
            var empty = new List<AssemblyCodeItem>();
            if (items == null || rule == null || items.Count == 0)
                return empty;

            var scored = new List<(AssemblyCodeItem Item, int Rating, int Index)>();

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string desc = string.IsNullOrEmpty(item.CategoryPath) 
                    ? (item.Description ?? "") 
                    : (item.CategoryPath + " " + (item.Description ?? ""));

                // bool1: anyTrue
                bool bool1 = rule.AnyTrue == null || rule.AnyTrue.Count == 0 ||
                             rule.AnyTrue.Any(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                // bool2: allTrue
                bool bool2 = rule.AllTrue == null || rule.AllTrue.Count == 0 ||
                             rule.AllTrue.All(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                // bool3: exceptions
                bool bool3 = rule.Exceptions != null && rule.Exceptions.Count > 0 &&
                             rule.Exceptions.Any(w => desc.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!bool1 || !bool2 || bool3)
                    continue;

                int rating = 1;
                if (rule.ExtraTrue != null)
                {
                    foreach (var kv in rule.ExtraTrue)
                    {
                        if (desc.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                            rating += kv.Value;
                    }
                }

                if (MatchesParentSection(item, itemsByCode, isUnderground))
                {
                    rating += 100;
                }

                if (rating > 0)
                {
                    scored.Add((item, rating, i));
                }
            }

            return scored
                .OrderByDescending(x => x.Rating)
                .ThenBy(x => x.Item.Code?.Length ?? 0)
                .ThenBy(x => x.Item.Code)
                .Select(x => x.Item)
                .ToList();
        }

        private static bool MatchesParentSection(
            AssemblyCodeItem candidate,
            IReadOnlyDictionary<string, AssemblyCodeItem> itemsByCode,
            bool wantUnderground)
        {
            string code = candidate.Code ?? "";
            int lastDot = code.LastIndexOf('.');
            if (lastDot < 0) return false;

            string parentCode = code.Substring(0, lastDot);
            if (!itemsByCode.TryGetValue(parentCode, out var parentItem)) return false;

            string parentDesc = parentItem.Description ?? "";
            bool parentIsUnderground = BelowZeroKeywords.Any(kw =>
                parentDesc.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);

            return parentIsUnderground == wantUnderground;
        }

        private static List<AssemblyCodeItem> FilterAllByParentSection(
            List<AssemblyCodeItem> candidates,
            IReadOnlyDictionary<string, AssemblyCodeItem> itemsByCode,
            bool wantUnderground)
        {
            var result = new List<AssemblyCodeItem>();
            foreach (var candidate in candidates)
            {
                string code = candidate.Code ?? "";
                int lastDot = code.LastIndexOf('.');
                if (lastDot < 0) continue;

                string parentCode = code.Substring(0, lastDot);
                if (!itemsByCode.TryGetValue(parentCode, out var parentItem)) continue;

                string parentDesc = parentItem.Description ?? "";
                bool parentIsUnderground = BelowZeroKeywords.Any(kw =>
                    parentDesc.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);

                if (parentIsUnderground == wantUnderground)
                    result.Add(candidate);
            }
            return result;
        }
    }
}
