using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ExIconChanger;

/// <summary>
/// 현재 사용자 기준(HKCU\Software\Classes)으로 아이콘을 변경/복원합니다. (관리자 권한 불필요)
/// 탐색기는 ProgID(pngfile, 7-Zip.rar 등)의 DefaultIcon을 우선하므로
/// .확장자\DefaultIcon + ProgID\DefaultIcon 양쪽에 HKCU 오버라이드를 씁니다.
/// 복원 = HKCU 오버라이드(DefaultIcon 서브키) 삭제로 Windows 기본값 복귀.
/// </summary>
internal static class IconManager
{
    // SHCNE_ASSOCCHANGED
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>예: "rar", ".rar", " RAR " → ".rar"</summary>
    public static string NormalizeExtension(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("확장자를 입력하세요. (예: rar)");

        string ext = input.Trim().TrimStart('.').Trim().ToLowerInvariant();

        // . 앞부분만 허용 (rar, zip, 7z 같은 형태)
        foreach (char c in ext)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException($"확장자에 사용할 수 없는 문자 '{c}' 가 있습니다.");
        }

        if (ext.Length == 0 || ext.Length > 16)
            throw new ArgumentException("확장자 길이가 올바르지 않습니다.");

        return "." + ext;
    }

    public static string ExtensionWithoutDot(string normalizedExt) => normalizedExt.TrimStart('.');

    /// <summary>아이콘 적용 (.확장자 + ProgID 양쪽에 기록)</summary>
    public static void SetCustomIcon(string normalizedExt, string icoPath)
    {
        if (!File.Exists(icoPath))
            throw new FileNotFoundException("ICO 파일을 찾을 수 없습니다.", icoPath);
        if (!icoPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ICO 파일(.ico)만 사용할 수 있습니다.");

        string iconValue = $"\"{Path.GetFullPath(icoPath)}\",0";

        // 1) .확장자\DefaultIcon (확장자 직접 오버라이드)
        using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{normalizedExt}", true))
        using (RegistryKey iconKey = extKey.CreateSubKey("DefaultIcon", true))
        {
            iconKey.SetValue("", iconValue, RegistryValueKind.String);
        }

        // 2) ProgID\DefaultIcon (탐색기가 실제로 보는 곳. HKCU에 섀도우를 만들어 HKLM/HKCR을 가림)
        string? progId = ResolveProgId(normalizedExt);
        if (!string.IsNullOrWhiteSpace(progId))
        {
            using RegistryKey progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}", true);
            using RegistryKey progIconKey = progKey.CreateSubKey("DefaultIcon", true);
            progIconKey.SetValue("", iconValue, RegistryValueKind.String);
        }

        RefreshExplorer();
    }

    /// <summary>Windows 기본값으로 복원 (HKCU 오버라이드 삭제)</summary>
    public static void ResetToDefault(string normalizedExt)
    {
        string? progId = ResolveProgId(normalizedExt);

        DeleteDefaultIconSubKey($@"Software\Classes\{normalizedExt}");
        if (!string.IsNullOrWhiteSpace(progId))
            DeleteDefaultIconSubKey($@"Software\Classes\{progId}");

        RefreshExplorer();
    }

    private static void DeleteDefaultIconSubKey(string relativePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(relativePath, writable: true);
            key?.DeleteSubKeyTree("DefaultIcon", throwOnMissingSubKey: false);
        }
        catch
        {
            // 삭제 실패는 무시하고 새로고침만 시도
        }
    }

    /// <summary>
    /// .확장자에 연결된 ProgID 조회. Win10+에서는 기본 앱(UserChoice)이 최우선이라
    /// 1) FileExts\UserChoice → 2) HKCU .ext → 3) HKCR .ext 순으로 봅니다.
    /// 예: .png → pngfile (또는 기본 앱에 따라 Honeyview.png 등)
    /// </summary>
    public static string? ResolveProgId(string normalizedExt)
    {
        try
        {
            using RegistryKey? uc = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{normalizedExt}\UserChoice");
            string? v = uc?.GetValue("ProgId") as string;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        catch { }

        try
        {
            using RegistryKey? hkcu = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{normalizedExt}");
            string? v = hkcu?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        catch { }

        try
        {
            using RegistryKey? hkcr = Registry.ClassesRoot.OpenSubKey(normalizedExt);
            string? v = hkcr?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        catch { }

        return null;
    }

    /// <summary>현재 HKCU 오버라이드 여부 확인용 (없으면 "Windows 기본값" 상태)</summary>
    public static string? GetCurrentOverride(string normalizedExt)
    {
        string? progId = ResolveProgId(normalizedExt);
        if (!string.IsNullOrWhiteSpace(progId))
        {
            using RegistryKey? progKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{progId}\DefaultIcon");
            string? pv = progKey?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(pv)) return $"ProgID {progId}: {pv}";
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{normalizedExt}\DefaultIcon");
        return key?.GetValue("") as string;
    }

    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 아이콘 캐시 DB 삭제 후 탐색기 재시작 (관리자 권한 불필요).
    /// 캐시가 비대/손상되면 SHChangeNotify가 무시되므로 직접 삭제합니다.
    /// </summary>
    public static string ClearIconCacheAndRestartExplorer()
    {
        int deleted = 0;
        var errors = new System.Collections.Generic.List<string>();

        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
            System.Threading.Thread.Sleep(1500);
        }
        catch (Exception ex) { errors.Add("탐색기 종료: " + ex.Message); }

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer");
            foreach (string pattern in new[] { "iconcache*.db", "thumbcache*.db" })
            {
                string[] files = Array.Empty<string>();
                try { files = Directory.GetFiles(dir, pattern); } catch { }
                foreach (string f in files)
                {
                    try { File.Delete(f); deleted++; }
                    catch (Exception ex) { errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { errors.Add("캐시 삭제: " + ex.Message); }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { errors.Add("탐색기 시작: " + ex.Message); }

        RefreshExplorer();

        string msg = $"캐시 파일 {deleted}개 삭제 후 탐색기를 재시작했습니다.";
        if (errors.Count > 0) msg += "\n일부 오류: " + string.Join(" / ", errors);
        return msg;
    }

    private static void RefreshExplorer()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            System.Threading.Thread.Sleep(200);
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        // 아이콘 캐시 즉시 갱신 시도 (실패해도 무시)
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ie4uinit.exe",
                Arguments = "-show",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
    }
}
