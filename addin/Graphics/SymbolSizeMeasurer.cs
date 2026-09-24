using System;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Замер размеров РАБОЧЕГО символа кабеля (rev.10.7, Фаза G шаг 4;
    /// решение пользователя 23.09.2026): пробная вставка SymbolReference у точки
    /// [SYMSIZE] → GetBoundingBox (KB 2.9, проверено 22.09.2026: ровно 2 точки —
    /// [0] левый-нижний, [1] правый-верхний) → W×H мм. Размер идёт в конфиг
    /// геометрии (зазор = (габарит по оси выноса)/2: H — A/2, V — B/2, rev.10.11;
    /// расчётный шаг уровней шин = max(min, B·min/14) — правило 2, rev.10.8;
    /// rev.10.10 — по уровням шин стоят и ряды символов).
    /// Замеряется ТОЛЬКО рабочий символ SPECIAL/CABDCP2/0 — GOST-проба [SYMBOX]
    /// (кандидат Фазы H) не замеряется. Резолв варианта и Remove — паттерн
    /// CableSymbolCreator/SymbolBoxProbe: удаление по ВАЛИДНОСТИ объекта в finally
    /// (урок п.48: аддин неидемпотентен — останцы искажают будущие замеры). Любой
    /// сбой — WARN [SYMSIZE], out = фолбэк SymbolFallbackSizeMm×SymbolFallbackSizeMm
    /// (14×14, CABDCP2), false.</summary>
    public static class SymbolSizeMeasurer
    {
        /// <summary>Замер W×H рабочего символа пробной вставкой. true — размеры
        /// реальные; false — отказ (WARN), out-параметры = фолбэк 14×14.
        /// Вырожденный бокс (0/NaN/Inf/инверсия) = отказ замера.</summary>
        public static bool TryMeasure(Page oPage, DiagnosticLogger log,
            out double dWidthMm, out double dHeightMm)
        {
            dWidthMm = AddInConfiguration.SymbolFallbackSizeMm;
            dHeightMm = AddInConfiguration.SymbolFallbackSizeMm;
            if (oPage == null)
            {
                log.Warn("[SYMSIZE] TryMeasure: page == null — фолбэк 14×14");
                return false;
            }

            string strId = "'" + AddInConfiguration.SymbolLibrary + "/" +
                AddInConfiguration.SymbolName + "/" + AddInConfiguration.SymbolVariant + "'";

            // Резолв варианта — паттерн CableSymbolCreator/SymbolBoxProbe:
            // SymbolLibrary(проект, имя библиотеки) → oLibrary[имя] → oSymbol[индекс].
            SymbolVariant oVariant;
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, AddInConfiguration.SymbolLibrary);
                Symbol oSymbol = oLibrary[AddInConfiguration.SymbolName];
                oVariant = oSymbol[AddInConfiguration.SymbolVariant];
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMSIZE] вариант " + strId + " недоступен: " +
                    oEx.GetType().Name + ": " + oEx.Message + " — фолбэк 14×14");
                return false;
            }

            SymbolReference oRef = null;
            try
            {
                oRef = new SymbolReference();
                oRef.Create(oPage, oVariant);
                oRef.Location = new PointD(AddInConfiguration.SymbolProbeX, AddInConfiguration.SymbolProbeY);

                // KB: GetBoundingBox размещённого символа — ровно 2 точки
                // ([0] левый-нижний, [1] правый-верхний).
                PointD[] arrBox = oRef.GetBoundingBox();
                if (arrBox == null || arrBox.Length < 2)
                    throw new InvalidOperationException("GetBoundingBox вернул " +
                        (arrBox == null ? "null" : arrBox.Length.ToString(CultureInfo.InvariantCulture) + " точек") +
                        " — ожидалось 2 (KB)");
                double dW = arrBox[1].X - arrBox[0].X;
                double dH = arrBox[1].Y - arrBox[0].Y;
                if (!(dW > 0) || !(dH > 0) || double.IsNaN(dW) || double.IsNaN(dH) ||
                    double.IsInfinity(dW) || double.IsInfinity(dH))
                    throw new InvalidOperationException("GetBoundingBox вернул вырожденный размер " +
                        Fmt(dW) + "×" + Fmt(dH) + " — ожидалась конечная положительная область");
                dWidthMm = dW;
                dHeightMm = dH;
                log.Log("[INFO] [SYMSIZE] " + strId + ": " +
                    Fmt(dW) + "×" + Fmt(dH) + " мм (замер пробной вставки)");
                return true;
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMSIZE] замер не удался: " + oEx.GetType().Name + ": " +
                    oEx.Message + " — фолбэк 14×14");
                return false;
            }
            finally
            {
                // Урок п.48 (паттерн SymbolBoxProbe): удаляем по ВАЛИДНОСТИ объекта,
                // а не по флагу «Create прошёл» — если движок добавил размещение и
                // бросил уже после, флаг бы пропустил Remove и остался бы останец.
                try
                {
                    if (oRef != null && oRef.IsValid)
                    {
                        oRef.Remove();
                        log.Log("[INFO] [SYMSIZE] пробный символ удалён");
                    }
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMSIZE] Remove бросил " + oEx.GetType().Name + ": " +
                        oEx.Message + " — возможен останец на странице (урок п.48)");
                }
            }
        }

        /// <summary>Формат чисел замера — «F3», инвариантная культура (как в
        /// CableSymbolCreator/SymbolBoxProbe).</summary>
        private static string Fmt(double dValue)
        {
            return dValue.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
