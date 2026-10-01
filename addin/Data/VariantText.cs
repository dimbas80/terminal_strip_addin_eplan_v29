using System;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Буква варианта символа (rev.17, план
    /// 2026-10-01-ui-emc-profiles Task 3). ЧИСТЫЙ модуль — без EPLAN-типов
    /// (паттерн EmcProfileCatalog/BreakPointResolver). Вариант символа в EPLAN
    /// задаётся индексом 0..25 и отображается буквой A..Z (0→A … 7→H — рабочие
    /// варианты BP); прочие значения (в т.ч. отрицательные и &gt;25) не
    /// отображаются буквой, а печатаются числом — сентинел «нет варианта».
    /// UI (Task 5, MainDialog) показывает букву в подписях профилей.</summary>
    public static class VariantText
    {
        /// <summary>Буква варианта: 0..25 → "A".."Z" (0→A … 7→H), любое иное
        /// значение (отрицательное или &gt;25) → десятичная запись числа
        /// (InvariantCulture — без разделителей групп и знаков локали).</summary>
        public static string Letter(int index)
        {
            if (index >= 0 && index <= 25)
                return ((char)('A' + index)).ToString();
            return index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
