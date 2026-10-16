using System.Text;

namespace RoG.Core.Localization;

/// <summary>
/// Übersetzungsschlüssel, die aus Code (nicht über <c>x:Uid</c>) gelesen werden. Sie stehen in
/// <c>src/RoG.App/Strings/{de-DE,en-US,fr-FR}/Resources.resw</c>; der Test <c>ReswCompletenessTests</c> prüft,
/// dass jeder Schlüssel in allen drei Sprachen existiert. Namen nur aus Buchstaben, Ziffern und <c>_</c>
/// (ein Punkt wäre in .resw eine <c>x:Uid</c>-Eigenschaft).
/// </summary>
public static class StringKeys
{
    public const string AppDisplayName = "AppDisplayName";

    public const string NavHome = "Nav_Home";
    public const string NavNews = "Nav_News";
    public const string NavSquads = "Nav_Squads";
    public const string NavMessages = "Nav_Messages";
    public const string NavCalendar = "Nav_Calendar";
    public const string NavServers = "Nav_Servers";
    public const string NavForum = "Nav_Forum";
    public const string NavWiki = "Nav_Wiki";
    public const string NavMembers = "Nav_Members";
    public const string NavInfo = "Nav_Info";
    public const string NavMatches = "Nav_Matches";
    public const string NavAdmin = "Nav_Admin";
    public const string NavAccount = "Nav_Account";

    public const string LoopbackTitle = "Loopback_Title";
    public const string LoopbackSuccess = "Loopback_Success";
    public const string LoopbackFailure = "Loopback_Failure";

    public const string ComingSoon = "Common_ComingSoon";
    public const string CommonCancel = "Common_Cancel";
    public const string CommonRetry = "Common_Retry";
    public const string CommonLoading = "Common_Loading";
    public const string CommonEmpty = "Common_Empty";
    public const string CommonBack = "Common_Back";
    public const string CommonLinkCopied = "Common_LinkCopied";
    public const string CommonCopyFailed = "Common_CopyFailed";

    // Mitglieder, Online-Stand
    public const string MembersLastSeen = "Members_LastSeen";
    public const string PresenceOnline = "Presence_Online";
    public const string PresenceVoice = "Presence_Voice";
    public const string PresenceServer = "Presence_Server";
    public const string PresenceIngame = "Presence_Ingame";
    public const string PresenceOffline = "Presence_Offline";

    // Voice ({0}/{1} = Personen/Plätze bzw. Server/Adresse)
    public const string VoiceTsOnline = "Voice_TsOnline";
    public const string VoiceTsOffline = "Voice_TsOffline";
    public const string VoiceTsLaunched = "Voice_TsLaunched";
    public const string VoiceTsMissing = "Voice_TsMissing";

    // Matches
    public const string MatchesWin = "Matches_Win";
    public const string MatchesLoss = "Matches_Loss";
    public const string MatchesDraw = "Matches_Draw";
    public const string MatchesOpen = "Matches_Open";

    // Info
    public const string InfoGroupRules = "Info_GroupRules";
    public const string InfoGroupPages = "Info_GroupPages";
    public const string InfoGroupLegal = "Info_GroupLegal";
    public const string InfoNeedsAcceptance = "Info_NeedsAcceptance";
    public const string InfoAccepted = "Info_Accepted";

    // Termine ({0}/{1}/{2} = Zusagen/Vielleicht/Plätze)
    public const string EventsCounts = "Events_Counts";
    public const string EventsCountsMax = "Events_CountsMax";
    public const string EventsCancelled = "Events_Cancelled";
    public const string EventsMandatory = "Events_Mandatory";
    public const string EventsRsvpYes = "Events_RsvpYes";
    public const string EventsRsvpMaybe = "Events_RsvpMaybe";
    public const string EventsRsvpNo = "Events_RsvpNo";
    public const string EventsRsvpSaved = "Events_RsvpSaved";
    public const string EventsNoCalendarApp = "Events_NoCalendarApp";

    // Squads
    public const string SquadsLeader = "Squads_Leader";
    public const string SquadsClanLevel = "Squads_ClanLevel";
    public const string SquadsSubstitute = "Squads_Substitute";
    public const string SquadsMembers = "Squads_Members";
    public const string SquadsRecruitingOpen = "Squads_RecruitingOpen";
    public const string SquadsRecruitingWaitlist = "Squads_RecruitingWaitlist";
    public const string SquadsRecruitingFull = "Squads_RecruitingFull";

    // News ({0} = Anzahl bzw. Minuten)
    public const string NewsComments = "News_Comments";
    public const string NewsReadingMinutes = "News_ReadingMinutes";

    // Inhalte (HTML der API)
    public const string ContentOpenVideo = "Content_OpenVideo";
    public const string ContentOpenImage = "Content_OpenImage";
    public const string ContentOpenEmbed = "Content_OpenEmbed";
    public const string ContentSpoiler = "Content_Spoiler";

    // Start ({0} = Name)
    public const string HomeGreeting = "Home_Greeting";
    public const string HomeGreetingGuest = "Home_GreetingGuest";

    // Konto ({0} = Datum)
    public const string AccountJoined = "Account_Joined";
    public const string AccountTwoFactorOn = "Account_TwoFactorOn";
    public const string AccountTwoFactorOff = "Account_TwoFactorOff";
    public const string AccountSignOutTitle = "Account_SignOutTitle";
    public const string AccountSignOutText = "Account_SignOutText";
    public const string AccountSignOutConfirm = "Account_SignOutConfirm";

    // Einstellungen ({0} = Version)
    public const string SettingsLanguageSystem = "Settings_LanguageSystem";
    public const string SettingsAppVersion = "Settings_AppVersion";
    public const string SettingsPlatformVersion = "Settings_PlatformVersion";
    public const string SettingsPlatformUnavailable = "Settings_PlatformUnavailable";

    // Server: Hinweise zu „Verbinden“ ({0} = Servername bzw. Adresse host:port)
    public const string ServersLaunchedSteam = "Servers_LaunchedSteam";
    public const string ServersLaunchedGame = "Servers_LaunchedGame";
    public const string ServersLaunchedWeb = "Servers_LaunchedWeb";
    public const string ServersNoHandlerSteam = "Servers_NoHandlerSteam";
    public const string ServersNoHandlerEt = "Servers_NoHandlerEt";
    public const string ServersNoHandlerOther = "Servers_NoHandlerOther";
    public const string ServersManualMinecraft = "Servers_ManualMinecraft";
    public const string ServersCopied = "Servers_Copied";
    public const string ServersCopyFailed = "Servers_CopyFailed";
    public const string ServersPassworded = "Servers_Passworded";
    public const string ServersOnline = "Servers_Online";
    public const string ServersOffline = "Servers_Offline";
    public const string ServersUnknown = "Servers_Unknown";
    public const string ServersPlayers = "Servers_Players";
    public const string ServersPlayerListHeader = "Servers_PlayerListHeader";
    public const string ServersConnectAutomation = "Servers_ConnectAutomation";
    public const string ServersCopyAutomation = "Servers_CopyAutomation";

    /// <summary>Präfix für Fehlermeldungen: <c>Error_&lt;code&gt;</c>.</summary>
    public const string ErrorPrefix = "Error_";

    /// <summary>Fehlercodes, für die es immer einen Text geben muss (Pipeline, Login, Guide 35).</summary>
    public static readonly IReadOnlyList<string> RequiredErrorCodes =
    [
        "network_error", "timeout", "unauthenticated", "forbidden", "not_found", "rate_limited", "server_error", "unknown_error",
        "validation_failed", "insufficient_scope", "invalid_grant", "too_many_devices", "account_suspended", "too_many_attempts",
        "invalid_request", "invalid_scope", "access_denied", "unsupported_response_type",
        "state_mismatch", "browser_failed", "login_timeout", "missing_code",
    ];

    /// <summary>Alle Schlüssel aus Code – für den Vollständigkeitstest.</summary>
    public static IEnumerable<string> All()
    {
        foreach (var field in typeof(StringKeys).GetFields())
        {
            if (field.IsLiteral && field.FieldType == typeof(string) && field.Name != nameof(ErrorPrefix))
            {
                yield return (string)field.GetValue(null)!;
            }
        }

        foreach (var code in RequiredErrorCodes)
        {
            yield return ForError(code);
        }
    }

    /// <summary>Schlüssel für einen API-/Login-Fehlercode, z. B. <c>invalid_grant</c> → <c>Error_invalid_grant</c>.</summary>
    public static string ForError(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var sb = new StringBuilder(ErrorPrefix, ErrorPrefix.Length + code.Length);
        foreach (var c in code)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }
}

/// <summary>Liefert übersetzte Texte (App: <c>ResourceLoader</c> des Windows App SDK).</summary>
public interface ILocalizer
{
    /// <summary>Text zum Schlüssel; fehlt er, der Schlüssel selbst (sichtbar statt leer).</summary>
    string GetString(string key);
}

public static class LocalizerExtensions
{
    /// <summary>Fehlertext für einen Code, Rückfall <c>Error_unknown_error</c>.</summary>
    public static string GetError(this ILocalizer localizer, string code)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var key = StringKeys.ForError(code);
        var text = localizer.GetString(key);
        return text == key ? localizer.GetString(StringKeys.ForError(Api.ApiProblem.Codes.Unknown)) : text;
    }
}
