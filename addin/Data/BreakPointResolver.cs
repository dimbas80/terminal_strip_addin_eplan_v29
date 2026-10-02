using System;

namespace MyEplanActions
{
    /// <summary>Классификация обратного конца кабеля и нейминг точки разрыва
    /// BP (rev.16.2, план rev16.2 Task 2; решения пользователя 30.09.2026).
    /// ЧИСТЫЙ модуль — без EPLAN-типов (паттерн BlockPropMath; из конфигурации
    /// тянется AddInConfiguration — она и без EPLAN, как GhostFrameMath).
    /// Вход — строки полных DT формата '=…++…+…#<n>-<имя>:<пин>'. rev16.4
    /// (задача 3.1-bis) эти строки — ПОЛНЫЕ ОУ КЛЕМ из строк [DM] Данной модели
    /// (EplanTerminalStripReader.AddStripEnd → DmReport.CableStripEnds), см.
    /// абзац rev16.4 ниже. Значения №20376 «Кабели: источник» / №20377 «Кабели:
    /// цель» — ТОЛЬКО fallback (DecideLegacy, rev.16.1 — читаются с главного
    /// определения функции, механика BlockFormatResolver) и источник ИМЕНИ точки
    /// разрыва. Правила (Decide) — первое совпавшее; Reason-строки фиксированы
    /// тестами c16–c34 (BreakPointResolverTests).
    /// rev16.3 (план 2026-10-02-bp-multistrip-cores Task 1): признак multi
    /// перенесён со СВОЙСТВ КАБЕЛЯ 20376/20377 на СВОЙСТВА СОЕДИНЕНИЙ ЖИЛ
    /// 31019/31020 → новая Decide(strStripOwnDt, string[] arrEnds) по списку
    /// DT (резолвер шкафной сигнатуры CabinetKeyOf). ИСТОРИЧЕСКИ: этот путь
    /// просуществовал одну волну и 02.10.2026 заменён на [DM] (см. абзац rev16.4
    /// ниже) — 31019/31020 вернулись к роли диагностики. Прежняя 3-аргументная
    /// Decide сохранена под именем DecideLegacy — fallback на 20376/20377
    /// (вызов AnalyzeAction.PrepareBreakPointDecisions).
    /// Фикс-волна 02.10.2026 (задача Ф-2): устранены три дефекта стенда —
    /// A1 (сбор пуст: ключ DmReport.CableCoreEnds был по oRow.CableName, который
    /// на стенде всегда null — EplanTerminalStripReader.FillCable), A3 (ключ шкафа
    /// склеивал ВСЕ '+'-сегменты, а в формате жил сегмент один ⇒ ЯЧ67/X3 и
    /// ЯЧ67/X2 давали РАЗНЫЕ ключи), A2 («свой конец» искался Ordinal-равенством
    /// СЫРОГО DT ⇒ не сходилось из-за «#1» и «:пин»). Теперь шкаф = пара
    /// (Место сборки, Место установки) [CabinetKeyOf], «свой конец» = пара
    /// (ключ шкафа, обозначение без «:пин») [DesignationOf].
    /// rev.16.4 (задача 3.1-bis): Decide НЕ МЕНЯЛАСЬ по существу — изменился только
    /// СПИСОК концов, который её передают. Теперь это ПОЛНЫЕ ОУ КЛЕМ из строк [DM]
    /// ('=HII-1.1++ЯЧ67+#1-X3:1'), а не значения №31019/№31020 ('=HII-1.1++ЯЧ67-X3:1'):
    /// EPLAN собирает 31019/31020 только из «идентифицирующих» свойств, поэтому у
    /// приборов сегменты «++»/«+» в строку не попадают, а настройка задаётся
    /// пользователем ⇒ прежний разбор зависел от конфигурации чужого проекта, и
    /// восстановить опущенный сегмент из строки нельзя. Разбор тем более корректен
    /// на полном DT: в нём есть сегмент «#структура», который в жилных значениях
    /// отсутствовал НИ РАЗУ (0 из 12 424 на стенде 02.10). Парсеры CabinetKeyOf /
    /// DesignationOf оба формата переваривают без правок — хвост «:пин» снимает
    /// DesignationOf, поэтому «X3:1» и «X3:3» — один конец.</summary>
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
    /// (null — такого нет). В multi-случае «свой шкаф, другое имя» при
    /// отсутствии чужого шкафа берётся первый по Ordinal из этого случая
    /// (иначе ОУ точки разрыва было бы пустым). Заполняет
    /// Decide(strStripOwnDt, string[]); DecideLegacy OppositeDt НЕ заполняет
    /// (заполняет вызывающий).</summary>
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
        /// (примеры стенда: '=HII-1.1++ЯЧ67+#1-X2' → 'X2', '=HII-1.1++ЯЧ67-X3:1'
        /// → 'X3:1'). null/пустой → "".
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

        /// <summary>ОБОЗНАЧЕНИЕ устройства = DeviceNameOf БЕЗ хвоста «:пин»
        /// (фикс-волна 02.10.2026, формат значений №31019/31020). Именно
        /// обозначение, а не сырой хвост, УЧАСТВУЕТ в сравнении концов: стенд даёт
        /// 'X3:1' и 'X3:2' у ПИН ОДНОГО клеммника — это ОДИН конец, тогда как
        /// 'X3' против 'X2' — разные. Хвост отбрасывается, только если он ЦЕЛИКОМ
        /// цифровой (пин): 'K2:X4:3' остаётся целиком (иначе имя вида
        /// '=HII-1.1-A1-K2:X4:3' потеряло бы значимую часть). null/"" → "".
        /// Порядок вызовов: только СВЕРХ DeviceNameOf, второй разбор имени
        /// заводить нельзя (расхождение разборов = ложный multi).</summary>
        public static string DesignationOf(string strDt)
        {
            string strName = DeviceNameOf(strDt);
            if (strName.Length == 0) return strName;
            int nColon = strName.LastIndexOf(':');
            if (nColon < 0) return strName;
            for (int i = nColon + 1; i < strName.Length; i++)
                if (!char.IsDigit(strName[i])) return strName;
            return strName.Substring(0, nColon);
        }

        /// <summary>«Шкафная» сигнатура DT (фикс-волна 02.10.2026, ПРАВИЛО
        /// ЗАКАЗЧИКА) — ПАРА (Место сборки, Место установки), U+0001-разделитель.
        /// ГРАНИЦА ОБОЗНАЧЕНИЯ — ПОСЛЕДНИЙ '-' ПОСЛЕ первого '+' (обозначение
        /// прибора, хвост ':пин' в ключ не входит); дефис ВНУТРИ значения поля —
        /// обычный символ и разбор НЕ прерывает ('ГрЩУ-2' остаётся 'ГрЩУ-2').
        /// Разбор оставшейся структурной части строго СЛЕВА НАПРАВО:
        ///   '++' → Место сборки, одиночный '+' → Место установки
        ///   (значение — до следующего '+', '#' или конца строки),
        ///   '#' → СТОП (структура, заданная пользователем, в шкаф не входит),
        ///   '=Установка' отбрасывается до первого '+', поле без маркера = "".
        /// Смысл: у клеммников ОДНОГО шкафа ОБА поля равны, одно может быть пустым
        /// (в этом проекте «+ Место установки» пуст, шкаф = «++»).
        /// Первый маркер каждого вида побеждает, вложенность не теряется
        /// ('=Уст+МестоУстановки++МестоСборки-…' → оба поля). Примеры
        /// (стенд 02.10 + фикстуры):
        ///   '=HII-1.1++ЯЧ67+#1-X3'    → 'ЯЧ67'    + U+0001  (наш ОУ, формат 20006)
        ///   '=HII-1.1++ЯЧ67-X3:1'     → 'ЯЧ67'    + U+0001  (ТОТ ЖЕ ключ! формат жилы)
        ///   '=HII-1.1++ЯЧ67-X2:1'     → 'ЯЧ67'    + U+0001  (тот же шкаф, другое имя)
        ///   '=HII-1.1++ПУ-XT1:1'      → 'ПУ'      + U+0001  (другой шкаф)
        ///   '=ТСН-1++ЯЧ17+#1-X2:8'    → 'ЯЧ17'    + U+0001  (другой шкаф, оба формата)
        ///   '=КНТ-3А++ГрЩУ-2+#1-X01'  → 'ГрЩУ-2'  + U+0001  (ДЕФИС ВНУТРИ имени
        ///        шкафа сохраняется; оба формата дают ОДИН ключ — '#1' отбрасывается
        ///        на '#', обозначение отбрасывается на ПОСЛЕДНЕМ '-')
        ///   '=HII-1.1++ЯЧ67-2+#1-X1'  → 'ЯЧ67-2'  + U+0001  ('ЯЧ67-3' → 'ЯЧ67-3':
        ///        РАЗНЫЕ ключи — прежний обрыв по первому дефису давал ложный
        ///        multi на двух клеммниках ОДНОГО шкафа)
        ///   '=HII-1.1++М+#1-SB1'      → 'М'       + U+0001  (устройство)
        ///   '=Уст+уровень++ЯЧ67-X3:1' → 'ЯЧ67' + U+0001 + 'уровень' (вложенность)
        /// '+' нет вовсе (или оба поля вышли пустыми) → маркеров полей нет: отдаём
        /// ВЕСЬ хвост без ведущего '=' ('=X3' → 'X3',
        /// '=станция-обозначение:пин' → 'станция-обозначение:пин'). ОДНОЙ общей
        /// пустой пары здесь быть НЕ должно: все DT без '+' тогда сочлись бы одним
        /// шкафом. Настоящий ключ пары ВСЕГДА содержит U+0001, «сырой» — никогда,
        /// поэтому он не совпадёт и с настоящим шкафом.
        /// null/"" → "". Пробелы НЕ триммируются (в DT их не бывает).
        /// Сравнение ключей — только Ordinal (регистр/алфавит значимы).
        /// ПРЕЖНЯЯ реализация (Task 1) склеивала ВСЕ непустые '+'-сегменты: в
        /// формате жил сегмент один ('=HII-1.1++ЯЧ67-X3:1' → 'ЯЧ67-X3:1'), и
        /// ЯЧ67/X3 против ЯЧ67/X2 давали РАЗНЫЕ ключи — «тот же шкаф» не
        /// срабатывал никогда (дефект A3).
        /// ФИКС-РАУНД 1 (задача Ф-3): обрыв поля стоял на ПЕРВОМ дефисе, поэтому
        /// '=КНТ-3А++ГрЩУ-2+#1-X01' давал 'ГрЩУ' и шкафы 'ЯЧ67-2'/'ЯЧ67-3' —
        /// ОДИН ключ 'ЯЧ67' ⇒ ложный multi. Обозначение прибора отделяет
        /// ПОСЛЕДНИЙ '-' (как в DeviceNameOf/DesignationOf — разборов имени здесь
        /// заводить второй нельзя), дефис внутри значения больше не прерывает
        /// разбор.
        /// ОГРАНИЧЕНИЕ ПРАВИЛА (Ф-3, раунд 2; риск подтверждён портом, правкой
        /// НЕ лечится — нужен разбор по спецификации, не «последний дефис»):
        /// правило НЕ РАЗЛИЧАЕТ дефис внутри имени шкафа и дефис внутри
        /// ОБОЗНАЧЕНИЯ. Обозначение с дефисом втягивается в имя шкафа:
        ///   '=Уст++Шкаф-Обозн'      → 'Шкаф'      + U+0001
        ///   '=Уст++Шкаф-Обозн-1'    → 'Шкаф-Обозн'+ U+0001   (обозначение — часть ключа!)
        ///   '=Уст++Шкаф-Обозн-А-1'  → 'Шкаф-Обозн-А'+U+0001
        /// Т.е. один и тот же шкаф может дать РАЗНЫЕ ключи, и настоящий multi
        /// ('=Уст++Шкаф-Обозн' против '=Уст++Шкаф-Обозн-1',
        /// '=Уст++Шкаф-Обозн-А-1' против '=Уст++Шкаф-Обозн-Б-2') ПОТЕРЯЕТСЯ —
        /// ровно та цена, которую платит и различение 'ГрЩУ-2'/'ЯЧ67-3'.
        /// Дефис ТОЛЬКО в последнем сегменте обозначения безопасен:
        /// '=Уст++Шкаф-Обозн-1' и '=Уст++Шкаф-Обозн-2' дают ОДИН ключ
        /// ('Шкаф-Обозн'), т.к. отрезается только последний сегмент.
        /// На стенде 02.10 риск НЕ ПРОЯВЛЯЕТСЯ: обозначения из 12 424 значений жил
        /// дефисов не содержат ('X3', 'X2:1', 'SB1', 'A1', 'K140'), а единственное
        /// значение с дефисом в обозначении ('=HII-1.1-A1-K2:X4:3') не содержит
        /// '++' и уходит в «сырую» ветку, где обозначение не режется вовсе.</summary>
        public static string CabinetKeyOf(string strDt)
        {
            if (string.IsNullOrEmpty(strDt)) return "";
            // 1) ПЕРВЫЙ '+' — начало полей; голова '=Установка' отбрасывается.
            int nPlus = strDt.IndexOf('+');
            if (nPlus < 0)
            {
                // '+' нет вовсе — маркеров полей нет, поэтому полей не будет и у
                // ключа: отдаём ВЕСЬ хвост без ведущего '=' как уникальный «сырой»
                // ключ. ВНИМАНИЕ (Ф-3, раунд 2): усечения по '#' ЗДЕСЬ НЕТ (в
                // прежней версии оно было: '=станция#1-Обозн' → 'станция'), ключ
                // получается длиннее и уникальнее. Последствия честно:
                //   • ложный multi исключён — разные значения не сойдутся;
                //   • но ПАРА настоящих значений ОДНОГО места со структурой '#'
                //     без '+' тоже перестанет сходиться ⇒ настоящий multi может
                //     ПРОПАСТЬ. На стенде инертно: ни одного '#' из 12 424 значений
                //     жил (02.10.2026), а собственный ОУ всегда содержит '++'.
                //     Осознанный размен, зафиксирован тестом в CaseCabinetKey.
                return strDt.StartsWith("=", StringComparison.Ordinal)
                    ? strDt.Substring(1) : strDt;
            }
            // 2) Отбросить обозначение прибора: последний '-' ПОСЛЕ первого '+'.
            //    Дефис ДО первого '+' (внутри Установки) границей не считается —
            //    иначе '=-+ВШ' потерял бы оба поля.
            string strStruct = strDt;
            for (int i = strDt.Length - 1; i > nPlus; i--)
            {
                if (strDt[i] != '-') continue;
                strStruct = strDt.Substring(0, i);
                break;
            }
            // 3) Разбор структурной части слева направо по маркерам.
            string strAssembly = "", strInstall = "";
            int nPos = nPlus;
            while (nPos < strStruct.Length)
            {
                char ch = strStruct[nPos];
                if (ch == '#') break;                        // структура пользователем
                if (ch != '+') { nPos++; continue; }         // страховка (недостижимо)
                bool bDouble = nPos + 1 < strStruct.Length && strStruct[nPos + 1] == '+';
                int nStart = bDouble ? nPos + 2 : nPos + 1;
                int nEnd = nStart;
                // Конец значения — '+', '#' или конец строки. Дефис ВНУТРИ значения
                // (например 'ГрЩУ-2') — обычный символ, разбор НЕ прерывает.
                while (nEnd < strStruct.Length && strStruct[nEnd] != '+' &&
                       strStruct[nEnd] != '#') nEnd++;
                // Первый маркер каждого вида побеждает — дальше только разбираемся
                // с ДРУГИМ полем (значение повторного поля отбрасываем).
                if (bDouble)
                {
                    if (strAssembly.Length == 0)
                        strAssembly = strStruct.Substring(nStart, nEnd - nStart);
                }
                else if (strInstall.Length == 0)
                    strInstall = strStruct.Substring(nStart, nEnd - nStart);
                nPos = nEnd;   // стоим на разделителе — цикл обработает '+' или выйдет
            }
            // 4) Оба поля пусты (вырожденное значение: единственный маркер ушёл
            //    вместе с обозначением, напр. '=Уст++-X3' или '=++') — та же
            //    уникальная «сырая» ветка: пустая пара сама с собой сошлась бы,
            //    т.е. разные DT дали бы один «шкаф». ВНИМАНИЕ: '=A+B-C' сюда НЕ
            //    относится — '+B' разбирается как Место установки, '-C' как
            //    обозначение ⇒ пара (' ', 'B') — это настоящий шкаф.
            if (strAssembly.Length == 0 && strInstall.Length == 0)
                return strDt.StartsWith("=", StringComparison.Ordinal)
                    ? strDt.Substring(1) : strDt;
            // 5) Ключ шкафа = ПАРА (Место сборки, Место установки).
            return strAssembly + "\u0001" + strInstall;
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

        /// <summary>Классификация по СПИСКУ ПОЛНЫХ ОУ КЛЕМ кабеля (источник сменился: rev16.3 — значения жил №31019/№31020; rev16.4, задача 3.1-bis — строки [DM], DmReport.CableStripEnds). arrEnds — все прочитанные DT концов кабеля;
        /// strStripOwnDt — полное ОУ нашего клеммника. Оба формата (с хвостом
        /// ':пин', с сегментом '#' или без) перевариваются одними и теми же
        /// CabinetKeyOf/DesignationOf.
        /// Шкаф = CabinetKeyOf = пара (Место сборки, Место установки); «свой конец» =
        /// пара (ключ шкафа, обозначение БЕЗ «:пин») — по ПРАВИЛУ ЗАКАЗЧИКА
        /// 02.10.2026 дословно: «если свойство совпадает с полным ОУ клеммника —
        /// это свой конец; если совпадает по шкафу, но буквенное обозначение
        /// отличается — multi; если не совпадает — противоположный конец».
        /// Сигнал multi = ТОТ ЖЕ шкаф И клеммник И отличное обозначение
        /// (X3 против X2). Всё прочее, что не равно нашей паре, —
        /// противоположный конец (в т.ч. клеммник ДРУГОГО шкафа и устройство
        /// своего шкафа: multi там НЕ присваивается).
        /// Правила (первое совпавшее), все сравнения строк Ordinal, элементы
        /// null/"" игнорируются:
        ///   0) разбор каждого элемента: пара (CabinetKeyOf, DesignationOf);
        ///      пара == наша → «свой конец» (пины 'X3:1'/'X3:2' — один конец);
        ///   1) нет ни одного непустого элемента → 'no-core-ends';
        ///   2) своего конца среди значений нет → 'own-end-missing' (проверяется
        ///      ДО multi, иначе «нет своего конца» выдавалось бы за multi);
        ///   3) есть «свой шкаф + клеммник + другое имя» → multi: при наличии
        ///      чужого шкафа Reason = 'multi-strip same-cabinet', иначе
        ///      'multi-strip';
        ///   4) иначе противоположный конец (чужой шкаф ИЛИ устройство своего
        ///      шкафа) → он же OppositeDt; его обозначение начинается с X/Х ⇒
        ///      'opposite-strip' при MultiStrip = false (клеммник чужого шкафа
        ///      multi НЕ даёт — ревью, I1), иначе 'opposite-device';
        ///   5) иначе (только свой конец) → 'own-end-only' (BP ставить не на что).
        /// OppositeDt — детерминированный (StringComparer.Ordinal, независимо от
        /// порядка чтения жил): правило 3 — первый чужой шкаф, иначе первый
        /// «свой шкаф + клеммник + другое имя» (иначе ОУ точки разрыва пусто —
        /// ревью, C2); правила 2 и 4 — первый «прочий».
        ///
        /// ВАЖНО, фикс-раунд 1 (02.10.2026): ЗДЕСЬ OppositeDt — ТОЛЬКО ДИАГНОСТИКА,
        /// и для имени точки разрыва ЕГО ИСПОЛЬЗОВАТЬ НЕЛЬЗЯ. Обоснование,
        /// записанное тогда, относилось к ЗНАЧЕНИЯМ ЖИЛ (31019/31020): на стенде их
        /// 12 424 штуки, ни одного '#' (нет пользовательской структуры), у значений
        /// для УСТРОЙСТВ нет даже '++МестоСборки', а хвост ':пин' не является
        /// частью имени. Поэтому:
        ///   '=HII-1.1++ПУ-XT1:1' → ComposeBpDeviceTag → '=HII-1.1++ПУ-K140(EXT)'
        ///     (нет '#'), и ParseDeviceTag отдаёт ВЕСЬ хвост как место сборки:
        ///     1400='ПУ-K140(EXT)', а 1600/20013/20014 пусты — структура ОУ
        ///     теряется (дефект, закрытый на стенде 01.10 «страница отчёта
        ///     структуру НЕ наследует»);
        ///   '=HII-1.1-SB1:1' (устройство) → DT как есть: теряются '++МестоСборки'
        ///     и '#структура', а в счётчик 20014 уходит '1:1'.
        /// ПРЕЖНЯЯ формулировка этого места («хвост :пин сохраняется,
        /// ComposeBpDeviceTag его отбрасывает») была НЕВЕРНОЙ: он его не
        /// отбрасывает, безопасности нет.
        /// rev16.4 (3.1-bis): ССЫЛАТЬСЯ НА ЭТО ОБОСНОВАНИЕ КАК НА ДЕЙСТВУЮЩЕЕ
        /// БОЛЬШЕ НЕЛЬЗЯ — вход Decide теперь полные ОУ клем из [DM], у которых
        /// сегмент '#' ЕСТЬ ('=HII-1.1++ПУ+#1-XT1:1'), и хвост ':пин' в имени
        /// тоже остаётся. Правило «решение здесь, имя — в потребителе» СОХРАНЕНО
        /// (не трогаем без стенда), но обосновано теперь ДРУГИМ: имя точки
        /// разрыва — это DT ТОЧКИ (с '#'-структурой и счётчиком EXT), которое
        /// есть только в 20376/20377; плюс приборный конец в [DM] не виден вовсе,
        /// так что подставить его имя по [DM] нельзя даже в принципе.
        /// РАЗДЕЛЕНИЕ ОТВЕТСТВЕННОСТИ (решение контроллера 02.10.2026; формулировка
        /// 02.10.2026 после 3.1-bis — «жилы» ⇒ «концы [DM]», роль ИСТОЧНИКА
        /// ИМЕНИ не изменилась):
        ///   концы [DM], полные ОУ клем (этот метод) → ТОЛЬКО РЕШЕНИЕ
        ///     (Kind / MultiStrip / Reason) + диагностический OppositeDt;
        ///   20376/20377 (полные DT, BlockFormatResolver.ResolveCableEnds) →
        ///     ИМЯ точки разрыва. Подмену делает потребитель
        ///     (AnalyzeAction.PrepareBreakPointDecisions: PickOppositeNameDt),
        ///     и в BreakPointPlacement.OppositeDt уходит полный DT оттуда;
        ///     если 20376/20377 не читаются — решение принудительно Unreadable
        ///     (BP не ставится, как в rev16.2), а не «жильное» имя с битой
        ///     структурой.</summary>
        public static BreakPointDecision Decide(string strStripOwnDt, string[] arrEnds)
        {
            // Разбор списка одним проходом: свой / «свой шкаф + клеммник + другое
            // имя» (сигнал multi) / всё остальное (кандидат в OppositeDt).
            // «Свой конец» = ПАРА (ключ шкафа, обозначение) равна нашей, а не
            // Ordinal-равенство сырых DT: на стенде свой ОУ приходит как
            // '=HII-1.1++ЯЧ67+#1-X3' (есть «#1»), а у жил — '=HII-1.1++ЯЧ67-X3:1'
            // (нет «#», есть «:пин»), поэтому точное равенство не сходилось
            // НИКОГДА, и каждый кабель уходил в 'own-end-missing' (дефект A2).
            string strOwnKey = CabinetKeyOf(strStripOwnDt);
            string strOwnDes = DesignationOf(strStripOwnDt);
            bool bOwn = false, bSameCabStrip = false;
            string strFirstSameCab = null;
            string strFirstForeign = null;
            if (arrEnds != null)
            {
                for (int i = 0; i < arrEnds.Length; i++)
                {
                    string strDt = arrEnds[i];
                    if (string.IsNullOrEmpty(strDt)) continue;
                    string strKey = CabinetKeyOf(strDt);
                    string strDes = DesignationOf(strDt);
                    // 1) СВОЙ конец: тот же шкаф И то же обозначение. Пин отброшен
                    // DesignationOf, поэтому 'X3:1' и 'X3:2' — один конец, а
                    // 'X3' против 'X2' — разные.
                    if (string.Equals(strKey, strOwnKey, StringComparison.Ordinal) &&
                        string.Equals(strDes, strOwnDes, StringComparison.Ordinal))
                    {
                        bOwn = true;
                        continue;
                    }
                    // 2) ТОТ ЖЕ ШКАФ + клеммник + ДРУГОЕ обозначение — сигнал multi.
                    // Проверка IsStripCodeLetter ОБЯЗАТЕЛЬНА: без неё любое
                    // устройство / предохранитель / реле своего шкафа даёт ложный
                    // multi ('=…++ЯЧ67+#1-SB1' — тот же шкаф, но НЕ клеммник) →
                    // две точки разрыва вместо одной (ревью, находка C1).
                    if (string.Equals(strKey, strOwnKey, StringComparison.Ordinal) &&
                        IsStripCodeLetter(strDes))
                    {
                        bSameCabStrip = true;
                        if (strFirstSameCab == null ||
                            string.CompareOrdinal(strDt, strFirstSameCab) < 0)
                            strFirstSameCab = strDt;
                    }
                    else if (strFirstForeign == null ||
                        string.CompareOrdinal(strDt, strFirstForeign) < 0)
                        strFirstForeign = strDt;
                }
            }
            // 1) ни одного читаемого DT жилы.
            if (!bOwn && !bSameCabStrip && strFirstForeign == null)
                return MkOpp(false, "no-core-ends", BpEndKind.Unreadable, null);
            // 2) своего клеммника среди значений нет — раньше любых multi.
            if (!bOwn)
                return MkOpp(false, "own-end-missing", BpEndKind.Unreadable, strFirstForeign);
            // 3) тот же шкаф, другое обозначение клеммника → multi (X3 против X2).
            // OppositeDt = чужой шкаф, а если его нет — «свой шкаф, другое имя»:
            // иначе кабель «наш клеммник + соседний клеммник ТОГО ЖЕ шкафа» дал
            // бы multi с ПУСТЫМ ОУ точки разрыва (ревью, находка C2).
            if (bSameCabStrip)
                return MkOpp(true,
                    strFirstForeign == null ? "multi-strip" : "multi-strip same-cabinet",
                    BpEndKind.TerminalStrip, strFirstForeign ?? strFirstSameCab);
            // 4) противоположный конец: чужой шкаф ИЛИ устройство своего шкафа —
            // он же OppositeDt. multi НЕ присваивается даже для клеммника
            // чужого шкафа: по ПРАВИЛУ ЗАКАЗЧИКА «если НЕ совпадает — это
            // противоположный конец», multi даёт только сигнал пункта 3
            // (ревью, находка I1).
            if (strFirstForeign != null)
            {
                bool bOppStrip = IsStripCodeLetter(DesignationOf(strFirstForeign));
                return MkOpp(false,
                    bOppStrip ? "opposite-strip" : "opposite-device",
                    bOppStrip ? BpEndKind.TerminalStrip : BpEndKind.Device,
                    strFirstForeign);
            }
            // 5) известен только свой конец.
            return MkOpp(false, "own-end-only", BpEndKind.Unreadable, null);
        }
    }
}
