namespace PrintForm
{
    internal sealed class PrintJob
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string? Alias { get; set; }
        public string Status { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public PrintConfig? PrintConfig { get; set; }
    }

    internal sealed class PrintConfig
    {
        public string? PaperSize { get; set; }
        public int Copies { get; set; }

        public string? ColorMode { get; set; }       // "color" | "bw"
        public string? Orientation { get; set; }     // "portrait" | "landscape"
        public string? PageRange { get; set; }       // e.g. "1-5" (jika PDF mendukung)
        public int ContentScale { get; set; } = 100; // 10 - 200 (%)
        public string? Notes { get; set; }           // e.g. "Catatan dari operator"
    }
}
