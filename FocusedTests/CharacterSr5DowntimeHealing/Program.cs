using Chummer.Tests;

var suite = new CharacterSr5DowntimeHealingRulesTests();
suite.Quote_derives_sr5_track_pool_interval_and_source_anchor();
suite.Physical_interval_runs_quote_reserve_start_roll_complete_and_receipt();
suite.Completion_recovery_distinguishes_not_applied_pending_and_conflict();
suite.Early_stale_wrong_pool_and_glitch_completion_fail_closed();
suite.Cancel_and_interrupt_have_zero_refund_and_digest_bound_recovery();
suite.Blocked_quotes_are_explicit_and_invalid_authority_is_rejected();
Console.WriteLine("SR5 Downtime Healing Core authority tests passed: 6");
