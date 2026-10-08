using System;
using System.Globalization;
using Eplan.EplApi.DataModel;

namespace MyEplanActions
{
    /// <summary>Маркер владельца графических объектов (rev17.0, артефакт ревью P1 —
    /// генерация стала идемпотентной). Значение AddInConfiguration.OwnerMarkValue
    /// пишется у КАЖДОГО вставляемого объекта Фазы G; RemoveOwned ПЕРЕД вставкой
    /// удаляет со страницы всё, что помечено маркером — повторный прогон больше
    /// не дублирует объекты. GhostFrameCreator НЕ участвует: призрак убирается
    /// своей логикой ([GHOST-CLEAN]).
    /// СТЕНД 08.10 (терминал 09:57): свойство №20901 (допол. поле, indexed
    /// 1..1000) EPLAN допускает ТОЛЬКО на Function/Connection/Page и т.п. — на
    /// графических объектах запись дала S063109 «Недействительное свойство»
    /// (PropertyNotFoundException: в GraphicalPlacementPropertyList нет ни 20901,
    /// ни в SymbolReferencePropertyList), а на Function запись БЕЗ индекса дала
    /// S063113 «Не удалось установить значение» (SettingValueFailedException —
    /// свойство indexed; KB 2.9: FunctionPropertyList~Property(AnyPropertyId,
    /// Int32) — «access indexed properties», запись через Properties[oId, 1]).
    /// Поэтому МАРКЕР АДАПТИВНЫЙ — попытки в порядке приоритета, первая успешная
    /// фиксирует ячейку: (1) №20901 С ИНДЕКСОМ 1 (FunctionPropertyList,
    /// только Function); (2) №20901 без индекса (раньше так писали — уже знает
    /// провал, оставлен для полноты); (3) №19100 INSTANCE_PARAMETRICRULE
    /// (GraphicalPlacementPropertyList, «Параметрические правила», незindexed,
    /// доступен на графике — линии/стрелки). ReadMark читает ТЕ ЖЕ ячейки той же
    /// цепочкой — маркер самозаключён. Каждая попытка — свой try/catch, лог
    /// [OWNER-MARK] с номером попытки; все отказы — WARN, НЕ фатальны.
    /// Паттерн записи свойств — CableSymbolCreator.cs:226
    /// (Properties[oId] = ...), перечисление объектов — как в
    /// BlockFormatResolver.ResolveCableEnds (DMObjectsFinder + Filters).</summary>
    public static class GraphicsOwnerMark
    {
        /// <summary>Пометить объект маркером владельца — адаптивная цепочка
        /// (см. class-комментарий): 20901[oId]=..., 20901, 19100. strStrip —
        /// ПОЛНОЕ ОУ клеммника ЭТОГО прогона: значение = "TERMINAL_STRIP_ADDIN
        /// |<ОУ>"; очистка RemoveOwned удаляет объекты только ЭТОГО клеммника
        /// (прогон 11:39: маркер без адресата сносил графику и другого
        /// клеммника страницы — регрессия исправлена разделителем ОУ).
        /// Голый маркер (ОУ пусто) — легаси; RemoveOwned любого прогона его
        /// удалит. Самоверификация: запись перечитывается (ReadMark), отказ —
        /// WARN с диагнозом ReadMarkDetail по ячейке. Успешная запись — INFO
        /// с ОУ. Отказы НЕ фатальны.</summary>
        public static bool Mark(StorableObject oObj, string strStrip, DiagnosticLogger log)
        {
            if (oObj == null) return false;
            string strWhich;
            if (!TryWrite(oObj, BuildOwnerValue(strStrip), out strWhich, log))
                return false;
            if (!ReadMark(oObj))
            {
                string strDetail;
                ReadMarkDetail(oObj, out strDetail);
                string strNote = (strDetail == null ? "" : " — ReadMarkDetail: " + strDetail);
                log.Warn("[OWNER-MARK] (v4) записано через " + strWhich +
                    ", но ReadMark НЕ вернул маркер (тип " + oObj.GetType().Name +
                    ")" + strNote +
                    " — удаление на следующем прогоне может пропустить объект");
                return false;
            }
            log.Log("[INFO] [OWNER-MARK] маркер записан через " + strWhich +
                " и прочитан обратно OK" +
                (string.IsNullOrEmpty(strStrip) ? "" : " (клеммник " + strStrip + ")"));
            return true;
        }

        /// <summary>Обратная-совместимая перегрузка БЕЗ ОУ клеммника: объект
        /// получит ГОЛЫЙ маркер, который RemoveOwned любого следующего прогона
        /// удалит (считается легаси). В Фазе G используется перегрузка с ОУ.
        public static bool Mark(StorableObject oObj, DiagnosticLogger log)
        {
            return Mark(oObj, null, log);
        }

        /// <summary>Значение маркера с ОУ (или голое — с пустым ОУ). Разделитель
        /// '|' безопасен: ОУ («=HII-1.1++М+#2-K140») содержит '=','+','#','-' и
        /// БЕЗ '|' и ';', а '@'/';' — служебные серым ML-обёртки.</summary>
        public static string BuildOwnerValue(string strStrip)
        {
            if (string.IsNullOrEmpty(strStrip)) return AddInConfiguration.OwnerMarkValue;
            return AddInConfiguration.OwnerMarkValue +
                AddInConfiguration.OwnerMarkStripSeparator + strStrip;
        }

        /// <summary>Одна попытка записи: типизированная ячейка по (номер, индекс).
        /// Индекс &gt;= 0 — по типизированному списку: Function
        /// (FunctionPropertyList) и InterruptionPoint (InterruptionPointPropertyList —
        /// BP, перекрытие Symbols; KB 2.9: у обоих есть Property(AnyPropertyId,
        /// Int32)); прочие типы на индексированном свойстве отбрасываются наружу
        /// (PropertyNotFoundException — норма для графики). Индекс -1 — голый
        /// indexer UniversalPropertyList. Отказ — исключение наружу.</summary>
        private static bool TryOne(StorableObject oObj, int nPropNumber, int nIndex,
            string strValue)
        {
            AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(nPropNumber);
            if (oId == null)
                throw new InvalidOperationException("id " + nPropNumber.ToString(
                    CultureInfo.InvariantCulture) + " не создался");
            if (nIndex >= 0)
            {
                Function oFunc = oObj as Function;
                if (oFunc != null)
                {
                    oFunc.Properties[oId, nIndex] = (PropertyValue)strValue;
                    return true;
                }
                InterruptionPoint oIp = oObj as InterruptionPoint;
                if (oIp != null)
                {
                    oIp.Properties[oId, nIndex] = (PropertyValue)strValue;
                    return true;
                }
                throw new InvalidOperationException("на типе " + oObj.GetType().Name +
                    " типизированный доступ к indexed-свойству недоступен");
            }
            oObj.Properties[oId] = (PropertyValue)strValue;
            return true;
        }

        /// <summary>Адаптивная запись ПО ЦЕПОЧКЕ (см. class-комментарий):
        /// у Function — попытка [20901, idx 1] (уже проверено, что без индекса
        /// EPLAN отказывает); затем [20901] без индекса; затем [19100] —
        /// графические объекты. Первая успешная — в strWhich. ReadMark идёт
        /// ПО ТЕМ ЖЕ ячейкам ТЕМ ЖЕ порядком — см. ReadMark.</summary>
        private static bool TryWrite(StorableObject oObj, string strValue,
            out string strWhich, DiagnosticLogger log)
        {
            strWhich = null;
            int[] arrNumbers = new int[] {
                AddInConfiguration.OwnerMarkPropertyNumber,    // 20901
                AddInConfiguration.OwnerMarkParametricNumber   // 19100
            };
            // Проба 1: 20901 С ИНДЕКСОМ 1 — Function и InterruptionPoint (BP).
            bool bTypedIndexCandidate = (oObj is Function) || (oObj is InterruptionPoint);
            if (bTypedIndexCandidate)
            {
                try
                {
                    TryOne(oObj, arrNumbers[0], 1, strValue);
                    strWhich = "20901[1]";
                    return true;
                }
                catch (Exception oEx)
                {
                    log.Warn("[OWNER-MARK] попытка 1 (20901[1]) бросила " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            // Проба 2: 20901 без индекса. Для GraphicalPlacement пропускается —
            // свойство недействительно (S063109 подтверждено прогоном 11:01),
            // попытка оставить только для прочих НЕтипизированных типов.
            if (!(oObj is Eplan.EplApi.DataModel.Graphics.GraphicalPlacement))
            {
                try
                {
                    TryOne(oObj, arrNumbers[0], -1, strValue);
                    strWhich = "20901";
                    return true;
                }
                catch (Exception oEx)
                {
                    log.Warn("[OWNER-MARK] попытка 2 (20901) бросила " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            // Проба 3: 19100 INSTANCE_PARAMETRICRULE — GraphicalPlacement.
            try
            {
                TryOne(oObj, arrNumbers[1], -1, strValue);
                strWhich = "19100";
                log.Log("[INFO] [OWNER-MARK] маркер записан через 19100");
                return true;
            }
            catch (Exception oEx)
            {
                log.Warn("[OWNER-MARK] попытка 3 (19100) бросила " +
                    oEx.GetType().Name + ": " + oEx.Message + " — маркер НЕ записан");
            }
            return false;
        }

        /// <summary>Чтение маркера владельца — ТЕ ЖЕ ячейки ТЕМ ЖЕ порядком
        /// (первая непустая = маркер). Сравнение строки — Ordinal. Пусто/ошибка/
        /// чужой текст — false.</summary>
        public static bool ReadMark(StorableObject oObj)
        {
            string strDetail;
            return ReadMarkDetail(oObj, out strDetail);
        }

        /// <summary>Чтение маркера с диагностикой по ячейке (strDetail <> null —
        /// маркер не прочитан и why). Используется обёрткой и самоверификацией
        /// Mark.</summary>
        private static bool ReadMarkDetail(StorableObject oObj, out string strDetail)
        {
            strDetail = null;
            if (oObj == null)
            {
                strDetail = "obj=null";
                return false;
            }
            int[] arrNumbers = new int[] {
                AddInConfiguration.OwnerMarkPropertyNumber,
                AddInConfiguration.OwnerMarkParametricNumber
            };
            string strVal;
            // Ячейка 1: 20901[1] — Function/InterruptionPoint типизированно.
            try
            {
                Function oFunc = oObj as Function;
                InterruptionPoint oIp = null;
                if (oFunc == null) oIp = oObj as InterruptionPoint;
                if (oFunc != null || oIp != null)
                {
                    AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(
                        arrNumbers[0]);
                    if (oId != null)
                    {
                        if (oFunc != null) strVal = oFunc.Properties[oId, 1];
                        else strVal = oIp.Properties[oId, 1];
                        if (strVal != null)
                        {
                            if (!IsMarkerValue(strVal))
                                strDetail = "ячейка 20901[1] = '" + Repr(strVal) +
                                    "' — не совпало с маркером";
                            return IsMarkerValue(strVal);
                        }
                        strDetail = "ячейка 20901[1] пуста";
                    }
                    else strDetail = "id 20901 не создался";
                }
                else strDetail = "типизации нет (" + oObj.GetType().Name + ")";
            }
            catch (Exception oEx)
            {
                strDetail = "чтение 20901[1] бросило " + oEx.GetType().Name;
            }
            // Ячейки 2/3: 20901, затем 19100.
            for (int i = 0; i < arrNumbers.Length; i++)
            {
                strVal = null;
                try
                {
                    AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(
                        arrNumbers[i]);
                    if (oId == null) continue;
                    strVal = oObj.Properties[oId];
                }
                catch (Exception oEx)
                {
                    strDetail = (strDetail == null ? "" : strDetail + "; ") +
                        "чтение " + arrNumbers[i].ToString(CultureInfo.InvariantCulture) +
                        " бросило " + oEx.GetType().Name;
                    strVal = null;
                    continue;
                }
                if (strVal != null)
                {
                    if (!IsMarkerValue(strVal))
                        strDetail = (strDetail == null ? "" : strDetail + "; ") +
                            arrNumbers[i].ToString(CultureInfo.InvariantCulture) +
                            " = '" + Repr(strVal) + "' — не совпало";
                    return IsMarkerValue(strVal);
                }
                strDetail = (strDetail == null ? "" : strDetail + "; ") +
                    arrNumbers[i].ToString(CultureInfo.InvariantCulture) + ": значение пусто";
            }
            if (strDetail == null) strDetail = "все ячейки пусты";
            return false;
        }

        /// <summary>Представление прочитанного значения для диагностики:
        /// контрольные символы и пустая строка видны явно, хвост обрезан
        /// до 60 символов.</summary>
        private static string Repr(string strVal)
        {
            if (strVal == null) return "<null>";
            if (string.IsNullOrEmpty(strVal)) return "''";
            string strShown = strVal;
            if (strShown.Length > 60)
                strShown = strShown.Substring(0, 60) + "…(" +
                    strVal.Length.ToString(CultureInfo.InvariantCulture) + ")";
            return strShown;
        }

        /// <summary>Маркер ли строка (+ при совпавшем — ОУ владельца, или null
        /// если легаси без ОУ)? №20901 хранится как MultiLangString:
        /// PropertyValue при чтении отдаёт полный ML-текст вида
        /// "язык@значение;" (стенд 08.10 11:39), поэтому точное Ordinal-
        /// сравнение завершается мимо. Разбор: токены по ';', у токена
        /// значение — после последнего '@'; НОВЫЙ формат значения —
        /// "马鞍TERMINAL_STRIP_ADDIN|<ОУ>" (BuildOwnerValue), старый — голый
        /// маркер. Плоская строка без '@' — прямое сравнение. null/пусто —
        /// false.</summary>
        private static bool IsMarkerValue(string strVal, out string strOwnerStrip)
        {
            strOwnerStrip = null;
            if (strVal == null) return false;
            string strPrefix = AddInConfiguration.OwnerMarkValue;
            string strSep = AddInConfiguration.OwnerMarkStripSeparator;
            if (ParseOwnerToken(strVal, strPrefix, strSep, out strOwnerStrip)) return true;
            string[] arrTokens = strVal.Split(';');
            for (int i = 0; i < arrTokens.Length; i++)
            {
                string strToken = arrTokens[i];
                int iAt = strToken.LastIndexOf('@');
                if (iAt >= 0) strToken = strToken.Substring(iAt + 1);
                if (ParseOwnerToken(strToken, strPrefix, strSep, out strOwnerStrip))
                    return true;
            }
            strOwnerStrip = null;
            return false;
        }

        /// <summary>Одно значение (без '@'/';'): голый маркер — strip=null;
        /// маркер+разделитель+ОУ — strip=ОУ; прочее — false.</summary>
        private static bool ParseOwnerToken(string strToken, string strPrefix,
            string strSep, out string strOwnerStrip)
        {
            strOwnerStrip = null;
            if (strToken == null) return false;
            if (string.Equals(strToken, strPrefix, StringComparison.Ordinal))
                return true;
            if (strToken.Length > strPrefix.Length + strSep.Length &&
                string.Equals(strToken.Substring(0, strPrefix.Length), strPrefix,
                    StringComparison.Ordinal) &&
                strToken.Substring(strPrefix.Length, strSep.Length) == strSep)
            {
                strOwnerStrip = strToken.Substring(strPrefix.Length + strSep.Length);
                return true;
            }
            return false;
        }

        private static bool IsMarkerValue(string strVal)
        {
            string strOwnerStrip;
            return IsMarkerValue(strVal, out strOwnerStrip);
        }

        /// <summary>Удаляет со страницы все объекты с маркером владельца
        /// (rev17.0: вызывается в AnalyzeAction ПЕРЕД обоими блоками вставки —
        /// headless Run и UI RunPipeline, правки в ОБОИХ ветках).
        /// Перечисление в ДВА прохода (как BlockFormatResolver.ResolveCableEnds):
        /// GetPlacements(PlacementsFilter.Page) — линии разводки,
        /// полилинии-стрелки и прочие графические объекты; GetFunctions(
        /// FunctionsFilter.Page) — символы кабелей (Function) и BP
        /// (SymbolReference/InterruptionPoint). Дубли пересекаются безвредно:
        /// удалённый в первом проходе объект во втором не читается
        /// (ReadMark — false). Страница null — 0. Каждое удаление — в своём
        /// try/catch, провал НЕ фатален; лог [OWNER-CLEAN] и итог
        /// [OWNER-CLEAN-SUM] — стиль [GHOST-CLEAN]. Возвращает число удалённых.</summary>
        public static int RemoveOwned(Page oPage, string strStrip, DiagnosticLogger log)
        {
            if (oPage == null) return 0;
            log.Log("[INFO] [OWNER-CLEAN] очистка маркерных объектов перед вставкой" +
                (string.IsNullOrEmpty(strStrip) ? "" : " (клеммник " + strStrip + ")"));
            DMObjectsFinder oFinder = null;
            try
            {
                oFinder = new DMObjectsFinder(oPage.Project);
            }
            catch (Exception oEx)
            {
                log.Warn("[OWNER-CLEAN] перечисление объектов не удалось (DMObjectsFinder): " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return 0;
            }

            OwnerCounts oCounts = new OwnerCounts();
            int nRemoved = 0;
            // Проход 1: placements страницы.
            try
            {
                PlacementsFilter oFilter = new PlacementsFilter();
                oFilter.Page = oPage;
                nRemoved += RemoveMarkedAll(oFinder.GetPlacements(oFilter), strStrip, oCounts, log);
            }
            catch (Exception oEx)
            {
                log.Warn("[OWNER-CLEAN] перечисление placements не удалось: " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            // Проход 2: функции страницы (символы кабелей, BP).
            try
            {
                FunctionsFilter oFilter = new FunctionsFilter();
                oFilter.Page = oPage;
                nRemoved += RemoveMarkedAll(oFinder.GetFunctions(oFilter), strStrip, oCounts, log);
            }
            catch (Exception oEx)
            {
                log.Warn("[OWNER-CLEAN] перечисление functions не удалось: " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }

            nRemoved = oCounts.nRemovedAll;
            log.Log("[INFO] [OWNER-CLEAN-SUM] удалено маркерных объектов " +
                nRemoved.ToString(CultureInfo.InvariantCulture) +
                " (этот клеммник " + oCounts.nRemovedOwn.ToString(CultureInfo.InvariantCulture) +
                ", легаси без ОУ " + oCounts.nRemovedLegacy.ToString(CultureInfo.InvariantCulture) +
                "); сохранены ЧУЖИХ " + oCounts.nKept.ToString(CultureInfo.InvariantCulture) +
                " (маркер другого клеммника).");
            return nRemoved;
        }

        /// <summary>Удаление помеченных объектов массива: ReadMark у каждого,
        /// ПРИНАДЛЕЖАЩИЙ ТЕКУЩЕМУ клеммнику (совпало ОУ) или легаси без ОУ
        /// удаляется; ЧУЖИЕ (маркер другого клеммника) — сохраняется и
        /// считается (прогон 11:39: очистка сносила и чужих). Удаление —
        /// as Placement (KB 2.9: Remove объявлен на Placement, а НЕ на
        /// StorableObject), каждый за своим try/catch — провал не фатален.
        /// Возвращает число удалённых.</summary>
        private static int RemoveMarkedAll(StorableObject[] arrObjs, string strStrip,
            OwnerCounts oCounts, DiagnosticLogger log)
        {
            if (arrObjs == null) return 0;
            int nDeleted = 0;
            foreach (StorableObject oObj in arrObjs)
            {
                if (oObj == null) continue;
                if (!ReadMark(oObj)) continue;
                string strOwnerStrip;
                bool bOwnerSame = false, bLegacy = false;
                // Чтение исходника ячейки повторно (ReadMarkDetail отдаёт только
                // диагноз) — парсинг strip из IsMarkerValue.
                string strRaw = ReadMarkedRaw(oObj);
                if (IsMarkerValue(strRaw, out strOwnerStrip))
                {
                    if (strOwnerStrip == null) bLegacy = true;
                    else if (string.Equals(strOwnerStrip, strStrip,
                        StringComparison.Ordinal)) bOwnerSame = true;
                }
                if (!bOwnerSame && !bLegacy)
                {
                    oCounts.nKept++;
                    continue;
                }
                try
                {
                    Placement oPl = oObj as Placement;
                    if (oPl != null)
                    {
                        oPl.Remove();
                        nDeleted++;
                        oCounts.nRemovedAll++;
                        if (bOwnerSame) oCounts.nRemovedOwn++;
                        else oCounts.nRemovedLegacy++;
                    }
                    else
                    {
                        log.Warn("[OWNER-CLEAN] объект не Placement — Remove недоступен, пропущен");
                    }
                }
                catch (Exception oEx)
                {
                    log.Warn("[OWNER-CLEAN] Remove бросил " + oEx.GetType().Name +
                        ": " + oEx.Message + " — объект пропущен");
                }
            }
            return nDeleted;
        }

        /// <summary>Счётчики прохода очистки: удалённые от ЭТОГО клеммника,
        /// легаси без ОУ, и сохранённые ЧУЖИЕ (иные ОУ). nRemovedAll —
        /// суммарно удалённые (Own + Legacy).</summary>
        private class OwnerCounts
        {
            internal int nRemovedAll;
            internal int nRemovedOwn;
            internal int nRemovedLegacy;
            internal int nKept;
        }

        /// <summary>Сырое значение маркерной ячейки (первая непустая из цепочки:
        /// 20901[1] типизированно для Function/InterruptionPoint, затем 20901 и
        /// 19100 без индекса — как ReadMarkDetail, но БЕЗ подробного диагнозов).
        /// null — не прочитано ни одна ячейка.</summary>
        private static string ReadMarkedRaw(StorableObject oObj)
        {
            if (oObj == null) return null;
            int[] arrNumbers = new int[] {
                AddInConfiguration.OwnerMarkPropertyNumber,
                AddInConfiguration.OwnerMarkParametricNumber
            };
            try
            {
                Function oFunc = oObj as Function;
                InterruptionPoint oIp = null;
                if (oFunc == null) oIp = oObj as InterruptionPoint;
                if (oFunc != null || oIp != null)
                {
                    AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(
                        arrNumbers[0]);
                    if (oId != null)
                    {
                        string strVal = (oFunc != null)
                            ? oFunc.Properties[oId, 1]
                            : oIp.Properties[oId, 1];
                        if (strVal != null) return strVal;
                    }
                }
            }
            catch { }
            for (int i = 0; i < arrNumbers.Length; i++)
            {
                try
                {
                    AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(
                        arrNumbers[i]);
                    if (oId == null) continue;
                    string strVal = oObj.Properties[oId];
                    if (strVal != null) return strVal;
                }
                catch { }
            }
            return null;
        }
    }
}