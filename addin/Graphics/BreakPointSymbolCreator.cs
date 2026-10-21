using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;   // NameService (AdjustVisibleName, путь B — S4)

namespace MyEplanActions
{
    /// <summary>Вставка символа точки разрыва BP «48 / BPIN» (rev.16.2 прод-волна,
    /// решения пользователя 30.09/01.10.2026, вердикты spike S1–S4): библиотека
    /// SPECIAL, константы AddInConfiguration (straight A(0)/D(3), multi
    /// E(4)/B(1)). На каждый BreakPointPlacement из геометрии:
    /// SymbolReference.Create(SymbolVariant, Page) (Function.Create =
    /// S063085 НАВСЕГДА — спецсимвол вне категорий, FD нет; вердикт S1;
    /// результат — InterruptionPoint) → Location = Position БЕЗ компенсации
    /// (R9: якорь = центр бокса, дельты симметричны — вердикт S3) →
    /// набор отображения из point/*.emc: скан каталога НА старте, выбор по
    /// kind-константе И варианту A2453 (Import чужого варианта — тихий
    /// no-op, S2 → BP без набора), Import(path, true) один раз на файл за
    /// прогон + Selected по имени схемы A2454 (Import кладёт в All, НЕ
    /// выбирает; S1) → ОУ через путь B (S4, Sepla RenameAll): LockObject +
    /// NameParts = offline (FUNC_CODE #20013 + FUNC_COUNTER #20014;
    /// структура наследуется от страницы отчёта — НЕ пишем 1100/1400/1200/
    /// 1600) + NameService.AdjustVisibleName; DT —
    /// BreakPointResolver.ComposeBpDeviceTag (клеммник → структура обратного
    /// конца + код кабеля + '(EXT)', устройство → как есть). rev17.0: маркер
    /// владельца у каждого BP — генерация идемпотентна
    /// (как [SYMBOL]; отказы — WARN [BP-ERR], отчёт не прерывается; итог
    /// [BP-SUM]). Кросс-ссылка зашита в .emc, отдельно не пишем.</summary>
    public static class BreakPointSymbolCreator
    {
        /// <summary>Папка point/ с наборами .emc — решение пользователя
        /// (01.10, стендинг): bin рядом со сборкой → CodeBase (реальная
        /// сборка ДО shadow-copy — ключ S1: копия EPLAN пуста). Первый
        /// существующий; assembly пуст / ничего нет → null → WARN у
        /// потребителя (BP без набора). env-override НЕ используется
        /// (упрощение решения пользователя).</summary>
        public static string ResolvePointFolder()
        {
            // rev.16.14: вызов без внешних кандидатов — поведение прежнее.
            return ResolvePointFolder(null);
        }

        /// <summary>rev.16.14: перегрузка с кандидатами из настроек EPLAN
        /// (AnalyzeAction.Execute собирает «Сценарии» + подпапку Add-in'а через
        /// LogDirResolver; null здесь = вызов без них). Порядок проб прежний:
        /// каталог загруженной сборки → CodeBase (реальная сборка ДО shadow-copy)
        /// → переданные каталоги, первый СУЩЕСТВУЮЩИЙ. Захардкоженных путей в
        /// проекте не осталось (решение заказчика 04.10), поэтому после
        /// переданных каталогов запасных проб нет вовсе — point/ ищется там, где
        /// её действительно клали. Контракт прежний: null/отказ → null, WARN у
        /// потребителя (BP без набора).</summary>
        public static string ResolvePointFolder(string[] arrExtraDirs)
        {
            try
            {
                Assembly oAsm = Assembly.GetExecutingAssembly();
                string strLocDir = Path.GetDirectoryName(oAsm.Location);
                if (!string.IsNullOrEmpty(strLocDir))
                {
                    string strTry = Path.Combine(strLocDir,
                        AddInConfiguration.PointSchemeFolder);
                    if (Directory.Exists(strTry)) return strTry;
                }
                // Кандидат CodeBase: реальная сборка ДО shadow-copy (Location —
                // копия EPLAN, point/ там пуста — S1). При не-shadow загрузке
                // CodeBase == Location — уже проверено выше.
                string strCodeBase = oAsm.CodeBase;
                if (!string.IsNullOrEmpty(strCodeBase) && strCodeBase.StartsWith(
                    "file://", StringComparison.OrdinalIgnoreCase))
                {
                    string strRealDir = Path.GetDirectoryName(
                        new Uri(strCodeBase).LocalPath);
                    if (!string.IsNullOrEmpty(strRealDir) &&
                        !string.Equals(strRealDir, strLocDir ?? "",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        string strTryReal = Path.Combine(strRealDir,
                            AddInConfiguration.PointSchemeFolder);
                        if (Directory.Exists(strTryReal)) return strTryReal;
                    }
                }
            }
            catch { }
            // rev.16.14: переданные кандидаты (01.10): папки из Diagnostics (лог/
            // настройки живут в папке «Сценарии» EPLAN — РЯДОМ с реальной
            // сборкой; CodeBase может указывать в иное место после регистрации
            // аддина в EPLAN). Это последняя проба: первые две — каталог сборки и
            // CodeBase, дальше запасных каталогов нет (решение заказчика 04.10).
            // Первый существующий.
            return TryPointFolderIn(arrExtraDirs);
        }

        /// <summary>rev.16.14: первый существующий point/ среди каталогов списка.
        /// null/пустой список и null/пустые элементы пропускаются; на каждый
        /// кандидат свой try/catch — один недоступный каталог (сеть, запрет) не
        /// должен обрывать перебор, как и до rev.16.14.</summary>
        private static string TryPointFolderIn(string[] arrDirs)
        {
            if (arrDirs == null)
                return null;
            foreach (string strCandidate in arrDirs)
            {
                try
                {
                    if (string.IsNullOrEmpty(strCandidate)) continue;
                    string strTryLog = Path.Combine(strCandidate,
                        AddInConfiguration.PointSchemeFolder);
                    if (Directory.Exists(strTryLog)) return strTryLog;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Вставка BP по BreakPointPlacement. Возвращает число созданных.
        /// Вариант: Multi → MultiH/MultiV, иначе StraightH/StraightV — по
        /// bVertical. Файл набора: multi → EmcMulti*, иначе по Kind
        /// (TerminalStrip → EmcStraightStrip*, Device → EmcStraightDevice*) и
        /// ориентации; вариант проверяется по A2453 (чужой вариант = тихий
        /// no-op Import — не импортируем). Каталог point/ строится при
        /// ПЕРВОЙ вставке ТОЛЬКО на headless-ветке (selections == null;
        /// решение 01.10: скан на старте команды — здесь
        /// эквивалентно, скан дешевле страниц). bVertical — ориентация ОТЧЁТА
        /// (как eOrientation). rev.17 (Task 8): selections == null — прежний
        /// headless-путь (kind-константы Emc* + PickScheme по A2453,
        /// байт-идентичен); selections != null — профиль по выбору пользователя
        /// (BpProfileSelections.For: kind/multi/ориентация → EmcSchemeInfo),
        /// For вернул null — BP с дефолтным отображением, WARN/лог.</summary>
        public static int CreateBreakPoints(Page oPage, CableGeometryResult oGeom,
            bool bVertical, string strPointFolder, DiagnosticLogger log,
            BpProfileSelections selections = null, string strStrip = null)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[BP] CreateBreakPoints: page или geometry == null — BP не создаём");
                log.Log("[INFO] [BP-SUM] точек BP 0 (page или geometry == null).");
                return 0;
            }
            int nTotal = oGeom.BreakPoints.Count;
            if (nTotal == 0)
            {
                log.Log("[INFO] [BP-SUM] точек BP 0.");
                return 0;
            }
            if (string.IsNullOrEmpty(strPointFolder))
                log.Warn("[BP] папка point/ не разрешена — наборы .emc не применяются");
            else
                log.Log("[INFO] [BP] папка наборов point/ = '" + strPointFolder + "'");

            // rev.16.2 (01.10): скан point/ НА СТАРТЕ (до первой вставки) —
            // A2453/A2454 всех *.emc. Пусто/нечитаемо — WARN, вставки БЕЗ набора.
            // rev.17 (fix review): каталог нужен ТОЛЬКО headless-ветке
            // (selections == null) — на ветке выбора пользователя файл и имя
            // схемы берутся из BpProfileSelections.For, скан не нужен.
            List<EmcSchemeInfo> lstEmc = null;
            if (selections == null && !string.IsNullOrEmpty(strPointFolder))
                lstEmc = EmcSchemeCatalog.ParseDirectory(strPointFolder, log);
            // Лукап библиотеки/символа — ОДИН раз на прогон (M2-ревью:
            // библиотека перечитывалась на каждый BP); вариант от Symbol.
            Symbol oSymbol = null;
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project,
                    AddInConfiguration.BreakPointSymbolLibrary);
                oSymbol = oLibrary[AddInConfiguration.BreakPointSymbolName];
            }
            catch (Exception oExLib)
            {
                log.Warn("[BP-ERR] лукап '" + AddInConfiguration.BreakPointSymbolLibrary +
                    "'['" + AddInConfiguration.BreakPointSymbolName + "'] бросил " +
                    oExLib.GetType().Name + ": " + oExLib.Message +
                    " — BP не вставляются");
                log.Log("[INFO] [BP-SUM] точек BP 0 (символ не разрешился).");
                return 0;
            }
            HashSet<string> setImportLogged = new HashSet<string>(); // [BP-EMC] 1 раз на файл/прогон (Import — на КАЖДЫЙ BP, идемпотентен S2)
            HashSet<string> setMissingWarned = new HashSet<string>(); // WARN 1 раз на имя файла
            HashSet<string> setProfileMissingLogged = new HashSet<string>(); // rev.17: «профиль не выбран» 1 раз на (kind,multi,ориентация)

            int nCreated = 0;
            foreach (BreakPointPlacement oBpPlacement in oGeom.BreakPoints)
            {
                string strName = oBpPlacement.CableName ?? "<без имени>";
                try
                {
                    int nVariant = oBpPlacement.Multi
                        ? (bVertical ? AddInConfiguration.BreakPointVariantMultiV
                                     : AddInConfiguration.BreakPointVariantMultiH)
                        : (bVertical ? AddInConfiguration.BreakPointVariantStraightV
                                     : AddInConfiguration.BreakPointVariantStraightH);

                    SymbolVariant oVariant = oSymbol[nVariant];

                    // VERDICT S1: спецсимвол (S063085 на Function.Create) —
                    // через SymbolReference.Create(SymbolVariant, Page);
                    // результат — производный тип (InterruptionPoint).
                    SymbolReference oRef = SymbolReference.Create(oVariant, oPage);
                    // Ruling R9 (вердикт S3): якорь = центр бокса, компенсация не
                    // нужна; Position всегда задан геометрией (struct).
                    oRef.Location = new PointD(oBpPlacement.Position.X,
                        oBpPlacement.Position.Y);

                    ApplyEmcScheme(oRef, nVariant, oBpPlacement, bVertical,
                        strPointFolder, lstEmc, setImportLogged, setMissingWarned,
                        setProfileMissingLogged, log, selections);

                    WriteBpDeviceTag(oRef, oBpPlacement, oPage, log);

                    // rev17.0 (прогон 11:01): маркер владельца — ПОСЛЕ записи ОУ
                    // (KB 2.9: объект уже LockObject'ован в WriteBpDeviceTag;
                    // свойство BP = InterruptionPointPropertyList, 20901 indexed —
                    // см. GraphicsOwnerMark: попытка 1 [20901, 1]; до правки
                    // маркер не писался: ни 20901 (S063113 до лок-а), ни 19100).
                    GraphicsOwnerMark.Mark(oRef, strStrip, log);

                    nCreated++;
                    log.Log("[INFO] [BP] '" + strName + "' #" +
                        oBpPlacement.CableIndex.ToString(CultureInfo.InvariantCulture) + " @ (" +
                        oBpPlacement.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oBpPlacement.Position.Y.ToString("F3", CultureInfo.InvariantCulture) +
                        ") variant=" + nVariant.ToString(CultureInfo.InvariantCulture) +
                        (oBpPlacement.Multi ? " multi" : "") +
                        " ОУ='" + WriteBpDeviceTagPreview(oBpPlacement) + "'");
                }
                catch (Exception oEx)
                {
                    log.Warn("[BP-ERR] '" + strName + "' #" +
                        oBpPlacement.CableIndex.ToString(CultureInfo.InvariantCulture) + ": " +
                        oEx.GetType().Name + ": " + oEx.Message + " — BP не вставлен");
                }
            }
            log.Log("[INFO] [BP-SUM] точек BP " + nCreated.ToString(CultureInfo.InvariantCulture) +
                " из " + nTotal.ToString(CultureInfo.InvariantCulture) + ".");
            return nCreated;
        }

        /// <summary>Имя файла .emc по решению: multi → EmcMulti*; иначе
        /// TerminalStrip → EmcStraightStrip*, Device → EmcStraightDevice*;
        /// пара (гориз/вертик) — по bVertical. Kind Unreadable → null.
        /// ОУ-строка для лога — через ComposeBpDeviceTag (null-надёжно).</summary>
        private static string GetEmcFileName(BreakPointPlacement oBp, bool bVertical)
        {
            if (oBp.Multi)
                return bVertical ? AddInConfiguration.EmcMultiStripV
                                 : AddInConfiguration.EmcMultiStripH;
            if (oBp.Kind == BpEndKind.Device)
                return bVertical ? AddInConfiguration.EmcStraightDeviceV
                                 : AddInConfiguration.EmcStraightDeviceH;
            if (oBp.Kind == BpEndKind.TerminalStrip)
                return bVertical ? AddInConfiguration.EmcStraightStripV
                                 : AddInConfiguration.EmcStraightStripH;
            return null;
        }

        private static string WriteBpDeviceTagPreview(BreakPointPlacement oBp)
        {
            // ОУ одинаков с WriteBpDeviceTag (multi — кабель как есть).
            return oBp.Multi
                ? (oBp.CableName ?? "<нечитаемо>")
                : (BreakPointResolver.ComposeBpDeviceTag(oBp.OppositeDt, oBp.CableName) ?? "<нечитаемо>");
        }

        /// <summary>Набор отображения (.emc) — Import + Selected. rev.17
        /// (Task 8): selections == null — ПРЕЖНИЙ путь: выбор ТОЛЬКО по
        /// kind-константе И варианту A2453 (чужой вариант — тихий no-op, S2):
        /// PickScheme не нашёл — WARN (Review Focus 4/5), BP с дефолтным
        /// отображением. selections != null — профиль из BpProfileSelections.For
        /// (kind/multi/ориентация), каталог point/ не сканируется; For вернул
        /// null — лог «профиль не выбран» один раз на слот за прогон,
        /// BP с дефолтным отображением.
        /// Применение (Import + Selected) ОБЩЕЕ — ApplyEmcSchemeInfo.
        /// Import — на КАЖДЫЙ BP (идемпотентен, S2: All 22→22); C1-ревью: кеш
        /// Imports на прогон был бы верен ТОЛЬКО если склад All виден всем BP —
        /// вердикт S2 говорит «да» (склад на уровне проекта/варианта), но кеш
        /// удалён — поведение не зависит от этого допущения, цена — дешёвый
        /// идемпотентный Import.</summary>
        private static void ApplyEmcScheme(SymbolReference oRef, int nVariant,
            BreakPointPlacement oBp, bool bVertical, string strPointFolder,
            List<EmcSchemeInfo> lstEmc, HashSet<string> setImportLogged,
            HashSet<string> setMissingWarned,
            HashSet<string> setProfileMissingLogged, DiagnosticLogger log,
            BpProfileSelections selections)
        {
            string strCable = oBp.CableName ?? "<без имени>";

            // rev.17 (Task 8): выбор пользователя. Для пары (kind, multi,
            // ориентация) For даёт профиль; null — слот не заполнен, BP
            // остаётся с дефолтным отображением (штатно, решение прод-волны).
            // Каталог point/ (lstEmc) в этой ветке НЕ нужен: файл и имя схемы
            // заданы выбором пользователя напрямую.
            if (selections != null)
            {
                EmcSchemeInfo oSel = selections.For(oBp.Kind, oBp.Multi, bVertical);
                if (oSel == null)
                {
                    LogProfileMissingOnce(oBp, bVertical, strCable,
                        setProfileMissingLogged, log);
                    return;
                }
                if (string.IsNullOrEmpty(strPointFolder))
                {
                    log.Warn("[BP] папка point/ не задана — профиль '" +
                        oSel.File + "' не применён");
                    return;
                }
                ApplyEmcSchemeInfo(oRef, oSel, nVariant, strPointFolder,
                    setImportLogged, strCable, log);
                return;
            }

            // Прежний путь (selections == null): файл набора — по kind-константе
            // И варианту A2453 (чужой вариант — тихий no-op, S2).
            string strFile = GetEmcFileName(oBp, bVertical);
            if (strFile == null)
            {
                log.Warn("[BP] '" + strCable + "': решение Unreadable — набор .emc не подставляется");
                return;
            }
            if (string.IsNullOrEmpty(strPointFolder))
            {
                log.Warn("[BP] папка point/ не задана — набор '" + strFile + "' не применён");
                return;
            }
            if (lstEmc == null || lstEmc.Count == 0)
            {
                LogMissingOnce(strCable, strFile, setMissingWarned,
                    "каталог point/ пуст или не читается", log);
                return;
            }
            EmcSchemeInfo oInfo = EmcSchemeCatalog.PickScheme(lstEmc, strFile, nVariant);
            if (oInfo == null)
            {
                LogMissingOnce(strCable, strFile, setMissingWarned,
                    "файл не найден (или A2453 != варианту " +
                    nVariant.ToString(CultureInfo.InvariantCulture) + ")", log);
                return;
            }
            ApplyEmcSchemeInfo(oRef, oInfo, nVariant, strPointFolder,
                setImportLogged, strCable, log);
        }

        /// <summary>Применение конкретного набора (rev.17, Task 8): Import(path,
        /// true) + ActivateSelectedSchema. ОБЩИЙ путь для пользовательского
        /// выбора и прежнего kind-константного выбора — логика Import/Selected
        /// не дублируется. Import — на КАЖДЫЙ BP (идемпотентен, S2);
        /// [BP-EMC] — один раз на файл за прогон.</summary>
        private static void ApplyEmcSchemeInfo(SymbolReference oRef,
            EmcSchemeInfo oInfo, int nVariant, string strPointFolder,
            HashSet<string> setImportLogged, string strCable, DiagnosticLogger log)
        {
            try
            {
                // KB ~Import.html | SymbolReference.PropertyPlacementsSchemasList:
                // «Imports customer property placements set(s) from the
                // specified file»; overwrite=true; повтор — идемпотентен (S2);
                // ПОЭТОМУ Import на каждый BP — не полагаемся на то, что склад
                // All уже наполнен предыдущим Import'ом.
                bool bFirstImport = setImportLogged == null ||
                    !setImportLogged.Contains(oInfo.File);
                oRef.PropertyPlacementsSchemas.Import(
                    Path.Combine(strPointFolder, oInfo.File), true);
                if (bFirstImport && setImportLogged != null)
                {
                    setImportLogged.Add(oInfo.File);
                    log.Log("[INFO] [BP-EMC] '" + oInfo.File +
                        "' импортирован (вариант " +
                        nVariant.ToString(CultureInfo.InvariantCulture) + ")");
                }
                ActivateSelectedSchema(oRef, oInfo.SchemeName, strCable, log);
            }
            catch (Exception oEx)
            {
                log.Warn("[BP-ERR] Import('" + oInfo.File + "') бросил " +
                    oEx.GetType().Name + ": " + oEx.Message +
                    " — набор не применён (BP с дефолтным отображением)");
            }
        }

        // WARN один раз НА ИМЯ ФАЙЛА (Review Focus 4) — повторные BP того же
        // файла не спамят.
        private static void LogMissingOnce(string strCable, string strFile,
            HashSet<string> setWarned, string strCause, DiagnosticLogger log)
        {
            if (setWarned == null || !setWarned.Add(strFile)) return;
            log.Warn("[BP] '" + strFile + "' (кабель '" + strCable + "'): " +
                strCause + " — набор не применён (BP с дефолтным отображением)");
        }

        // rev.17 (fix review): «профиль не выбран» — INFO один раз на ключ
        // (kind, multi, ориентация) за прогон (было: на каждый BP слота) —
        // тот же стиль дедупа, что LogMissingOnce.
        private static void LogProfileMissingOnce(BreakPointPlacement oBp,
            bool bVertical, string strCable, HashSet<string> setLogged,
            DiagnosticLogger log)
        {
            if (setLogged == null) return;
            string strKey = oBp.Kind.ToString() + "|" +
                (oBp.Multi ? "M" : "S") + "|" + (bVertical ? "V" : "H");
            if (!setLogged.Add(strKey)) return;
            log.Log("[INFO] [BP] '" + strCable +
                "': профиль не выбран — BP с дефолтным отображением");
        }

        /// <summary>Активация набора: Selected {get;set} : PropertyPlacementsSchema
        /// (KB, вердикт S1: Import кладёт схему в All, НЕ выбирает). Поиск по
        /// имени схемы A2454 (контролл Schema.Name; exact-сравнение Ordinal);
        /// не найдено в коллекции (тихий no-op Import чужого варианта и т.п.)
        /// — WARN, объект остаётся с дефолтным набором.</summary>
        private static void ActivateSelectedSchema(SymbolReference oRef,
            string strSchemeName, string strCable, DiagnosticLogger log)
        {
            try
            {
                object oList = oRef.PropertyPlacementsSchemas;
                PropertyInfo oPiAll = oList.GetType().GetProperty("All");
                PropertyInfo oPiSel = oList.GetType().GetProperty("Selected");
                System.Collections.IEnumerable oEnumAll = oPiAll == null ? null :
                    oPiAll.GetValue(oList, null) as System.Collections.IEnumerable;
                if (oEnumAll == null || oPiSel == null)
                {
                    log.Warn("[BP] '" + strCable + "': коллекция схем недоступна (All/Selected) — набор не активирован");
                    return;
                }
                object oChosen = null;
                foreach (object oScheme in oEnumAll)
                {
                    if (oScheme == null) continue;
                    string strFound = TryGetStringMember(oScheme, "Name");
                    if (string.Equals(strFound, strSchemeName, StringComparison.Ordinal))
                    {
                        oChosen = oScheme;
                        break;
                    }
                }
                if (oChosen == null)
                {
                    log.Warn("[BP] '" + strCable + "': схема '" + strSchemeName +
                        "' не найдена в All после Import — набор не активирован");
                    return;
                }
                oPiSel.SetValue(oList, oChosen, null);
                log.Log("[INFO] [BP-SCHEME] '" + strSchemeName + "' применён к BP кабеля '" +
                    strCable + "'");
            }
            catch (Exception oEx)
            {
                log.Warn("[BP] '" + strCable + "': активация '" + strSchemeName +
                    "' бросила " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        private static string TryGetStringMember(object oObj, string strMember)
        {
            try
            {
                PropertyInfo oProp = oObj.GetType().GetProperty(strMember,
                    BindingFlags.Public | BindingFlags.Instance);
                if (oProp != null)
                {
                    object oVal = oProp.GetValue(oObj, null);
                    if (oVal != null) return oVal.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>ОУ на InterruptionPoint — путь B (вердикт S4-v11; Sepla
        /// InterruptionPointUtility.RenameAll): LockObject + NameParts =
        /// offline-список + AdjustVisibleName(Page, FunctionBase) (KB,
        /// перегрузка есть; rev.12.9-паттерн). ОУ: multi (шинный BP) — ПОЛНОЕ
        /// ОУ САМОГО КАБЕЛЯ (CableName) без суффикса — решение пользователя
        /// 01.10 (стенд); прямой — ComposeBpDeviceTag (структура обратного
        /// конца + код кабеля + '(EXT)' / устройство как есть). Структура
        /// пишется ЯВНО (правило 4, стенд 01.10: страница отчёта структуру
        /// НЕ наследует). Объект — SymbolReference от Create (S1); каст в
        /// InterruptionPoint (S4: OK); не кастуется — WARN, ОУ не записано.</summary>
        private static void WriteBpDeviceTag(SymbolReference oRef,
            BreakPointPlacement oBp, Page oPage, DiagnosticLogger log)
        {
            string strCable = oBp.CableName ?? "<без имени>";
            // ОУ: multi (шинный BP) — полное ОУ САМОГО кабеля без (EXT)
            // (решение 01.10: '=HII-1.1++М+#2-K140'); прямой — правило BP.
            string strDtBp = oBp.Multi
                ? oBp.CableName
                : BreakPointResolver.ComposeBpDeviceTag(oBp.OppositeDt, oBp.CableName);
            if (string.IsNullOrEmpty(strDtBp))
            {
                log.Warn("[BP] '" + strCable + "': ОУ не составлено (OppositeDt / имя кабеля пусты) — BP с пустым ОУ");
                return;
            }
            // BreakPointResolver: имя = хвост после последнего '-'; полный
            // offline-список частей — CableSymbolCreator.BuildNamePartList
            // (структура 1100/1400/1200/1600 + код/счётчик 20013/20014);
            // правка стенда 01.10: только имена дали «K190(EXT)» без структуры
            // — страница отчёта структуру НЕ наследует, пишем ЯВНО.
            try
            {
                // C1/M1-ревью: каст ПЕРЕД сборкой частей (не-IP — части не нужны).
                InterruptionPoint oIp = oRef as InterruptionPoint;
                if (oIp == null)
                {
                    log.Warn("[BP] '" + strCable + "': oRef не InterruptionPoint (" +
                        oRef.GetType().Name + ") — ОУ не записано");
                    return;
                }
                FunctionBasePropertyList oParts =
                    CableSymbolCreator.BuildNamePartList(strDtBp, log);
                oIp.LockObject();
                oIp.NameParts = oParts;
                NameService oNamesSvc = new NameService();
                oNamesSvc.Page = oPage;
                if (oNamesSvc.AdjustVisibleName(oPage, oIp))
                    log.Log("[INFO] [BP] ОУ записан: '" + strDtBp + "' (путь B, AdjustVisibleName ok)");
                else
                    log.Warn("[BP] '" + strCable + "': AdjustVisibleName вернул false — полное ОУ записано, видимое могло не вычислиться");
            }
            catch (Exception oEx)
            {
                log.Warn("[BP-ERR] '" + strCable + "': запись ОУ '" + strDtBp +
                    "' бросила " + oEx.GetType().Name + ": " + oEx.Message +
                    " — BP вставлен с дефолтным ОУ");
            }
        }

    }
}
