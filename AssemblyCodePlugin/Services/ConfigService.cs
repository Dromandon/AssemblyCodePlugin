using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using AssemblyCodePlugin.Models;

namespace AssemblyCodePlugin.Services
{
    public static class ConfigService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AssemblyCodePlugin");

        private static readonly string ConfigsDir = Path.Combine(ConfigDir, "Configs");
        private static readonly string ActiveConfigFile = Path.Combine(ConfigDir, "ActiveConfig.txt");
        private static readonly string LegacyConfigFile = Path.Combine(ConfigDir, "Settings.json");

        public static string GetConfigsFolder()
        {
            if (!Directory.Exists(ConfigsDir))
                Directory.CreateDirectory(ConfigsDir);
            return ConfigsDir;
        }

        public static List<string> GetAvailableConfigs()
        {
            try
            {
                var folder = GetConfigsFolder();
                var files = Directory.GetFiles(folder, "*.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .OrderBy(n => n.Equals("По умолчанию", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(n => n)
                    .ToList();

                if (files.Count == 0)
                {
                    // Миграция старого Settings.json или создание конфигурации по умолчанию
                    var def = LoadLegacyOrDefault();
                    SaveConfig("По умолчанию", def);
                    files.Add("По умолчанию");
                }
                return files;
            }
            catch
            {
                return new List<string> { "По умолчанию" };
            }
        }

        public static string GetActiveConfigName()
        {
            try
            {
                if (File.Exists(ActiveConfigFile))
                {
                    string name = File.ReadAllText(ActiveConfigFile).Trim();
                    if (!string.IsNullOrEmpty(name) && File.Exists(Path.Combine(GetConfigsFolder(), name + ".json")))
                        return name;
                }
            }
            catch { }

            var configs = GetAvailableConfigs();
            return configs.Count > 0 ? configs[0] : "По умолчанию";
        }

        public static void SetActiveConfigName(string name)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ActiveConfigFile, name ?? "По умолчанию");
            }
            catch { }
        }

        public static PluginSettings LoadConfig(string configName)
        {
            try
            {
                string filePath = Path.Combine(GetConfigsFolder(), configName + ".json");
                if (File.Exists(filePath))
                {
                    return ImportFromFile(filePath);
                }
            }
            catch { }

            return LoadLegacyOrDefault();
        }

        public static void SaveConfig(string configName, PluginSettings settings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configName)) configName = "По умолчанию";
                string filePath = Path.Combine(GetConfigsFolder(), configName + ".json");
                ExportToFile(filePath, settings);
                SetActiveConfigName(configName);
            }
            catch { }
        }

        public static bool DeleteConfig(string configName)
        {
            try
            {
                string filePath = Path.Combine(GetConfigsFolder(), configName + ".json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool RenameConfig(string oldName, string newName)
        {
            try
            {
                string oldPath = Path.Combine(GetConfigsFolder(), oldName + ".json");
                string newPath = Path.Combine(GetConfigsFolder(), newName + ".json");
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    if (GetActiveConfigName() == oldName)
                        SetActiveConfigName(newName);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static PluginSettings ImportFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var s = new DataContractJsonSerializer(typeof(PluginSettings));
                    using (var f = File.OpenRead(filePath))
                    {
                        var loaded = s.ReadObject(f) as PluginSettings;
                        if (loaded?.Rules?.Count > 0) return loaded;
                    }
                }
            }
            catch { }
            return GetDefaults();
        }

        public static void ExportToFile(string filePath, PluginSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var s = new DataContractJsonSerializer(typeof(PluginSettings));
                using (var f = File.Create(filePath))
                    s.WriteObject(f, settings);
            }
            catch { }
        }

        private static PluginSettings LoadLegacyOrDefault()
        {
            try
            {
                if (File.Exists(LegacyConfigFile))
                {
                    var s = new DataContractJsonSerializer(typeof(PluginSettings));
                    using (var f = File.OpenRead(LegacyConfigFile))
                    {
                        var loaded = s.ReadObject(f) as PluginSettings;
                        if (loaded?.Rules?.Count > 0) return loaded;
                    }
                }
            }
            catch { }
            return GetDefaults();
        }

        public static PluginSettings Load()
        {
            return LoadConfig(GetActiveConfigName());
        }

        public static void Save(PluginSettings settings)
        {
            SaveConfig(GetActiveConfigName(), settings);
        }

        public static PluginSettings ResetToDefaults()
        {
            var d = GetDefaults();
            Save(d);
            return d;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Настройки по умолчанию — точное воспроизведение двух CodeBlock Dynamo
        // ─────────────────────────────────────────────────────────────────────────
        public static PluginSettings GetDefaults()
        {
            // Слова-исключения для НАДЗЕМНЫХ (содержат "Подзем")
            var excAbove = new List<string>
            {
                "Подзем", "Демонтаж", "штроб", "архитект", "пробив",
                "анкер", "крышк", "арх", "помещ", "отдел", "срубка"
            };
            // Слова-исключения для ПОДЗЕМНЫХ ("Подзем" — НЕТ, он идёт в extraTrue)
            var excBelow = new List<string>
            {
                "Демонтаж", "штроб", "архитект", "пробив",
                "анкер", "крышк", "арх", "помещ", "отдел", "срубка"
            };

            var s = new PluginSettings
            {
                AssemblyCodeParamName = "Код по классификатору",
                UndergroundParamName = "FAM_Underground",
                UndergroundValueText = "Подземная часть",
                AbovegroundValueText = "Надземная часть",
                ZeroLevel = new ZeroLevelSettings()
            };

            // ── Пилоны ──────────────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Пилоны",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_StructuralColumns",
                    CategoryDisplayName = "Несущие колонны",
                    TypeNameContainsAny = new List<string> { "_P_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "пилон" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "пилон" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Парапеты ────────────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Парапеты",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Walls",
                    CategoryDisplayName = "Стены",
                    TypeNameContainsAny = new List<string> { "_PA_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "парапет" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "парапет" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                }
            });

            // ── Стены лифтовых шахт ─────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Стены лифтовых шахт",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Walls",
                    CategoryDisplayName = "Стены",
                    TypeNameContainsAny = new List<string> { "_CSH_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "стен", "лифт" },
                    Exceptions = Combine(excAbove, "сбор"),
                    ExtraTrue = new Dictionary<string, int>
                    {
                        { "лифтовых шахт из монолитного", 10 },
                        { "лифтовая шахта из монолитного", 10 },
                        { "монолит", 1 }, { "стен", 1 }
                    }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "шахт", "стен", "лифт" },
                    Exceptions = Combine(excBelow, "сбор"),
                    ExtraTrue = new Dictionary<string, int>
                    {
                        { "лифтовая шахта из монолитного", 10 },
                        { "Подзем", 100 }, { "монолит", 1 }, { "стен", 1 }
                    }
                }
            });

            // ── Стены ───────────────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Стены",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Walls",
                    CategoryDisplayName = "Стены",
                    TypeNameContainsAny = new List<string> { "_W_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "стен" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "стен" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Балки (по имени семейства S_FRA_, но не S_FRA_STE) ─────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Балки",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_StructuralFraming",
                    CategoryDisplayName = "Каркас несущих конструкций",
                    FamilyNameContainsAny = new List<string> { "S_FRA_" },
                    FamilyNameNotContains = new List<string> { "S_FRA_STE" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "балки", "балок", "балка" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "балки", "балок" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Плиты перекрытий ────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Плиты перекрытий",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Floors",
                    CategoryDisplayName = "Перекрытия",
                    TypeNameContainsAny = new List<string> { "_F_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "плит", "перекрыт" },
                    Exceptions = Combine(excAbove, "Фунд", "плит вставок деф", "шахт"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "плит", "перекрыт" },
                    Exceptions = Combine(excBelow, "Фунд", "плит вставок деф", "шахт"),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Капители ────────────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Капители",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Floors",
                    CategoryDisplayName = "Перекрытия",
                    TypeNameContainsAny = new List<string> { "_C_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    Exceptions = Combine(excAbove, "Фунд", "плит вставок деф"),
                    ExtraTrue = new Dictionary<string, int>
                    {
                        { "Капитель", 5 }, { "плит", 1 }, { "перекрыт", 1 }, { "монолит", 1 }
                    }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "Капитель", "плит" },
                    Exceptions = Combine(excBelow, "Фунд", "плит вставок деф"),
                    ExtraTrue = new Dictionary<string, int>
                    {
                        { "Подзем", 100 }, { "Капитель", 5 }, { "плит", 1 }, { "монолит", 1 }
                    }
                }
            });

            // ── Лестничные площадки (по имени семейства "Landing") ───────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Лестничные площадки",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_GenericModel",
                    CategoryDisplayName = "Обобщённые модели",
                    FamilyNameContainsAny = new List<string> { "Landing" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "лест", "Лестнич", "плит", "перекрыт" },
                    Exceptions = Combine(excAbove, "Сбор", "Фунд", "плит вставок деф", "шахт"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 }, { "площад", 2 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "лест", "Лестнич", "плит", "перекрыт" },
                    Exceptions = Combine(excBelow, "Сбор", "Фунд", "плит вставок деф", "шахт"),
                    ExtraTrue = new Dictionary<string, int>
                    {
                        { "Подзем", 100 }, { "монолит", 1 }, { "площад", 2 }
                    }
                }
            });

            // ── Лестничные марши (по имени семейства "Stair", но не "Landing") ──
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Лестничные марши",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_GenericModel",
                    CategoryDisplayName = "Обобщённые модели",
                    FamilyNameContainsAny = new List<string> { "Stair" },
                    FamilyNameNotContains = new List<string> { "Landing" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "лестниц", "лестничных маршей", "Лестничный марш", "ЛМ" },
                    Exceptions = Combine(excAbove, "Сбор", "металл", "покрыт"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "лестниц", "лестничных маршей", "Лестничный марш" },
                    Exceptions = Combine(excBelow, "Сбор", "металл", "покрыт"),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Термовкладыши (по имени семейства "_HeatInsulating") ─────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Термовкладыши",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_GenericModel",
                    CategoryDisplayName = "Обобщённые модели",
                    FamilyNameContainsAny = new List<string> { "_HeatInsulating" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "термовклад" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int>()
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "термовклад" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int>()
                }
            });

            // ── Колонны (S_SCO_RCO_Column или S_SCO_Column в имени семейства) ───
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Колонны",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_StructuralColumns",
                    CategoryDisplayName = "Несущие колонны",
                    FamilyNameContainsAny = new List<string> { "S_SCO_RCO_Column", "S_SCO_Column" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "Колон" },
                    Exceptions = Combine(excAbove, "Стал", "метал"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "Колон" },
                    Exceptions = Combine(excBelow, "сталежелезо", "Стал", "метал"),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Рампы ───────────────────────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Рампы",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Floors",
                    CategoryDisplayName = "Перекрытия",
                    TypeNameContainsAny = new List<string> { "_R_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "рамп" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "рамп" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Плиты деформационных швов ────────────────────────────────────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Плиты деформационных швов",
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_Floors",
                    CategoryDisplayName = "Перекрытия",
                    TypeNameContainsAny = new List<string> { "_EJF_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "плит вставок деф" },
                    Exceptions = new List<string>(excAbove),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AllTrue = new List<string> { "плит вставок деф" },
                    Exceptions = new List<string>(excBelow),
                    ExtraTrue = new Dictionary<string, int> { { "Подзем", 100 }, { "монолит", 1 } }
                }
            });

            // ── Фундаментные плиты (_Pit_ в семействе) — не переименовываются ───
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Фундаментные плиты",
                ExcludeFromBglRename = true,
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_StructuralFoundation",
                    CategoryDisplayName = "Фундаменты",
                    FamilyNameContainsAny = new List<string> { "_Pit_" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "Фунд", "ростверк" },
                    Exceptions = Combine(excBelow, "гидроиз", "утепл", "деформ", "молниезащ", "усилен"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "Фунд", "ростверк" },
                    Exceptions = Combine(excBelow, "гидроиз", "утепл", "деформ", "молниезащ", "усилен"),
                    ExtraTrue = new Dictionary<string, int> { { "монолит", 1 } }
                }
            });

            // ── Сваи (S_SCO_RCO_Pile или S_SCO_Pile в имени семейства) ──────────
            s.Rules.Add(new ClassificationRule
            {
                ElementTypeName = "Сваи",
                ExcludeFromBglRename = true,
                RevitFilter = new RevitElementFilterRule
                {
                    TargetCategory = "OST_StructuralFoundation",
                    CategoryDisplayName = "Фундаменты",
                    FamilyNameContainsAny = new List<string> { "S_SCO_RCO_Pile", "S_SCO_Pile" }
                },
                AboveGroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "свай", "сваи", "свая" },
                    Exceptions = Combine(excBelow, "составн", "испытан", "Jet", "бур", "водонепрониц", "вдавл"),
                    ExtraTrue = new Dictionary<string, int> { { "забив", 100 }, { "основан", 1 } }
                },
                UndergroundSearchRule = new ClassifierSearchRule
                {
                    AnyTrue = new List<string> { "свай", "сваи", "свая" },
                    Exceptions = Combine(excBelow, "составн", "испытан", "Jet", "бур", "водонепрониц", "вдавл"),
                    ExtraTrue = new Dictionary<string, int> { { "забив", 100 }, { "основан", 1 } }
                }
            });

            return s;
        }

        private static List<string> Combine(List<string> source, params string[] extra)
        {
            var result = new List<string>(source);
            result.AddRange(extra);
            return result;
        }
    }
}
