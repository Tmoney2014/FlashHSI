# Pleora eBUS SDK 네이티브 DLL 설정 가이드

SDK를 설치하지 않고, DLL 파일만 복사하여 어느 PC에서나 바로 실행되도록 설정하는 절차.

---

## 현재 상태

- `FlashHSI.Core/libs/` 폴더에 현재 **2개** 파일만 있음
  - `PvDotNet.dll` (.NET managed wrapper)
  - `PvGUIDotNet.dll` (.NET managed wrapper)
- `PvDotNet.dll`이 내부적으로 아래 **10개 네이티브 DLL**을 추가로 필요로 함 → **현재 누락**

---

## TODO

### Step 1 — 회사 PC에서 DLL 파일 복사 (회사 가서 할 것)

eBUS SDK가 설치된 PC에서 아래 10개 파일을 찾아 복사.

**SDK 설치 경로 (일반적)**:
```
C:\Program Files\Pleora Technologies Inc\eBUS SDK\
또는
C:\Program Files (x86)\Pleora Technologies Inc\eBUS SDK\
```

**복사할 파일 목록 (eBUS SDK 5.1.x 기준)**:

| 파일명 | 역할 |
|--------|------|
| `PvAppUtils64.dll` | 유틸리티 |
| `PvBase64.dll` | 기반 라이브러리 |
| `PvBuffer64.dll` | 버퍼 관리 |
| `PvCameraBridge64.dll` | 카메라 브릿지 |
| `PvDevice64.dll` | 디바이스 제어 |
| `PvGenICam64.dll` | GenICam 프로토콜 |
| `PvPersistence64.dll` | 설정 저장 |
| `PvSerial64.dll` | 시리얼 통신 |
| `PvStream64.dll` | 스트림 수신 |
| `PvSystem64.dll` | 시스템 열거 |
| `PvTransmitter64.dll` | 송신 |
| `PvVirtualDevice64.dll` | 가상 디바이스 |

> 총 10개를 찾아 `FlashHSI.Core/libs/` 폴더에 복사.

---

### Step 2 — csproj 설정 확인 (이미 완료)

`FlashHSI.Core/FlashHSI.Core.csproj`에 아래 설정이 있으면 빌드 시 자동으로 출력 폴더에 복사됨.

```xml
<ItemGroup>
  <!-- 기존 managed wrapper -->
  <Reference Include="PvDotNet">
    <HintPath>libs\PvDotNet.dll</HintPath>
  </Reference>
  <Reference Include="PvGUIDotNet">
    <HintPath>libs\PvGUIDotNet.dll</HintPath>
  </Reference>

  <!-- 네이티브 DLL 자동 복사 (Step 1 완료 후 동작) -->
  <None Include="libs\PvAppUtils64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvBase64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvBuffer64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvCameraBridge64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvDevice64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvGenICam64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvPersistence64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvSerial64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvStream64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvSystem64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvTransmitter64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="libs\PvVirtualDevice64.dll">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

---

### Step 3 — 빌드 및 실행 확인

```bash
dotnet build FlashHSI.sln
dotnet run --project FlashHSI.UI/FlashHSI.UI.csproj
```

빌드 후 `FlashHSI.UI/bin/Debug/net8.0-windows/` 폴더에 12개 DLL이 모두 있으면 성공.

---

## 완료 체크리스트

- [ ] `FlashHSI.Core/libs/`에 10개 네이티브 DLL 복사
- [ ] `dotnet build` 성공 확인
- [ ] 앱 실행 시 `PvDotNet.dll` 로드 에러 없음 확인
- [ ] git commit (`chore: Pleora 네이티브 DLL libs에 추가`)

---

## 참고

- eBUS SDK 버전: **5.1.5.4563**
- 아키텍처: **x64 전용** (32-bit DLL 복사 시 동작 안 함)
- `.gitignore`에 `*.dll`이 포함되어 있으면 `git add -f`로 강제 추가 필요
