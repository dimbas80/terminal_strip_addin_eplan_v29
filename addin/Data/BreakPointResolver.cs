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
    /// тестами c16–c22 (BreakPointResolverTests).
    /// rev16.3 (план 2026-10-02-bp-multistrip-cores Task 1): признак multi
    /// переносится со СВОЙСТВ КАБЕЛЯ 20376/20377 на СВОЙСТВА СОЕДИНЕНИЙ ЖИЛ
    /// 31019/31020 → новая Decide(strStripOwnDt, string[] arrEnds) по списку
    /// DT (резолвер шкафной сигнатуры CabinetKeyOf); прежняя 3-аргументная
    /// Decide сохранена под именем DecideLegacy — fallback на 20376/20377
    /// (вызов AnalyzeAction.PrepareBreakPointDecisions).</summary>
    public enum BpEndKind
    {
        TerminalStrip,
        Device,
        Unreadable
    }

    /// <summary>Решение по одному кабелю: Kind — тип ПРОТИВОПОЛОЖНОГО конца,
    /// MultiStrip — оба конца клеммники (кабель между клеммниками → вариант
    /// G/F и набор ТР_кабель(int_*.emc)), Reason — строка-обоснование.
    /// OppositeDt (rev16.3) — ДЕТЕРМИНИРОВАННЫЙ противоположный DT из списка
    /// концов жил: первый по StringComparer.Ordinal среди «чужих шкафов»
    /// (null — такого нет). Заполняет Decide(strStripOwnDt, string[]);
    /// DecideLegacy OppositeDt НЕ заполняет (заполняет вызывающий).</summary>
    public sealed class BreakPointDecision
    {
        public BpEndKind Kind;
        public bool MultiStrip;
        public string Reason;
        public string OppositeDt;
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
        /// имени, остальные блоки не разбираются (не используются правилами).
        /// rev16.3: public — переиспользуется Decide-списком, второй разбор
        /// имени заводить нельзя (расхождение разборов = ложный multi).</summary>
        public static string DeviceNameOf(string strDt)
        {
            if (string.IsNullOrEmpty(strDt)) return "";
            int nDash = strDt.LastIndexOf('-');
            return nDash >= 0 && nDash + 1 < strDt.Length ? strDt.Substring(nDash + 1) : "";
        }

        /// <summary>«Шкафная» сигнатура DT (rev16.3, решение пользователя
        /// 02.10.2026) — ТОЛЬКО сегменты между '+' строки DT: без '=…'-префикса
        /// станции (первый '+' включительно отрезается) и без '#…'-суффикса
        /// установки (хвост от первого '#' включительно отрезается). Остаток
        /// делится по '+', пустые сегменты выбрасываются, непустые склеиваются
        /// разделителем U+0001. Примеры стенда:
        ///   '=HII-1.1++ЯЧ67+#1-X3' → 'ЯЧ67'  (тот же шкаф, другое имя)
        ///   '=HII-1.1++ЯЧ67+#1-X2' → 'ЯЧ67'
        ///   '=ТСН-1++ЯЧ17+#1-X2'   → 'ЯЧ17'  (другой шкаф)
        ///   '=HII-1.1++М+#1-SB1'   → 'М'     (устройство)
        ///   '=КНТ-3А++ГрЩУ-2+#1-X01' → 'ГрЩУ-2' (вложенный шкаф)
        /// '+' нет вовсе → остаток без ведущего '=' ('=X3' → 'X3');
        /// null/"" → "". Пробелы НЕ триммируются (в DT их не бывает).
        /// Сравнение ключей — только Ordinal (регистр/алфавит значимы).</summary>
        public static string CabinetKeyOf(string strDt)
        {
            if (string.IsNullOrEmpty(strDt)) return "";
            string strRest = strDt;
            int nHash = strRest.IndexOf('#');
            if (nHash >= 0) strRest = strRest.Substring(0, nHash);
            int nPlus = strRest.IndexOf('+');
            if (nPlus >= 0) strRest = strRest.Substring(nPlus + 1);
            else if (strRest.StartsWith("=", StringComparison.Ordinal))
                strRest = strRest.Substring(1);
            string strKey = "";
            string[] arrSeg = strRest.Split('+');
            for (int i = 0; i < arrSeg.Length; i++)
            {
                if (arrSeg[i].Length == 0) continue;
                strKey = strKey.Length == 0 ? arrSeg[i] : strKey + "\u0001" + arrSeg[i];
            }
            return strKey;
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

        /// <summary>Классификация обратного конца по СВОЙСТВАМ КАБЕЛЯ (правила по
        /// порядку, первое совпавшее) — ПРЕЖНЯЯ логика rev16.2, с 02.10.2026
        /// fallback: работает, пока признак multi читается из 20376/20377.
        /// strStripOwnDt — полное ОУ нашего клеммника (№20006); strEndA/B —
        /// 20376/20377 кабеля (null/"" = нечитаемо). Своя сторона= конец,
        /// равный ОУ клеммника (Ordinal). Reason-строки зафиксированы
        /// регресс-тестами c18/c19. OppositeDt НЕ заполняется — поле есть,
        /// но значение определит вызывающий (у этой формы его нет в выходе:
        /// A/B не различаются по «противоположности», если оба ≠ своему).
        /// rev16.3 → переименовано из Decide; новая Decide — по списку DT.</summary>
        public static BreakPointDecision DecideLegacy(string strStripOwnDt, string strEndA, string strEndB)
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

        /// <summary>Mk + OppositeDt (rev16.3). strOppositeDt может быть null —
        /// противоположного конца нет (поле остаётся null).</summary>
        private static BreakPointDecision MkOpp(bool bMulti, string strReason,
            BpEndKind eKind, string strOppositeDt)
        {
            BreakPointDecision oD = Mk(bMulti, strReason, eKind);
            oD.OppositeDt = strOppositeDt;
            return oD;
        }

        /// <summary>Классификация по СПИСКУ DT концов жил (№31019/31020,
        /// rev16.3, решение пользователя 02.10.2026). arrEnds — все прочитанные
        /// DT соединений жил кабеля; strStripOwnDt — полное ОУ нашего клеммника.
        /// Шкаф = CabinetKeyOf (сегменты между '+'); один шкаф + РАЗНОЕ буквенное
        /// обозначение после последнего дефиса (X3 против X2) = кабель с одного
        /// конца подключён к нескольким клемникам → multi. Разный шкаф — это
        /// противоположный конец. Правила (первое совпавшее), все сравнения
        /// строк Ordinal, элементы null/"" игнорируются:
        ///   1) нет ни одного непустого элемента → 'no-core-ends';
        ///   2) своего DT среди значений нет → 'own-end-missing' (проверяется
        ///      ДО multi, иначе «нет своего конца» выдавалось бы за multi);
        ///   3) есть чужой DT в том же шкафу → 'multi-strip', при наличии
        ///      чужого шкафа Reason = 'multi-strip same-cabinet';
        ///   4) есть чужой DT из другого шкафа → он и есть OppositeDt; если его
        ///      имя начинается с X/Х → 'multi-strip' (оба конца — клеммники
        ///      РАЗНЫХ шкафов), иначе 'opposite-device';
        ///   5) иначе (только свой DT) → 'own-end-only' (BP ставить не на что).
        /// OppositeDt = первый по StringComparer.Ordinal среди чужих шкафов
        /// (детерминированно, независимо от порядка чтения жил).</summary>
        public static BreakPointDecision Decide(string strStripOwnDt, string[] arrEnds)
        {
            // Разбор списка одним проходом: свой / тот же шкаф / чужой шкаф.
            string strOwnKey = CabinetKeyOf(strStripOwnDt);
            bool bOwn = false, bSameCab = false;
            string strFirstForeign = null;
            if (arrEnds != null)
            {
                for (int i = 0; i < arrEnds.Length; i++)
                {
                    string strDt = arrEnds[i];
                    if (string.IsNullOrEmpty(strDt)) continue;
                    if (string.Equals(strDt, strStripOwnDt, StringComparison.Ordinal))
                    {
                        bOwn = true;
                        continue;
                    }
                    if (string.Equals(CabinetKeyOf(strDt), strOwnKey, StringComparison.Ordinal))
                        bSameCab = true;
                    else if (strFirstForeign == null ||
                        string.CompareOrdinal(strDt, strFirstForeign) < 0)
                        strFirstForeign = strDt;
                }
            }
            // 1) ни одного читаемого DT жилы.
            if (!bOwn && !bSameCab && strFirstForeign == null)
                return MkOpp(false, "no-core-ends", BpEndKind.Unreadable, null);
            // 2) своего клеммника среди значений нет — раньше любых multi.
            if (!bOwn)
                return MkOpp(false, "own-end-missing", BpEndKind.Unreadable, strFirstForeign);
            // 3) тот же шкаф, другое обозначение → multi (X3 против X2).
            if (bSameCab)
                return MkOpp(true,
                    strFirstForeign == null ? "multi-strip" : "multi-strip same-cabinet",
                    BpEndKind.TerminalStrip, strFirstForeign);
            // 4) противоположный конец из другого шкафа.
            if (strFirstForeign != null)
                return MkOpp(IsStripCodeLetter(DeviceNameOf(strFirstForeign))
                        ? Mk(true, "multi-strip", BpEndKind.TerminalStrip)
                        : Mk(false, "opposite-device", BpEndKind.Device),
                    strFirstForeign);
            // 5) известен только свой конец.
            return MkOpp(false, "own-end-only", BpEndKind.Unreadable, null);
        }
    }
}
