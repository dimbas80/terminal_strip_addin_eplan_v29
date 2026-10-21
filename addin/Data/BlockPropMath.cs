using System;

namespace MyEplanActions
{

    /// <summary>rev.16.1: чистые хелперы ГЛАВНОГО определения функции
    /// кабеля (PickMainIndex/IsMainFlag; п.ревижа rev16.1)). rev.16.2
    /// (решение 01.10): Decide/BlockFormatDecision (решение «Формат блока»
    /// 20202[x]) УДАЛЕНЫ — фича снята целиком; чтение 20376/20377 остаётся
    /// в BlockFormatResolver.ResolveCableEnds (потребитель — BP).
    ///</summary>
    public static class BlockPropMath
    {

        /// <summary>rev.16.1: индекс ГЛАВНОГО определения функции среди
        /// определений одного кабеля — ПЕРВЫЙ TRUE в flags (FC_FUNC_MAINFUNCTION
        /// #20122 «Главная функция» = TRUE только у главного определения);
        /// -1, если TRUE нет вовсе. nDefinitions — ожидаемое число
        /// определений (число элементов flags); перебор по flags.
        /// ЧИСТАЯ функция (без EPLAN-типов) — фаза A резолвера + тесты.</summary>
        public static int PickMainIndex(int nDefinitions,
            System.Collections.Generic.IEnumerable<bool> flags)
        {
            int iIndex = 0;
            foreach (bool bFlag in flags)
            {
                if (bFlag) return iIndex;
                iIndex++;
            }
            return -1;
        }

        /// <summary>rev.16.1: разбор СЫРОГО значения свойства #20122
        /// «Главная функция» (строка или «—» = не читается): "1" или "true"
        /// (OrdinalIgnoreCase) = TRUE; всё остальное (включая null/""/"—")
        /// = FALSE. ЧИСТАЯ функция — резолвер + тесты.</summary>
        public static bool IsMainFlag(string raw)
        {
            if (raw == null) return false;
            if (raw == "1") return true;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
