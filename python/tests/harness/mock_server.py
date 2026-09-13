"""Runtime-generated HTTPS mock server for the M0 contract tests."""

from __future__ import annotations

import ipaddress
import json
import os
import socket
import ssl
import tempfile
import threading
import time
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, ClassVar, cast

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509 import ObjectIdentifier
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

_TEMP_DIRECTORY_LOCK = threading.Lock()


def _temporary_directory(parent: Path) -> tempfile.TemporaryDirectory[str]:
    """Create a writable TemporaryDirectory on the managed Windows host."""
    if os.name != "nt":
        return tempfile.TemporaryDirectory(prefix="bastionvault-mock-", dir=str(parent))
    with _TEMP_DIRECTORY_LOCK:
        tempfile_module = cast(Any, tempfile)
        original_mkdir = tempfile_module._os.mkdir

        def writable_mkdir(path: Any, mode: int = 0o700) -> None:
            del mode
            original_mkdir(path, 0o777)

        tempfile_module._os.mkdir = writable_mkdir
        try:
            return tempfile.TemporaryDirectory(prefix="bastionvault-mock-", dir=str(parent))
        finally:
            tempfile_module._os.mkdir = original_mkdir


def _json_body(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")


@dataclass(frozen=True)
class MockResponse:
    """A deterministic HTTP response controlled by a mock route."""

    status: int
    headers: dict[str, str]
    body: bytes = b""


SCENARIO_RESPONSES: dict[str, MockResponse] = {
    "sealed": MockResponse(
        503, {"Content-Type": "application/json"}, _json_body({"errors": ["server is sealed"]})
    ),
    "standby_health": MockResponse(429, {"Content-Type": "application/json"}, _json_body({"standby": True})),
    "uninitialised": MockResponse(
        501, {"Content-Type": "application/json"}, _json_body({"errors": ["server is uninitialised"]})
    ),
    "dos_ban": MockResponse(
        429,
        {"Content-Type": "application/json", "Retry-After": "60"},
        _json_body({"errors": ["request blocked by DoS protection"]}),
    ),
    "namespace_quota": MockResponse(
        429, {"Content-Type": "application/json"}, _json_body({"errors": ["namespace quota exceeded"]})
    ),
    "not_found": MockResponse(404, {}, b""),
    "method_not_allowed": MockResponse(405, {}, b""),
    "no_content": MockResponse(204, {}, b""),
    "login_failure": MockResponse(
        200, {"Content-Type": "application/json"}, _json_body({"data": {"error": "invalid credentials"}})
    ),
}
SCENARIO_RESPONSES["uninitialized"] = SCENARIO_RESPONSES["uninitialised"]


class _CountingThreadingHTTPServer(ThreadingHTTPServer):
    """A threading HTTP server whose TLS handshake happens per-connection.

    The listening socket itself is never SSL-wrapped: wrapping the listening socket
    makes ``accept()`` perform the TLS handshake synchronously in the single accept
    loop, so one slow or failing handshake (for example, a client that omits the
    required mTLS certificate) can race with, or momentarily starve, the next
    ``accept()`` call and surface as a flaky plain TCP reset instead of a clean TLS
    alert (TST-003). Handshaking per-connection, inside the request's own worker
    thread, keeps every connection's TLS outcome independent and deterministic.
    """

    allow_reuse_address = True
    daemon_threads = True
    owner: MockHttpsServer
    ssl_context: ssl.SSLContext

    def __init__(
        self,
        server_address: tuple[str, int],
        handler_class: type[BaseHTTPRequestHandler],
        ssl_context: ssl.SSLContext,
    ) -> None:
        self.accepted_connections = 0
        self._counter_lock = threading.Lock()
        self.ssl_context = ssl_context
        super().__init__(server_address, handler_class)

    def get_request(self) -> tuple[socket.socket, Any]:
        request, address = super().get_request()
        with self._counter_lock:
            self.accepted_connections += 1
        return request, address

    def process_request(
        self, request: socket.socket | tuple[bytes, socket.socket], client_address: Any
    ) -> None:
        # Overridden (instead of relying on ThreadingMixIn.process_request) so the TLS
        # handshake runs inside the spawned worker thread, and so the handshaken SSL
        # socket -- not the plain socket accept() returned -- is what finish_request and
        # shutdown_request operate on and close.
        thread = threading.Thread(
            target=self._process_request_thread,
            args=(request, client_address),
            daemon=self.daemon_threads,
        )
        thread.start()

    def _process_request_thread(
        self, request: socket.socket | tuple[bytes, socket.socket], client_address: Any
    ) -> None:
        assert isinstance(request, socket.socket)
        try:
            ssl_socket = self.ssl_context.wrap_socket(request, server_side=True)
        except (ssl.SSLError, OSError):
            request.close()
            return
        try:
            self.finish_request(ssl_socket, client_address)
        except Exception:
            self.handle_error(ssl_socket, client_address)
        finally:
            self.shutdown_request(ssl_socket)


class _RequestHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "BastionVaultMock/0"
    sys_version = ""

    def do_LIST(self) -> None:
        self._handle_request()

    def do_GET(self) -> None:
        self._handle_request()

    def do_POST(self) -> None:
        self._handle_request()

    def do_PUT(self) -> None:
        self._handle_request()

    def do_DELETE(self) -> None:
        self._handle_request()

    def do_PATCH(self) -> None:
        self._handle_request()

    def _handle_request(self) -> None:
        owner = cast("MockHttpsServer", getattr(self.server, "owner"))
        owner.record_request(self.command, self.path)
        if owner.response_delay:
            time.sleep(owner.response_delay)
        response = owner.response_for(self.path)
        self.send_response_only(response.status)
        response_headers = dict(response.headers)
        response_headers["Content-Length"] = str(len(response.body))
        for name, value in response_headers.items():
            self.send_header(name, value)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(response.body)

    def log_message(self, format: str, *args: object) -> None:
        del format, args


class MockHttpsServer:
    """A loopback HTTPS listener with ephemeral CA, server, and client material."""

    _supported_scenarios: ClassVar[frozenset[str]] = frozenset(SCENARIO_RESPONSES)

    def __init__(
        self,
        *,
        hostname_mismatch: bool = False,
        require_client_certificate: bool = False,
        response_delay: float = 0.0,
        oversized_response_size: int | None = None,
    ) -> None:
        if response_delay < 0:
            raise ValueError("response_delay cannot be negative")
        if oversized_response_size is not None and oversized_response_size < 0:
            raise ValueError("oversized_response_size cannot be negative")
        self.response_delay = response_delay
        self._routes: dict[str, MockResponse] = {}
        self._request_count = 0
        self._list_request_count = 0
        self._request_lock = threading.Lock()
        temp_parent = Path(__file__).resolve().parents[3]
        self._temporary_directory = _temporary_directory(temp_parent)
        self.temp_dir = Path(self._temporary_directory.name)
        self._generate_certificates(hostname_mismatch)
        if oversized_response_size is not None:
            self.configure_oversized_response("/oversized", oversized_response_size)

        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        context.load_cert_chain(self.server_certificate_path, self.server_key_path)
        if require_client_certificate:
            # TLS 1.3 verifies the client certificate *after* the handshake completes,
            # so a missing certificate surfaces to the client as a non-deterministic mix
            # of a clean SSLError, an abrupt SSLEOFError, or even a bare connection reset
            # depending on scheduling (TST-003). Capping at TLS 1.2 makes the client
            # certificate exchange part of the handshake itself, so failure is always a
            # handshake-time alert the client observes as ssl.SSLError.
            context.maximum_version = ssl.TLSVersion.TLSv1_2
            context.verify_mode = ssl.CERT_REQUIRED
            context.load_verify_locations(cadata=self.ca_pem.decode("ascii"))
        self._server = _CountingThreadingHTTPServer(("127.0.0.1", 0), _RequestHandler, context)
        self._server.owner = self
        self._thread = threading.Thread(
            target=self._server.serve_forever, name="bastionvault-mock", daemon=True
        )
        self._thread.start()
        self._closed = False

    @property
    def base_url(self) -> str:
        host, port = self._server.server_address[0], self._server.server_address[1]
        return f"https://{host!s}:{port}"

    @property
    def ca_pem(self) -> bytes:
        return self.ca_certificate_path.read_bytes()

    @property
    def client_cert_pem(self) -> bytes:
        return self.client_certificate_path.read_bytes()

    @property
    def client_key_pem(self) -> bytes:
        return self.client_key_path.read_bytes()

    @property
    def server_certificate_path(self) -> str:
        return str(self.temp_dir / "server.crt.pem")

    @property
    def server_key_path(self) -> str:
        return str(self.temp_dir / "server.key.pem")

    @property
    def ca_certificate_path(self) -> Path:
        return self.temp_dir / "ca.crt.pem"

    @property
    def client_certificate_path(self) -> Path:
        return self.temp_dir / "client.crt.pem"

    @property
    def client_key_path(self) -> Path:
        return self.temp_dir / "client.key.pem"

    @property
    def accepted_connections(self) -> int:
        with self._server._counter_lock:
            return self._server.accepted_connections

    @property
    def request_count(self) -> int:
        with self._request_lock:
            return self._request_count

    @property
    def list_request_count(self) -> int:
        with self._request_lock:
            return self._list_request_count

    def set_scenario(self, path: str, scenario: str) -> None:
        """Route a path to one of the TST-021 response simulations."""
        if scenario not in self._supported_scenarios:
            raise ValueError(f"unsupported mock scenario: {scenario}")
        self._routes[path] = SCENARIO_RESPONSES[scenario]

    configure_scenario = set_scenario
    simulate = set_scenario

    def set_response(self, path: str, response: MockResponse) -> None:
        """Install a precise custom response for a test route."""
        self._routes[path] = response

    def configure_response_delay(self, seconds: float) -> None:
        if seconds < 0:
            raise ValueError("response delay cannot be negative")
        self.response_delay = seconds

    def configure_oversized_response(self, path: str, size: int) -> None:
        if size < 0:
            raise ValueError("response size cannot be negative")
        self._routes[path] = MockResponse(200, {"Content-Type": "application/octet-stream"}, b"x" * size)

    def response_for(self, path: str) -> MockResponse:
        return self._routes.get(
            path,
            MockResponse(200, {"Content-Type": "application/json"}, _json_body({"ok": True})),
        )

    def record_request(self, method: str, path: str) -> None:
        with self._request_lock:
            self._request_count += 1
            if method == "LIST":
                self._list_request_count += 1

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(timeout=5)
        self._temporary_directory.cleanup()

    def __enter__(self) -> "MockHttpsServer":
        return self

    def __exit__(self, exc_type: object, exc_value: object, traceback: object) -> None:
        del exc_type, exc_value, traceback
        self.close()

    def _generate_certificates(self, hostname_mismatch: bool) -> None:
        ca_key = ec.generate_private_key(ec.SECP256R1())
        ca_name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "BastionVault test CA")])
        now = datetime.now(timezone.utc)
        ca_certificate = (
            x509.CertificateBuilder()
            .subject_name(ca_name)
            .issuer_name(ca_name)
            .public_key(ca_key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - timedelta(minutes=1))
            .not_valid_after(now + timedelta(days=1))
            .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
            .sign(ca_key, hashes.SHA256())
        )
        server_key = ec.generate_private_key(ec.SECP256R1())
        certificate_host = "wronghost.invalid" if hostname_mismatch else "localhost"
        server_name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, certificate_host)])
        server_sans: list[x509.GeneralName] = [x509.DNSName(certificate_host)]
        if not hostname_mismatch:
            server_sans.append(x509.IPAddress(ipaddress.ip_address("127.0.0.1")))
        server_certificate = self._signed_certificate(
            subject=server_name,
            public_key=server_key.public_key(),
            issuer=ca_name,
            signer=ca_key,
            now=now,
            san=server_sans,
            usage=ExtendedKeyUsageOID.SERVER_AUTH,
        )
        client_key = ec.generate_private_key(ec.SECP256R1())
        client_name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "bastionvault-test-client")])
        client_certificate = self._signed_certificate(
            subject=client_name,
            public_key=client_key.public_key(),
            issuer=ca_name,
            signer=ca_key,
            now=now,
            san=[],
            usage=ExtendedKeyUsageOID.CLIENT_AUTH,
        )
        self._write_private_key(self.temp_dir / "ca.key.pem", ca_key)
        self._write_private_key(self.temp_dir / "server.key.pem", server_key)
        self._write_private_key(self.temp_dir / "client.key.pem", client_key)
        self.ca_certificate_path.write_bytes(ca_certificate.public_bytes(serialization.Encoding.PEM))
        (self.temp_dir / "server.crt.pem").write_bytes(
            server_certificate.public_bytes(serialization.Encoding.PEM)
        )
        (self.temp_dir / "client.crt.pem").write_bytes(
            client_certificate.public_bytes(serialization.Encoding.PEM)
        )

    @staticmethod
    def _signed_certificate(
        *,
        subject: x509.Name,
        public_key: Any,
        issuer: x509.Name,
        signer: Any,
        now: datetime,
        san: list[x509.GeneralName],
        usage: ObjectIdentifier,
    ) -> x509.Certificate:
        builder = (
            x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(issuer)
            .public_key(public_key)
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - timedelta(minutes=1))
            .not_valid_after(now + timedelta(days=1))
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
            .add_extension(x509.ExtendedKeyUsage([usage]), critical=False)
        )
        if san:
            builder = builder.add_extension(x509.SubjectAlternativeName(san), critical=False)
        return builder.sign(signer, hashes.SHA256())

    @staticmethod
    def _write_private_key(path: Path, key: Any) -> None:
        path.write_bytes(
            key.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.TraditionalOpenSSL,
                serialization.NoEncryption(),
            )
        )
