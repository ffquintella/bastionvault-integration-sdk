//! D-M2-7's two harness instruments, and the secret-leak assertion they exist to make
//! possible.
//!
//! Both were missing in all three languages before M2a. The schema has defined
//! `fixture.clock` since M0 and no driver read it, which is why
//! `auth.token.lookup-self-remaining-ttl` was silently unasserted; and no language had a
//! capturing logger, so **`TST-051` had never been asserted anywhere**.

use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use bastionvault_integration_sdk::{
    Clock, ClientLogger, RequestEvent, RequestObserver, Transport, TransportRequest, TransportResponse,
};
use serde_json::Value;

use super::fixture::Fixture;

/// **Instrument one: the fixture `clock`** (D-M2-7).
///
/// Reads `fixture.clock.start` (an absolute timestamp) and `fixture.clock.advance`
/// (durations applied **between** exchanges), and is injected as the client's [`Clock`].
///
/// A fixture that declares a `clock` a driver ignores must **fail**, not pass, so
/// [`Self::reads`] counts resolutions and the driver refuses a scripted-clock fixture whose
/// clock was never read.
///
/// With no `clock` block the behaviour is a clock frozen at the unix epoch, so no existing
/// fixture changes meaning.
#[derive(Debug)]
pub struct FixtureClock {
    scripted: bool,
    state: Mutex<ClockState>,
    /// The monotonic base. D-M2-2 keeps the two readings distinct, and a *monotonic*
    /// reading must not jump when `clock.advance` moves wall-clock time — it feeds backoff
    /// and attempt-duration arithmetic, not `AUT-014`.
    monotonic_base: Instant,
}

#[derive(Debug)]
struct ClockState {
    now: SystemTime,
    advances: Vec<Duration>,
    consumed: usize,
    reads: u32,
}

impl FixtureClock {
    /// Builds the clock a fixture declares, or the frozen default when it declares none.
    pub fn from_fixture(fixture: &Fixture) -> Result<Self, String> {
        let Some(clock) = fixture.clock.as_ref() else {
            return Ok(Self::frozen(UNIX_EPOCH, Vec::new(), false));
        };
        let start = match clock.start.as_deref() {
            Some(text) => parse_rfc3339_utc(text)
                .ok_or_else(|| format!("fixture {} has an unparsable clock.start {text:?}", fixture.id))?,
            None => UNIX_EPOCH,
        };
        let advances = clock
            .advance
            .iter()
            .map(|text| {
                parse_iso8601_duration(text)
                    .ok_or_else(|| format!("fixture {} has an unparsable clock.advance {text:?}", fixture.id))
            })
            .collect::<Result<Vec<_>, _>>()?;
        Ok(Self::frozen(start, advances, true))
    }

    fn frozen(now: SystemTime, advances: Vec<Duration>, scripted: bool) -> Self {
        Self {
            scripted,
            state: Mutex::new(ClockState {
                now,
                advances,
                consumed: 0,
                reads: 0,
            }),
            monotonic_base: Instant::now(),
        }
    }

    /// Whether the fixture declared a `clock` block.
    pub fn is_scripted(&self) -> bool {
        self.scripted
    }

    /// How many times the code under test asked what wall-clock time it is.
    pub fn reads(&self) -> u32 {
        self.lock().reads
    }

    /// Applies the next `clock.advance` entry. Called once per completed exchange, which is
    /// what "applied between exchanges" means: the first entry takes effect after the first
    /// exchange and before the second.
    pub fn advance_after_exchange(&self) {
        let mut state = self.lock();
        if state.consumed < state.advances.len() {
            let advance = state.advances[state.consumed];
            state.consumed += 1;
            state.now += advance;
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, ClockState> {
        self.state.lock().unwrap_or_else(|poison| poison.into_inner())
    }
}

impl Clock for FixtureClock {
    fn now_monotonic(&self) -> Instant {
        // Deliberately not driven by `clock.advance`: a monotonic reading measures elapsed
        // real time for backoff and attempt durations, and a fixture advancing wall-clock
        // time by an hour must not make one attempt appear to have taken an hour (D-M2-2).
        self.monotonic_base
    }

    fn now_utc(&self) -> SystemTime {
        let mut state = self.lock();
        state.reads += 1;
        state.now
    }

    fn delay(
        &self,
        _duration: Duration,
    ) -> std::pin::Pin<Box<dyn std::future::Future<Output = ()> + Send + 'static>> {
        // D-M1b-7: no test in this crate sleeps in real time.
        Box::pin(std::future::ready(()))
    }
}

/// `2026-09-13T12:00:00Z` → [`SystemTime`], with no date-library dependency.
///
/// `chrono` and `time` are both refused under `CNF-024` (D-M2-2), and this is a fixture
/// vocabulary of one shape — an RFC 3339 instant in UTC, whole seconds — not a general
/// parser.
fn parse_rfc3339_utc(text: &str) -> Option<SystemTime> {
    let text = text.strip_suffix('Z').or_else(|| text.strip_suffix("+00:00"))?;
    let (date, time) = text.split_once('T')?;
    let mut date = date.split('-');
    let year: i64 = date.next()?.parse().ok()?;
    let month: i64 = date.next()?.parse().ok()?;
    let day: i64 = date.next()?.parse().ok()?;
    if date.next().is_some() || !(1..=12).contains(&month) || !(1..=31).contains(&day) {
        return None;
    }
    let mut time = time.split(':');
    let hour: i64 = time.next()?.parse().ok()?;
    let minute: i64 = time.next()?.parse().ok()?;
    // Fractional seconds are truncated: every clock field in this specification is whole
    // seconds (`creation_time`, `creation_ttl`).
    let second: i64 = time
        .next()
        .map(|value| value.split('.').next().unwrap_or(value))
        .unwrap_or("0")
        .parse()
        .ok()?;
    if time.next().is_some() || hour > 23 || minute > 59 || second > 60 {
        return None;
    }
    let days = days_from_civil(year, month, day);
    let seconds = days * 86_400 + hour * 3600 + minute * 60 + second;
    (seconds >= 0).then(|| UNIX_EPOCH + Duration::from_secs(seconds as u64))
}

/// Howard Hinnant's `days_from_civil`: days since 1970-01-01 for a proleptic Gregorian
/// date. Exact integer arithmetic, no table, no leap-year special-casing at the call site.
fn days_from_civil(year: i64, month: i64, day: i64) -> i64 {
    let year = if month <= 2 { year - 1 } else { year };
    let era = if year >= 0 { year } else { year - 399 } / 400;
    let year_of_era = year - era * 400;
    let day_of_year = (153 * (if month > 2 { month - 3 } else { month + 9 }) + 2) / 5 + day - 1;
    let day_of_era = year_of_era * 365 + year_of_era / 4 - year_of_era / 100 + day_of_year;
    era * 146_097 + day_of_era - 719_468
}

/// `PT2S`, `PT1H`, `PT1M30S`, `PT0.5S` → [`Duration`]. The fixtures' own ISO-8601 subset
/// (Appendix C), not a general implementation.
fn parse_iso8601_duration(text: &str) -> Option<Duration> {
    let rest = text.strip_prefix("PT")?;
    if rest.is_empty() {
        return None;
    }
    let mut seconds = 0.0_f64;
    let mut number = String::new();
    for character in rest.chars() {
        match character {
            '0'..='9' | '.' => number.push(character),
            'H' | 'M' | 'S' => {
                let value: f64 = number.parse().ok()?;
                number.clear();
                seconds += value
                    * match character {
                        'H' => 3600.0,
                        'M' => 60.0,
                        _ => 1.0,
                    };
            }
            _ => return None,
        }
    }
    number.is_empty().then(|| Duration::from_secs_f64(seconds))
}

/// **Instrument two, half one** (`TST-051`): captures every line the SDK writes through
/// its `CNF-030` logger seam.
#[derive(Debug, Default)]
pub struct CapturingLogger {
    lines: Mutex<Vec<String>>,
}

impl CapturingLogger {
    /// Everything the SDK logged during the fixture run.
    pub fn lines(&self) -> Vec<String> {
        self.lines.lock().unwrap_or_else(|poison| poison.into_inner()).clone()
    }
}

impl ClientLogger for CapturingLogger {
    fn warn(&self, message: &str) {
        self.lines
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .push(message.to_owned());
    }
}

/// **Instrument two, half two** (`TST-051`, `CFG-080`): captures every observer event.
///
/// Named explicitly by D-M2-7 because this — not the log — is where the leak this
/// milestone was most likely to ship actually surfaces: [`RequestEvent::path`] carries the
/// request path, and `AUT-080`'s `auth/token/renew/{token}` and `Auth.Token.Lookup`'s
/// `auth/token/lookup/{token}` put a live token in it. `ERR-003` already required the path
/// redacted in *errors*; the observer is the second consumer of the same string, and .NET
/// shipped the unredacted form until this instrument caught it.
#[derive(Debug, Default)]
pub struct CapturingObserver {
    events: Mutex<Vec<RequestEvent>>,
}

impl CapturingObserver {
    /// Every attempt the SDK reported during the fixture run.
    pub fn events(&self) -> Vec<RequestEvent> {
        self.events.lock().unwrap_or_else(|poison| poison.into_inner()).clone()
    }
}

impl RequestObserver for CapturingObserver {
    fn on_request_completed(&self, event: &RequestEvent) {
        self.events
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .push(event.clone());
    }
}

/// A transport decorator that advances [`FixtureClock`] once per completed exchange.
///
/// Not a second transport implementation (D-M1b-15): every request is still served by the
/// SDK's own public `FakeTransport`, which is the object real `Client` code runs against.
/// This adds the one thing the SDK's fake cannot know about — where a fixture's exchange
/// boundary is.
#[derive(Debug)]
pub struct ClockAdvancingTransport {
    inner: Arc<dyn Transport>,
    clock: Arc<FixtureClock>,
}

impl ClockAdvancingTransport {
    pub fn new(inner: Arc<dyn Transport>, clock: Arc<FixtureClock>) -> Self {
        Self { inner, clock }
    }
}

impl Transport for ClockAdvancingTransport {
    fn send<'a>(
        &'a self,
        request: TransportRequest,
    ) -> std::pin::Pin<
        Box<dyn std::future::Future<Output = Result<TransportResponse, bastionvault_integration_sdk::Error>> + Send + 'a>,
    > {
        Box::pin(async move {
            let outcome = self.inner.send(request).await;
            self.clock.advance_after_exchange();
            outcome
        })
    }

    fn supports_custom_verbs(&self) -> bool {
        self.inner.supports_custom_verbs()
    }
}

/// `TST-051`'s assertion: no fixture secret literal appears in any captured log line,
/// observer event, error message or rendered error.
///
/// It works **only** because `TST-050` forces distinctive fixture secrets (`s.FAKE…`,
/// `password-fixture`), which is what makes a substring search a valid test rather than a
/// gesture. [`harvest`] is therefore asserted non-empty for every fixture that carries a
/// credential.
pub mod secrets {
    use super::*;

    /// The JSON property names whose value is credential material regardless of its
    /// spelling. The `s.FAKE…`/`password-fixture` conventions catch most of it; these catch
    /// a fixture that spells a secret some other way.
    const SECRET_BEARING_PROPERTIES: [&str; 9] = [
        "token",
        "password",
        "secret_id",
        "machine_token",
        "totp_code",
        "client_token",
        "child_token",
        "user_token",
        "x-bastionvault-token",
    ];

    /// Every secret literal a fixture carries, de-duplicated and ordered.
    pub fn harvest(document: &Value) -> Vec<String> {
        let mut found = Vec::new();
        walk(document, None, &mut found);
        found.sort();
        found.dedup();
        found
    }

    fn walk(value: &Value, property: Option<&str>, found: &mut Vec<String>) {
        match value {
            Value::Object(map) => {
                for (name, child) in map {
                    walk(child, Some(name), found);
                }
            }
            Value::Array(items) => {
                for item in items {
                    walk(item, property, found);
                }
            }
            Value::String(text) if is_secret(text, property) => found.push(text.clone()),
            _ => {}
        }
    }

    fn is_secret(value: &str, property: Option<&str>) -> bool {
        if value.is_empty() {
            return false;
        }
        // TST-050's two conventions.
        if value.contains("s.FAKE") || value.contains("password-fixture") {
            return true;
        }
        property.is_some_and(|name| {
            SECRET_BEARING_PROPERTIES.contains(&name.to_ascii_lowercase().as_str())
        })
    }

    /// Fails when any harvested literal appears in anything the SDK **surfaced**.
    ///
    /// The recorded *requests* are deliberately not searched: the wire legitimately carries
    /// the token, and `TRN-015` is what says where.
    pub fn assert_no_leak(fixture_id: &str, document: &Value, haystacks: &[String]) -> Result<(), String> {
        let mut failures = Vec::new();
        for secret in harvest(document) {
            for haystack in haystacks {
                if haystack.contains(&secret) {
                    failures.push(format!(
                        "secret '{}' appears in a surfaced string: {}",
                        mask(&secret),
                        haystack.replace(&secret, &mask(&secret))
                    ));
                }
            }
        }
        if failures.is_empty() {
            Ok(())
        } else {
            Err(format!(
                "TST-051: fixture '{fixture_id}' leaked secret material. {}",
                failures.join("; ")
            ))
        }
    }

    fn mask(secret: &str) -> String {
        if secret.len() <= 6 {
            "***".to_owned()
        } else {
            format!("{}***", &secret[..6])
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_the_fixture_clock_vocabulary_d_m2_7() {
        // The one `clock.start` the corpus uses today.
        assert_eq!(
            parse_rfc3339_utc("2026-09-13T12:00:00Z")
                .expect("parsable")
                .duration_since(UNIX_EPOCH)
                .expect("after the epoch")
                .as_secs(),
            1_789_300_800
        );
        assert_eq!(parse_rfc3339_utc("1970-01-01T00:00:00Z"), Some(UNIX_EPOCH));
        // A leap day, and the `+00:00` spelling of UTC.
        assert!(parse_rfc3339_utc("2024-02-29T23:59:59+00:00").is_some());
        // Not UTC, not a timestamp, out of range: refused rather than silently defaulted.
        for bad in ["2026-09-13T12:00:00-03:00", "2026-09-13", "not a date", "2026-13-01T00:00:00Z"] {
            assert!(parse_rfc3339_utc(bad).is_none(), "{bad} must not parse");
        }
    }

    #[test]
    fn parses_the_fixture_duration_vocabulary_d_m2_7() {
        assert_eq!(parse_iso8601_duration("PT2S"), Some(Duration::from_secs(2)));
        assert_eq!(parse_iso8601_duration("PT1H"), Some(Duration::from_secs(3600)));
        assert_eq!(parse_iso8601_duration("PT1M30S"), Some(Duration::from_secs(90)));
        assert_eq!(parse_iso8601_duration("PT0.5S"), Some(Duration::from_millis(500)));
        for bad in ["2S", "PT", "PT1X", "PT1H30"] {
            assert!(parse_iso8601_duration(bad).is_none(), "{bad} must not parse");
        }
    }

    #[test]
    fn harvest_finds_both_tst_050_conventions_and_secret_bearing_properties() {
        let document = serde_json::json!({
            "client": { "token": "s.FAKEtoken0000000000000000" },
            "operation": { "args": { "password": "password-fixture", "username": "alice" } },
            "exchanges": [{ "expectRequest": { "headers": { "X-BastionVault-Token": "opaque-value" } } }],
            "title": "not a secret"
        });
        let harvested = secrets::harvest(&document);
        assert!(harvested.contains(&"s.FAKEtoken0000000000000000".to_owned()));
        assert!(harvested.contains(&"password-fixture".to_owned()));
        // Caught by property name rather than by spelling.
        assert!(harvested.contains(&"opaque-value".to_owned()));
        assert!(!harvested.contains(&"alice".to_owned()));
        assert!(!harvested.contains(&"not a secret".to_owned()));
    }

    #[test]
    fn assert_no_leak_masks_the_secret_it_reports_tst_051() {
        let document = serde_json::json!({ "client": { "token": "s.FAKEtoken0000000000000000" } });
        let error = secrets::assert_no_leak(
            "x",
            &document,
            &["GET auth/token/lookup/s.FAKEtoken0000000000000000".to_owned()],
        )
        .expect_err("a leak must be reported");
        assert!(error.contains("s.FAKE***"));
        assert!(!error.contains("s.FAKEtoken0000000000000000"), "the report must not leak it either");
        secrets::assert_no_leak("x", &document, &["GET auth/token/lookup/<redacted>".to_owned()])
            .expect("a redacted path is not a leak");
    }
}
