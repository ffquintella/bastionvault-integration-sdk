//! Step 4 of the ERR-020 mapping algorithm: the ordered Appendix B §2 rule list, applied
//! to the normalised server message *before* the status table (D-M1c-3).
//!
//! The rules themselves are generated
//! ([`crate::generated::error_catalog_data::RULES`]); this module is only the matcher and
//! the D-M1c-4 capture interpreter. It is deliberately regex-free — the crate carries no
//! regex dependency and the four capture shapes are a closed set, so the three SDKs
//! implement the same shapes rather than three regex dialects.
//!
//! Recognition is **internal**. It implements ERR-020 and is not a supported extension
//! point; making it public would freeze the rule representation (D-M1c-7).

use std::collections::BTreeMap;

use crate::error::DetailValue;
use crate::generated::error_catalog_data::{CAPTURES, DetailsCaptureRow, RULES, RecognitionRow};

/// The outcome of step 4: a code, plus whatever ERR-035 could capture.
pub(crate) struct Recognised {
    pub(crate) code: &'static str,
    pub(crate) details: BTreeMap<String, DetailValue>,
}

/// D-M1c-3's matching-time normalisation: trim; strip a single trailing `.`; strip a
/// trailing `(retry after Ns)`; lower-case. The *original* server message is never
/// modified — it stays on [`crate::Error::server_message`] exactly as the server sent it.
///
/// The lower-casing is ASCII-only on purpose: every Appendix B literal is ASCII, and an
/// ASCII fold preserves byte length, which is what makes a `prefix` rule's literal length
/// a valid byte offset into the original message for the D-M1c-4 captures below.
pub(crate) fn normalise(message: &str) -> String {
    let text = message.trim();
    let text = text.strip_suffix('.').unwrap_or(text);
    let (text, _) = split_retry_after_suffix(text);
    text.trim().to_ascii_lowercase()
}

/// Splits a trailing `(retry after Ns)` suffix off `text`, returning the remainder and
/// `N`. Case-insensitive, any integer `N`, optional surrounding whitespace (D-M1c-3).
fn split_retry_after_suffix(text: &str) -> (&str, Option<i64>) {
    let trimmed = text.trim_end();
    let Some(before_close) = trimmed.strip_suffix(')') else {
        return (text, None);
    };
    let Some(open) = before_close.rfind('(') else {
        return (text, None);
    };
    let inner = before_close[open + 1..].trim_start();
    let Some(inner) = strip_prefix_ignore_ascii_case(inner, "retry after") else {
        return (text, None);
    };
    // `\s+` between the literal and the number: without at least one space this is some
    // other parenthesised clause, not the suffix.
    if !inner.starts_with(char::is_whitespace) {
        return (text, None);
    }
    let inner = inner.trim_start();
    let digits = inner.len() - inner.trim_start_matches(|c: char| c.is_ascii_digit()).len();
    if digits == 0 {
        return (text, None);
    }
    let seconds = inner[..digits].parse::<i64>().ok();
    let after = inner[digits..].trim_start();
    let Some(after) = strip_prefix_ignore_ascii_case(after, "s") else {
        return (text, None);
    };
    if !after.trim().is_empty() {
        return (text, None);
    }
    (before_close[..open].trim_end(), seconds)
}

fn strip_prefix_ignore_ascii_case<'a>(text: &'a str, prefix: &str) -> Option<&'a str> {
    let candidate = text.get(..prefix.len())?;
    candidate
        .eq_ignore_ascii_case(prefix)
        .then(|| &text[prefix.len()..])
}

/// The first rule in Appendix B §2 table order whose text and guards all hold, or `None`
/// to fall through to the D-M1b-4 status table unchanged (D-M1c-3).
pub(crate) fn recognise(server_message: Option<&str>, status: u16, path: &str) -> Option<Recognised> {
    let server_message = server_message?;
    let original = server_message.trim();
    let normalised = normalise(server_message);
    if normalised.is_empty() {
        return None;
    }

    let rule = RULES
        .iter()
        .find(|rule| matches(rule, &normalised, status, path))?;
    let details = match rule.7 {
        Some(index) => capture(&CAPTURES[index], original),
        None => BTreeMap::new(),
    };
    Some(Recognised {
        code: rule.6,
        details,
    })
}

fn matches(rule: &RecognitionRow, normalised: &str, status: u16, path: &str) -> bool {
    let (kind, text, contains_all, exact_status, status_class, path_scope, _, _) = *rule;

    if let Some(scope) = path_scope
        && !path.to_ascii_lowercase().contains(&scope.to_ascii_lowercase())
    {
        return false;
    }
    if let Some(expected) = exact_status
        && status != expected
    {
        return false;
    }
    if let Some(expected) = status_class
        && status / 100 != expected
    {
        return false;
    }

    // The rule text keeps Appendix B's own spelling, including the load-bearing trailing
    // space in `machine `/`key `/`version `/`role `/`ip ` (D-M1c-14 item 2); only
    // `prefix` can observe it, so `exact` and `contains` compare the trimmed literal.
    let text_matches = match kind {
        "exact" => normalised == text.trim(),
        "prefix" => normalised.starts_with(text),
        _ => normalised.contains(text.trim()),
    };
    if !text_matches {
        return false;
    }

    contains_all
        .iter()
        .all(|required| normalised.contains(required.trim()))
}

/// Applies one D-M1c-4 capture to the original (case-preserving, trimmed) server message.
///
/// A capture that fails is **not** an error: the code is still assigned and the key is
/// simply absent, because recognition must never be more fragile than the code it
/// produces (D-M1c-4).
fn capture(capture: &DetailsCaptureRow, original: &str) -> BTreeMap<String, DetailValue> {
    let (kind, keys, prefix, suffix) = *capture;
    let mut details = BTreeMap::new();
    match kind {
        "token_after_prefix" => {
            // Every D-M1c-4 capture attaches to a `prefix` rule — the generator hard-fails
            // otherwise (D-M1c-14 item 4) — so the literal sits at offset 0 of the trimmed
            // message and `prefix.len()` is in bounds and on a character boundary.
            if let Some(token) = original[prefix.len()..]
                .split_whitespace()
                .next()
                .and_then(clean)
            {
                details.insert(keys[0].to_owned(), DetailValue::Str(token));
            }
        }
        "retry_after_secs" => {
            let trimmed = trim_trailing_stop(original);
            if let (_, Some(seconds)) = split_retry_after_suffix(trimmed) {
                details.insert(keys[0].to_owned(), DetailValue::Int(seconds));
            }
        }
        "two_ints" => {
            let numbers = integers(original);
            if numbers.len() >= 2 {
                details.insert(keys[0].to_owned(), DetailValue::Int(numbers[0]));
                details.insert(keys[1].to_owned(), DetailValue::Int(numbers[1]));
            }
        }
        // `token_list_between`: the comma/space separated tokens between the capture's
        // prefix and its suffix. A missing suffix yields an empty span and therefore no
        // key, which D-M1c-4 explicitly allows.
        _ => {
            let items = token_list_between(original, prefix.len(), suffix);
            if !items.is_empty() {
                details.insert(keys[0].to_owned(), DetailValue::List(items));
            }
        }
    }
    details
}

fn trim_trailing_stop(value: &str) -> &str {
    let trimmed = value.trim();
    trimmed.strip_suffix('.').map_or(trimmed, str::trim_end)
}

fn integers(original: &str) -> Vec<i64> {
    let mut numbers = Vec::new();
    for run in original.split(|c: char| !c.is_ascii_digit()) {
        if !run.is_empty()
            && let Ok(value) = run.parse::<i64>()
        {
            numbers.push(value);
        }
    }
    numbers
}

fn token_list_between(original: &str, start: usize, suffix: &str) -> Vec<String> {
    let rest = &original[start..];
    let end = rest
        .to_ascii_lowercase()
        .find(&suffix.to_ascii_lowercase())
        .unwrap_or(0);
    rest[..end]
        .split([',', ' ', '\t'])
        .filter_map(clean)
        .filter(|item| !item.is_empty())
        .collect()
}

/// Strips the punctuation Appendix B's prose wraps a captured value in, so `app-admin:`
/// and `` `app-admin` `` both yield `app-admin`.
fn clean(value: &str) -> Option<String> {
    let trimmed = value
        .trim()
        .trim_matches(['`', '"', '\''])
        .trim_end_matches(['.', ',', ';', ':']);
    (!trimmed.is_empty()).then(|| trimmed.to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalisation_trims_the_stop_the_retry_suffix_and_the_case() {
        assert_eq!(normalise("  Permission denied.  "), "permission denied");
        assert_eq!(normalise("PERMISSION DENIED"), "permission denied");
        assert_eq!(
            normalise("Account temporarily locked (retry after 300s)."),
            "account temporarily locked"
        );
        assert_eq!(
            normalise("Account temporarily locked ( RETRY AFTER 300 S )"),
            "account temporarily locked"
        );
    }

    #[test]
    fn a_parenthesised_clause_that_is_not_the_retry_suffix_is_left_alone() {
        for message in [
            "locked (retry soon)",
            "locked (retry afterNs)",
            "locked (retry after s)",
            "locked (retry after 5 minutes)",
            "locked retry after 5s",
            "locked (retry after 5s) trailing",
        ] {
            assert_eq!(split_retry_after_suffix(message).1, None, "{message}");
        }
        assert_eq!(split_retry_after_suffix("locked (retry after 5s)").1, Some(5));
    }

    #[test]
    fn an_over_long_retry_value_still_strips_the_suffix_but_captures_nothing() {
        let (rest, seconds) = split_retry_after_suffix("locked (retry after 99999999999999999999s)");
        assert_eq!(rest, "locked");
        assert_eq!(seconds, None);
    }

    #[test]
    fn a_blank_or_absent_message_never_recognises() {
        assert!(recognise(None, 400, "secret/data/x").is_none());
        assert!(recognise(Some(""), 400, "secret/data/x").is_none());
        assert!(recognise(Some("   "), 400, "secret/data/x").is_none());
        assert!(recognise(Some("."), 400, "secret/data/x").is_none());
        assert!(recognise(Some("nothing the catalogue knows"), 400, "secret/data/x").is_none());
    }

    #[test]
    fn clean_strips_quoting_and_trailing_punctuation() {
        assert_eq!(clean("`app-admin`"), Some("app-admin".to_owned()));
        assert_eq!(clean("app-admin:"), Some("app-admin".to_owned()));
        assert_eq!(clean("  "), None);
        assert_eq!(clean("::"), None);
    }

    #[test]
    fn integers_reads_every_digit_run_in_order() {
        assert_eq!(integers("batch has 200 operations, exceeds max 128"), vec![200, 128]);
        assert_eq!(integers("no numbers here"), Vec::<i64>::new());
    }
}
