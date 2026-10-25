namespace GhubLite.Core.Hidpp;

public static class HidppConstants
{
    public const byte ReportIdLong = 0x11;
    public const byte ReportIdShort = 0x10;
    public const byte DeviceIndexDirect = 0xFF;
    public const int LongReportSize = 20;
    public const int ShortReportSize = 7;

    public static class Features
    {
        public const ushort Root = 0x0000;
        public const ushort FeatureSet = 0x0001;
        public const ushort DeviceName = 0x0005;
        public const ushort BatteryStatus = 0x1000;
        public const ushort BatteryUnified = 0x1004;
        public const ushort AdjustableDpi = 0x2201;
        public const ushort ReportRate = 0x8060;
        public const ushort OnboardProfiles = 0x8100;
        public const ushort MouseButtonFilter = 0x8110;
    }

    public static class OnboardFunctions
    {
        public const byte GetDescription = 0x00;
        public const byte SetMode = 0x01;
        public const byte GetMode = 0x02;
        public const byte SetCurrentProfile = 0x03;
        public const byte GetCurrentProfile = 0x04;
        public const byte ReadSector = 0x05;
        public const byte StartWrite = 0x06;
        public const byte WriteChunk = 0x07;
        public const byte CommitWrite = 0x08;
    }
}
