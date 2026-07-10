namespace AssemblyCodePlugin.Models
{
    // Результат определения зоны элемента относительно уровня нуля
    public enum ZoneResult
    {
        AboveZero,    // Элемент полностью выше нуля
        BelowZero,    // Элемент полностью ниже нуля
        Spanning,     // Элемент пересекает зону нуля (предупреждение)
        Unknown       // Не удалось определить
    }
}
