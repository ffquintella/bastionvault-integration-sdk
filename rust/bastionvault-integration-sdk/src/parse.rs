//! The three shared value parsers (D-M1a-4 / D-M1a-17): one boolean parser, one
//! duration parser, one integer parser, used by every setting of the matching type in a
//! single declared resolution order (see `src/config.rs`).

use std::time::Duration;

use crate::error::config_errors::invalid_setting_value;
use crate::error::Error;

/// CFG-003: `1/true/yes/on` (case-insensitive) is `true`; `0/false/no/off`/empty is
/// `false`; anything else is `BV-CONFIG-003` naming `setting` in `Details.setting`.
pub(crate) fn parse_bool(setting: &'static str, raw: &str) -> Result<bool, Error> {
    match raw.to_ascii_lowercase().as_str() {
        "1" | "true" | "yes" | "on" => Ok(true),
        "0" | "false" | "no" | "off" | "" => Ok(false),
        _ => Err(invalid_setting_value().with_detail("setting", setting)),
    }
}

/// CFG-004: accepts Go/Vault-style durations (`30s`, `1m30s`, `500ms`, `2h`, units
/// `ns`/`us`/`µs`/`ms`/`s`/`m`/`h`) and a bare integer interpreted as seconds. Anything
/// else is `BV-CONFIG-003` naming `setting`.
pub(crate) fn parse_duration(setting: &'static str, raw: &str) -> Result<Duration, Error> {
    let trimmed = raw.trim();
    let fail = || invalid_setting_value().with_detail("setting", setting);
    if trimmed.is_empty() {
        return Err(fail());
    }
    if trimmed.chars().all(|c| c.is_ascii_digit()) {
        let seconds: u64 = trimmed.parse().map_err(|_| fail())?;
        return Ok(Duration::from_secs(seconds));
    }

    let chars: Vec<char> = trimmed.chars().collect();
    let mut index = 0usize;
    let mut total = Duration::ZERO;
    // `trimmed` is non-empty here (checked above), so this loop always runs at least
    // once; every path through one iteration either returns an error or advances
    // `index`, so falling out of the loop always means at least one segment was parsed.
    while index < chars.len() {
        let number_start = index;
        while index < chars.len() && (chars[index].is_ascii_digit() || chars[index] == '.') {
            index += 1;
        }
        if index == number_start {
            return Err(fail());
        }
        let number: f64 = chars[number_start..index]
            .iter()
            .collect::<String>()
            .parse()
            .map_err(|_| fail())?;

        let unit_start = index;
        while index < chars.len() && !chars[index].is_ascii_digit() && chars[index] != '.' {
            index += 1;
        }
        if index == unit_start {
            return Err(fail());
        }
        let unit: String = chars[unit_start..index].iter().collect();
        let seconds_per_unit = match unit.as_str() {
            "ns" => 1e-9,
            "us" | "\u{b5}s" => 1e-6,
            "ms" => 1e-3,
            "s" => 1.0,
            "m" => 60.0,
            "h" => 3600.0,
            _ => return Err(fail()),
        };
        let segment_seconds = number * seconds_per_unit;
        if segment_seconds < 0.0 || !segment_seconds.is_finite() {
            return Err(fail());
        }
        total += Duration::from_secs_f64(segment_seconds);
    }
    Ok(total)
}

/// Shared by every integer-valued env setting (`MAX_RETRIES`, `RATE_PER_SEC`,
/// `RATE_BURST`): a single parser, an identical `BV-CONFIG-003` + `Details.setting`
/// failure shape (D-M1a-17).
pub(crate) fn parse_int(setting: &'static str, raw: &str) -> Result<i64, Error> {
    raw.trim()
        .parse::<i64>()
        .map_err(|_| invalid_setting_value().with_detail("setting", setting))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn boolean_parser_accepts_every_documented_truthy_and_falsy_form_cfg_003() {
        for truthy in ["1", "true", "TRUE", "yes", "YES", "on", "On"] {
            assert!(parse_bool("Setting", truthy).unwrap(), "{truthy}");
        }
        for falsy in ["0", "false", "FALSE", "no", "off", ""] {
            assert!(!parse_bool("Setting", falsy).unwrap(), "{falsy}");
        }
    }

    #[test]
    fn boolean_parser_rejects_anything_else_cfg_003() {
        let error = parse_bool("TlsSkipVerify", "maybe").expect_err("must reject");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str("TlsSkipVerify".to_owned()))
        );
    }

    #[test]
    fn duration_parser_accepts_go_vault_style_strings_and_bare_integers_cfg_004() {
        assert_eq!(
            parse_duration("Timeout", "30s").unwrap(),
            Duration::from_secs(30)
        );
        assert_eq!(
            parse_duration("Timeout", "1m30s").unwrap(),
            Duration::from_secs(90)
        );
        assert_eq!(
            parse_duration("Timeout", "500ms").unwrap(),
            Duration::from_millis(500)
        );
        assert_eq!(
            parse_duration("Timeout", "2h").unwrap(),
            Duration::from_secs(7200)
        );
        assert_eq!(
            parse_duration("Timeout", "45").unwrap(),
            Duration::from_secs(45)
        );
    }

    #[test]
    fn duration_parser_rejects_anything_else_cfg_004() {
        let error = parse_duration("Timeout", "soon").expect_err("must reject");
        assert_eq!(error.code(), "BV-CONFIG-003");
        let error = parse_duration("Timeout", "").expect_err("must reject empty");
        assert_eq!(error.code(), "BV-CONFIG-003");
        let error = parse_duration("Timeout", "10x").expect_err("must reject unknown unit");
        assert_eq!(error.code(), "BV-CONFIG-003");
    }

    #[test]
    fn duration_parser_rejects_a_decimal_number_with_no_trailing_unit_cfg_004() {
        let error = parse_duration("Timeout", "5.5").expect_err("must reject");
        assert_eq!(error.code(), "BV-CONFIG-003");
    }

    #[test]
    fn duration_parser_rejects_a_segment_that_overflows_to_infinity_cfg_004() {
        let huge_number = "9".repeat(400);
        let error = parse_duration("Timeout", &format!("{huge_number}h")).expect_err("must reject");
        assert_eq!(error.code(), "BV-CONFIG-003");
    }

    #[test]
    fn integer_parser_shares_one_shape_across_every_caller() {
        assert_eq!(parse_int("BASTIONVAULT_RATE_PER_SEC", "8").unwrap(), 8);
        let error = parse_int("BASTIONVAULT_RATE_PER_SEC", "abc").expect_err("must reject");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str(
                "BASTIONVAULT_RATE_PER_SEC".to_owned()
            ))
        );
    }
}
