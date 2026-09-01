using System;
using System.Globalization;
using System.Resources;
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Localisation;

public static class O2LazerStrings
{
    private const string resourcePrefix = "osu.Game.Rulesets.O2Lazer.Resources.Localisation.O2LazerStrings";

    private static readonly ResourceManager englishResources = new(resourcePrefix, typeof(O2LazerStrings).Assembly);
    private static readonly ResourceManager chineseResources = new($"{resourcePrefix}.zh", typeof(O2LazerStrings).Assembly);

    public static LocalisableString RulesetName => get("ruleset_name");
    public static LocalisableString EditorUnavailable => get("editor_unavailable");
    public static LocalisableString O2Ma => get("o2ma");
    public static LocalisableString O2JamLevel => get("o2jam_level");
    public static LocalisableString Level => get("level");
    public static LocalisableString O2JamLevelGroupRange(int minimum, int maximum) => get("o2jam_level_group_range", minimum, maximum);
    public static LocalisableString O2JamLevelGroupOver(int level) => get("o2jam_level_group_over", level);
    public static LocalisableString O2JamLevelAcronym => get("o2jam_level_acronym");
    public static LocalisableString StarRating => get("star_rating");
    public static LocalisableString StarRatingAcronym => get("star_rating_acronym");
    public static LocalisableString MissingManiaStarRatingDescription => get("missing_mania_star_rating_description");
    public static LocalisableString ManiaStarRatingDescription => get("mania_star_rating_description");
    public static LocalisableString O2JamStarRatingDescription => get("o2jam_star_rating_description");
    public static LocalisableString Layout => get("layout");
    public static LocalisableString SevenKeys => get("seven_keys");
    public static LocalisableString ScrollDirection => get("scroll_direction");
    public static LocalisableString ScrollSpeed => get("scroll_speed");
    public static LocalisableString SyncSourceFolderCollections => get("sync_source_folder_collections");
    public static LocalisableString SourceFolderCollectionPrefix => get("source_folder_collection_prefix");
    public static LocalisableString SourceFolderCollectionName(string folder) => get("source_folder_collection_name", folder);
    public static LocalisableString MetadataEncoding => get("ojn_metadata_encoding");
    public static LocalisableString MetadataEncodingDescription => get("ojn_metadata_encoding_description");
    public static LocalisableString MetadataEncodingAutomatic => get("ojn_metadata_encoding_automatic");
    public static LocalisableString MetadataEncodingGbk => get("ojn_metadata_encoding_gbk");
    public static LocalisableString MetadataEncodingCp949 => get("ojn_metadata_encoding_cp949");
    public static LocalisableString MetadataEncodingUtf8 => get("ojn_metadata_encoding_utf8");
    public static LocalisableString O2JamLongNoteVisual => get("o2jam_long_note_visual");
    public static LocalisableString O2JamLongNoteVisualDescription => get("o2jam_long_note_visual_description");
    public static LocalisableString PercyLongNoteBodyRepeat => get("percy_long_note_body_repeat");
    public static LocalisableString PercyLongNoteBodyRepeatDescription => get("percy_long_note_body_repeat_description");
    public static LocalisableString ImportPath => get("import_path");
    public static LocalisableString ImportPathHint => get("import_path_hint");
    public static LocalisableString ImportPathPlaceholder => get("import_path_placeholder");
    public static LocalisableString RefreshBeatmaps => get("refresh_beatmaps");
    public static LocalisableString RefreshBeatmapsTooltip => get("refresh_beatmaps_tooltip");
    public static LocalisableString DeleteAllImportedFiles => get("delete_all_imported_files");
    public static LocalisableString DeleteAllConfirmation => get("delete_all_confirmation");
    public static LocalisableString RefreshingProgress(int processed, int total) => get("refreshing_progress", processed, total);
    public static LocalisableString RefreshComplete => get("refresh_complete");
    public static LocalisableString ImportSelectedFile => get("import_selected_file");
    public static LocalisableString ImportCurrentFolder => get("import_current_folder");
    public static LocalisableString ImportDirectoryRecursive => get("import_directory_recursive");
    public static LocalisableString ImportDirectoryRecursiveTooltip => get("import_directory_recursive_tooltip");
    public static LocalisableString ImportInitialising => get("import_initialising");
    public static LocalisableString ImportFailed => get("import_failed");
    public static LocalisableString SetAlreadyImported => get("set_already_imported");
    public static LocalisableString RulesetUnavailable => get("ruleset_unavailable");
    public static LocalisableString AllSetsAlreadyImported(int count) => get("all_sets_already_imported", count);
    public static LocalisableString ImportedSets(int count) => get("imported_sets", count);
    public static LocalisableString UpdatedSets(int count) => get("updated_sets", count);
    public static LocalisableString ImportedAndUpdatedSets(int imported, int updated) => get("imported_and_updated_sets", imported, updated);
    public static LocalisableString ImportCompletedWithFailures(int imported, int total, int failed) =>
        get("import_completed_with_failures", imported, total, failed);
    public static LocalisableString Cool => get("cool");
    public static LocalisableString Good => get("good");
    public static LocalisableString Bad => get("bad");
    public static LocalisableString Miss => get("miss");
    public static LocalisableString DifficultyName(object difficulty, int level) => get("difficulty_name", difficulty, level);
    public static LocalisableString ScrollSpeedValue(double milliseconds, double speed) => get("scroll_speed_value", milliseconds, speed);
    public static LocalisableString ScrollSpeedTooltipWithO2JamGrade(LocalisableString tooltip, double multiplier) =>
        get("scroll_speed_tooltip_with_o2jam_grade", tooltip, multiplier);
    public static LocalisableString ActionKey1 => get("action_key_1");
    public static LocalisableString ActionKey2 => get("action_key_2");
    public static LocalisableString ActionKey3 => get("action_key_3");
    public static LocalisableString ActionKey4 => get("action_key_4");
    public static LocalisableString ActionKey5 => get("action_key_5");
    public static LocalisableString ActionKey6 => get("action_key_6");
    public static LocalisableString ActionKey7 => get("action_key_7");
    public static LocalisableString CrossRulesetConversionUnsupported => get("cross_ruleset_conversion_unsupported");
    public static LocalisableString ModNoFailDescription => get("mod_no_fail_description");
    public static LocalisableString ModHalfTimeDescription => get("mod_half_time_description");
    public static LocalisableString ModDaycoreDescription => get("mod_daycore_description");
    public static LocalisableString ModNoReleaseDescription => get("mod_no_release_description");
    public static LocalisableString ModSuddenDeathDescription => get("mod_sudden_death_description");
    public static LocalisableString ModPerfectDescription => get("mod_perfect_description");
    public static LocalisableString ModDoubleTimeDescription => get("mod_double_time_description");
    public static LocalisableString ModNightcoreDescription => get("mod_nightcore_description");
    public static LocalisableString ModFadeInDescription => get("mod_fade_in_description");
    public static LocalisableString ModHiddenDescription => get("mod_hidden_description");
    public static LocalisableString ModCoverDescription => get("mod_cover_description");
    public static LocalisableString ModFlashlightDescription => get("mod_flashlight_description");
    public static LocalisableString ModAccuracyChallengeDescription => get("mod_accuracy_challenge_description");
    public static LocalisableString ModPerfectRequirePerfectHits => get("mod_perfect_require_perfect_hits");
    public static LocalisableString ModConstantSpeedDescription => get("mod_constant_speed_description");
    public static LocalisableString ModMirrorDescription => get("mod_mirror_description");
    public static LocalisableString ModRandomDescription => get("mod_random_description");
    public static LocalisableString ModInvertDescription => get("mod_invert_description");
    public static LocalisableString ModWindUpDescription => get("mod_wind_up_description");
    public static LocalisableString ModWindDownDescription => get("mod_wind_down_description");
    public static LocalisableString ModMutedDescription => get("mod_muted_description");
    public static LocalisableString ModAdaptiveSpeedDescription => get("mod_adaptive_speed_description");
    public static LocalisableString ModManiaScoreName => get("mod_mania_score_name");
    public static LocalisableString ModManiaScoreAcronym => get("mod_mania_score_acronym");
    public static LocalisableString ModManiaScoreDescription => get("mod_mania_score_description");

    private static LocalisableString get(string key, params object[] args)
    {
        var english = englishResources.GetString(key, CultureInfo.InvariantCulture)
                      ?? throw new InvalidOperationException($"Missing English localisation resource: {key}");
        var chinese = chineseResources.GetString(key, CultureInfo.InvariantCulture) ?? english;
        return new ResourceString(key, english, chinese, args);
    }

    private sealed class ResourceString(string key, string english, string chinese, object[] args)
        : LocalisableFormattableString(english, args)
    {
        private readonly string resourceKey = key;

        protected override string FormatString(string format, object?[] formatArgs, LocalisationParameters parameters)
        {
            var culture = parameters.Store?.EffectiveCulture.Name;
            var useChinese = culture?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
            return base.FormatString(useChinese ? chinese : format, formatArgs, parameters);
        }

        public override bool Equals(ILocalisableStringData? other) =>
            other is ResourceString resource && resourceKey == resource.resourceKey && base.Equals(resource);

        public override bool Equals(object? obj) => obj is ILocalisableStringData localisable && Equals(localisable);

        public override int GetHashCode() => HashCode.Combine(resourceKey, base.GetHashCode());
    }
}
