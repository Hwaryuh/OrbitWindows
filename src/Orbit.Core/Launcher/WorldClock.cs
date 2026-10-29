namespace Orbit.Core.Launcher;

/// <summary>A city and its time zone.</summary>
public sealed record City(string Name, TimeZoneInfo Zone);

/// <summary>
/// Cities by their English and Korean names, and "서울 시간" / "time in tokyo" (Orbit:
/// SmartAnswers.swift, <c>Cities</c> and <c>worldClock</c>). Zones are IANA ids, which .NET reads
/// on Windows through ICU.
/// </summary>
public static class WorldClock
{
    private static readonly (string Names, string Display, string Zone)[] List =
    [
        ("seoul 서울", "Seoul", "Asia/Seoul"), ("busan 부산", "Busan", "Asia/Seoul"),
        ("tokyo 도쿄 동경", "Tokyo", "Asia/Tokyo"), ("osaka 오사카", "Osaka", "Asia/Tokyo"),
        ("beijing 베이징 북경", "Beijing", "Asia/Shanghai"), ("shanghai 상하이", "Shanghai", "Asia/Shanghai"),
        ("hong kong hongkong 홍콩", "Hong Kong", "Asia/Hong_Kong"), ("taipei 타이베이 대만", "Taipei", "Asia/Taipei"),
        ("singapore 싱가포르", "Singapore", "Asia/Singapore"), ("bangkok 방콕 태국", "Bangkok", "Asia/Bangkok"),
        ("hanoi 하노이 vietnam 베트남", "Hanoi", "Asia/Ho_Chi_Minh"), ("ho chi minh 호치민", "Ho Chi Minh City", "Asia/Ho_Chi_Minh"),
        ("manila 마닐라 필리핀", "Manila", "Asia/Manila"), ("jakarta 자카르타 인도네시아", "Jakarta", "Asia/Jakarta"),
        ("delhi 델리 india 인도", "New Delhi", "Asia/Kolkata"), ("mumbai 뭄바이", "Mumbai", "Asia/Kolkata"),
        ("dubai 두바이", "Dubai", "Asia/Dubai"), ("moscow 모스크바 러시아", "Moscow", "Europe/Moscow"),
        ("istanbul 이스탄불 터키", "Istanbul", "Europe/Istanbul"), ("berlin 베를린 germany 독일", "Berlin", "Europe/Berlin"),
        ("paris 파리 france 프랑스", "Paris", "Europe/Paris"), ("london 런던 uk 영국", "London", "Europe/London"),
        ("madrid 마드리드 spain 스페인", "Madrid", "Europe/Madrid"), ("rome 로마 italy 이탈리아", "Rome", "Europe/Rome"),
        ("amsterdam 암스테르담", "Amsterdam", "Europe/Amsterdam"), ("zurich 취리히 스위스", "Zurich", "Europe/Zurich"),
        ("new york nyc 뉴욕", "New York", "America/New_York"), ("washington 워싱턴", "Washington, D.C.", "America/New_York"),
        ("boston 보스턴", "Boston", "America/New_York"), ("toronto 토론토", "Toronto", "America/Toronto"),
        ("chicago 시카고", "Chicago", "America/Chicago"), ("denver 덴버", "Denver", "America/Denver"),
        ("los angeles la 엘에이 로스앤젤레스", "Los Angeles", "America/Los_Angeles"),
        ("san francisco sf 샌프란시스코 실리콘밸리", "San Francisco", "America/Los_Angeles"),
        ("seattle 시애틀", "Seattle", "America/Los_Angeles"), ("vancouver 밴쿠버", "Vancouver", "America/Vancouver"),
        ("honolulu hawaii 하와이 호놀룰루", "Honolulu", "Pacific/Honolulu"), ("mexico city 멕시코", "Mexico City", "America/Mexico_City"),
        ("sao paulo 상파울루 브라질", "São Paulo", "America/Sao_Paulo"), ("sydney 시드니 호주", "Sydney", "Australia/Sydney"),
        ("melbourne 멜버른", "Melbourne", "Australia/Melbourne"), ("auckland 오클랜드 뉴질랜드", "Auckland", "Pacific/Auckland"),
        ("utc gmt", "UTC", "UTC"),
    ];

    private static readonly string[] Cues = ["time in ", "time ", " time", " 시간", "시간", "지금 ", " 몇시", "몇시"];

    /// <summary>A city named exactly by one of its words, or containing the name when it has 3 or more letters.</summary>
    public static City? Find(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var (names, display, zone) in List)
        {
            if (names.Split(' ').Contains(n) || (names.Contains(n, StringComparison.Ordinal) && n.Length >= 3))
                return Zone(zone) is { } z ? new City(display, z) : null;
        }
        return null;
    }

    /// <summary>"서울 시간", "time in tokyo", "뉴욕 몇시": the city asked about.</summary>
    public static City? Parse(string query)
    {
        var lower = query.ToLowerInvariant();
        if (!Cues.Any(lower.Contains)) return null;
        var name = lower;
        foreach (var cue in Cues) name = name.Replace(cue, " ");
        name = name.Trim();
        return name.Length == 0 ? null : Find(name);
    }

    /// <summary>The city the user is in, guessed from Windows' time zone (weather without a place).</summary>
    public static string Local
    {
        get
        {
            if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)) return "Seoul";
            foreach (var (_, display, zone) in List)
                if (zone == iana) return display;
            return iana.Split('/')[^1].Replace('_', ' ');
        }
    }

    /// <summary>How far <paramref name="zone"/> is ahead of Windows' own zone at <paramref name="now"/>.</summary>
    public static TimeSpan OffsetFromLocal(TimeZoneInfo zone, DateTimeOffset now) =>
        zone.GetUtcOffset(now) - TimeZoneInfo.Local.GetUtcOffset(now);

    private static TimeZoneInfo? Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            Log.Warn($"Time zone {id} is not available");
            return null;
        }
    }
}
