//! `AUT-001`'s token source and `D-M2-9`'s resolution seam.
//!
//! A [`crate::Client`] holds exactly one [`TokenSource`] (`AUT-001`), reachable as
//! `client.auth().token_source()`; `Client::set_token` and `Auth::token().r#use()`
//! replace it with a [`TokenSourceKind::Static`] one.
//!
//! **The seam is asynchronous (D-M2-9)**, because two of the three variants perform I/O.
//! The executor resolves *through* this type rather than reading a field, and it does so
//! exactly once per pass, above the retry loop — which is `CFG-070`'s "in-flight requests
//! keep the token they started with" (D-M1b-9; the snapshot did not move, only its source
//! changed).
//!
//! **A `Login` resolution is single-flighted (D-M2-11(a))** and **re-arms on failure
//! (D-M2-17)**. Both invariants live here, in the slice that decides them, rather than in
//! the slice that first uses them: the public `Login` factory lands in M2b with its
//! credential types (D-M2-12), so M2a ships the variant's *contract*.

use std::fmt;
use std::future::Future;
use std::pin::Pin;
use std::sync::{Arc, Mutex};

use tokio::sync::OnceCell;

use crate::error::Error;
use crate::secret::SecretString;

/// Which of `AUT-001`'s three variants a [`TokenSource`] is.
///
/// The minimal observable that makes `AUT-001`'s "exactly one source" and "`SetToken`
/// replaces it with `Static`" assertable at all (D-M2-12).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TokenSourceKind {
    /// A token held directly: from configuration, `set_token`, or the token file.
    Static,
    /// A login performed on first use and cached (`AUT-002`). The login call itself is M2b.
    Login,
    /// An application-provided asynchronous function (e.g. secrets from a KMS).
    Callback,
}

/// What a [`TokenCallback`] returns. `'static` so a flight can outlive the caller that
/// started it (see [`TokenSource::resolve`]); implementors clone what they need into the
/// future rather than borrowing from `&self`.
pub type TokenFuture =
    Pin<Box<dyn Future<Output = Result<SecretString, Box<dyn std::error::Error + Send + Sync>>> + Send + 'static>>;

/// The application-supplied half of [`TokenSourceKind::Callback`] (`AUT-001`).
///
/// **Asynchronous** (D-M2-6): the specification's own example for this variant is a KMS
/// lookup (`05-authentication.md:13`), which is I/O-bound, and a synchronous signature
/// would force every real implementation to block a runtime thread inside the request
/// path. A boxed future rather than a native `async fn`, so the trait stays object-safe —
/// the same shape [`crate::Clock::delay`] already uses.
///
/// A callback is **not** de-duplicated: it is the application's own function and the SDK
/// does not get to coalesce its calls on its behalf (D-M2-11(a)).
pub trait TokenCallback: fmt::Debug + Send + Sync + 'static {
    /// Produces the token this source currently stands for.
    fn resolve(&self) -> TokenFuture;
}

/// One flight's outcome, shared by every awaiter of that flight (D-M2-17). The failure is
/// in the cell deliberately: if only successes were cached, the losers of a failed race
/// would each run the login themselves, which is the retry storm D-M2-11(a) exists to
/// prevent.
type FlightOutcome = Result<SecretString, Arc<Error>>;

/// `AUT-001`'s token source.
pub struct TokenSource {
    kind: TokenSourceKind,
    static_token: SecretString,
    callback: Option<Arc<dyn TokenCallback>>,
    login: Option<Arc<dyn TokenCallback>>,
    /// D-M2-11(a)'s named primitive: a [`tokio::sync::OnceCell`] for the flight, plus a
    /// [`Mutex`] for re-arming it. The cell is behind an `Arc` so an awaiter holds the
    /// flight it actually joined, and a re-arm swaps the *pointer* rather than mutating
    /// the cell an awaiter is still reading.
    flight: Mutex<Arc<OnceCell<FlightOutcome>>>,
}

impl fmt::Debug for TokenSource {
    /// Names the variant and nothing else. A `Static` source holds live token material and
    /// a `Callback` source holds application state; neither belongs in a debug rendering
    /// (`CNF-031`/`CNF-032`).
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("TokenSource").field("kind", &self.kind).finish()
    }
}

impl TokenSource {
    fn new(
        kind: TokenSourceKind,
        static_token: SecretString,
        callback: Option<Arc<dyn TokenCallback>>,
        login: Option<Arc<dyn TokenCallback>>,
    ) -> Self {
        Self {
            kind,
            static_token,
            callback,
            login,
            flight: Mutex::new(Arc::new(OnceCell::new())),
        }
    }

    /// A source holding `token` directly (`AUT-001`).
    ///
    /// Spelled with a raw identifier because `static` is a Rust keyword: this is the
    /// canonical name `TokenSource.Static` after the `00-overview.md` idiom mapping, not a
    /// renaming of it.
    pub fn r#static(token: SecretString) -> Self {
        Self::new(TokenSourceKind::Static, token, None, None)
    }

    /// A source that calls `callback` on **every** resolution (`AUT-001`, D-M2-11(a)).
    pub fn callback(callback: Arc<dyn TokenCallback>) -> Self {
        Self::new(TokenSourceKind::Callback, SecretString::new(""), Some(callback), None)
    }

    /// The [`TokenSourceKind::Login`] variant, with its login performed by `login`.
    ///
    /// Internal at M2a: the performer is M2b's login call (`AUT-002`), and the
    /// single-flight machinery around it is M2a's (D-M2-11(a)). A public factory added
    /// later is not a breaking change; a factory that throws today would be a stub, which
    /// D-M1c-25 forbids (D-M2-12).
    ///
    /// **Owned exception, removed by M2b.** Its production caller is M2b's `Login` factory;
    /// M2a has only the concurrency tests D-M2-11(a) and D-M2-17 require, which is
    /// deliberate — the invariants are asserted in the slice that decides them, not in the
    /// slice that first uses them. The bodies are fully covered by those tests, so the
    /// attribute suppresses a reachability warning, never a coverage gap.
    #[allow(dead_code)]
    pub(crate) fn login_with(login: Arc<dyn TokenCallback>) -> Self {
        Self::new(TokenSourceKind::Login, SecretString::new(""), None, Some(login))
    }

    /// Which variant this is (`AUT-001`, D-M2-12).
    pub fn kind(&self) -> TokenSourceKind {
        self.kind
    }

    /// D-M2-9's seam: the token this source currently stands for, resolving it if that
    /// takes I/O.
    ///
    /// `None` means the source resolved to *no token*, which is a different state from a
    /// `Login` source that has not resolved yet — the distinction `ERR-022`'s preflight
    /// turns on, and the one whose absence blocked revision 1 of this record. The
    /// preflight itself is M2b's.
    ///
    /// A [`TokenSourceKind::Login`] resolution is single-flighted (D-M2-11(a)): N
    /// concurrent first-use resolutions await **one** login and receive its token. A
    /// flight that *fails* is not cached (D-M2-17) — its awaiters all observe its failure
    /// and none of them retries inside its own call, and the next resolution after it
    /// attempts again.
    pub async fn resolve(&self) -> Result<Option<SecretString>, Error> {
        if let Some(callback) = &self.callback {
            return invoke(Arc::clone(callback)).await.map(Some);
        }

        let Some(login) = &self.login else {
            // A `Static` resolve performs no I/O, so there is nothing to fail and nothing
            // to cancel.
            return Ok(Some(self.static_token.clone()));
        };

        let joined = self.current_flight();
        let login = Arc::clone(login);
        let outcome = joined
            .get_or_init(|| async move {
                // Spawned, so the flight is owned by the runtime rather than by whichever
                // caller won the race to start it. A Rust caller cancels by dropping its
                // future; without the spawn, the winner dropping would abandon a login the
                // other awaiters were sharing, and one of them would start a second one —
                // the thundering herd at N = 1. This is the Rust equivalent of .NET
                // running the login under `CancellationToken.None` while each caller waits
                // on its own token.
                match tokio::spawn(invoke(login)).await {
                    Ok(result) => result.map_err(Arc::new),
                    Err(join_error) => Err(Arc::new(classify_join_failure(&join_error))),
                }
            })
            .await;

        match outcome {
            Ok(token) => Ok(Some(token.clone())),
            Err(error) => {
                // D-M2-17. The re-arm is what stops one transient login failure at startup
                // making the client permanently unusable: `Lazy`/`OnceCell` caches the
                // failed outcome, and without a re-arm every later caller would get a stale
                // error with the wrong `attempts` and no observer event. Conditional on
                // *this* flight still being the live one, so the N awaiters of one failed
                // flight re-arm it once between them and a later resolution's fresh flight
                // is never clobbered by a straggler — .NET's `CompareExchange`, spelled with
                // a pointer comparison under the mutex D-M2-11(a) names.
                self.rearm_if_current(&joined);
                Err(error.duplicate())
            }
        }
    }

    /// Discards a cached [`TokenSourceKind::Login`] result so the next [`Self::resolve`]
    /// logs in again, re-arming the single-flight cell rather than clearing it
    /// (D-M2-11(a)). A no-op on the other two variants: `Static` has nothing to re-resolve
    /// and `Callback` already resolves every time.
    ///
    /// **Owned exception, removed by M2b**, whose `AUT-003` replay and `AUT-093` relogin are
    /// its production callers. See [`Self::login_with`].
    #[allow(dead_code)]
    pub(crate) fn invalidate(&self) {
        if self.login.is_some() {
            *self.lock_flight() = Arc::new(OnceCell::new());
        }
    }

    fn current_flight(&self) -> Arc<OnceCell<FlightOutcome>> {
        Arc::clone(&self.lock_flight())
    }

    fn rearm_if_current(&self, observed: &Arc<OnceCell<FlightOutcome>>) {
        let mut flight = self.lock_flight();
        if Arc::ptr_eq(&flight, observed) {
            *flight = Arc::new(OnceCell::new());
        }
    }

    /// A poisoned mutex is recovered rather than propagated, exactly as the token cell and
    /// the rate-gate state already do: the guarded value is a cell pointer, so a panic
    /// while holding the lock cannot have left it in a state a later reader must not see.
    fn lock_flight(&self) -> std::sync::MutexGuard<'_, Arc<OnceCell<FlightOutcome>>> {
        self.flight.lock().unwrap_or_else(|poison| poison.into_inner())
    }
}

/// Calls one performer and turns whatever it produced into this crate's `Error`, in the
/// order D-M2-18 item 1 pins.
async fn invoke(performer: Arc<dyn TokenCallback>) -> Result<SecretString, Error> {
    performer.resolve().await.map_err(classify_resolution_failure)
}

/// **The arm order is load-bearing, and Rust has no exception filters** (D-M2-18 item 1).
///
/// .NET reads `catch (OperationCanceledException)` *before*
/// `catch (Exception) when (exception is not BastionVaultException)`. Inverted, a
/// cancellation reports `BV-AUTH-017` instead of `BV-TRANSPORT-005`, and .NET's filter
/// ordering is the only thing keeping them apart. Rust has no filters, so the order is
/// written out:
///
/// 1. **Cancellation first** → `BV-TRANSPORT-005`, `attempts = 0`. No request was issued.
/// 2. Already a BastionVault error → **passed through unwrapped**, so M2b's `Login`
///    source keeps its recogniser codes: `AUT-003`'s replay keys on `BV-AUTHZ-001`
///    specifically and wrapping would break it outright (D-M2-18 item 3).
/// 3. Anything else → `BV-AUTH-017 TokenSourceFailed`, `attempts = 0`, the source's own
///    error preserved as the cause. `BV-AUTH-001 NoToken` is the closest existing code and
///    is wrong: its message is "No token is *configured*", and here a token *is*
///    configured and its resolution failed (D-M2-16).
fn classify_resolution_failure(cause: Box<dyn std::error::Error + Send + Sync>) -> Error {
    if is_cancellation(cause.as_ref()) {
        return crate::error::catalog_errors::transport_cancelled();
    }
    match cause.downcast::<Error>() {
        Ok(already_coded) => *already_coded,
        Err(other) => crate::error::catalog_errors::auth_token_source_failed().with_cause(BoxedCause(other)),
    }
}

/// A spawned flight that did not finish: a cancelled task is a cancellation
/// (`BV-TRANSPORT-005`), and a panicking performer is a source failure
/// (`BV-AUTH-017`) — the same two arms, in the same order, as
/// [`classify_resolution_failure`].
fn classify_join_failure(join_error: &tokio::task::JoinError) -> Error {
    if join_error.is_cancelled() {
        crate::error::catalog_errors::transport_cancelled()
    } else {
        crate::error::catalog_errors::auth_token_source_failed()
            .with_cause(BoxedCause(Box::new(PerformerPanicked)))
    }
}

/// Whether `cause` is the application saying "cancelled".
///
/// Rust has no ambient cancellation token: a caller cancels by dropping its future, and a
/// source that wants to *report* a cancellation does so with one of the two standard
/// carriers below. Both are recognised, so a `TokenCallback` written against either
/// convention reaches arm 1 rather than arm 3.
fn is_cancellation(cause: &(dyn std::error::Error + 'static)) -> bool {
    if let Some(error) = cause.downcast_ref::<std::io::Error>() {
        return error.kind() == std::io::ErrorKind::Interrupted;
    }
    cause.downcast_ref::<tokio::task::JoinError>().is_some_and(tokio::task::JoinError::is_cancelled)
}

/// The application's own error, kept whole as the `Error`'s cause (D-M2-16: "the source's
/// own exception is the cause and is preserved as such").
#[derive(Debug)]
struct BoxedCause(Box<dyn std::error::Error + Send + Sync>);

impl fmt::Display for BoxedCause {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        self.0.fmt(f)
    }
}

impl std::error::Error for BoxedCause {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        self.0.source()
    }
}

#[derive(Debug)]
struct PerformerPanicked;

impl fmt::Display for PerformerPanicked {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("the token source's login performer panicked")
    }
}

impl std::error::Error for PerformerPanicked {}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

    /// A performer that counts its calls and answers as told, so every assertion below is
    /// about how many logins happened, not about what one returned.
    #[derive(Debug)]
    struct ScriptedLogin {
        calls: Arc<AtomicU32>,
        answer: Answer,
        /// When set, the performer holds until the test releases it. This is what makes the
        /// concurrency assertions **deterministic**: without it, an awaiter scheduled after
        /// the flight already resolved is a *subsequent* resolution, which D-M2-17 correctly
        /// allows to re-attempt — so the test would be asserting scheduler luck rather than
        /// single-flight.
        gate: Option<Arc<AtomicBool>>,
    }

    #[derive(Debug, Clone, Copy)]
    enum Answer {
        Token,
        PlainFailure,
        CodedFailure,
        Cancelled,
        Panic,
        FailFirstThenSucceed,
    }

    impl TokenCallback for ScriptedLogin {
        fn resolve(&self) -> TokenFuture {
            let call = self.calls.fetch_add(1, Ordering::SeqCst) + 1;
            let answer = self.answer;
            let gate = self.gate.clone();
            Box::pin(async move {
                // Yields once, so concurrent callers genuinely overlap: without a suspension
                // point the first caller would run to completion before the second is polled
                // and the single-flight assertion would pass vacuously.
                tokio::task::yield_now().await;
                if let Some(gate) = gate {
                    while !gate.load(Ordering::SeqCst) {
                        tokio::task::yield_now().await;
                    }
                }
                match answer {
                    Answer::Token => Ok(SecretString::new(format!("s.FAKElogin{call}"))),
                    Answer::PlainFailure => {
                        Err(Box::new(std::io::Error::other("kms unreachable")) as Box<dyn std::error::Error + Send + Sync>)
                    }
                    Answer::CodedFailure => Err(Box::new(
                        crate::error::catalog_errors::input_invalid_argument(),
                    ) as Box<dyn std::error::Error + Send + Sync>),
                    Answer::Cancelled => Err(Box::new(std::io::Error::new(
                        std::io::ErrorKind::Interrupted,
                        "cancelled",
                    )) as Box<dyn std::error::Error + Send + Sync>),
                    Answer::Panic => panic!("performer panicked on purpose"),
                    Answer::FailFirstThenSucceed if call == 1 => {
                        Err(Box::new(std::io::Error::other("transient")) as Box<dyn std::error::Error + Send + Sync>)
                    }
                    Answer::FailFirstThenSucceed => Ok(SecretString::new(format!("s.FAKElogin{call}"))),
                }
            })
        }
    }

    fn login_source(answer: Answer) -> (TokenSource, Arc<AtomicU32>) {
        let calls = Arc::new(AtomicU32::new(0));
        let source = TokenSource::login_with(Arc::new(ScriptedLogin {
            calls: Arc::clone(&calls),
            answer,
            gate: None,
        }));
        (source, calls)
    }

    /// A `Login` source whose performer holds until the returned gate is opened, so a test
    /// can prove that all N awaiters joined **one** in-flight login rather than N that
    /// happened to run one after another.
    fn gated_login_source(answer: Answer) -> (Arc<TokenSource>, Arc<AtomicU32>, Arc<AtomicBool>) {
        let calls = Arc::new(AtomicU32::new(0));
        let gate = Arc::new(AtomicBool::new(false));
        let source = Arc::new(TokenSource::login_with(Arc::new(ScriptedLogin {
            calls: Arc::clone(&calls),
            answer,
            gate: Some(Arc::clone(&gate)),
        })));
        (source, calls, gate)
    }

    /// Spawns `count` concurrent resolutions, all of which are guaranteed to have joined the
    /// same flight before it completes, then releases the flight and collects every outcome.
    async fn join_one_flight(
        source: &Arc<TokenSource>,
        calls: &Arc<AtomicU32>,
        gate: &Arc<AtomicBool>,
        count: usize,
    ) -> Vec<Result<Option<SecretString>, Error>> {
        // `arrived` is incremented immediately before `resolve()`, and `resolve()` reaches
        // the flight cell **synchronously** on its first poll (there is no await before
        // `current_flight()`). So `arrived == count` means all `count` awaiters have already
        // joined *this* flight, which is what has to be true before the flight is allowed to
        // finish: otherwise a straggler would join the re-armed cell instead and start a
        // second login, and the test would be measuring scheduling rather than single-flight.
        let arrived = Arc::new(AtomicU32::new(0));
        let mut tasks = Vec::with_capacity(count);
        for _ in 0..count {
            let source = Arc::clone(source);
            let arrived = Arc::clone(&arrived);
            tasks.push(tokio::spawn(async move {
                arrived.fetch_add(1, Ordering::SeqCst);
                source.resolve().await
            }));
        }
        while arrived.load(Ordering::SeqCst) < count as u32 || calls.load(Ordering::SeqCst) == 0 {
            tokio::task::yield_now().await;
        }
        gate.store(true, Ordering::SeqCst);
        let mut outcomes = Vec::with_capacity(count);
        for task in tasks {
            outcomes.push(task.await.expect("no awaiter may panic"));
        }
        outcomes
    }

    #[tokio::test]
    async fn a_static_source_resolves_to_its_own_token_aut_001() {
        let source = TokenSource::r#static(SecretString::new("s.FAKEstatic"));
        assert_eq!(source.kind(), TokenSourceKind::Static);
        let resolved = source.resolve().await.expect("a static resolve cannot fail");
        assert_eq!(resolved.map(|token| token.reveal().to_owned()), Some("s.FAKEstatic".to_owned()));
    }

    #[tokio::test]
    async fn a_callback_source_is_invoked_on_every_resolution_and_is_never_deduplicated_aut_001_d_m2_11() {
        let calls = Arc::new(AtomicU32::new(0));
        let source = TokenSource::callback(Arc::new(ScriptedLogin {
            calls: Arc::clone(&calls),
            answer: Answer::Token,
            gate: None,
        }));
        assert_eq!(source.kind(), TokenSourceKind::Callback);
        for _ in 0..3 {
            source.resolve().await.expect("must resolve");
        }
        // D-M2-11(a): a callback is the application's own function, so the SDK does not
        // coalesce its calls on its behalf. Three resolutions, three calls.
        assert_eq!(calls.load(Ordering::SeqCst), 3);
    }

    #[tokio::test(flavor = "multi_thread", worker_threads = 4)]
    async fn eight_concurrent_first_use_resolutions_await_one_login_d_m2_11_cfg_070() {
        let (source, calls, gate) = gated_login_source(Answer::Token);
        let outcomes = join_one_flight(&source, &calls, &gate, 8).await;
        // The thundering-herd login D-M2-11(a) exists to prevent: eight tokens issued,
        // seven orphaned, against the one path the server's DoS guard rate-limits, so a
        // later one returns `rate_limited: …` → BV-RATE-001, non-retryable under ERR-006,
        // and the application's first request fails at startup.
        assert_eq!(calls.load(Ordering::SeqCst), 1, "concurrent first-use resolutions must share one login");
        for outcome in outcomes {
            let token = outcome.expect("the shared flight succeeded").expect("a login yields a token");
            assert_eq!(token.reveal(), "s.FAKElogin1", "every awaiter must receive the one flight's token");
        }
    }

    #[tokio::test(flavor = "multi_thread", worker_threads = 4)]
    async fn the_awaiters_of_one_faulted_flight_all_observe_it_and_none_retries_d_m2_17() {
        let (source, calls, gate) = gated_login_source(Answer::PlainFailure);
        let outcomes = join_one_flight(&source, &calls, &gate, 8).await;
        // "The awaiters of one flight all observe that flight's failure and none retries
        // inside its own call" — so exactly one login ran, not eight. There is no retry
        // storm, and (see the re-arm test) no permanent poisoning either.
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        for outcome in outcomes {
            let error = outcome.expect_err("the flight failed");
            assert_eq!(error.code(), "BV-AUTH-017");
            assert_eq!(error.attempts(), 0);
        }
    }

    #[tokio::test]
    async fn a_faulted_flight_re_arms_so_a_subsequent_resolution_re_attempts_d_m2_17() {
        let (source, calls) = login_source(Answer::FailFirstThenSucceed);
        let first = source.resolve().await.expect_err("the first flight fails");
        assert_eq!(first.code(), "BV-AUTH-017");
        // The whole point of D-M2-17: one transient login failure at startup must not make
        // the client permanently unusable.
        let second = source
            .resolve()
            .await
            .expect("the re-armed flight must attempt again")
            .expect("and succeed");
        assert_eq!(second.reveal(), "s.FAKElogin2");
        assert_eq!(calls.load(Ordering::SeqCst), 2);
        // And the success is now cached: a third resolution adds no login.
        source.resolve().await.expect("cached").expect("cached");
        assert_eq!(calls.load(Ordering::SeqCst), 2);
    }

    #[tokio::test]
    async fn a_performer_that_fails_immediately_does_not_poison_the_cell_permanently_d_m2_17() {
        // .NET's synchronous-throw hazard: `LazyThreadSafetyMode.ExecutionAndPublication`
        // caches a *factory* exception and rethrows it forever, which not even a re-arm in
        // the resolve path can reach. Rust's equivalent is a performer whose future is
        // already `Err` when first polled. Asserted rather than assumed, because the .NET
        // pass needed a specific fix here.
        let (source, calls) = login_source(Answer::PlainFailure);
        for _ in 0..3 {
            let error = source.resolve().await.expect_err("each resolution fails");
            assert_eq!(error.code(), "BV-AUTH-017");
        }
        assert_eq!(calls.load(Ordering::SeqCst), 3, "each sequential resolution must re-attempt");
    }

    #[tokio::test]
    async fn a_cancelled_resolution_reports_transport_005_not_auth_017_d_m2_18_1() {
        // The arm order. Inverted, this is BV-AUTH-017 and a caller cannot tell "I
        // cancelled" from "the source broke".
        let (source, _) = login_source(Answer::Cancelled);
        let error = source.resolve().await.expect_err("cancellation is an error");
        assert_eq!(error.code(), "BV-TRANSPORT-005");
        assert_eq!(error.attempts(), 0);
    }

    #[tokio::test]
    async fn a_source_failure_reports_auth_017_with_the_cause_preserved_d_m2_16() {
        let (source, _) = login_source(Answer::PlainFailure);
        let error = source.resolve().await.expect_err("a broken source is an error");
        assert_eq!(error.code(), "BV-AUTH-017");
        assert!(!error.retryable(), "ERR-006's retryable set is closed and does not list BV-AUTH-017");
        assert_eq!(error.attempts(), 0, "no request was issued");
        let cause = std::error::Error::source(&error).expect("the source's own error is the cause");
        assert!(cause.to_string().contains("kms unreachable"));
    }

    #[tokio::test]
    async fn an_error_that_already_carries_a_code_is_not_wrapped_d_m2_18_3() {
        // M2b's `Login` source keeps its recogniser codes: AUT-003's replay keys on
        // BV-AUTHZ-001 specifically, and wrapping would break it outright.
        let (source, _) = login_source(Answer::CodedFailure);
        let error = source.resolve().await.expect_err("must fail");
        assert_eq!(error.code(), "BV-INPUT-001");
    }

    #[tokio::test]
    async fn a_panicking_performer_is_a_coded_error_not_an_escaping_panic_err_020() {
        let (source, _) = login_source(Answer::Panic);
        let error = source.resolve().await.expect_err("must fail");
        assert_eq!(error.code(), "BV-AUTH-017");
        assert!(
            std::error::Error::source(&error)
                .expect("a cause")
                .to_string()
                .contains("panicked")
        );
    }

    #[tokio::test]
    async fn invalidate_re_arms_a_login_source_and_is_a_no_op_elsewhere_d_m2_11() {
        let (source, calls) = login_source(Answer::Token);
        source.resolve().await.expect("first").expect("token");
        source.resolve().await.expect("cached").expect("token");
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        source.invalidate();
        source.resolve().await.expect("re-armed").expect("token");
        assert_eq!(calls.load(Ordering::SeqCst), 2);

        // No-ops: `Static` has nothing to re-resolve, `Callback` already resolves every time.
        let r#static = TokenSource::r#static(SecretString::new("s.FAKEstatic"));
        r#static.invalidate();
        assert_eq!(r#static.kind(), TokenSourceKind::Static);
        let callback_calls = Arc::new(AtomicU32::new(0));
        let callback = TokenSource::callback(Arc::new(ScriptedLogin {
            calls: Arc::clone(&callback_calls),
            answer: Answer::Token,
            gate: None,
        }));
        callback.invalidate();
        callback.resolve().await.expect("must resolve").expect("token");
        assert_eq!(callback_calls.load(Ordering::SeqCst), 1);
    }

    #[test]
    fn the_debug_form_names_the_variant_and_never_the_token_d_m2_3() {
        let source = TokenSource::r#static(SecretString::new("s.FAKEsecret-token"));
        let rendered = format!("{source:?}");
        assert!(rendered.contains("Static"));
        assert!(!rendered.contains("FAKEsecret-token"), "{rendered}");
    }
}
