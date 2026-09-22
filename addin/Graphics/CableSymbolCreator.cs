using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Вставка символов кабеля (Фаза G, spec
    /// docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md; мастер-план §13 —
    /// Этап 9). KB 2.9 (проверено 22.09.2026): SymbolLibrary(Project, String) →
    /// Symbol = oLibrary[имя] → SymbolVariant = oSymbol[индекс]; SymbolReference.Create
    /// — ИНСТАНСНЫЙ (public virtual void Create(Page, SymbolVariant)); позиция —
    /// Placement.Location (get/set). Библиотека/имя/вариант — из AddInConfiguration
    /// (в Фазе H — выбор пользователя в UI). DT-свойства символа (rev.10.1, spec §9.5):
    /// полный DT кабеля разбирается на части и пишется в 1120/1220/1620/20000 —
    /// каждый отказ WARN [SYMDT] и не прерывает. Поворот 0° (spec §7). Центр круга
    /// = точке вставки (rev.10.4: офсет п.47 опровергнут — замер был загрязнён
    /// останцами старых прогонов). Отказ —
    /// WARN [SYMBOL]. НЕ идемпотентно (очистка — Фаза I).</summary>
    public static class CableSymbolCreator
    {
        /// <summary>Символ на каждый CableSymbolPlacement. Возвращает число созданных.
        /// Библиотека/символ/вариант недоступны — один WARN, 0 созданных.</summary>
        public static int CreateSymbols(Page oPage, CableGeometryResult oGeom,
            DiagnosticLogger log)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[SYMBOL] CreateSymbols: page или geometry == null — символов не создаём");
                return 0;
            }
            int nTotal = oGeom.Symbols.Count;
            if (nTotal == 0) return 0;

            SymbolVariant oVariant;
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, AddInConfiguration.SymbolLibrary);
                Symbol oSymbol = oLibrary[AddInConfiguration.SymbolName];
                oVariant = oSymbol[AddInConfiguration.SymbolVariant];
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMBOL] вариант '" + AddInConfiguration.SymbolLibrary + "'/" +
                    AddInConfiguration.SymbolName + "/" + AddInConfiguration.SymbolVariant +
                    " недоступен: " + oEx.GetType().Name + ": " + oEx.Message);
                log.Log("[INFO] [SYMBOL-SUM] символов 0 из " + nTotal + ".");
                return 0;
            }

            int nCreated = 0;
            foreach (CableSymbolPlacement oSym in oGeom.Symbols)
            {
                try
                {
                    SymbolReference oRef = new SymbolReference();
                    oRef.Create(oPage, oVariant);
                    // rev.10.4: офсет центра CABDCP2 из п.47 ОПРОВЕРГНУТ прогоном rev.10.3
                    // (круг ушёл 1:1 с точкой вставки): центр круга = точке вставки.
                    // Пишем геометрическую позицию напрямую (замер п.47 был загрязнён
                    // останцами старых прогонов — аддин неидемпотентен).
                    oRef.Location = new PointD(oSym.Position.X, oSym.Position.Y);
                    nCreated++;
                    log.Log("[INFO] [SYMBOL] '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + " @ (" +
                        oSym.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oSym.Position.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");

                    // rev.10.1 (spec §9.5): DT-свойства символа из полного DT кабеля.
                    // Отказы — WARN [SYMDT] внутри, счётчик и остальные символы не страдают.
                    if (!string.IsNullOrEmpty(oSym.CableName))
                        WriteDeviceTagProperties(oRef, oSym, log);
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMBOL] Create бросил " + oEx.GetType().Name + ": " + oEx.Message +
                        " (символ '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            log.Log("[INFO] [SYMBOL-SUM] символов " + nCreated + " из " + nTotal + ".");
            return nCreated;
        }

        /// <summary>Запись DT-свойств символа (rev.10.1, spec §9.5): разбор полного DT
        /// из CableName и запись 1120 ← установка, 1220 ← место сборки, 1620 ←
        /// определяющая структура, 20000 ← имя. Место установки НЕ пишется никогда
        /// (spec §9.5). Дамп [SYMDT] — что записано; отсутствующая часть — «—».</summary>
        private static void WriteDeviceTagProperties(SymbolReference oRef,
            CableSymbolPlacement oSym, DiagnosticLogger log)
        {
            string[] arrParts = ParseDeviceTag(oSym.CableName);
            string strInstallation = arrParts[0];
            string strMountingSite = arrParts[1];
            string strUserStruct = arrParts[2];
            string strName = arrParts[3];

            WriteSymProperty(oRef, log, 1120, strInstallation);
            WriteSymProperty(oRef, log, 1220, strMountingSite);
            WriteSymProperty(oRef, log, 1620, strUserStruct);
            WriteSymProperty(oRef, log, 20000, strName);

            log.Log("[INFO] [SYMDT] '" + oSym.CableName + "': =" +
                (strInstallation ?? "—") + " ++" + (strMountingSite ?? "—") + " #" +
                (strUserStruct ?? "—") + " имя=" + (strName ?? "—"));
        }

        /// <summary>Одна запись свойства символа. Пустая часть — тихий пропуск.
        /// Отказ (id не создан reflection-ом, свойство неприменимо) — WARN [SYMDT]
        /// "(id) ('часть'): Тип: сообщение", остальные части пишутся дальше.</summary>
        private static void WriteSymProperty(SymbolReference oRef, DiagnosticLogger log,
            int nPropertyId, string strPart)
        {
            if (string.IsNullOrEmpty(strPart)) return;
            try
            {
                AnyPropertyId oId = CreateAnyPropertyIdFromNumber(nPropertyId);
                if (oId == null)
                    throw new InvalidOperationException("CreateAnyPropertyIdFromNumber вернул null");
                oRef.Properties[oId].Set(strPart);
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMDT] свойство " + nPropertyId.ToString(CultureInfo.InvariantCulture) +
                    " ('" + strPart + "'): " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Разбор полного DT кабеля на структурные части (spec §9.5).
        /// Формат: =<установка>++<место сборки>+<место установки>#<опред. структура>-<имя>.
        /// Возвращает 4 строки: installation / mountingSite / userStruct / name;
        /// null = блок отсутствует (пустой блок приравнен к отсутствующему).
        /// Место установки (между '+' и '#'/'-') не разбирается — никогда не пишется.
        /// Любая аномалия (нет маркеров) — возвращается что разобрано, остальное null.</summary>
        private static string[] ParseDeviceTag(string strFullName)
        {
            string strInstallation = null;
            string strMountingSite = null;
            string strUserStruct = null;
            string strName = null;

            if (string.IsNullOrEmpty(strFullName))
                return new string[] { null, null, null, null };

            string s = strFullName.StartsWith("=", StringComparison.Ordinal)
                ? strFullName.Substring(1) : strFullName;

            // Установка — до "++". Без "++" установка/места/структура отсутствуют:
            // имя — хвост после последнего '-'.
            int nInstallEnd = s.IndexOf("++", StringComparison.Ordinal);
            if (nInstallEnd < 0)
            {
                int nDash = s.LastIndexOf('-');
                if (nDash >= 0 && nDash < s.Length - 1)
                    strName = NullIfEmpty(s.Substring(nDash + 1));
                return new string[] { null, null, null, strName };
            }
            strInstallation = NullIfEmpty(s.Substring(0, nInstallEnd));

            // Место сборки — после "++" до следующего '+'. Без '+' весь хвост — место сборки.
            string strTail = s.Substring(nInstallEnd + 2);
            int nMountEnd = strTail.IndexOf('+');
            if (nMountEnd < 0)
                return new string[] { strInstallation, NullIfEmpty(strTail), null, null };
            strMountingSite = NullIfEmpty(strTail.Substring(0, nMountEnd));

            // Место установки — после '+' до '#' или '-' — игнорируется (не пишем, spec §9.5).
            string strTail2 = strTail.Substring(nMountEnd + 1);
            int nHash = strTail2.IndexOf('#');
            if (nHash < 0)
            {
                // Без '#': имя — хвост после '-' (место установки до него).
                int nDash = strTail2.IndexOf('-');
                if (nDash >= 0 && nDash < strTail2.Length - 1)
                    strName = NullIfEmpty(strTail2.Substring(nDash + 1));
                return new string[] { strInstallation, strMountingSite, null, strName };
            }
            string strTail3 = strTail2.Substring(nHash + 1);

            // Определяющая структура — после '#' до '-'; имя — после этого '-'.
            int nStructEnd = strTail3.IndexOf('-');
            if (nStructEnd < 0)
                return new string[] { strInstallation, strMountingSite, NullIfEmpty(strTail3), null };
            strUserStruct = NullIfEmpty(strTail3.Substring(0, nStructEnd));
            if (nStructEnd < strTail3.Length - 1)
                strName = NullIfEmpty(strTail3.Substring(nStructEnd + 1));

            return new string[] { strInstallation, strMountingSite, strUserStruct, strName };
        }

        /// <summary>Пустой блок == отсутствующий: пустая строка нормализуется в null.</summary>
        private static string NullIfEmpty(string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return null;
            return strValue;
        }

        /// <summary>Создаёт AnyPropertyId из номера свойства. По документации API 2.9
        /// конвертация есть ИЗ номера (op_Implicit Int32 -> AnyPropertyId). Оператор
        /// вызывается через reflection (порт из spike rev.7), чтобы сборка не зависела
        /// от точной сигнатуры оператора. Не удалось — null (логирует вызывающий).</summary>
        private static AnyPropertyId CreateAnyPropertyIdFromNumber(int nNumber)
        {
            try
            {
                MethodInfo[] arrMethods =
                    typeof(AnyPropertyId).GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo oMethod in arrMethods)
                {
                    if ((oMethod.Name != "op_Implicit" && oMethod.Name != "op_Explicit") ||
                        oMethod.ReturnType != typeof(AnyPropertyId))
                        continue;
                    ParameterInfo[] arrParams = oMethod.GetParameters();
                    if (arrParams.Length != 1 || arrParams[0].ParameterType != typeof(int)) continue;

                    object oResult = oMethod.Invoke(null, new object[] { nNumber });
                    if (oResult is AnyPropertyId) return (AnyPropertyId)oResult;
                }
            }
            catch { }
            return null;
        }
    }
}
