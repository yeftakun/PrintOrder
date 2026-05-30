namespace PrintForm
{
    internal sealed class NotificationOptions
    {
        public bool SoundEnabled { get; set; } = true;
        public bool DesktopEnabled { get; set; } = true;

        public NotificationOptions Clone()
        {
            return new NotificationOptions
            {
                SoundEnabled = SoundEnabled,
                DesktopEnabled = DesktopEnabled
            };
        }
    }
}