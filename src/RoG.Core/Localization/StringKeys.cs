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
    public const string NavUploads = "Nav_Uploads";
    public const string NavMedia = "Nav_Media";
    public const string NavAdmin = "Nav_Admin";
    public const string NavAccount = "Nav_Account";
    public const string NavNotifications = "Nav_Notifications";

    public const string LoopbackTitle = "Loopback_Title";
    public const string LoopbackSuccess = "Loopback_Success";
    public const string LoopbackFailure = "Loopback_Failure";

    public const string ComingSoon = "Common_ComingSoon";
    public const string CommonCancel = "Common_Cancel";
    public const string CommonConfirm = "Common_Confirm";
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

    // Forum ({0}/{1} = Themen/Beiträge bzw. Anzahl)
    public const string ForumCategoryStats = "Forum_CategoryStats";
    public const string ForumReplies = "Forum_Replies";
    public const string ForumBlockedAuthor = "Forum_BlockedAuthor";
    public const string ForumEdited = "Forum_Edited";
    public const string ForumPending = "Forum_Pending";
    public const string ForumPollVoters = "Forum_PollVoters";
    public const string ForumPollClosed = "Forum_PollClosed";
    public const string ForumReplySent = "Forum_ReplySent";
    public const string ForumNewThread = "Forum_NewThread";
    public const string ForumNewThreadTitle = "Forum_NewThreadTitle";
    public const string ForumNewThreadBody = "Forum_NewThreadBody";
    public const string ForumCreate = "Forum_Create";

    // Medien ({0} = Anzahl bzw. Dateiname)
    public const string MediaItems = "Media_Items";
    public const string MediaDownloadCount = "Media_DownloadCount";
    public const string MediaDownloaded = "Media_Downloaded";

    // Kurzlinks ({0} = Klicks)
    public const string LinksClicks = "Links_Clicks";
    public const string LinksBlocked = "Links_Blocked";
    public const string LinksInactive = "Links_Inactive";
    public const string LinksCreated = "Links_Created";
    public const string LinksCreatedCopied = "Links_CreatedCopied";

    // Uploads ({0} = Datei, {1}/{2} = Nummer/Anzahl)
    public const string UploadsPublic = "Uploads_Public";
    public const string UploadsMembers = "Uploads_Members";
    public const string UploadsPrivate = "Uploads_Private";
    public const string UploadsUploading = "Uploads_Uploading";
    public const string UploadsDone = "Uploads_Done";
    public const string UploadsDoneCopied = "Uploads_DoneCopied";
    public const string UploadsUnsupported = "Uploads_Unsupported";
    public const string UploadsProcessing = "Uploads_Processing";
    public const string UploadsDropCaption = "Uploads_DropCaption";

    // Nachrichten
    public const string MessagesDeleted = "Messages_Deleted";

    // Suche ({0} = Suchbegriff)
    public const string SearchHeader = "Search_Header";

    // Matches
    public const string MatchesWin = "Matches_Win";
    public const string MatchesLoss = "Matches_Loss";
    public const string MatchesDraw = "Matches_Draw";
    public const string MatchesOpen = "Matches_Open";

    // Info
    public const string RulesPromptTitle = "RulesPrompt_Title";
    public const string RulesPromptText = "RulesPrompt_Text";
    public const string RulesPromptRead = "RulesPrompt_Read";
    public const string RulesPromptLater = "RulesPrompt_Later";
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
    public const string AccountThisDevice = "Account_ThisDevice";
    public const string AccountDeviceRemoved = "Account_DeviceRemoved";
    public const string AccountExportRequested = "Account_ExportRequested";
    public const string AccountExportReady = "Account_ExportReady";
    public const string AccountExportPending = "Account_ExportPending";
    public const string AccountDeleteTitle = "Account_DeleteTitle";
    public const string AccountDeleteWarning = "Account_DeleteWarning";
    public const string AccountDeleteConfirmHeader = "Account_DeleteConfirmHeader";
    public const string AccountDeleteConfirm = "Account_DeleteConfirm";

    // Bewerbung ({0} = Squad, Datum, Alter, Prozent bzw. Regelwerk)
    public const string ApplyTitle = "Apply_Title";
    public const string ApplyTitleFor = "Apply_TitleFor";
    public const string ApplyWaitlist = "Apply_Waitlist";
    public const string ApplyBirthdate = "Apply_Birthdate";
    public const string ApplyYes = "Apply_Yes";
    public const string ApplyNo = "Apply_No";
    public const string ApplyAcceptRules = "Apply_AcceptRules";
    public const string ApplyReadRules = "Apply_ReadRules";
    public const string ApplyTrialConsent = "Apply_TrialConsent";
    public const string ApplyReferral = "Apply_Referral";
    public const string ApplySubmit = "Apply_Submit";
    public const string ApplyIncomplete = "Apply_Incomplete";
    public const string ApplySubmittedAt = "Apply_SubmittedAt";
    public const string ApplyTrialEnds = "Apply_TrialEnds";
    public const string ApplyVoteParticipation = "Apply_VoteParticipation";
    public const string ApplyYou = "Apply_You";
    public const string ApplyMessageSent = "Apply_MessageSent";

    // Votes ({0} = Alter, Beteiligung/Quorum, Datum bzw. Anzahl)
    public const string VotesAge = "Votes_Age";
    public const string VotesParticipation = "Votes_Participation";
    public const string VotesEnds = "Votes_Ends";
    public const string VotesCast = "Votes_Cast";
    public const string VotesMineYes = "Votes_MineYes";
    public const string VotesMineNo = "Votes_MineNo";
    public const string VotesMineAbstain = "Votes_MineAbstain";
    public const string HomeOpenVotes = "Home_OpenVotes";

    // Server-Konsole ({0} CPU, {1} RAM, {2} Disk, {3} Laufzeit)
    public const string ConsoleResources = "Console_Resources";
    public const string ConsoleStateRunning = "Console_StateRunning";
    public const string ConsoleStateStarting = "Console_StateStarting";
    public const string ConsoleStateStopping = "Console_StateStopping";
    public const string ConsoleStateOffline = "Console_StateOffline";
    public const string ConsoleDisconnected = "Console_Disconnected";
    public const string ConsolePowerSent = "Console_PowerSent";
    public const string ConsoleKillTitle = "Console_KillTitle";
    public const string ConsoleKillText = "Console_KillText";
    public const string ConsoleKillConfirm = "Console_KillConfirm";

    // Configs und Backups ({0} = Name)
    public const string ConfigAfterSave = "Config_AfterSave";
    public const string ConfigAfterExec = "Config_AfterExec";
    public const string ConfigAfterRestart = "Config_AfterRestart";
    public const string ConfigUnchanged = "Config_Unchanged";
    public const string ConfigSaved = "Config_Saved";
    public const string ConfigPending = "Config_Pending";
    public const string ConfigRollbackTitle = "Config_RollbackTitle";
    public const string BackupCreated = "Backup_Created";
    public const string BackupRestoreTitle = "Backup_RestoreTitle";
    public const string BackupRestoreText = "Backup_RestoreText";
    public const string BackupRestoreStarted = "Backup_RestoreStarted";
    public const string BackupDeleteTitle = "Backup_DeleteTitle";
    public const string BackupFailed = "Backup_Failed";
    public const string BackupRunning = "Backup_Running";
    public const string BackupLocked = "Backup_Locked";
    public const string BackupLock = "Backup_Lock";
    public const string BackupUnlock = "Backup_Unlock";

    // Sperren und Freigaben ({0} = Datum)
    public const string BanPlayerMissing = "Ban_PlayerMissing";
    public const string BanDone = "Ban_Done";
    public const string BanLiftTitle = "Ban_LiftTitle";
    public const string BanUntil = "Ban_Until";
    public const string BanPermanent = "Ban_Permanent";
    public const string ApprovalApproveTitle = "Approval_ApproveTitle";
    public const string ApprovalRejectTitle = "Approval_RejectTitle";
    public const string ApprovalApproved = "Approval_Approved";
    public const string ApprovalRejected = "Approval_Rejected";
    public const string ApprovalOutdated = "Approval_Outdated";

    // Statistiken (Zeitraum/Kennzahl/Award werden als "Stats_Period_" + Wert usw. gebildet)
    public const string StatsMyRank = "Stats_MyRank";
    public const string StatsAwardsTitle = "Stats_AwardsTitle";
    public const string StatsPeriod7d = "Stats_Period_7d";
    public const string StatsPeriod30d = "Stats_Period_30d";
    public const string StatsPeriodAll = "Stats_Period_all";
    public const string StatsMetricKills = "Stats_Metric_kills";
    public const string StatsMetricKd = "Stats_Metric_kd";
    public const string StatsMetricPlaytime = "Stats_Metric_playtime";
    public const string StatsMetricRounds = "Stats_Metric_rounds";
    public const string StatsMetricHeadshots = "Stats_Metric_headshots";
    public const string StatsAwardMostKills = "Stats_Award_most_kills";
    public const string StatsAwardBestKd = "Stats_Award_best_kd";
    public const string StatsAwardMostHeadshots = "Stats_Award_most_headshots";
    public const string StatsAwardObjectiveHero = "Stats_Award_objective_hero";
    public const string StatsAwardMostPlaytime = "Stats_Award_most_playtime";
    public const string HomeOnboardingProgress = "Home_OnboardingProgress";

    // Mitgliederkarte
    public const string MapTitle = "Map_Title";
    public const string MapEmpty = "Map_Empty";
    public const string MapNoTiles = "Map_NoTiles";
    public const string MapZoomIn = "Map_ZoomIn";
    public const string MapZoomOut = "Map_ZoomOut";

    // Wiki ({0} = Name)
    public const string WikiOfflineList = "Wiki_OfflineList";
    public const string WikiWatch = "Wiki_Watch";
    public const string WikiUnwatch = "Wiki_Unwatch";
    public const string WikiWatching = "Wiki_Watching";
    public const string WikiNotWatching = "Wiki_NotWatching";
    public const string WikiRestored = "Wiki_Restored";
    public const string WikiSaved = "Wiki_Saved";
    public const string WikiPending = "Wiki_Pending";
    public const string WikiConflict = "Wiki_Conflict";
    public const string WikiApproved = "Wiki_Approved";
    public const string WikiRejected = "Wiki_Rejected";

    // Moderation (Typ/Grund/Bereich werden als "Mod_Type_" + Wert usw. gebildet)
    public const string ModStatusOpen = "Mod_StatusOpen";
    public const string ModStatusResolved = "Mod_StatusResolved";
    public const string ModStatusDismissed = "Mod_StatusDismissed";
    public const string ModWordReplace = "Mod_Word_replace";
    public const string ModWordBlock = "Mod_Word_block";
    public const string ModWordReview = "Mod_Word_review";
    public const string ModReportUpdated = "Mod_ReportUpdated";
    public const string ModLiftTitle = "Mod_LiftTitle";
    public const string ModLifted = "Mod_Lifted";
    public const string ModWordDeleteTitle = "Mod_WordDeleteTitle";
    public const string ModReportedBy = "Mod_ReportedBy";
    public const string ModAgainst = "Mod_Against";
    public const string ModActivePoints = "Mod_ActivePoints";
    public const string ModWarnings = "Mod_Warnings";
    public const string ModInactive = "Mod_Inactive";
    public const string ModRevoke = "Mod_Revoke";
    public const string ModRevoked = "Mod_Revoked";
    public const string ModBans = "Mod_Bans";
    public const string BanLiftAction = "Ban_LiftAction";
    public const string ModReason = "Mod_Reason";
    public const string ModPoints = "Mod_Points";
    public const string ModDays = "Mod_Days";
    public const string ModScope = "Mod_ScopeHeader";
    public const string ModPublic = "Mod_Public";
    public const string ModNewAction = "Mod_NewAction";
    public const string ModWarn = "Mod_Warn";
    public const string ModBan = "Mod_Ban";
    public const string ModReasonMissing = "Mod_ReasonMissing";
    public const string ModBanned = "Mod_Banned";
    public const string ModWarned = "Mod_Warned";
    public const string ModScopeSite = "Mod_Scope_site";
    public const string ModScopeForum = "Mod_Scope_forum";
    public const string ModScopeGameserver = "Mod_Scope_gameserver";
    public const string ModTypeMessage = "Mod_Type_message";
    public const string ModTypeProfile = "Mod_Type_profile";
    public const string ModTypeGalleryItem = "Mod_Type_gallery_item";
    public const string ModTypeNewsComment = "Mod_Type_news_comment";
    public const string ModTypeForumPost = "Mod_Type_forum_post";
    public const string ModTypeDiscordMessage = "Mod_Type_discord_message";
    public const string ReportReasonSpam = "Report_Reason_spam";
    public const string ReportReasonInsult = "Report_Reason_insult";
    public const string ReportReasonHarassment = "Report_Reason_harassment";
    public const string ReportReasonIllegal = "Report_Reason_illegal";
    public const string ReportReasonCheating = "Report_Reason_cheating";
    public const string ReportReasonOther = "Report_Reason_other";

    // Melden
    public const string ReportTitle = "Report_Title";
    public const string ReportHint = "Report_Hint";
    public const string ReportReason = "Report_ReasonHeader";
    public const string ReportComment = "Report_Comment";
    public const string ReportSend = "Report_Send";
    public const string ReportReasonMissing = "Report_ReasonMissing";
    public const string ReportThanks = "Report_Thanks";

    // Bewerbungen verwalten (Status/Entscheidung/Quelle/Ergebnis werden als "AppAdmin_Status_" + Wert usw. gebildet)
    public const string AppAdminNoActivity = "AppAdmin_NoActivity";
    public const string AppAdminVote = "AppAdmin_Vote";
    public const string AppAdminMissing = "AppAdmin_Missing";
    public const string AppAdminReminded = "AppAdmin_Reminded";
    public const string AppAdminDecided = "AppAdmin_Decided";
    public const string AppAdminDecisionMessage = "AppAdmin_DecisionMessage";
    public const string AppAdminStatusOpen = "AppAdmin_Status_open";
    public const string AppAdminStatusSubmitted = "AppAdmin_Status_submitted";
    public const string AppAdminStatusWaitlisted = "AppAdmin_Status_waitlisted";
    public const string AppAdminStatusTrial = "AppAdmin_Status_trial";
    public const string AppAdminStatusVoting = "AppAdmin_Status_voting";
    public const string AppAdminStatusAccepted = "AppAdmin_Status_accepted";
    public const string AppAdminStatusRejected = "AppAdmin_Status_rejected";
    public const string AppAdminStatusWithdrawn = "AppAdmin_Status_withdrawn";
    public const string AppAdminStatusCancelled = "AppAdmin_Status_cancelled";
    public const string AppAdminStatusExpired = "AppAdmin_Status_expired";
    public const string AppAdminDecisionRelease = "AppAdmin_Decision_release";
    public const string AppAdminDecisionTrial = "AppAdmin_Decision_trial";
    public const string AppAdminDecisionVote = "AppAdmin_Decision_vote";
    public const string AppAdminDecisionAccept = "AppAdmin_Decision_accept";
    public const string AppAdminDecisionExtend = "AppAdmin_Decision_extend";
    public const string AppAdminDecisionReject = "AppAdmin_Decision_reject";
    public const string AppAdminDecisionCancel = "AppAdmin_Decision_cancel";
    public const string AppAdminSourceDiscordVoice = "AppAdmin_Source_discord_voice";
    public const string AppAdminSourceTeamspeak = "AppAdmin_Source_teamspeak";
    public const string AppAdminSourceWebsite = "AppAdmin_Source_website";
    public const string AppAdminSourceGameserver = "AppAdmin_Source_gameserver";
    public const string AppAdminResultPending = "AppAdmin_Result_pending";
    public const string AppAdminResultAccept = "AppAdmin_Result_accept";
    public const string AppAdminResultReject = "AppAdmin_Result_reject";
    public const string AppAdminResultTie = "AppAdmin_Result_tie";

    // Verwaltung – Übersicht (Systemstatus als "Admin_System_" + Wert)
    public const string AdminSystemNever = "Admin_SystemNever";
    public const string AdminSystemOk = "Admin_System_ok";
    public const string AdminSystemDegraded = "Admin_System_degraded";
    public const string AdminSystemDown = "Admin_System_down";
    public const string AdminSystemUnknown = "Admin_System_unknown";

    // Offline-Warteschlange
    public const string OutboxQueued = "Outbox_Queued";
    public const string OutboxFailedTitle = "Outbox_FailedTitle";
    public const string OutboxLabelMessage = "Outbox_LabelMessage";
    public const string OutboxLabelForum = "Outbox_LabelForum";
    public const string OutboxLabelRsvp = "Outbox_LabelRsvp";

    // Verwaltung – Mitglieder (Status als "MemberAdmin_Status_" + Wert)
    public const string MemberAdminJoined = "MemberAdmin_Joined";
    public const string MemberAdminLastSeen = "MemberAdmin_LastSeen";
    public const string MemberAdminScopeGlobal = "MemberAdmin_ScopeGlobal";
    public const string MemberAdminScopeSquad = "MemberAdmin_ScopeSquad";
    public const string MemberAdminAssigned = "MemberAdmin_Assigned";
    public const string MemberAdminRevoked = "MemberAdmin_Revoked";
    public const string MemberAdminStatusActive = "MemberAdmin_Status_active";
    public const string MemberAdminStatusSuspended = "MemberAdmin_Status_suspended";
    public const string MemberAdminStatusFormer = "MemberAdmin_Status_former";

    // Galerie einreichen ({0} = Anzahl)
    public const string GalleryCaption = "Gallery_Caption";
    public const string GalleryRights = "Gallery_Rights";
    public const string GallerySubmitTitle = "Gallery_SubmitTitle";
    public const string GallerySubmitConfirm = "Gallery_SubmitConfirm";
    public const string GallerySubmitted = "Gallery_Submitted";
    public const string GallerySubmittedPending = "Gallery_SubmittedPending";

    // Termine verwalten (Ort-Typ als "EventsNew_Location_" + Wert)
    public const string EventsScopeClan = "Events_ScopeClan";
    public const string EventsCancelTitle = "Events_CancelTitle";
    public const string EventsCancelDone = "Events_CancelDone";
    public const string EventsDeleteTitle = "Events_DeleteTitle";
    public const string EventsDeleted = "Events_Deleted";
    public const string EventsWholeSeries = "Events_WholeSeries";
    public const string EventsNewType = "EventsNew_Type";
    public const string EventsNewScope = "EventsNew_Scope";
    public const string EventsNewTitle = "EventsNew_Title";
    public const string EventsNewDate = "EventsNew_Date";
    public const string EventsNewTime = "EventsNew_Time";
    public const string EventsNewDuration = "EventsNew_Duration";
    public const string EventsNewLocationType = "EventsNew_LocationType";
    public const string EventsNewLocation = "EventsNew_Location";
    public const string EventsNewMandatory = "EventsNew_Mandatory";
    public const string EventsNewWeekly = "EventsNew_Weekly";
    public const string EventsNewCount = "EventsNew_Count";
    public const string EventsNewDialogTitle = "EventsNew_DialogTitle";
    public const string EventsNewCreate = "EventsNew_Create";
    public const string EventsNewLocationTeamspeak = "EventsNew_Location_teamspeak";
    public const string EventsNewLocationDiscord = "EventsNew_Location_discord";
    public const string EventsNewLocationServer = "EventsNew_Location_server";
    public const string EventsNewLocationOnline = "EventsNew_Location_online";
    public const string EventsNewLocationAddress = "EventsNew_Location_address";

    // Server-Verlauf ({0} = Spitze, {1} = Verfügbarkeit in %)
    public const string ServersHistoryEmpty = "Servers_HistoryEmpty";
    public const string ServersHistorySummary = "Servers_HistorySummary";

    // Squad-Statistiken und Turniere ({0} = Anzahl)
    public const string SquadStatsEmpty = "SquadStats_Empty";
    public const string TournamentParticipants = "Tournament_Participants";

    // Benachrichtigungs-Einstellungen (Sammel-Mail als "NotifySettings_Digest_" + Wert)
    public const string NotifySettingsSaved = "NotifySettings_Saved";
    public const string NotifySettingsQuietInvalid = "NotifySettings_QuietInvalid";
    public const string NotifySettingsDigestInstant = "NotifySettings_Digest_instant";
    public const string NotifySettingsDigestDaily = "NotifySettings_Digest_daily";
    public const string NotifySettingsDigestWeekly = "NotifySettings_Digest_weekly";

    // App-Sperre
    public const string LockPrompt = "Lock_Prompt";

    // Abwesenheit
    public const string AbsenceReported = "Absence_Reported";
    public const string AbsenceActive = "Absence_Active";
    public const string AbsenceUpcoming = "Absence_Upcoming";

    // Profil bearbeiten
    public const string ProfileSaved = "Profile_Saved";
    public const string ProfileImageSaved = "Profile_ImageSaved";

    // Einstellungen ({0} = Version)
    public const string SettingsLanguageSystem = "Settings_LanguageSystem";
    public const string SettingsAppVersion = "Settings_AppVersion";
    public const string SettingsPlatformVersion = "Settings_PlatformVersion";
    public const string SettingsPlatformUnavailable = "Settings_PlatformUnavailable";
    public const string SettingsAutostartDisabledByUser = "Settings_AutostartDisabledByUser";
    public const string SettingsAutostartPolicy = "Settings_AutostartPolicy";

    // Taskleiste/Tray ({0} = Zahl ungelesen)
    public const string JumpListGroup = "JumpList_Group";
    public const string TrayOpen = "Tray_Open";
    public const string TrayExit = "Tray_Exit";
    public const string TrayTooltipUnread = "Tray_TooltipUnread";
    public const string TrayStillRunning = "Tray_StillRunning";

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
    public const string FilesTooLarge = "Files_TooLarge";

    public const string FilesCopyTitle = "Files_CopyTitle";

    public const string FilesRenameTitle = "Files_RenameTitle";

    public const string FilesDeleteTitle = "Files_DeleteTitle";

    public const string FilesBackupNameHint = "Files_BackupNameHint";

    public const string FilesCopied = "Files_Copied";

    public const string FilesDeleted = "Files_Deleted";

    public const string WhitelistAdded = "Whitelist_Added";

    public const string WhitelistRemoveTitle = "Whitelist_RemoveTitle";

    public const string TemplateListHint = "Template_ListHint";

    public const string TemplatePasswordKeep = "Template_PasswordKeep";

    public const string TemplatePasswordNone = "Template_PasswordNone";

    public const string TemplatePasswordClear = "Template_PasswordClear";

    public const string TemplateNote = "Template_Note";

    public const string TemplateApply = "Template_Apply";

    public const string TemplateInvalid = "Template_Invalid";

    public const string Errornotemplate = "Error_no_template";

    public const string Errorwhitelistnotsupported = "Error_whitelist_not_supported";

    public const string WikiCreated = "Wiki_Created";

    public const string WikiNoParent = "Wiki_NoParent";

    public const string WikiImageOnly = "Wiki_ImageOnly";

    public const string WikiImageInserted = "Wiki_ImageInserted";

    public const string WikiNewTitle = "Wiki_NewTitle";

    public const string WikiNewFormat = "Wiki_NewFormat";

    public const string WikiNewParent = "Wiki_NewParent";

    public const string WikiNewContent = "Wiki_NewContent";

    public const string WikiNewSummary = "Wiki_NewSummary";

    public const string WikiNewPageTitle = "Wiki_NewPageTitle";

    public const string WikiNewCreate = "Wiki_NewCreate";

    public const string WikiNewTitleMissing = "Wiki_NewTitleMissing";

    public const string SquadAdminSaved = "SquadAdmin_Saved";

    public const string SquadAdminNothing = "SquadAdmin_Nothing";

    public const string SquadAdminImageQueued = "SquadAdmin_ImageQueued";

    public const string SquadAdminRemoveLogo = "SquadAdmin_RemoveLogo";

    public const string SquadAdminRemoveHeader = "SquadAdmin_RemoveHeader";

    public const string SquadAdminPickMember = "SquadAdmin_PickMember";

    public const string SquadAdminMemberAdded = "SquadAdmin_MemberAdded";

    public const string SquadAdminMemberSaved = "SquadAdmin_MemberSaved";

    public const string SquadAdminRemoveMemberTitle = "SquadAdmin_RemoveMemberTitle";

    public const string SquadAdminArchive = "SquadAdmin_Archive";

    public const string SquadAdminRestore = "SquadAdmin_Restore";

    public const string SquadAdminArchiveText = "SquadAdmin_ArchiveText";

    public const string SquadAdminRestoreText = "SquadAdmin_RestoreText";

    public const string SquadAdminArchived = "SquadAdmin_Archived";

    public const string SquadAdminRestored = "SquadAdmin_Restored";

    public const string NewsEditPublic = "NewsEdit_Public";

    public const string NewsEditMembers = "NewsEdit_Members";

    public const string NewsEditDraft = "NewsEdit_Draft";

    public const string NewsEditPublished = "NewsEdit_Published";

    public const string NewsEditScheduled = "NewsEdit_Scheduled";

    public const string NewsEditNew = "NewsEdit_New";

    public const string NewsEditEdit = "NewsEdit_Edit";

    public const string NewsEditNoCategory = "NewsEdit_NoCategory";

    public const string NewsEditSaved = "NewsEdit_Saved";

    public const string NewsEditPublishedDone = "NewsEdit_PublishedDone";

    public const string NewsEditScheduledDone = "NewsEdit_ScheduledDone";

    public const string NewsEditDeleteTitle = "NewsEdit_DeleteTitle";

    public const string NewsEditDeleted = "NewsEdit_Deleted";

    public const string NewsEditWindow = "NewsEdit_Window";

    public const string MatchEditNew = "MatchEdit_New";

    public const string MatchEditEdit = "MatchEdit_Edit";

    public const string MatchEditSaved = "MatchEdit_Saved";

    public const string MatchEditDeleteTitle = "MatchEdit_DeleteTitle";

    public const string MatchEditDeleted = "MatchEdit_Deleted";

    public const string MatchEditAttached = "MatchEdit_Attached";

    public const string MatchEditRemoveAttachment = "MatchEdit_RemoveAttachment";

    public const string EventsAbsent = "Events_Absent";

    public const string EventsParticipantsYes = "Events_ParticipantsYes";

    public const string EventsParticipantsMaybe = "Events_ParticipantsMaybe";

    public const string EventsParticipantsNo = "Events_ParticipantsNo";

    public const string EventsParticipantsOpen = "Events_ParticipantsOpen";

    public const string EventsLineupDraft = "Events_LineupDraft";

    public const string EventsLineupStarters = "Events_LineupStarters";

    public const string EventsLineupSubstitutes = "Events_LineupSubstitutes";

    public const string EventsLineupPublished = "Events_LineupPublished";

    public const string EventsLineupSaved = "Events_LineupSaved";

    public const string EventsLineupSubstitute = "Events_LineupSubstitute";

    public const string EventsLineupPosition = "Events_LineupPosition";

    public const string EventsRsvpYesShort = "Events_RsvpYesShort";

    public const string EventsRsvpMaybeShort = "Events_RsvpMaybeShort";

    public const string EventsRsvpOpenShort = "Events_RsvpOpenShort";

    public const string EventsLineupSuggest = "Events_LineupSuggest";

    public const string EventsLineupNote = "Events_LineupNote";

    public const string EventsLineupTitle = "Events_LineupTitle";

    public const string EventsLineupPublish = "Events_LineupPublish";

    public const string EventsLineupSaveDraft = "Events_LineupSaveDraft";

    public const string MediaAdminRejectTitle = "MediaAdmin_RejectTitle";

    public const string MediaAdminApproved = "MediaAdmin_Approved";

    public const string MediaAdminRejected = "MediaAdmin_Rejected";

    public const string MediaAdminStorage = "MediaAdmin_Storage";

    public const string MediaAdminDeleteUploadTitle = "MediaAdmin_DeleteUploadTitle";

    public const string MediaAdminUploadDeleted = "MediaAdmin_UploadDeleted";

    public const string MediaAdminUploadKept = "MediaAdmin_UploadKept";

    public const string ConsoleNoAccess = "Console_NoAccess";

    public const string ConsoleConnecting = "Console_Connecting";

    public const string ConsoleConnected = "Console_Connected";

    public const string ConsoleReconnecting = "Console_Reconnecting";

    public const string ConsoleLost = "Console_Lost";

    public const string ConsoleDemo = "Console_Demo";

    public const string AdminFormNew = "AdminForm_New";

    public const string AdminFormCheckFields = "AdminForm_CheckFields";

    public const string AdminFormNoChanges = "AdminForm_NoChanges";

    public const string AdminFormSaved = "AdminForm_Saved";

    public const string AdminFormDeleted = "AdminForm_Deleted";

    public const string AdminFormDeleteTitle = "AdminForm_DeleteTitle";

    public const string AdminFormDeleteText = "AdminForm_DeleteText";

    public const string AdminFormDeleteConfirm = "AdminForm_DeleteConfirm";

    public const string AdminFormOnePerLine = "AdminForm_OnePerLine";

    public const string AdminFormPartialLanguage = "AdminForm_PartialLanguage";

    public const string AdminFormErrorRequired = "AdminForm_ErrorRequired";

    public const string AdminFormErrorRequiredGerman = "AdminForm_ErrorRequiredGerman";

    public const string AdminFormErrorTooLong = "AdminForm_ErrorTooLong";

    public const string AdminFormErrorNumber = "AdminForm_ErrorNumber";

    public const string AdminFormErrorRange = "AdminForm_ErrorRange";

    public const string AdminFormErrorColor = "AdminForm_ErrorColor";

    public const string AdminFormErrorUrl = "AdminForm_ErrorUrl";

    public const string AdminFormErrorDate = "AdminForm_ErrorDate";

    public const string AdminFormErrorChoice = "AdminForm_ErrorChoice";

    public const string NewsCommentBlocked = "News_CommentBlocked";

    public const string ModTypewikipage = "Mod_Type_wiki_page";

    public const string ModTypewikirevision = "Mod_Type_wiki_revision";

    public const string SocialNoChannels = "Social_NoChannels";

    public const string SocialNotEligible = "Social_NotEligible";

    public const string SocialScheduledAt = "Social_ScheduledAt";

    public const string SocialRetryStarted = "Social_RetryStarted";

    public const string SocialUnusable = "Social_Unusable";

    public const string SocialRetryOne = "Social_RetryOne";

    public const string SocialRepostTitle = "Social_RepostTitle";

    public const string SocialRepostText = "Social_RepostText";

    public const string SocialRepostConfirm = "Social_RepostConfirm";

    public const string SocialRedrafted = "Social_Redrafted";

    public const string ProvisionTitle = "Provision_Title";

    public const string ProvisionManageTitle = "Provision_ManageTitle";

    public const string ProvisionNodeInfo = "Provision_NodeInfo";

    public const string ProvisionChooseNodeEgg = "Provision_ChooseNodeEgg";

    public const string ProvisionConfirmTitle = "Provision_ConfirmTitle";

    public const string ProvisionConfirmText = "Provision_ConfirmText";

    public const string ProvisionConfirmYes = "Provision_ConfirmYes";

    public const string ProvisionCreated = "Provision_Created";

    public const string ProvisionStatusUnknown = "Provision_StatusUnknown";

    public const string ProvisionExpiresAt = "Provision_ExpiresAt";

    public const string ProvisionNoExpiry = "Provision_NoExpiry";

    public const string ProvisionExpirySaved = "Provision_ExpirySaved";

    public const string ProvisionDeleteTitle = "Provision_DeleteTitle";

    public const string ProvisionDeleteText = "Provision_DeleteText";

    public const string ProvisionDeleted = "Provision_Deleted";

    public const string AccountDeleteLeaveText = "Account_DeleteLeaveText";

    public const string AccountDeleteLeaveUnavailable = "Account_DeleteLeaveUnavailable";

    public const string AccountDeleteLeft = "Account_DeleteLeft";

    public const string AccountDeleteLeaveButton = "Account_DeleteLeaveButton";

    public const string AccountDeleteLeaveConfirm = "Account_DeleteLeaveConfirm";

    public const string CommonRetryAfter = "Common_RetryAfter";

    public const string ModReasonAll = "Mod_ReasonAll";

    public const string NavTools = "Nav_Tools";

    public const string ToolsNoGame = "Tools_NoGame";

    public const string ToolsDraft = "Tools_Draft";

    public const string ToolsTooLarge = "Tools_TooLarge";

    public const string ToolsLoadFailed = "Tools_LoadFailed";

    public const string ToolsCopied = "Tools_Copied";

    public const string ToolsOpenLinkTitle = "Tools_OpenLinkTitle";

    public const string ToolsOpenLinkText = "Tools_OpenLinkText";

    public const string ToolsOpenLinkConfirm = "Tools_OpenLinkConfirm";

    public const string ToolsSquadEmpty = "Tools_SquadEmpty";

    public const string ServersPortsHeader = "Servers_PortsHeader";

    public const string ServersPrimaryPort = "Servers_PrimaryPort";

    public const string ErrorPrefix = "Error_";

    /// <summary>Fehlercodes, für die es immer einen Text geben muss (Pipeline, Login, Guide 35).</summary>
    public static readonly IReadOnlyList<string> RequiredErrorCodes =
    [
        "network_error", "timeout", "unauthenticated", "forbidden", "not_found", "rate_limited", "server_error", "unknown_error",
        "validation_failed", "insufficient_scope", "invalid_grant", "too_many_devices", "account_suspended", "too_many_attempts",
        "invalid_request", "invalid_scope", "access_denied", "unsupported_response_type",
        "state_mismatch", "browser_failed", "login_timeout", "missing_code",
        "checksum_mismatch", "download_failed", "upload_failed",
        "discord_required", "already_member", "application_blocked",
        "console_unavailable", "approval_required", "backup_locked", "backup_busy", "backup_not_found",
        "config_changed", "file_operation_forbidden", "pterodactyl_write_blocked",
        "forbidden_cvar", "rcon_write_blocked", "rcon_target_forbidden", "player_identity_conflict", "ban_managed_by_moderation",
        "own_change", "change_not_pending", "members_only", "invalid_signature", "edit_conflict", "not_pending",
        "report_target_missing", "report_own_content", "too_many_reports", "moderation_muted", "moderation_owner", "no_gameserver_targets", "vote_not_passed",
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
