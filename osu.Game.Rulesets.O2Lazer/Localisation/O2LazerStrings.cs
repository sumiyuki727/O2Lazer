using System;
using System.Globalization;
using System.Resources;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;


namespace osu.Game.Rulesets.O2Lazer.Localisation;

public static class O2LazerStrings
{
    private const string resource_prefix = "osu.Game.Rulesets.O2Lazer.Resources.Localisation.O2LazerStrings";

    private static readonly ResourceManager english_resources = new(resource_prefix, typeof(O2LazerStrings).Assembly);
    private static readonly ResourceManager chinese_resources = new($"{resource_prefix}.zh", typeof(O2LazerStrings).Assembly);

    public static LocalisableString Layout => get("layout");

    public static LocalisableString RulesetName => get("ruleset_name");

    public static LocalisableString O2JamLevel => get("o2jam_level");

    public static LocalisableString O2JamLevelDescription => get("o2jam_level_description");


    public static LocalisableString Timeline => get("timeline");

    public static LocalisableString HitScatter => get("hit_scatter");

    public static LocalisableString HitOffset => get("hit_offset");

    public static LocalisableString StandardDeviation(double value) => get("standard_deviation", value);



    public static LocalisableString VisualOffset => get("visual_offset");


    public static LocalisableString OffsetMilliseconds(double value) => get("offset_milliseconds", value);

    public static LocalisableString VisualOffsetTooltip(double value) => value switch
    {
        > 0 => get("visual_offset_earlier", value),
        < 0 => get("visual_offset_later", value),
        _ => OffsetMilliseconds(value),
    };

    public static LocalisableString VisualOffsetSuggestionNote => get("visual_offset_suggestion_note");

    public static LocalisableString VisualOffsetSuggestionCorrect(int plays) => get("visual_offset_suggestion_correct", plays);

    public static LocalisableString VisualOffsetSuggestionReceived(int plays, LocalisableString value) =>
        get("visual_offset_suggestion_received", plays, value);

    public static LocalisableString ApplySuggestedVisualOffset => get("apply_suggested_visual_offset");

    public static LocalisableString AdjustVisualOffsetAutomatically => get("adjust_visual_offset_automatically");

    public static LocalisableString AdjustVisualOffsetAutomaticallyTooltip => get("adjust_visual_offset_automatically_tooltip");



    public static LocalisableString SongProgressColour => get("song_progress_colour");

    public static LocalisableString SongProgressColourDescription => get("song_progress_colour_description");














    public static LocalisableString JudgementLineOffset => get("judgement_line_offset");

    public static LocalisableString JudgementLineOffsetDescription => get("judgement_line_offset_description");

    public static LocalisableString LightPositionOffset => get("light_position_offset");

    public static LocalisableString LightPositionOffsetDescription => get("light_position_offset_description");

    public static LocalisableString NoteHeightScale => get("note_height_scale");

    public static LocalisableString NoteHeightScaleDescription => get("note_height_scale_description");

    public static LocalisableString ScaleStageWidthByColumns => get("scale_stage_width_by_columns");

    public static LocalisableString ScaleStageWidthByColumnsDescription => get("scale_stage_width_by_columns_description");













    public static LocalisableString AutoHideDelay => get("auto_hide_delay");

    public static LocalisableString AutoHideDelayDescription => get("auto_hide_delay_description");

    public static LocalisableString MinVisibleCombo => get("min_visible_combo");

    public static LocalisableString MinVisibleComboDescription => get("min_visible_combo_description");

    public static LocalisableString ScoreGraphCurrent => get("score_graph_current");

    public static LocalisableString ScoreGraphPersonalBest => get("score_graph_personal_best");

    public static LocalisableString ScoreGraphPersonalBestShort => get("score_graph_personal_best_short");

    public static LocalisableString ScoreGraphTarget(ScoreRank rank) => get("score_graph_target", ScoreGraphRank(rank));

    public static LocalisableString ScoreGraphRank(ScoreRank rank) => rank switch
    {
        ScoreRank.X or ScoreRank.XH => get("score_graph_rank_x"),
        ScoreRank.S or ScoreRank.SH => get("score_graph_rank_s"),
        ScoreRank.A => get("score_graph_rank_a"),
        ScoreRank.B => get("score_graph_rank_b"),
        ScoreRank.C => get("score_graph_rank_c"),
        _ => get("score_graph_rank_d"),
    };

    public static LocalisableString ScoreGraphRankThreshold(ScoreRank rank, int score) =>
        get("score_graph_rank_threshold", ScoreGraphRank(rank), score);

    public static LocalisableString ScoreGraphExScore(int score) => get("score_graph_ex_score", score);

    public static LocalisableString ScoreGraphDifference(int difference) => get("score_graph_difference", difference);

    public static LocalisableString ScoreGraphJudgement(HitResult result) => result switch
    {
        HitResult.Perfect => get("score_graph_judgement_pgreat"),
        HitResult.Great => get("score_graph_judgement_great"),
        HitResult.Good => get("score_graph_judgement_good"),
        HitResult.Ok => get("score_graph_judgement_bad"),
        HitResult.Meh => get("score_graph_judgement_poor"),
        _ => get("score_graph_judgement_empty_poor"),
    };

    public static LocalisableString ScoreGraphJudgementCount(int count) => get("score_graph_judgement_count", count);

    public static LocalisableString ScoreGraphJudgementUnavailable => get("score_graph_judgement_unavailable");

    public static LocalisableString ScoreGraphCurrentColour => get("score_graph_current_colour");

    public static LocalisableString ScoreGraphCurrentColourDescription => get("score_graph_current_colour_description");

    public static LocalisableString ScoreGraphPersonalBestColour => get("score_graph_personal_best_colour");

    public static LocalisableString ScoreGraphPersonalBestColourDescription => get("score_graph_personal_best_colour_description");

    public static LocalisableString ScoreGraphTargetColour => get("score_graph_target_colour");

    public static LocalisableString ScoreGraphTargetColourDescription => get("score_graph_target_colour_description");

    public static LocalisableString ScoreGraphShowBars => get("score_graph_show_bars");

    public static LocalisableString ScoreGraphShowBarsDescription => get("score_graph_show_bars_description");

    public static LocalisableString ScoreGraphShowScoreDifference => get("score_graph_show_score_difference");

    public static LocalisableString ScoreGraphShowScoreDifferenceDescription => get("score_graph_show_score_difference_description");

    public static LocalisableString ScoreGraphShowJudgementComparison => get("score_graph_show_judgement_comparison");

    public static LocalisableString ScoreGraphShowJudgementComparisonDescription => get("score_graph_show_judgement_comparison_description");



    public static LocalisableString PreviewPlayKeysounds => get("preview_play_keysounds");

    public static LocalisableString AutoPlayKeysounds => get("auto_play_keysounds");


    public static LocalisableString SyncSourceFolderCollections => get("sync_source_folder_collections");



    public static LocalisableString UnlockFrameRateLimit => get("unlock_frame_rate_limit");

    public static LocalisableString UnlockFrameRateLimitHint => get("unlock_frame_rate_limit_hint");

    public static LocalisableString FixedScrollSpeed => get("fixed_scroll_speed");


    public static LocalisableString ScrollSpeedTooltipWithO2JamGrade(LocalisableString tooltip, string grade) =>
        get("scroll_speed_tooltip_with_o2jam_grade", tooltip, grade);







    public static LocalisableString ImportPath => get("import_path");

    public static LocalisableString ImportPathHint => get("import_path_hint");

    public static LocalisableString ImportPathPlaceholder => get("import_path_placeholder");

    public static LocalisableString RefreshBeatmaps => get("refresh_beatmaps");

    public static LocalisableString RefreshBeatmapsTooltip => get("refresh_beatmaps_tooltip");



    public static LocalisableString DeleteAllImportedFiles => get("delete_all_imported_files");

    public static LocalisableString SettingsGroupImport => get("settings_group_import");

    public static LocalisableString SettingsGroupScrollSpeed => get("settings_group_scroll_speed");

    public static LocalisableString SettingsGroupVisual => get("settings_group_visual");

    public static LocalisableString SettingsGroupOther => get("settings_group_other");



    public static LocalisableString DeleteAllConfirmation => get("delete_all_confirmation");


    public static LocalisableString ImportSelectedFile => get("import_selected_file");

    public static LocalisableString ImportCurrentFolder => get("import_current_folder");

    public static LocalisableString ImportDirectoryRecursive => get("import_directory_recursive");

    public static LocalisableString ImportDirectoryRecursiveTooltip => get("import_directory_recursive_tooltip");

    public static LocalisableString SelectFileOrFolder => get("select_file_or_folder");

















    public static LocalisableString EditorUnsupported => get("editor_unsupported");



    public static LocalisableString Start => get("start");

    public static LocalisableString Time => get("time");

    public static LocalisableString Overall => get("overall");


    public static LocalisableString Fast => get("fast");

    public static LocalisableString Slow => get("slow");

    public static LocalisableString HitErrorMeterShowEmptyPoor => get("hit_error_meter_show_empty_poor");

    public static LocalisableString HitErrorMeterShowEmptyPoorDescription => get("hit_error_meter_show_empty_poor_description");

    public static LocalisableString HitErrorMeterShowPoor => get("hit_error_meter_show_poor");

    public static LocalisableString HitErrorMeterShowPoorDescription => get("hit_error_meter_show_poor_description");

    public static LocalisableString HitErrorMeterFadeDuration => get("hit_error_meter_fade_duration");

    public static LocalisableString HitErrorMeterFadeDurationDescription => get("hit_error_meter_fade_duration_description");

    public static LocalisableString Key(int number) => get("key", number);

    public static LocalisableString Notes => get("notes");

    public static LocalisableString Judgement => get("judgement");

    public static LocalisableString FastSlow => get("fast_slow");

    public static LocalisableString Note => get("note");

    public static LocalisableString LongNote => get("long_note");

    public static LocalisableString Cool => get("cool");

    public static LocalisableString Miss => get("miss");



    public static LocalisableString Bad => get("bad");

    public static LocalisableString Good => get("good");















    public static LocalisableString HitCount(int count) => get("hit_count", count);

    public static LocalisableString ImportInitialising => get("import_initialising");

    public static LocalisableString ScanningOrphans => get("scanning_orphans");

    public static LocalisableString CleanupComplete(int count) => get("cleanup_complete", count);

    public static LocalisableString NoOrphansFound => get("no_orphans_found");

    public static LocalisableString CleanupFailed => get("cleanup_failed");

    public static LocalisableString DeletingBeatmaps => get("deleting_beatmaps");

    public static LocalisableString DeletedSets(int count) => get("deleted_sets", count);

    public static LocalisableString DeleteCancelled(int count) => get("delete_cancelled", count);

    public static LocalisableString DeleteWasCancelled => get("delete_was_cancelled");

    public static LocalisableString DeleteFailed => get("delete_failed");

    public static LocalisableString ImportFailed => get("import_failed");

    public static LocalisableString SetAlreadyImported => get("set_already_imported");

    public static LocalisableString AllSetsAlreadyImported(int count) => get("all_sets_already_imported", count);

    public static LocalisableString ImportedSets(int count) => get("imported_sets", count);

    public static LocalisableString ImportedSetsProgress(int imported, int total) => get("imported_sets_progress", imported, total);

    public static LocalisableString ImportCompletedWithFailures(int imported, int total, int failed) =>
        get("import_completed_with_failures", imported, total, failed);

    public static LocalisableString RulesetUnavailable => get("ruleset_unavailable");

    public static LocalisableString ScanningFiles => get("scanning_files");

    public static LocalisableString NoChartsFound => get("no_charts_found");

    public static LocalisableString PreparingImport => get("preparing_import");

    public static LocalisableString ImportWasCancelled => get("import_was_cancelled");

    public static LocalisableString ImportCancelledProgress(int imported, int total) => get("import_cancelled_progress", imported, total);













    public static LocalisableString ActionKey1 => get("action_key_1");

    public static LocalisableString ActionKey2 => get("action_key_2");

    public static LocalisableString ActionKey3 => get("action_key_3");

    public static LocalisableString ActionKey4 => get("action_key_4");

    public static LocalisableString ActionKey5 => get("action_key_5");

    public static LocalisableString ActionKey6 => get("action_key_6");

    public static LocalisableString ActionKey7 => get("action_key_7");



























    public static LocalisableString ActionIncreaseScrollSpeed => get("action_increase_scroll_speed");

    public static LocalisableString ActionDecreaseScrollSpeed => get("action_decrease_scroll_speed");
















    private static LocalisableString get(string key, params object[] args)
    {
        var english = english_resources.GetString(key, CultureInfo.InvariantCulture)
                      ?? throw new InvalidOperationException($"Missing English localisation resource: {key}");
        var chinese = chinese_resources.GetString(key, CultureInfo.InvariantCulture) ?? english;

        return new O2LazerLocalisableString(key, english, chinese, args);
    }

    private sealed class O2LazerLocalisableString(string key, string english, string chinese, object[] args)
        : LocalisableFormattableString(english, args)
    {
        private readonly string key = key;

        protected override string FormatString(string format, object?[] formatArgs, LocalisationParameters parameters)
        {
            var culture = parameters.Store?.EffectiveCulture.Name;
            var useChinese = culture != null &&
                             (culture.Equals("zh", StringComparison.OrdinalIgnoreCase)
                              || culture.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                              || culture.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase));

            return base.FormatString(useChinese ? chinese : format, formatArgs, parameters);
        }

        public override bool Equals(ILocalisableStringData? other) =>
            other is O2LazerLocalisableString localisable
            && key == localisable.key
            && base.Equals(localisable);

        public override bool Equals(object? obj) => obj is ILocalisableStringData localisable && Equals(localisable);

        public override int GetHashCode() => HashCode.Combine(key, base.GetHashCode());
    }


}

