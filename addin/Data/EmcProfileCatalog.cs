using System;
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Слот профиля набора отображения (rev.17, план
    /// 2026-10-01-ui-emc-profiles Task 2): 6 комбинаций потребителя —
    /// клеммник/устройство × горизонталь/вертикаль отчёта и мульти-кабель
    /// (кабель между двумя клеммниками) × горизонталь/вертикаль. Слот
    /// определяет вариант символа BP: A(0)/H(7)/G(6)/F(5) —
    /// EmcProfileCatalog.VariantFor.</summary>
    public enum BpProfileSlot
    {
        StripH,
        StripV,
        DeviceH,
        DeviceV,
        LinkH,
        LinkV
    }

    /// <summary>Каталог выбора профиля .emc по варианту (rev.17, Task 2).
    /// ЧИСТЫЙ модуль — без EPLAN-типов (паттерн BreakPointResolver/
    /// EmcSchemeCatalog; из конфигурации тянется только AddInConfiguration —
    /// она тоже без EPLAN). UI (Task 5) отбирает профили варианта
    /// (ForVariant) и кодирует выбор пользователя в строку настройки
    /// (EncodeSelection); прод-вставка BP (Task 8) декодирует строку
    /// (TryDecodeSelection) и раскладывает профили по слотам
    /// (BpProfileSelections).</summary>
    public static class EmcProfileCatalog
    {
        /// <summary>Профили заданного варианта A2453 в порядке входа кандидатов
        /// (линейный отбор; список короткий — отдельного индекса не строим,
        /// паттерн PickScheme). null-каталог → ПУСТОЙ список, не null: UI не
        /// обязан проверять на null. Пустой результат — варианта нет
        /// (у потребителя: WARN, BP без набора).</summary>
        public static List<EmcSchemeInfo> ForVariant(List<EmcSchemeInfo> lstAll,
            int nVariant)
        {
            List<EmcSchemeInfo> lst = new List<EmcSchemeInfo>();
            if (lstAll == null) return lst;
            foreach (EmcSchemeInfo oInfo in lstAll)
            {
                if (oInfo == null || oInfo.Variant != nVariant) continue;
                lst.Add(oInfo);
            }
            return lst;
        }

        /// <summary>Вариант символа BP для слота: StripH/DeviceH → A(0),
        /// StripV/DeviceV → H(7), LinkH → G(6), LinkV → F(5). Значения — из
        /// констант AddInConfiguration (числа не хардкодим). Неизвестный слот
        /// (приведение произвольного int) → -1 — сентинел «нет варианта»,
        /// как EmcSchemeInfo.Variant.</summary>
        public static int VariantFor(BpProfileSlot eSlot)
        {
            switch (eSlot)
            {
                case BpProfileSlot.StripH:
                case BpProfileSlot.DeviceH:
                    return AddInConfiguration.BreakPointVariantStraightH;
                case BpProfileSlot.StripV:
                case BpProfileSlot.DeviceV:
                    return AddInConfiguration.BreakPointVariantStraightV;
                case BpProfileSlot.LinkH:
                    return AddInConfiguration.BreakPointVariantMultiH;
                case BpProfileSlot.LinkV:
                    return AddInConfiguration.BreakPointVariantMultiV;
                default:
                    return -1;
            }
        }

        /// <summary>Кодирование выбора пользователя в одну строку настройки
        /// (UI Task 5): 'файл|имя схемы'. Разделитель '|' выбран потому, что
        /// в именах файлов Windows он недопустим, а в A2454-именах схем
        /// (по стенду) не встречается. Пустые части не проверяются —
        /// валидность на декодировании.</summary>
        public static string EncodeSelection(string strFile, string strSchemeName)
        {
            return strFile + "|" + strSchemeName;
        }

        /// <summary>Декодирование строки EncodeSelection. Разделитель — ПЕРВЫЙ
        /// '|' (правая часть берётся целиком). null/пусто/без '|'/пустая хотя
        /// бы одна часть → false; в этом случае out-параметры = "" (не null —
        /// потребителю достаточно проверить флаг). Иначе true и части строки.</summary>
        public static bool TryDecodeSelection(string strValue,
            out string strFile, out string strSchemeName)
        {
            strFile = "";
            strSchemeName = "";
            if (string.IsNullOrEmpty(strValue)) return false;
            int nBar = strValue.IndexOf('|');
            if (nBar < 0) return false;
            string strF = strValue.Substring(0, nBar);
            string strS = strValue.Substring(nBar + 1);
            if (strF.Length == 0 || strS.Length == 0) return false;
            strFile = strF;
            strSchemeName = strS;
            return true;
        }
    }

    /// <summary>Раскладка выбранных профилей по 6 слотам (rev.17, Task 2).
    /// Заполняется UI (Task 5) из настроек; For — выбор профиля для пары
    /// (тип обратного конца, мульти-кабель, ориентация отчёта) на вставке BP
    /// (Task 8). Незаполненный слот → null → BP вставляется БЕЗ набора
    /// (штатно, решение прод-волны). Ссылки на EmcSchemeInfo не копируются —
    /// потребитель сравнивает/логирует сам объект каталога.</summary>
    public sealed class BpProfileSelections
    {
        public EmcSchemeInfo StripH;
        public EmcSchemeInfo StripV;
        public EmcSchemeInfo DeviceH;
        public EmcSchemeInfo DeviceV;
        public EmcSchemeInfo LinkH;
        public EmcSchemeInfo LinkV;

        /// <summary>Профиль для вставки: multi (кабель между двумя
        /// клеммниками) → Link*, иначе kind==Device → Device*, иначе
        /// kind==TerminalStrip → Strip*; ориентация vertical → *V,
        /// горизонталь → *H. Unreadable/прочее → null (по контракту For
        /// вызывается только для валидных kind; null = BP без набора).</summary>
        public EmcSchemeInfo For(BpEndKind eKind, bool bMulti, bool bVertical)
        {
            if (bMulti) return bVertical ? LinkV : LinkH;
            if (eKind == BpEndKind.Device) return bVertical ? DeviceV : DeviceH;
            if (eKind == BpEndKind.TerminalStrip)
                return bVertical ? StripV : StripH;
            return null;
        }
    }
}
