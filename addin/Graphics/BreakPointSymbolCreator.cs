using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Вставка символа точки разрыва BP «8 / BP» (rev.16.2, решения
    /// пользователя 30.09.2026): библиотека SPECIAL, константы AddInConfiguration
    /// (BreakPointSymbol*: straight A(0)/H(7), multi G(6)/F(5)). На каждый
    /// BreakPointPlacement из геометрии: Function.Create по SymbolVariant
    /// (паттерн SymbolSizeMeasurer: библиотека[имя][вариант]) → Location =
    /// Position (компенсация — по вердикту spike S3, сейчас БЕЗ offset) →
    /// применение набора отображения свойств из .emc
    /// (SymbolReference.PropertyPlacementsSchemasList.Import(path, true), KB
    /// 2.9; набор в проект НЕ импортируется — файл рядом со сборкой аддина,
    /// папка point/) → ОУ через WriteDtPropertiesCore (NameParts-паттерн
    /// rev.11.13/11.15; строка — BreakPointResolver.ComposeBpDeviceTag:
    /// клеммник → структура обратного конца + код кабеля + '(EXT)', устройство
    /// → ОУ устройства как есть). НЕ идемпотентно (как [SYMBOL], Фаза I
    /// отменена). Отказы — WARN [BP-ERR], отчёт не прерывается; итог [BP-SUM].
    /// Кросс-ссылка — зашита в .emc (решение пользователя), отдельно не пишем.</summary>
    public static class BreakPointSymbolCreator
    {
        /// <summary>Папка point/ рядом со сборкой аддина (вариант (а), решение
        /// пользователя; сборка в shadow-copy EPLAN — путь фактический; отказ —
        /// assembly пуст — null → WARN у потребителя).</summary>
        public static string ResolvePointFolder()
        {
            try
            {
                string strDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(strDir)) return null;
                return Path.Combine(strDir, AddInConfiguration.PointSchemeFolder);
            }
            catch { return null; }
        }

        /// <summary>Вставка BP по BreakPointPlacement. Возвращает число созданных.
        /// Вариант: Multi → MultiH/MultiV, иначе StraightH/StraightV — по
        /// bVertical. Файл набора: multi → EmcMulti*, иначе по Kind
        /// (TerminalStrip → EmcStraightStrip*, Device → EmcStraightDevice*) и
        /// ориентации. bVertical — ориентация ОТЧЁТА (как eOrientation).</summary>
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

                    SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project,
                        AddInConfiguration.BreakPointSymbolLibrary);
                    Symbol oSymbol = oLibrary[AddInConfiguration.BreakPointSymbolName];
                    SymbolVariant oVariant = oSymbol[nVariant];

                    Function oFuncBp = new Function();
                    oFuncBp.Create(oPage, oVariant);
                    // rev.16.2 (ruling): Location = Position БЕЗ компенсации;
                    // факт (dx,dy) вариантов BP — вердикт spike S3. Position
                    // всегда задан геометрией (struct — null-стража не нужно).
                    oFuncBp.Location = new PointD(oBpPlacement.Position.X,
                        oBpPlacement.Position.Y);

                    // Набор отображения свойств (.emc) — Import от файла.
                    ApplyEmcFile(oFuncBp, oBpPlacement, bVertical, strPointFolder, log);

                    // ОУ точки разрыва: клеммник — структура обратного конца +
                    // код кабеля + "(EXT)"; устройство — как есть. Запись — общий
                    // контур CableSymbolCreator ([SYMDT], NameParts rev.11.13/11.15).
                    string strDtBp = BreakPointResolver.ComposeBpDeviceTag(
                        oBpPlacement.OppositeDt, oBpPlacement.CableName);
                    if (!string.IsNullOrEmpty(strDtBp))
                        CableSymbolCreator.WriteDtPropertiesCore(oPage, oFuncBp, strDtBp, log);

                    nCreated++;
                    log.Log("[INFO] [BP] '" + strName + "' #" +
                        oBpPlacement.CableIndex.ToString(CultureInfo.InvariantCulture) + " @ (" +
                        oBpPlacement.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oBpPlacement.Position.Y.ToString("F3", CultureInfo.InvariantCulture) +
                        ") variant=" + nVariant.ToString(CultureInfo.InvariantCulture) +
                        (oBpPlacement.Multi ? " multi" : "") +
                        " ОУ='" + strDtBp + "'");
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

        /// <summary>Имя файла .emc по решению (Константы Task 1): multi — int_*;
        /// иначе TerminalStrip → ТР_Кабель*, Device → ТР_Устройство*, пара
        /// (гориз/вертик) — по bVertical. Kind unreadable / файл не найден /
        /// Import отказал — WARN, символ остаётся с дефолтным отображением
        /// (Review Focus п.4/5: отчёт не прерывается, BP остаётся).</summary>
        private static void ApplyEmcFile(Function oFuncBp, BreakPointPlacement oBp,
            bool bVertical, string strPointFolder, DiagnosticLogger log)
        {
            string strFile;
            if (oBp.Multi)
                strFile = bVertical ? AddInConfiguration.EmcMultiStripV
                                    : AddInConfiguration.EmcMultiStripH;
            else if (oBp.Kind == BpEndKind.Device)
                strFile = bVertical ? AddInConfiguration.EmcStraightDeviceV
                                    : AddInConfiguration.EmcStraightDeviceH;
            else if (oBp.Kind == BpEndKind.TerminalStrip)
                strFile = bVertical ? AddInConfiguration.EmcStraightStripV
                                    : AddInConfiguration.EmcStraightStripH;
            else
            {
                log.Warn("[BP] '" + (oBp.CableName ?? "<без имени>") +
                    "': решение Unreadable — набор .emc не подставляется");
                return;
            }
            if (string.IsNullOrEmpty(strPointFolder))
            {
                log.Warn("[BP] папка point/ не задана — набор '" + strFile + "' не применён");
                return;
            }
            string strPath = Path.Combine(strPointFolder, strFile);
            if (!File.Exists(strPath))
            {
                log.Warn("[BP] файл набора не найден: '" + strPath +
                    "' — набор '" + strFile + "' не применён (BP с дефолтным отображением)");
                return;
            }
            try
            {
                // KB www.eplan.help | SymbolReference.PropertyPlacementsSchemasList
                // ~Import.html: «Imports customer property placements set(s) from
                // the specified file to the symbol»; оverwrite=true.
                oFuncBp.PropertyPlacementsSchemas.Import(strPath, true);
                log.Log("[INFO] [BP-SCHEME] '" + strFile + "' применён к BP кабеля '" +
                    (oBp.CableName ?? "<без имени>") + "'");
            }
            catch (Exception oEx)
            {
                // spike S1 ещё не верифицирован: Import может отвергнуть чужой
                // SourceProject в Pxf — WARN [BP-ERR], BP остаётся.
                log.Warn("[BP-ERR] Import('" + strFile + "') бросил " +
                    oEx.GetType().Name + ": " + oEx.Message +
                    " — набор не применён (BP с дефолтным отображением)");
            }
        }
    }
}
