using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Вход построения дерева клеммников (rev.17, план
    /// 2026-10-01-ui-emc-profiles Task 1): полное ОУ + четыре свойства
    /// структуры (№1100 Plant «=», №1400 MountingSite «++», №1200
    /// PlaceOfInstallation «+», №1600 UserStruct «#»). Поля публичные —
    /// паттерн EmcSchemeInfo/Dm* (чистый модуль без EPLAN-типов).</summary>
    public sealed class StripNodeInput
    {
        public string FullName;
        public string Plant;
        public string MountingSite;
        public string PlaceOfInstallation;
        public string UserStruct;
    }

    /// <summary>Узел дерева клеммников: Text — префикс+значение уровня
    /// (напр. «=HII-1.1», «++ПУ», «#1») либо краткое имя устройства у листа
    /// («-XT1»); FullName непусто ТОЛЬКО у листа — полное ОУ для простановки
    /// в UI; Children всегда не null (пустой список у листа).</summary>
    public sealed class StripTreeNode
    {
        public string Text;
        public string FullName;
        public List<StripTreeNode> Children;
    }

    /// <summary>Дерево клеммников из структуры ОУ (rev.17, план
    /// 2026-10-01-ui-emc-profiles Task 1) — ЧИСТЫЙ модуль (паттерн
    /// EmcSchemeCatalog/BreakPointResolver): только System.Collections.Generic,
    /// без EPLAN-типов, компилируется тест-раннером (tests\build_tests.bat).
    /// Порядок уровней фиксирован: «=» (Plant) → «++» (MountingSite) →
    /// «+» (PlaceOfInstallation) → «#» (UserStruct); создаются ТОЛЬКО
    /// присутствующие (непустые) уровни. Клеммники с одинаковой цепочкой
    /// уровней делят узлы; листья под последним уровнем (неидентичные листья
    /// не схлопываются). Build предпочитает четыре свойства структуры; если
    /// ВСЕ четыре пусты — разбирает FullName (фоллбэк, ParseLevels).</summary>
    public static class StripStructureTree
    {
        // Структурные префиксы ОУ. Порядок ВАЖЕН: «++» проверяется раньше «+»
        // (иначе «++» распалось бы на два «+»).
        private const string PrefixPlant = "=";
        private const string PrefixMount = "++";
        private const string PrefixPlace = "+";
        private const string PrefixUser = "#";

        /// <summary>Построение дерева: для каждого входа — список текстов
        /// уровней (из свойств или ParseLevels(FullName) при всех пустых
        /// свойствах), затем спуск по дереву с поиском/созданием узла по Text;
        /// лист (FullName = полное ОУ) добавляется под последним уровнем.
        /// null-вход — пустой список. Вход с пустым листом пропускается
        /// (уровни без устройства не создают узел-лист).</summary>
        public static List<StripTreeNode> Build(List<StripNodeInput> lstInputs)
        {
            List<StripTreeNode> lstRoot = new List<StripTreeNode>();
            if (lstInputs == null) return lstRoot;
            foreach (StripNodeInput oIn in lstInputs)
            {
                if (oIn == null) continue;
                List<string> lstLevels = LevelsOf(oIn);
                string strLeaf = LeafOf(oIn.FullName);
                List<StripTreeNode> lstCur = lstRoot;
                foreach (string strLevel in lstLevels)
                {
                    StripTreeNode oNode = FindChild(lstCur, strLevel);
                    if (oNode == null)
                    {
                        oNode = new StripTreeNode();
                        oNode.Text = strLevel;
                        oNode.FullName = null;
                        oNode.Children = new List<StripTreeNode>();
                        lstCur.Add(oNode);
                    }
                    lstCur = oNode.Children;
                }
                if (!string.IsNullOrEmpty(strLeaf))
                {
                    StripTreeNode oLeaf = new StripTreeNode();
                    oLeaf.Text = strLeaf;
                    oLeaf.FullName = oIn.FullName; // у листа FullName непусто
                    oLeaf.Children = new List<StripTreeNode>();
                    lstCur.Add(oLeaf);
                }
            }
            return lstRoot;
        }

        /// <summary>Краткое имя устройства — остаток ПОСЛЕ последнего
        /// структурного токена (напр. «-XT1»). Если строка НЕ начинается со
        /// структурного префикса («=», «++», «+», «#») — возвращается целиком
        /// (напр. «-X9»); null/пусто → как есть. Единственный структурный
        /// токен — вся строка есть его значение, устройства нет → пусто
        /// (напр. «=HII-1.1» → "", дефис принадлежит значению Plant).
        /// Граница листа — последний «-» после НАЧАЛА последнего токена,
        /// поэтому «HII-1.1» внутри более длинной цепочки листом не станет.
        /// Разделитель — та же конвенция, что в BreakPointResolver.DeviceNameOf,
        /// НО LeafOf СОЗНАТЕЛЬНО включает дефис («-XT1»), тогда как
        /// DeviceNameOf возвращает хвост БЕЗ дефиса («X2»).</summary>
        public static string LeafOf(string strFullName)
        {
            if (string.IsNullOrEmpty(strFullName)) return strFullName;
            if (MatchPrefix(strFullName, 0) == null) return strFullName;
            return strFullName.Substring(StructureEnd(strFullName));
        }

        // Список текстов уровней: приоритет — четыре свойства; все пусты —
        // фоллбэк на разбор FullName. Порядок уровней фиксирован, пустые
        // значения пропускаются.
        private static List<string> LevelsOf(StripNodeInput oIn)
        {
            if (string.IsNullOrEmpty(oIn.Plant) &&
                string.IsNullOrEmpty(oIn.MountingSite) &&
                string.IsNullOrEmpty(oIn.PlaceOfInstallation) &&
                string.IsNullOrEmpty(oIn.UserStruct))
                return ParseLevels(oIn.FullName);
            List<string> lst = new List<string>();
            if (!string.IsNullOrEmpty(oIn.Plant))
                lst.Add(PrefixPlant + oIn.Plant);
            if (!string.IsNullOrEmpty(oIn.MountingSite))
                lst.Add(PrefixMount + oIn.MountingSite);
            if (!string.IsNullOrEmpty(oIn.PlaceOfInstallation))
                lst.Add(PrefixPlace + oIn.PlaceOfInstallation);
            if (!string.IsNullOrEmpty(oIn.UserStruct))
                lst.Add(PrefixUser + oIn.UserStruct);
            return lst;
        }

        // Линейный проход по СТРУКТУРНОЙ части строки (до StructureEnd —
        // граница листа): каждый токен = префикс + значение до начала
        // следующего структурного префикса; «++» проверяется раньше «+».
        // Нет структурного префикса в начале — уровней нет.
        private static List<string> ParseLevels(string strFullName)
        {
            List<string> lst = new List<string>();
            if (string.IsNullOrEmpty(strFullName)) return lst;
            if (MatchPrefix(strFullName, 0) == null) return lst;
            string strStructure = strFullName.Substring(0,
                StructureEnd(strFullName));
            int nPos = 0;
            while (nPos < strStructure.Length)
            {
                string strPrefix = MatchPrefix(strStructure, nPos);
                if (strPrefix == null) break; // мусор до префикса — стоп
                int nStart = nPos + strPrefix.Length;
                int nNext = FindNextPrefix(strStructure, nStart);
                if (nNext < 0) nNext = strStructure.Length;
                lst.Add(strPrefix + strStructure.Substring(nStart,
                    nNext - nStart));
                nPos = nNext;
            }
            return lst;
        }

        // Индекс конца структурной части (= начала листа) для строки,
        // начинающейся со структурного префикса; = длине строки, если листа
        // нет (вызывать после проверки MatchPrefix(…, 0) != null).
        // Токены — префикс + значение до следующего префикса; лист — остаток
        // после ПОСЛЕДНЕГО токена. Единственный структурный токен — вся строка
        // есть его значение, устройства нет (напр. «=HII-1.1» → конец строки).
        // Иначе граница листа — последний «-» ПОСЛЕ начала последнего токена
        // (дефис внутри значения, напр. Plant «HII-1.1», листом не считается).
        private static int StructureEnd(string strFullName)
        {
            int nLastTokenStart = 0;
            int nCount = 0;
            int nPos = 0;
            while (nPos < strFullName.Length)
            {
                string strPrefix = MatchPrefix(strFullName, nPos);
                if (strPrefix == null) break;
                nLastTokenStart = nPos;
                nCount++;
                int nNext = FindNextPrefix(strFullName, nPos + strPrefix.Length);
                if (nNext < 0) break;
                nPos = nNext;
            }
            if (nCount <= 1) return strFullName.Length;
            int nDash = strFullName.LastIndexOf('-');
            if (nDash < nLastTokenStart) return strFullName.Length;
            return nDash;
        }

        // Первый структурный префикс в позиции nPos (null — нет). «++» раньше «+».
        private static string MatchPrefix(string strText, int nPos)
        {
            if (string.IsNullOrEmpty(strText) || nPos < 0 ||
                nPos >= strText.Length) return null;
            if (nPos + 1 < strText.Length && strText[nPos] == '+' &&
                strText[nPos + 1] == '+') return PrefixMount;
            char cChar = strText[nPos];
            if (cChar == '=') return PrefixPlant;
            if (cChar == '+') return PrefixPlace;
            if (cChar == '#') return PrefixUser;
            return null;
        }

        // Индекс ближайшего структурного префикса начиная с nFrom (-1 — нет).
        private static int FindNextPrefix(string strText, int nFrom)
        {
            for (int i = nFrom; i < strText.Length; i++)
            {
                if (MatchPrefix(strText, i) != null) return i;
            }
            return -1;
        }

        // Поиск существующего узла с тем же Text среди детей (дедуп уровней).
        private static StripTreeNode FindChild(List<StripTreeNode> lst,
            string strText)
        {
            foreach (StripTreeNode oNode in lst)
            {
                if (oNode != null && oNode.Text == strText) return oNode;
            }
            return null;
        }
    }
}
