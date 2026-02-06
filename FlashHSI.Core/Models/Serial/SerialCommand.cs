namespace FlashHSI.Core.Models.Serial;

public static class SerialCommand
{
    public const int
        DIO_RD_WT_IN = 0x5000,      // 상태값 Read 에러 체크 및 에로 초기화
        DIO_WT_AC = 0x5021,         // 벨트 구동 온오프
        DIO_WT_LAMP = 0x5022,       // 램프 구동 온오프
        DIO_WT_BOOT = 0x5023,       // 부트로더 구동 온오프
        DIO_WT_OUT = 0x5010,        // 시스템 오프시 날림
        DiO_WT_AVH = 0x5020,        // 시스템 시작시 날림 피삭 피더보드 사용처리
        FDR_WT_POWER_ALL = 0x3001,  // 피더 전체 온오프
        FDR_WT_INPUT0 = 0x3010,     // 피더 쌔기
        FDR_WT_ONOFF0 = 0x3002,     // 피더 사용 유무
        FDR_RD_FEEDERONOFF = 0x3060; // 값 Read Feeder On Off 상태 체크


    public static string COMM_R(int nId, int nCommand)
    {
        return (char)5 + nId.ToString("D2") + (char)27 + "R" + nCommand.ToString("X4") + "01" + (char)13;
    }


    public static string COMM_W(int nId, int nCmd, int nData)
    {
        return (char)5 + nId.ToString("D2") + (char)27 + "W" + nCmd.ToString("X4") + "01" +
               ((short)nData).ToString("X4") + (char)13;
    }
}
