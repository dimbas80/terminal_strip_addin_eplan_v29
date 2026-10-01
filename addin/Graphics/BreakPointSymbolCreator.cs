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
    /// <summary>Вставка символа точки разрыва BP «8 / BP» (rev.16.2 прод-волна,
    /// решения пользователя 30.09/01.10.2026, вердикты spike S1–S4): библиотека
    /// SPECIAL, константы AddInConfiguration (straight A(0)/H(7), multi
    /// G(6)/F(5)). На каждый BreakPointPlacement из геометрии:
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
    /// конца + код кабеля + '(EXT)', устройство → как есть). НЕ идемпотентно
    /// (как [SYMBOL]); отказы — WARN [BP-ERR], отчёт не прерывается; итог
    /// [BP-SUM]. Кросс-ссылка зашита в .emc, отдельно не пишем.</summary>
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
            return null;
        }

        /// <summary>Вставка BP по BreakPointPlacement. Возвращает число созданных.
        /// Вариант: Multi → MultiH/MultiV, иначе StraightH/StraightV — по
        /// bVertical. Файл набора: multi → EmcMulti*, иначе по Kind
        /// (TerminalStrip → EmcStraightStrip*, Device → EmcStraightDevice*) и
        /// ориентации; вариант проверяется по A2453 (чужой вариант = тихий
        /// no-op Import — не импортируем). Каталог point/ строится при
        /// ПЕРВОЙ вставке (решение 01.10: скан на старте команды — здесь
        /// эквивалентно, скан дешевле страниц). bVertical — ориентация ОТЧЁТА
        /// (как eOrientation).</summary>
        public static int CreateBreakPoints(Page oPage, CableGeometryResult oGeom,
            bool bVertical, string strPointFolder, DiagnosticLogger log)
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

            // rev.16.2 (01.10): скан point/ НА СТАРТЕ (до первой вставки) —
            // A2453/A2454 всех *.emc. Пусто/нечитаемо — WARN, вставки БЕЗ набора.
            List<EmcSchemeInfo> lstEmc = null;
            if (!string.IsNullOrEmpty(strPointFolder))
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
                        strPointFolder, lstEmc, setImportLogged, setMissingWarned, log);

                    WriteBpDeviceTag(oRef, oBpPlacement, oPage, log);

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
            return BreakPointResolver.ComposeBpDeviceTag(oBp.OppositeDt, oBp.CableName) ?? "<нечитаемо>";
        }

        /// <summary>Набор отображения (.emc) — Import + Selected. Выбор ТОЛЬКО
        /// по kind-константе И варианту A2453 (чужой вариант — тихий no-op,
        /// S2): PickScheme не нашёл — WARN (Review Focus 4/5), BP с дефолтным
        /// отображением. Import — на КАЖДЫЙ BP (идемпотентен, S2: All 22→22);
        /// C1-ревью: кеш Imports на прогон был бы верен ТОЛЬКО если склад
        /// All виден всем BP — вердикт S2 говорит «да» (склад на уровне
        /// проекта/варианта), но кеш удалён — поведение не зависит от этого
        /// допущения, цена — дешёвый идемпотентный Import.</summary>
        private static void ApplyEmcScheme(SymbolReference oRef, int nVariant,
            BreakPointPlacement oBp, bool bVertical, string strPointFolder,
            List<EmcSchemeInfo> lstEmc, HashSet<string> setImportLogged,
            HashSet<string> setMissingWarned, DiagnosticLogger log)
        {
            string strCable = oBp.CableName ?? "<без имени>";
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
        /// offline-список (FUNC_CODE #20013 = код, FUNC_COUNTER #20014 =
        /// счётчик имени) + AdjustVisibleName(Page, FunctionBase) (KB,
        /// перегрузка есть; rev.12.9-паттерн). Структура НЕ пишется —
        /// платформа наследует от страницы отчёта (S4 readback
        /// '=+++#-K190(EXT)'). Полное ОУ — ComposeBpDeviceTag (null — WARN,
        /// вставлен с пустым ОУ). Объект — SymbolReference от Create (S1);
        /// каст в InterruptionPoint (S4: OK); не кастуется (другой производный
        /// тип) — WARN, ОУ не записано.</summary>
        private static void WriteBpDeviceTag(SymbolReference oRef,
            BreakPointPlacement oBp, Page oPage, DiagnosticLogger log)
        {
            string strCable = oBp.CableName ?? "<без имени>";
            string strDtBp = BreakPointResolver.ComposeBpDeviceTag(
                oBp.OppositeDt, oBp.CableName);
            if (string.IsNullOrEmpty(strDtBp))
            {
                log.Warn("[BP] '" + strCable + "': ОУ не составлено (OppositeDt / имя кабеля пусты) — BP с пустым ОУ");
                return;
            }
            // BreakPointResolver: имя = хвост после последнего '-'; код —
            // ведущие нецифровые, счётчик — хвост (хелперы там же, теперь
            // public — путь B потребляет их напрямую).
            string strCode, strCounter;
            BreakPointResolver.SplitLetterCounter(
                BreakPointResolver.DeviceNameOf(strDtBp), out strCode, out strCounter);
            try
            {
                // C1/M1-ревью: каст ПЕРЕД построением parts (не-IP — части не нужны).
                InterruptionPoint oIp = oRef as InterruptionPoint;
                if (oIp == null)
                {
                    log.Warn("[BP] '" + strCable + "': oRef не InterruptionPoint (" +
                        oRef.GetType().Name + ") — ОУ не записано");
                    return;
                }
                FunctionBasePropertyList oParts = new FunctionBasePropertyList();
                SetNamePart(oParts, 20013, strCode, log, strCable);
                SetNamePart(oParts, 20014, strCounter, log, strCable);
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

        /// <summary>oParts[nId] = значение (паттерн CableSymbolCreator.SetNamePart;
        /// коротко: только WARN, без fallback — путь B сам отберёт пустые
        /// части).</summary>
        private static void SetNamePart(FunctionBasePropertyList oParts, int nProp,
            string strValue, DiagnosticLogger log, string strCable)
        {
            if (string.IsNullOrEmpty(strValue)) return;
            try
            {
                AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(nProp);
                if (oId == null)
                    throw new InvalidOperationException(
                        "CreateAnyPropertyIdFromNumber(" +
                        nProp.ToString(CultureInfo.InvariantCulture) + ") вернул null");
                oParts[oId] = (PropertyValue)strValue;
            }
            catch (Exception oEx)
            {
                log.Warn("[BP-ERR] '" + strCable + "': часть " +
                    nProp.ToString(CultureInfo.InvariantCulture) + " ('" + strValue +
                    "'): " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }
    }
}
