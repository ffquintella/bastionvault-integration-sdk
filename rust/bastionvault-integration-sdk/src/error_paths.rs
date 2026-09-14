//! ERR-003's path redaction and ERR-002's one-line collapse, applied once where the
//! error is built so no surface can miss them (D-M1c-14 item 6).

/// The four token-bearing route shapes ERR-003 names.
const TOKEN_SEGMENTS: [&str; 4] = ["lookup", "renew", "revoke", "revoke-orphan"];

/// Replaces the segment that follows `lookup`, `renew`, `revoke` or `revoke-orphan`
/// with `<redacted>` (ERR-003).
///
/// Applied to [`crate::Error::path`] at construction, so the one-line form and any hint
/// that interpolates the path are redacted by the same rule rather than by two that can
/// drift.
pub(crate) fn redact(path: &str) -> String {
    if !path.contains('/') {
        return path.to_owned();
    }

    let mut segments = path.split('/').map(str::to_owned).collect::<Vec<_>>();
    for index in 0..segments.len().saturating_sub(1) {
        if segments[index + 1].is_empty() {
            continue;
        }
        if TOKEN_SEGMENTS
            .iter()
            .any(|marker| segments[index].eq_ignore_ascii_case(marker))
        {
            segments[index + 1] = "<redacted>".to_owned();
        }
    }
    segments.join("/")
}

/// Collapses every newline and control character to a single space (ERR-002's one-line
/// rule). Nothing in the catalogue carries one, but a server message is
/// attacker-influenced input and must not be able to forge a second log line.
pub(crate) fn one_line(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    let mut last_was_space = false;
    for character in value.chars() {
        if character == '\r' || character == '\n' || character.is_control() {
            if !last_was_space {
                out.push(' ');
                last_was_space = true;
            }
            continue;
        }
        out.push(character);
        last_was_space = character == ' ';
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn redaction_replaces_only_the_err_003_token_segments() {
        for marker in ["lookup", "renew", "revoke", "revoke-orphan"] {
            assert_eq!(
                redact(&format!("auth/token/{marker}/abc123")),
                format!("auth/token/{marker}/<redacted>")
            );
        }
        assert_eq!(redact("secret/data/app"), "secret/data/app");
        assert_eq!(redact("lookup"), "lookup");
        assert_eq!(redact("auth/token/lookup/"), "auth/token/lookup/");
        assert_eq!(redact(""), "");
        // The marker match is case-insensitive, as ERR-003's route names are.
        assert_eq!(redact("auth/token/LOOKUP/abc"), "auth/token/LOOKUP/<redacted>");
    }

    #[test]
    fn one_line_collapses_every_break_to_a_single_space() {
        assert_eq!(one_line("a\nb\r\nc"), "a b c");
        assert_eq!(one_line("a\u{7}b"), "a b");
        assert_eq!(one_line("plain text"), "plain text");
        assert_eq!(one_line("trailing \n"), "trailing ");
    }
}
