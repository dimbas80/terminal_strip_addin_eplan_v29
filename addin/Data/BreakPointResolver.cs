using System;

namespace MyEplanActions
{
    /// <summary>Классификация обратного конца кабеля и нейминг точки разрыва
    /// BP (rev.16.2, план rev16.2 Task 2; решения пользователя 30.09.2026).
    /// ЧИСТЫЙ модуль — без EPLAN-типов (паттерн BlockPropMath; из конфигурации
    /// тянется AddInConfiguration — она и без EPLAN, как GhostFrameMath).
    /// Вход — строки полных DT формата '=…++…+…#<n>-<имя>' (значения №20376
    /// «Кабели: источник» / №20377 «Кабели: цель»; rev.16.1 — читаются только
    /// с главного определения функции, механика BlockFormatResolver).
    /// Правила (Decide) — первое совпавшее; Reason-строки фиксированы
    /// тестами c16–c22 (BreakPointResolverTests).</summary>
    public enum BpEndKind
    {
        TerminalStrip,
        Device,
        Unreadable
    }

    /// <summary>Решение по одному кабелю: Kind — тип ПРОТИВОПОЛОЖНОГО конца,
    /// MultiStrip — оба конца клеммники (кабель между клеммниками → вариант
    /// G/F и набор ТР_кабель(int_*.emc)), Reason — строка-обоснование.</summary>
    public sealed class BreakPointDecision
    {
        public BpEndKind Kind;
        public bool MultiStrip;
        public string Reason;
    }

    public static class BreakPointResolver
    {
        /// <summary>Первая буква кода == X (лат. U+0058) или Х (кир. U+0425)
        /// → клеммник (решение пользователя: любые XT/ХТ/XTA/Х — всё X-start).
        /// null/пустая строка → false (не клеммник).</summary>
        public static bool IsStripCodeLetter(string strCode)
        {
            if (string.IsNullOrEmpty(strCode)) return false;
            return strCode[0] == 'X' || strCode[0] == '\u0425';
        }

        /// <summary>Имя устройства в полном DT — хвост после ПОСЛЕДНЕГО дефиса
        /// (примеры стенда: '=HII-1.1++ЯЧ67+#1-X2' → 'X2'). null/пустой → "".
        /// Паттерн ParseDeviceTag (CableSymbolCreator) — тут нужен только блок
        /// имени, остальные блоки не разбираются (не используются правилами).</summary>
        private static string DeviceNameOf(string strDt)
        {
            if (string.IsNullOrEmpty(strDt)) return "";
            int nDash = strDt.LastIndexOf('-');
            return nDash >= 0 && nDash + 1 < strDt.Length ? strDt.Substring(nDash + 1) : "";
        }

        /// <summary>Буквенный код / счётчик имени устройства: ведущие нецифровые
        /// — код, хвост-цифры — счётчик (паттерн SplitDeviceTagLetterCounter).
        /// Без цифр — всё в код, счётчик ""; имя начинается с цифры — всё в код.</summary>
        private static void SplitLetterCounter(string strName, out string strCode, out string strCounter)
        {
            strCode = strName ?? "";
            strCounter = "";
            if (string.IsNullOrEmpty(strName)) return;
            int i = 0;
            while (i < strName.Length && !char.IsDigit(strName[i])) i++;
            if (i > 0)
            {
                strCode = strName.Substring(0, i);
                strCounter = strName.Substring(i);
            }
        }

        /// <summary>Сборка полного ОУ точки разрыва. Клеммник-случай (имя
        /// обратного конца — X/Х): структура DT от strOppositeDt НЕ меняется,
        /// только имя устройства: код_кабеля + счётчик_кабеля + суффикс
        /// BreakPointSuffix. Пример плана: '=HII-1.1++ЯЧ17+#1-X2' + кабель
        /// '=HII-1.1++М+#2-K190' → '=HII-1.1++ЯЧ17+#1-K190(EXT)'.
        /// Устройство: ОУ устройства как есть (имя кабеля не нужно — работает
        /// даже без него). null: пустые DT → null; клеммник при нечитаемом
        /// имени кабеля → null (называть нечем). Проверяет вызывающий.</summary>
        public static string ComposeBpDeviceTag(string strOppositeDt, string strCableName)
        {
            if (string.IsNullOrEmpty(strOppositeDt)) return null;
            if (!IsStripCodeLetter(DeviceNameOf(strOppositeDt)))
                return strOppositeDt;
            if (string.IsNullOrEmpty(strCableName)) return null;
            string strCabName = DeviceNameOf(strCableName);
            if (string.IsNullOrEmpty(strCabName)) return null;
            string strCode, strCounter;
            SplitLetterCounter(strCabName, out strCode, out strCounter);
            int nDash = strOppositeDt.LastIndexOf('-');
            string strHead = strOppositeDt.Substring(0, nDash + 1);
            string strSuffix = AddInConfiguration.BreakPointSuffix;
            return strHead + strCode +
                (string.IsNullOrEmpty(strCounter) ? "" : strCounter + strSuffix);
        }

        /// <summary>Классификация обратного конца (правила по порядку, первое
        /// совпавшее). strStripOwnDt — полное ОУ нашего клеммника (№20006);
        /// strEndA/B — 20376/20377 кабеля (null/"" = нечитаемо). Своя сторона=
        /// конец, равный ОУ клеммника (Ordinal). Reason-строки фиксированы.</summary>
        public static BreakPointDecision Decide(string strStripOwnDt, string strEndA, string strEndB)
        {
            // 1) оба конца нечитаемы.
            if (string.IsNullOrEmpty(strEndA) && string.IsNullOrEmpty(strEndB))
                return Mk(false, "both-ends-unreadable", BpEndKind.Unreadable);
            // 2) своя сторона; противоположный = другой конец.
            bool bOwnA = strEndA == strStripOwnDt;
            bool bOwnB = strEndB == strStripOwnDt;
            if (!bOwnA && !bOwnB)
            {
                // Оба конца ≠ нашему: оба — клеммники → кабель между клеммниками
                // (свой конец не в 20376/20377 — multi); иначе «не нашей стороны».
                if (IsStripCodeLetter(DeviceNameOf(strEndA)) &&
                    IsStripCodeLetter(DeviceNameOf(strEndB)))
                    return Mk(true, "multi-strip", BpEndKind.TerminalStrip);
                return Mk(false, "side-not-found", BpEndKind.Unreadable);
            }
            string strOpposite = bOwnA ? strEndB : strEndA;
            // Наш конец найден, противоположный пуст.
            if (string.IsNullOrEmpty(strOpposite))
                return Mk(false, "opposite-unreadable", BpEndKind.Unreadable);
            // 4/5) противоположный — клеммник / устройство.
            return IsStripCodeLetter(DeviceNameOf(strOpposite))
                ? Mk(false, "opposite-strip", BpEndKind.TerminalStrip)
                : Mk(false, "opposite-device", BpEndKind.Device);
        }

        private static BreakPointDecision Mk(bool bMulti, string strReason, BpEndKind eKind)
        {
            BreakPointDecision oD = new BreakPointDecision();
            oD.Kind = eKind;
            oD.MultiStrip = bMulti;
            oD.Reason = strReason;
            return oD;
        }
    }
}
