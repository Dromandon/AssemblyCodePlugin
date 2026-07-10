namespace AssemblyCodePlugin.Models
{
    public class ZeroLevelSettings
    {
        // Нижний уровень нуля (низ зоны нуля)
        public string LowLevelName { get; set; } = "";
        public double LowLevelOffsetMm { get; set; } = 0;

        // Верхний уровень нуля (верх зоны нуля)
        public string HighLevelName { get; set; } = "";
        public double HighLevelOffsetMm { get; set; } = 0;
    }
}
