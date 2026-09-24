using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Свод группировки соединений клеммника по кабелям (Фаза E, §4.6).
    /// Cables — кабельные группы (IsCable == true); NoCableConnections — провода
    /// (IsCable == false), не входящие ни в один кабель.</summary>
    public sealed class CableLayoutModel
    {
        public readonly List<CableModel> Cables = new List<CableModel>();
        public readonly List<TerminalConnectionModel> NoCableConnections = new List<TerminalConnectionModel>();
    }
}
