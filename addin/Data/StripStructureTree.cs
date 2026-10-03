using System;
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
        /// Лист при заданных свойствах — остаток FullName после префикса из
        /// свойств (LeafAfterStructure, fix review: «=HII-XT1» → «-XT1»);
        /// все свойства пусты — прежний LeafOf(FullName).
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
                string strLeaf = LeafAfterStructure(oIn);
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

        /// <summary>rev16.11 (03.10): индекс конца структурной части полного
        /// ОУ (начало листа), если имя СОГЛАСОВАНО с четырьмя свойствами
        /// структуры, и -1 если не согласовано либо разбирать нечего. Сверка
        /// ПОУРОВНЕВАЯ (по каждому виду уровня «=»/«++»/«+»/«#»), а НЕ
        /// префиксом строки: прежняя склейка непустых частей даёт на пустом
        /// месте установки №1200 префикс «=HII-1.1++ЯЧ67#1», который не является
        /// префиксом имени «=HII-1.1++ЯЧ67+#1-X3» — 15 ложных [STRIPTREE] WARN
        /// на стенде (прогон 21:12, проект ЯЧ67, [NPT]: NP1200=''). ПРАВИЛО по
        /// КАЖДОМУ виду уровня: (1) свойство НЕПУСТОЕ ⇒ уровень в имени
        /// обязателен и значения обязаны совпасть (Ordinal); (2) свойство
        /// ПУСТОЕ ⇒ уровень в имени либо отсутствует, либо присутствует
        /// ПУСТЫМ — такой уровень и в дереве не появится (уровни строятся из
        /// свойств), то есть структура НЕ теряется; непустой уровень при
        /// пустом свойстве, наоборот, ПОТЕРЯЛСЯ бы в дереве, а пустому
        /// свойству доверять нельзя ⇒ -1. ИСКЛЮЧЕНИЕ: все четыре свойства
        /// пусты — сверять нечего, разбор имени и так применяется целиком
        /// (фоллбэк вызывающей стороны) ⇒ индекс находится. null/пустое имя
        /// либо имя без структурного префикса ⇒ -1 (разбирать нечего).
        /// Разбор — СУЩЕСТВУЮЩИЕ приватные ParseLevels/MatchPrefix (своей копии
        /// разбора нет). ГРАНИЦА листа: по дефису через LeafOf
        /// (Length − LeafOf.Length) — точна, когда лист в имени есть; когда
        /// уровень один и дефис лист не отделяет (дефис принадлежит значению
        /// уровня, «=HII-XT1» при Plant=HII), границу даёт склейка
        /// согласованных уровней из свойств.</summary>
        public static int MatchStructureEnd(string strFullName, string strPlant,
            string strMount, string strPlace, string strUser)
        {
            if (string.IsNullOrEmpty(strFullName)) return -1;
            List<string> lstLevels = ParseLevels(strFullName);
            if (lstLevels.Count == 0) return -1;   // структурного префикса нет
            string[] arrPrefix = new string[] { PrefixPlant, PrefixMount, PrefixPlace,
                PrefixUser };
            string[] arrProp = new string[] { strPlant, strMount, strPlace, strUser };
            string strLeaf = LeafOf(strFullName);
            // Единственный уровень + лист по дефису не отделился ⇒ дефис
            // принадлежит значению уровня, и сверка идёт как «свойство + хвост».
            bool bTailInValue = (lstLevels.Count == 1 && strLeaf.Length == 0);
            bool bAnyProp = false;
            for (int i = 0; i < arrProp.Length; i++)
                if (!string.IsNullOrEmpty(arrProp[i])) { bAnyProp = true; break; }
            int nMatchedLen = 0;   // склейка согласованных уровней (граница без листа)
            for (int i = 0; i < arrPrefix.Length; i++)
            {
                int nCount = 0;
                string strLevel = FindLevel(lstLevels, arrPrefix[i], out nCount);
                if (nCount > 1)
                    return -1;   // два уровня одного вида — имя разобрано неоднозначно
                if (string.IsNullOrEmpty(arrProp[i]))
                {
                    if (bAnyProp && strLevel != null
                        && strLevel.Length > arrPrefix[i].Length)
                        return -1;   // непустой уровень имени при пустом свойстве
                    continue;
                }
                if (strLevel == null) return -1;   // значения свойства в имени нет
                if (!LevelMatches(strLevel, arrPrefix[i], arrProp[i], bTailInValue))
                    return -1;
                nMatchedLen += arrPrefix[i].Length + arrProp[i].Length;
            }
            if (strLeaf.Length > 0) return strFullName.Length - strLeaf.Length;
            if (nMatchedLen > strFullName.Length) return strFullName.Length;
            return nMatchedLen;
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

        // rev.17 (fix review): лист при ЗАДАННЫХ свойствах структуры —
        // остаток FullName после ВОССТАНОВЛЕННОГО префикса («=Plant» +
        // «++MountingSite» + «+PlaceOfInstallation» + «#UserStruct», пустые
        // части пропускаются). «=HII-XT1» (Plant=HII) → «-XT1»; «=HII-1.1»
        // (Plant=HII-1.1, устройства нет) → «» — клеммник остаётся
        // выбираемым (fix: LeafOf давал «» и лист не создавался). Все четыре
        // свойства пусты → прежний LeafOf(FullName) (фоллбэк). Рассинхрон
        // FullName/свойства или пустое имя → фоллбэк LeafOf (безопасно).
        private static string LeafAfterStructure(StripNodeInput oIn)
        {
            if (string.IsNullOrEmpty(oIn.Plant) &&
                string.IsNullOrEmpty(oIn.MountingSite) &&
                string.IsNullOrEmpty(oIn.PlaceOfInstallation) &&
                string.IsNullOrEmpty(oIn.UserStruct))
                return LeafOf(oIn.FullName);
            string strPrefix = "";
            if (!string.IsNullOrEmpty(oIn.Plant))
                strPrefix += PrefixPlant + oIn.Plant;
            if (!string.IsNullOrEmpty(oIn.MountingSite))
                strPrefix += PrefixMount + oIn.MountingSite;
            if (!string.IsNullOrEmpty(oIn.PlaceOfInstallation))
                strPrefix += PrefixPlace + oIn.PlaceOfInstallation;
            if (!string.IsNullOrEmpty(oIn.UserStruct))
                strPrefix += PrefixUser + oIn.UserStruct;
            if (string.IsNullOrEmpty(oIn.FullName)) return oIn.FullName;
            if (!oIn.FullName.StartsWith(strPrefix, StringComparison.Ordinal))
                return LeafOf(oIn.FullName);
            return oIn.FullName.Substring(strPrefix.Length);
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

        // Первый уровень вида strPrefix среди разобранных уровней (null — нет)
        // + счётчик таких уровней (nCount): два уровня одного вида означают
        // неоднозначное имя (MatchStructureEnd → -1). Вид уровня определяем ТЕМ
        // ЖЕ MatchPrefix, что и ParseLevels, иначе «++» посчитался бы за «+».
        private static string FindLevel(List<string> lstLevels, string strPrefix,
            out int nCount)
        {
            nCount = 0;
            string strFound = null;
            foreach (string strLevel in lstLevels)
            {
                if (string.IsNullOrEmpty(strLevel)) continue;
                if (!string.Equals(MatchPrefix(strLevel, 0), strPrefix,
                        StringComparison.Ordinal)) continue;
                nCount++;
                if (strFound == null) strFound = strLevel;
            }
            return strFound;
        }

        // Значение уровня в имени согласуется со свойством, когда равно ему
        // (Ordinal). Для одиночного уровня, у которого дефис лист не отделил
        // (bTailInValue), допустимо и «свойство + хвост» («=HII-XT1» при
        // Plant=HII) — но хвост обязан начинаться с «-»: иначе это не лист
        // устройства (оборванный подчинённый сегмент, мусор), т.е. структура
        // имени не разобрана.
        private static bool LevelMatches(string strLevel, string strPrefix,
            string strProp, bool bTailInValue)
        {
            string strValue = strLevel.Substring(strPrefix.Length);
            if (string.Equals(strValue, strProp, StringComparison.Ordinal)) return true;
            if (!bTailInValue || strValue.Length <= strProp.Length) return false;
            if (!strValue.StartsWith(strProp, StringComparison.Ordinal)) return false;
            return strValue.Substring(strProp.Length).StartsWith("-",
                StringComparison.Ordinal);
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
